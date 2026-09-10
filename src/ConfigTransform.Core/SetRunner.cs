namespace ConfigTransform.Core;

/// <summary>
/// The `set` verb's orchestration (docs/FIELD_AUTHORING_DESIGN.md), unified across every
/// registered format engine — resolves the target patch file/base file via
/// <see cref="SetTargetResolver"/>, delegates the actual field-authoring to whichever
/// <see cref="FormatEngine"/> matches the target resource's own extension, writes the result, and
/// prints the auto-diff. Different enough in shape from the resolve/list/dry-run/real-run flow
/// (optional client/environment, a computed write target instead of --output) that it's handled
/// separately from <see cref="CliRunner"/> rather than folded into it.
/// </summary>
public static class SetRunner
{
    public static void Run(CliOptions options, string root, FormatEngineRegistry engines, TextWriter stdout)
    {
        var engine = engines.Require(options.Resource!);

        var matches = options.Match.Select(m => MatchSpec.Parse(m, "key")).ToList();
        var setFields = options.SetFields.Select(m => MatchSpec.Parse(m, "value")).ToList();

        var target = SetTargetResolver.Resolve(root, options.Resource!, options.Client, options.Environment, options.Host, engine.PatchExtension);

        var preceding = target.IsBaseTarget
            ? File.ReadAllText(target.ResourceBasePath)
            : engine.Merge(target.ResourceBasePath, target.PrecedingPatchPathsInOrder);

        // A base-target write's `existingTarget` is the base file's own current content (== `preceding`)
        // -- for JSON, writing directly to the base file is mechanically the same operation as writing
        // an overlay (set a nested path to a value), so it needs the real starting content. For XML,
        // Author's isBaseTarget branch never reads existingTarget at all (it edits `preceding` in place
        // and returns before that parameter is touched), so this is dead input there -- one rule is
        // correct for both formats, not a compromise between them.
        var existingTarget = target.IsBaseTarget
            ? preceding
            : File.Exists(target.PatchPath) ? File.ReadAllText(target.PatchPath!) : null;

        var newContent = engine.Author(preceding, existingTarget, target.IsBaseTarget, matches, setFields);

        if (options.DryRun)
        {
            stdout.WriteLine(newContent);
            return;
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
        var baseOnly = engine.Merge(target.ResourceBasePath, []);
        var mergedAfterWrite = target.IsBaseTarget
            ? engine.Merge(target.ResourceBasePath, [])
            : engine.Merge(target.ResourceBasePath, [.. target.PrecedingPatchPathsInOrder, target.PatchPath!]);

        var diffBase = target.IsBaseTarget ? preceding : baseOnly;
        var diff = GitDiff.Render(diffBase, mergedAfterWrite);
        stdout.WriteLine(string.IsNullOrWhiteSpace(diff) ? "(no changes)" : diff);
    }
}
