namespace ConfigTransform.Core;

/// <summary>
/// Shared CLI orchestration for both ConfigTransform.Xml and ConfigTransform.Json: parse args,
/// resolve the target layer's `extends` chain (docs/SELF_DESCRIBING_OVERLAYS_DESIGN.md), and
/// either print (--dry-run/--diff) or write (--output) the result — for one resource
/// (--resource given) or every resource the layer touches in this tool's own format (omitted).
/// --list is a separate, earlier branch handled by <see cref="LayerLister"/>. This class knows
/// nothing about XML or JSON specifically — only the shape both tools share; <paramref
/// name="merge"/> and <paramref name="ownedExtensions"/> are what a caller supplies to make it
/// format-specific.
/// </summary>
public static class CliRunner
{
    public static int Run(
        string[] args, TextWriter stdout, TextWriter stderr,
        Func<string, IReadOnlyList<string>, string> merge, IReadOnlyList<string> ownedExtensions,
        string? workingDirectory = null)
    {
        try
        {
            var options = CliOptionsParser.Parse(args);
            var root = workingDirectory ?? Directory.GetCurrentDirectory();

            if (options.List)
            {
                if (options.Resource is not null)
                    LayerLister.ListReverseLookup(root, options.Resource, stdout);
                else
                    LayerLister.ListLayer(root, LayerPathResolver.Resolve(root, options.Client, options.Environment)!, stdout);
                return 0;
            }

            var targetLayerPath = LayerPathResolver.Resolve(root, options.Client, options.Environment);
            var chain = LayerChain.Build(root, targetLayerPath);

            if (options.Resource is not null)
            {
                RunOneResource(options, root, chain, merge, ownedExtensions, stdout);
                return 0;
            }

            RunEveryResource(options, root, chain, merge, ownedExtensions, stdout, stderr);
            return 0;
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static void RunOneResource(
        CliOptions options, string root, IReadOnlyList<ResolvedLayer> chain,
        Func<string, IReadOnlyList<string>, string> merge, IReadOnlyList<string> ownedExtensions, TextWriter stdout)
    {
        RequireOwnedExtension(options.Resource!, ownedExtensions);

        var resolved = LayerChain.ResolveResource(root, chain, options.Resource!);
        foreach (var line in resolved.Report)
            stdout.WriteLine(line);

        var merged = merge(resolved.BasePath, resolved.PatchPathsInOrder);

        if (options.Diff)
        {
            var baseOnly = merge(resolved.BasePath, []);
            var diff = GitDiff.Render(baseOnly, merged);
            stdout.WriteLine(string.IsNullOrWhiteSpace(diff) ? "(no changes)" : diff);
            return;
        }

        if (options.DryRun)
        {
            stdout.WriteLine(merged);
            return;
        }

        var outputPath = options.Output
            ?? throw new InvalidOperationException("--output was not set for a real run.");
        var outputDir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(outputDir))
            Directory.CreateDirectory(outputDir);

        File.WriteAllText(outputPath, merged);
        stdout.WriteLine($"Wrote merged result to '{outputPath}'.");
    }

    /// <summary>
    /// Omitting --resource processes every resource the resolved layer touches, in this tool's
    /// own format only — a resource in the other format is skipped with a stderr note, not
    /// silently dropped or an error (docs/SELF_DESCRIBING_OVERLAYS_DESIGN.md "Open items for
    /// implementation" defers true mixed-format single-binary dispatch to the separate CLI-
    /// unification pass; this is the two-tool interim behavior).
    /// </summary>
    private static void RunEveryResource(
        CliOptions options, string root, IReadOnlyList<ResolvedLayer> chain,
        Func<string, IReadOnlyList<string>, string> merge, IReadOnlyList<string> ownedExtensions,
        TextWriter stdout, TextWriter stderr)
    {
        var allResources = LayerChain.ResolveAllResources(chain);
        var owned = allResources.Where(r => IsOwnedExtension(r, ownedExtensions)).ToList();
        var skipped = allResources.Count - owned.Count;

        if (skipped > 0)
        {
            stderr.WriteLine(
                $"Skipped {skipped} resource(s) not in this tool's format ({string.Join(", ", ownedExtensions)}); " +
                "run the matching tool for those.");
        }

        if (owned.Count == 0)
        {
            stdout.WriteLine("(no resources of this tool's format at this layer)");
            return;
        }

        if (!options.DryRun && !options.Diff)
        {
            var outputRoot = options.Output
                ?? throw new InvalidOperationException("--output was not set for a real run.");

            foreach (var resourcePath in owned)
            {
                var resolved = LayerChain.ResolveResource(root, chain, resourcePath);
                var merged = merge(resolved.BasePath, resolved.PatchPathsInOrder);

                var outPath = Path.Combine(outputRoot, resourcePath.Replace('/', Path.DirectorySeparatorChar));
                var outDir = Path.GetDirectoryName(outPath);
                if (!string.IsNullOrEmpty(outDir))
                    Directory.CreateDirectory(outDir);

                File.WriteAllText(outPath, merged);
                stdout.WriteLine($"Wrote '{outPath}'.");
            }

            return;
        }

        foreach (var resourcePath in owned)
        {
            var resolved = LayerChain.ResolveResource(root, chain, resourcePath);
            var merged = merge(resolved.BasePath, resolved.PatchPathsInOrder);

            stdout.WriteLine($"=== {resourcePath} ===");

            if (options.Diff)
            {
                var baseOnly = merge(resolved.BasePath, []);
                var diff = GitDiff.Render(baseOnly, merged);
                stdout.WriteLine(string.IsNullOrWhiteSpace(diff) ? "(no changes)" : diff);
            }
            else
            {
                stdout.WriteLine(merged);
            }

            stdout.WriteLine();
        }
    }

    private static void RequireOwnedExtension(string resourcePath, IReadOnlyList<string> ownedExtensions)
    {
        if (!IsOwnedExtension(resourcePath, ownedExtensions))
            throw new ArgumentException(
                $"'{resourcePath}' has an extension this tool doesn't handle (expected one of: " +
                $"{string.Join(", ", ownedExtensions)}) -- run the matching tool for this resource.");
    }

    private static bool IsOwnedExtension(string resourcePath, IReadOnlyList<string> ownedExtensions) =>
        ownedExtensions.Contains(Path.GetExtension(resourcePath), StringComparer.OrdinalIgnoreCase);
}
