using ConfigTransform.Core;

namespace ConfigTransform.Json;

/// <summary>
/// The ConfigTransform.Json entry point, factored out of Program.cs so it can be exercised
/// directly in tests without spawning a subprocess. Delegates the shared resolve/list/diff/
/// dry-run/real-run orchestration to <see cref="CliRunner"/>, supplying
/// <see cref="JsonLayerMerger.Merge"/> as the merge engine. The <c>set</c> verb
/// (docs/FIELD_AUTHORING_DESIGN.md) is handled separately, mirroring
/// <c>ConfigTransform.Xml.XmlCliRunner</c>'s <c>RunSet</c> — target-file resolution is shared
/// via <see cref="SetTargetResolver"/>, only the authoring logic itself differs.
/// </summary>
public static class JsonCliRunner
{
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr, string? workingDirectory = null)
    {
        CliOptions options;
        try
        {
            options = CliOptionsParser.Parse(args);
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"Error: {ex.Message}");
            return 1;
        }

        return options.Set
            ? RunSet(options, stdout, stderr, workingDirectory)
            : CliRunner.Run(args, stdout, stderr, JsonLayerMerger.Merge, workingDirectory);
    }

    private static int RunSet(CliOptions options, TextWriter stdout, TextWriter stderr, string? workingDirectory)
    {
        try
        {
            var workDir = workingDirectory ?? Directory.GetCurrentDirectory();

            var manifestPath = options.ManifestPath ?? ManifestDiscovery.Discover(workDir);
            var manifestFullPath = Path.GetFullPath(manifestPath, workDir);
            var manifest = ManifestLoader.Load(manifestFullPath);

            var manifestDir = Path.GetDirectoryName(manifestFullPath)
                ?? throw new InvalidOperationException($"Could not determine the directory of '{manifestFullPath}'.");

            var entry = ManifestEntrySelector.Select(manifest, options.File);
            var overlayRoot = Path.Combine(manifestDir, entry.OverlayFolderName);
            var directory = Path.GetFullPath(manifest.Directory, workDir);
            var basePath = FileResolver.ResolveCaseInsensitiveRequired(directory, entry.RelativeToDirectory);

            var matches = options.Match.Select(m => MatchSpec.Parse(m, "key")).ToList();
            var setFields = options.SetFields.Select(m => MatchSpec.Parse(m, "value")).ToList();

            var target = SetTargetResolver.Resolve(
                overlayRoot, entry.RelativeToDirectory, basePath, options.Client, options.Environment);
            var targetPath = target.TargetPath;
            var isBaseTarget = target.IsBaseTarget;
            var environmentOverlayPath = target.EnvironmentOverlayPath;

            var precedingJson = isBaseTarget
                ? File.ReadAllText(basePath)
                : JsonLayerMerger.Merge(basePath, options.Client is null ? null : environmentOverlayPath, null);

            // Unlike XML, JSON has no separate "edit the matched element in place" base-target
            // code path -- writing directly to the base file is mechanically the same operation
            // as writing an overlay (set a nested path to a value), so the base file's own
            // current content (== precedingJson for a base-target write) is what to start from.
            var existingTargetJson = isBaseTarget
                ? precedingJson
                : File.Exists(targetPath) ? File.ReadAllText(targetPath) : null;

            var newContent = JsonFieldAuthor.Author(precedingJson, existingTargetJson, isBaseTarget, matches, setFields);

            if (options.DryRun)
            {
                stdout.WriteLine(newContent);
                return 0;
            }

            var targetDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(targetDir))
                Directory.CreateDirectory(targetDir);

            File.WriteAllText(targetPath, newContent);
            stdout.WriteLine($"Wrote '{targetPath}'.");

            // Auto-diff (docs/FIELD_AUTHORING_DESIGN.md): show the effective change at whichever
            // granularity was just written, not a diff of the overlay snippet's own raw text.
            var baseOnly = JsonLayerMerger.Merge(basePath, null, null);
            string mergedAfterWrite;
            if (isBaseTarget)
                mergedAfterWrite = JsonLayerMerger.Merge(targetPath, null, null);
            else if (options.Client is null)
                mergedAfterWrite = JsonLayerMerger.Merge(basePath, targetPath, null);
            else
                mergedAfterWrite = JsonLayerMerger.Merge(basePath, environmentOverlayPath, targetPath);

            var diffBase = isBaseTarget ? precedingJson : baseOnly;
            var diff = GitDiff.Render(diffBase, mergedAfterWrite);
            stdout.WriteLine(string.IsNullOrWhiteSpace(diff) ? "(no changes)" : diff);

            return 0;
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}
