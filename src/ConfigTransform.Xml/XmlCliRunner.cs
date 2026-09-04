using ConfigTransform.Core;

namespace ConfigTransform.Xml;

/// <summary>
/// The ConfigTransform.Xml entry point, factored out of Program.cs so it can be exercised
/// directly in tests without spawning a subprocess. Delegates the shared resolve/list/diff/
/// dry-run/real-run orchestration to <see cref="CliRunner"/>, supplying
/// <see cref="XmlLayerMerger.Merge"/> as the merge engine and <see cref="OwnedExtensions"/> so a
/// multi-resource invocation (no --resource) only ever touches XML resources — a mixed-format
/// configtransform.json is expected under docs/SELF_DESCRIBING_OVERLAYS_DESIGN.md, and true
/// single-binary dispatch across formats is a deliberately separate, not-yet-implemented pass
/// (see that document's "Open items for implementation"). The <c>set</c> verb
/// (docs/FIELD_AUTHORING_DESIGN.md) is different enough in shape — optional client/environment,
/// a computed write target instead of --output, XML-specific authoring logic — that it's
/// handled separately here rather than folded into <see cref="CliRunner"/>.
/// </summary>
public static class XmlCliRunner
{
    private static readonly IReadOnlyList<string> OwnedExtensions = [".config", ".xml"];

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
            : CliRunner.Run(args, stdout, stderr, XmlLayerMerger.Merge, OwnedExtensions, workingDirectory);
    }

    private static int RunSet(CliOptions options, TextWriter stdout, TextWriter stderr, string? workingDirectory)
    {
        try
        {
            var root = workingDirectory ?? Directory.GetCurrentDirectory();

            var matches = options.Match.Select(m => MatchSpec.Parse(m, "key")).ToList();
            var setFields = options.SetFields.Select(m => MatchSpec.Parse(m, "value")).ToList();

            var target = SetTargetResolver.Resolve(root, options.Resource!, options.Client, options.Environment, "xml");

            var precedingXml = target.IsBaseTarget
                ? File.ReadAllText(target.ResourceBasePath)
                : XmlLayerMerger.Merge(target.ResourceBasePath, target.PrecedingPatchPathsInOrder);

            var existingTargetXml = !target.IsBaseTarget && File.Exists(target.PatchPath)
                ? File.ReadAllText(target.PatchPath!)
                : null;

            var newContent = XmlFieldAuthor.Author(precedingXml, existingTargetXml, target.IsBaseTarget, matches, setFields);

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
            var baseOnly = XmlLayerMerger.Merge(target.ResourceBasePath, []);
            var mergedAfterWrite = target.IsBaseTarget
                ? XmlLayerMerger.Merge(target.ResourceBasePath, [])
                : XmlLayerMerger.Merge(target.ResourceBasePath, [.. target.PrecedingPatchPathsInOrder, target.PatchPath!]);

            var diffBase = target.IsBaseTarget ? precedingXml : baseOnly;
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
