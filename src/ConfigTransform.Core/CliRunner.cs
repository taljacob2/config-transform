namespace ConfigTransform.Core;

/// <summary>
/// Shared CLI orchestration for the `configtransform` dispatcher: parse args, resolve the target
/// layer's `extends` chain (docs/SELF_DESCRIBING_OVERLAYS_DESIGN.md), and either print
/// (--dry-run/--diff) or write (--output) the result — for one resource (--resource given) or
/// every resource the layer touches, across every registered format, in one call (omitted).
/// --list is a separate, earlier branch handled by <see cref="LayerLister"/>; `set` is handled by
/// <see cref="SetRunner"/>; `init` (scaffolding a tree, docs/INIT_COMMAND_DESIGN.md) is handled by
/// <see cref="InitRunner"/>; help (no arguments, `help`, `--help`/`-h`) is checked first, before
/// even resolving a working directory, and short-circuits everything else via
/// <see cref="HelpPrinter"/>. This class knows nothing about XML or JSON specifically — only the
/// shape every format shares; <paramref name="engines"/> is what the caller (the CLI entry point)
/// supplies to make it concrete. <paramref name="stdin"/>/<paramref name="interactiveAllowed"/>
/// exist only for `init`'s interactive form — the real entry point passes <see cref="Console.In"/>
/// and <c>!Console.IsInputRedirected</c>; every other mode ignores both.
/// </summary>
public static class CliRunner
{
    public static int Run(
        string[] args, TextWriter stdout, TextWriter stderr, FormatEngineRegistry engines,
        string? workingDirectory = null, TextReader? stdin = null, bool interactiveAllowed = false)
    {
        try
        {
            var options = CliOptionsParser.Parse(args);

            if (options.Help)
            {
                HelpPrinter.Print(stdout, engines);
                return 0;
            }

            var root = workingDirectory ?? Directory.GetCurrentDirectory();

            if (options.Init)
            {
                InitRunner.Run(options, root, engines, stdout, stdin ?? Console.In, interactiveAllowed);
                return 0;
            }

            if (options.Set)
            {
                SetRunner.Run(options, root, engines, stdout);
                return 0;
            }

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
                RunOneResource(options, root, chain, engines, stdout);
                return 0;
            }

            RunEveryResource(options, root, chain, engines, stdout, stderr);
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
        FormatEngineRegistry engines, TextWriter stdout)
    {
        var engine = engines.Require(options.Resource!);

        var resolved = LayerChain.ResolveResource(root, chain, options.Resource!);
        foreach (var line in resolved.Report)
            stdout.WriteLine(line);

        var merged = engine.Merge(resolved.BasePath, resolved.PatchPathsInOrder);

        if (options.Diff)
        {
            var baseOnly = engine.Merge(resolved.BasePath, []);
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
    /// Omitting --resource processes every resource the resolved layer touches, across every
    /// registered format, in one call — the real capability CLI unification delivers
    /// (docs/SELF_DESCRIBING_OVERLAYS_DESIGN.md "Settled decisions" #2/#7). A resource whose
    /// extension no registered engine handles is skipped with a stderr note, not silently
    /// dropped or an error — the only remaining skip case, and the seam a future format (e.g.
    /// YAML, docs/CONFIG_MANAGEMENT.md §5.5) plugs into with no orchestration changes.
    /// </summary>
    private static void RunEveryResource(
        CliOptions options, string root, IReadOnlyList<ResolvedLayer> chain,
        FormatEngineRegistry engines, TextWriter stdout, TextWriter stderr)
    {
        var allResources = LayerChain.ResolveAllResources(chain);
        var owned = allResources.Where(r => engines.Find(r) is not null).ToList();
        var skipped = allResources.Count - owned.Count;

        if (skipped > 0)
        {
            stderr.WriteLine(
                $"Skipped {skipped} resource(s) with no registered format handler; " +
                $"supported formats: {engines.SupportedExtensions}.");
        }

        if (owned.Count == 0)
        {
            stdout.WriteLine("(no resources with a registered format handler at this layer)");
            return;
        }

        if (!options.DryRun && !options.Diff)
        {
            var outputRoot = options.Output
                ?? throw new InvalidOperationException("--output was not set for a real run.");

            foreach (var resourcePath in owned)
            {
                var engine = engines.Require(resourcePath);
                var resolved = LayerChain.ResolveResource(root, chain, resourcePath);
                var merged = engine.Merge(resolved.BasePath, resolved.PatchPathsInOrder);

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
            var engine = engines.Require(resourcePath);
            var resolved = LayerChain.ResolveResource(root, chain, resourcePath);
            var merged = engine.Merge(resolved.BasePath, resolved.PatchPathsInOrder);

            stdout.WriteLine($"=== {resourcePath} ===");

            if (options.Diff)
            {
                var baseOnly = engine.Merge(resolved.BasePath, []);
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
}
