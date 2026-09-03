using ConfigTransform.Core;

namespace ConfigTransform.Xml;

/// <summary>
/// The ConfigTransform.Xml entry point, factored out of Program.cs so it can be exercised
/// directly in tests without spawning a subprocess. Delegates the shared resolve/list/diff/
/// dry-run/real-run orchestration to <see cref="CliRunner"/>, supplying
/// <see cref="XmlLayerMerger.Merge"/> as the merge engine. The <c>set</c> verb
/// (docs/FIELD_AUTHORING_DESIGN.md) is different enough in shape — optional client/environment,
/// a computed write target instead of --output, XML-specific authoring logic — that it's
/// handled separately here rather than folded into <see cref="CliRunner"/>.
/// </summary>
public static class XmlCliRunner
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
            : CliRunner.Run(args, stdout, stderr, XmlLayerMerger.Merge, workingDirectory);
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
            var extension = Path.GetExtension(entry.RelativeToDirectory);

            var matches = options.Match.Select(m => MatchSpec.Parse(m, "key")).ToList();
            var setFields = options.SetFields.Select(m => MatchSpec.Parse(m, "value")).ToList();

            // Resolve the environment overlay once -- reused both to compute what "precedes"
            // a client-layer write and, after writing, to compute the effective merged result
            // for the auto-diff, so it's never resolved inconsistently between the two.
            string? environmentOverlayPath = options.Environment is null
                ? null
                : FileResolver.TryResolveCaseInsensitive(
                    Path.Combine(overlayRoot, "Environments"), $"{options.Environment}{extension}");

            string targetPath;
            bool isBaseTarget;
            string precedingXml;

            if (options.Client is null && options.Environment is null)
            {
                targetPath = basePath;
                isBaseTarget = true;
                precedingXml = File.ReadAllText(basePath);
            }
            else if (options.Client is null)
            {
                var environmentDir = Path.Combine(overlayRoot, "Environments");
                var fileName = $"{options.Environment}{extension}";
                targetPath = environmentOverlayPath ?? Path.Combine(environmentDir, fileName);
                isBaseTarget = false;
                precedingXml = XmlLayerMerger.Merge(basePath, null, null);
            }
            else
            {
                var clientDir = Path.Combine(overlayRoot, "Clients", options.Client);
                var fileName = $"{options.Environment}{extension}";
                targetPath = FileResolver.TryResolveCaseInsensitive(clientDir, fileName)
                    ?? Path.Combine(clientDir, fileName);
                isBaseTarget = false;
                precedingXml = XmlLayerMerger.Merge(basePath, environmentOverlayPath, null);
            }

            var existingTargetXml = !isBaseTarget && File.Exists(targetPath) ? File.ReadAllText(targetPath) : null;

            var newContent = XmlFieldAuthor.Author(precedingXml, existingTargetXml, isBaseTarget, matches, setFields);

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
            var baseOnly = XmlLayerMerger.Merge(basePath, null, null);
            string mergedAfterWrite;
            if (isBaseTarget)
                mergedAfterWrite = XmlLayerMerger.Merge(targetPath, null, null);
            else if (options.Client is null)
                mergedAfterWrite = XmlLayerMerger.Merge(basePath, targetPath, null);
            else
                mergedAfterWrite = XmlLayerMerger.Merge(basePath, environmentOverlayPath, targetPath);

            var diffBase = isBaseTarget ? precedingXml : baseOnly;
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
