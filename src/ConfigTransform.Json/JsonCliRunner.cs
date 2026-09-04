using ConfigTransform.Core;

namespace ConfigTransform.Json;

/// <summary>
/// The ConfigTransform.Json entry point, factored out of Program.cs so it can be exercised
/// directly in tests without spawning a subprocess. Delegates the shared resolve/list/diff/
/// dry-run/real-run orchestration to <see cref="CliRunner"/>, supplying
/// <see cref="JsonLayerMerger.Merge"/> as the merge engine and <see cref="OwnedExtensions"/> so a
/// multi-resource invocation (no --resource) only ever touches JSON resources — see
/// <c>ConfigTransform.Xml.XmlCliRunner</c>'s remarks for why. The <c>set</c> verb
/// (docs/FIELD_AUTHORING_DESIGN.md) is handled separately, mirroring
/// <c>ConfigTransform.Xml.XmlCliRunner</c>'s <c>RunSet</c> — target resolution is shared via
/// <see cref="SetTargetResolver"/>, only the authoring logic itself differs.
/// </summary>
public static class JsonCliRunner
{
    private static readonly IReadOnlyList<string> OwnedExtensions = [".json"];

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
            : CliRunner.Run(args, stdout, stderr, JsonLayerMerger.Merge, OwnedExtensions, workingDirectory);
    }

    private static int RunSet(CliOptions options, TextWriter stdout, TextWriter stderr, string? workingDirectory)
    {
        try
        {
            var root = workingDirectory ?? Directory.GetCurrentDirectory();

            var matches = options.Match.Select(m => MatchSpec.Parse(m, "key")).ToList();
            var setFields = options.SetFields.Select(m => MatchSpec.Parse(m, "value")).ToList();

            var target = SetTargetResolver.Resolve(root, options.Resource!, options.Client, options.Environment, "json");

            var precedingJson = target.IsBaseTarget
                ? File.ReadAllText(target.ResourceBasePath)
                : JsonLayerMerger.Merge(target.ResourceBasePath, target.PrecedingPatchPathsInOrder);

            // Unlike XML, JSON has no separate "edit the matched element in place" base-target
            // code path -- writing directly to the base file is mechanically the same operation
            // as writing an overlay (set a nested path to a value), so the base file's own
            // current content (== precedingJson for a base-target write) is what to start from.
            var existingTargetJson = target.IsBaseTarget
                ? precedingJson
                : File.Exists(target.PatchPath) ? File.ReadAllText(target.PatchPath!) : null;

            var newContent = JsonFieldAuthor.Author(precedingJson, existingTargetJson, target.IsBaseTarget, matches, setFields);

            if (options.DryRun)
            {
                stdout.WriteLine(newContent);
                return 0;
            }

            if (target.IsBaseTarget)
            {
                File.WriteAllText(target.ResourceBasePath, newContent);
                stdout.WriteLine($"Wrote '{target.ResourceBasePath}'.");
            }
            else
            {
                var targetDir = Path.GetDirectoryName(target.PatchPath);
                if (!string.IsNullOrEmpty(targetDir))
                    Directory.CreateDirectory(targetDir);

                File.WriteAllText(target.PatchPath!, newContent);
                SetTargetResolver.EnsureResourceListed(root, target);
                stdout.WriteLine($"Wrote '{target.PatchPath}'.");
            }

            // Auto-diff (docs/FIELD_AUTHORING_DESIGN.md): show the effective change at whichever
            // granularity was just written, not a diff of the overlay snippet's own raw text.
            var baseOnly = JsonLayerMerger.Merge(target.ResourceBasePath, []);
            var mergedAfterWrite = target.IsBaseTarget
                ? JsonLayerMerger.Merge(target.ResourceBasePath, [])
                : JsonLayerMerger.Merge(target.ResourceBasePath, [.. target.PrecedingPatchPathsInOrder, target.PatchPath!]);

            var diffBase = target.IsBaseTarget ? precedingJson : baseOnly;
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
