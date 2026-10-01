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
/// and <c>!Console.IsInputRedirected</c>; every other mode ignores both. <paramref name="autoColor"/>
/// is what <c>--color auto</c> (the default) resolves to — the entry point passes "stdout is a
/// terminal and NO_COLOR is unset"; it defaults to false so an in-process caller capturing output
/// in a StringWriter gets plain text, the same as a redirect would. See <see cref="ColorMode"/>.
/// <paramref name="environmentVariables"/> is where <c>CFSECRET_*</c> overrides are read from
/// (docs/SECRETS_DESIGN.md) — the process environment by default; tests pass their own.
/// </summary>
public static class CliRunner
{
    public static int Run(
        string[] args, TextWriter stdout, TextWriter stderr, FormatEngineRegistry engines,
        string? workingDirectory = null, TextReader? stdin = null, bool interactiveAllowed = false,
        bool autoColor = false, Func<string, string?>? environmentVariables = null)
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
            var color = options.Color switch
            {
                ColorMode.Always => true,
                ColorMode.Never => false,
                _ => autoColor,
            };

            if (options.Init)
            {
                InitRunner.Run(options, root, engines, stdout, stdin ?? Console.In, interactiveAllowed);
                return 0;
            }

            if (options.Set)
            {
                SetRunner.Run(options, root, engines, stdout, color);
                return 0;
            }

            if (options.List)
            {
                if (options.Resource is not null)
                    LayerLister.ListReverseLookup(root, options.Resource, stdout);
                else
                    LayerLister.ListLayer(root, LayerPathResolver.Resolve(root, options.Client, options.Environment, options.Host)!, stdout);
                return 0;
            }

            var targetLayerPath = LayerPathResolver.Resolve(root, options.Client, options.Environment, options.Host);
            var chain = LayerChain.Build(root, targetLayerPath);
            var secrets = SecretResolver.Build(root, chain, environmentVariables ?? Environment.GetEnvironmentVariable);

            if (options.Resource is not null)
            {
                RunOneResource(options, root, chain, secrets, engines, stdout, color);
                return 0;
            }

            RunEveryResource(options, root, targetLayerPath, chain, secrets, engines, stdout, stderr, color);
            return 0;
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static void RunOneResource(
        CliOptions options, string root, IReadOnlyList<ResolvedLayer> chain, SecretSet secrets,
        FormatEngineRegistry engines, TextWriter stdout, bool color)
    {
        var resolved = LayerChain.ResolveResource(root, chain, options.Resource!);

        // A whole-file secret needs no format engine at all -- resolved before asking for one, so a
        // replaced .p12 or .pem works even though no engine handles its extension.
        if (resolved.ReplacePath is not null)
        {
            RunReplacedResource(options, root, resolved, stdout, color);
            return;
        }

        var engine = engines.Require(options.Resource!);
        var merged = engine.Merge(resolved.BasePath, resolved.PatchPathsInOrder);

        PrintResolutionReport(stdout, options.Resource!, resolved);
        SecretsStep.PrintReport(stdout, merged, secrets);

        // Previews: placeholders as written, unless --reveal-secrets. In a revealed diff every side
        // resolves with the whole chain's secrets, so a diff never shows a placeholder turning into
        // its value (docs/SECRETS_DESIGN.md).
        string Preview(string content) => SecretsStep.ForPreview(engine, content, secrets, options.RevealSecrets);

        if (options.DiffLayers)
        {
            var sections = LayerDiffAttribution.Compute(
                resolved, (basePath, patches) => Preview(engine.Merge(basePath, patches)), color);
            stdout.WriteLine();
            stdout.WriteLine(sections.Count == 0 ? "(no changes)" : string.Join("\n\n", sections.Select(s => s.Diff)));
            return;
        }

        if (options.Diff)
        {
            var baseOnly = engine.Merge(resolved.BasePath, []);
            var diff = GitDiff.Render(Preview(baseOnly), Preview(merged), color);
            stdout.WriteLine();
            stdout.WriteLine(string.IsNullOrWhiteSpace(diff) ? "(no changes)" : diff);
            return;
        }

        if (options.DryRun)
        {
            stdout.WriteLine();
            stdout.WriteLine(Preview(merged));
            return;
        }

        var final = SecretsStep.ForRealRun(engine, options.Resource!, merged, secrets);

        var outputPath = options.Output
            ?? throw new InvalidOperationException("--output was not set for a real run.");
        var outputDir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(outputDir))
            Directory.CreateDirectory(outputDir);

        File.WriteAllText(outputPath, final);
        stdout.WriteLine($"Wrote merged result to '{outputPath}'.");
    }

    private static void RunReplacedResource(
        CliOptions options, string root, ResolvedResource resolved, TextWriter stdout, bool color)
    {
        PrintResolutionReport(stdout, options.Resource!, resolved);

        if (options.DryRun || options.Diff || options.DiffLayers)
        {
            stdout.WriteLine();
            stdout.WriteLine(ReplaceStep.Preview(root, resolved, options.RevealSecrets, diff: !options.DryRun, color));
            return;
        }

        var bytes = ReplaceStep.ForRealRun(root, options.Resource!, resolved);
        var outputPath = options.Output
            ?? throw new InvalidOperationException("--output was not set for a real run.");
        var outputDir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(outputDir))
            Directory.CreateDirectory(outputDir);

        File.WriteAllBytes(outputPath, bytes);
        stdout.WriteLine($"Wrote '{outputPath}' (replaced by {LayerChain.ToRepoRelative(root, resolved.ReplacePath!)}).");
    }

    /// <summary>Prints the "Resolving '&lt;path&gt;'" header, then the shared chain rendering — see <see cref="LayerChain.PrintChain"/>.</summary>
    private static void PrintResolutionReport(TextWriter stdout, string resourcePath, ResolvedResource resolved)
    {
        stdout.WriteLine($"Resolving '{resourcePath}'");
        LayerChain.PrintChain(stdout, resourcePath, resolved);
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
        CliOptions options, string root, string? targetLayerPath, IReadOnlyList<ResolvedLayer> chain, SecretSet secrets,
        FormatEngineRegistry engines, TextWriter stdout, TextWriter stderr, bool color)
    {
        var allResources = LayerChain.ResolveAllResources(chain);
        // A resource replaced by a whole-file secret needs no format engine (docs/SECRETS_DESIGN.md).
        var owned = allResources.Where(r => engines.Find(r) is not null || LayerChain.IsReplaced(root, chain, r)).ToList();
        var skipped = allResources.Count - owned.Count;

        if (skipped > 0)
        {
            stderr.WriteLine(
                $"Skipped {skipped} resource(s) with no registered format handler; " +
                $"supported formats: {engines.SupportedExtensions}.");
        }

        if (owned.Count == 0)
        {
            if (allResources.Count > 0)
            {
                stdout.WriteLine("(no resources with a registered format handler at this layer)");
            }
            else if (targetLayerPath is not null && chain.Count == 0)
            {
                // The target layer file itself is missing, not just empty -- distinct from "this
                // layer legitimately has no resources," and worth calling out since it's usually a
                // typo'd --client/--environment rather than an intentionally-unconfigured layer
                // (missing overlays elsewhere in a chain stay silent per CONFIG_MANAGEMENT.md §5.1;
                // this is the target itself, the one layer whose absence a real caller most likely
                // didn't intend).
                var expected = LayerChain.ToRepoRelative(root, Path.GetFullPath(targetLayerPath, root));
                var target = options.Client is not null
                    ? $"--client '{options.Client}' --environment '{options.Environment}'"
                    : $"--environment '{options.Environment}'";
                if (options.Host is not null)
                    target += $" --host '{options.Host}'";
                stdout.WriteLine(
                    $"(no configtransform.json found for {target} -- expected at '{expected}'.\n" +
                    "Try: check the spelling, or run 'configtransform init' to scaffold it.)");
            }
            else
            {
                stdout.WriteLine("(no resources with a registered format handler at this layer)");
            }

            return;
        }

        if (!options.DryRun && !options.Diff && !options.DiffLayers)
        {
            var outputRoot = options.Output
                ?? throw new InvalidOperationException("--output was not set for a real run.");

            if (File.Exists(outputRoot))
            {
                throw new ArgumentException(
                    $"--output '{outputRoot}' already exists as a file, but --resource was omitted, so " +
                    "--output must be a directory (one file is written per resource).\n" +
                    $"Try: add --resource <path> to target and overwrite that one file directly, " +
                    "or point --output at a different or empty directory.");
            }

            // All-or-nothing: every resource is merged and its secrets resolved before any file is
            // written, so an unresolvable secret in one resource never leaves a half-updated output.
            var finals = owned.Select(resourcePath =>
            {
                var resolved = LayerChain.ResolveResource(root, chain, resourcePath);
                if (resolved.ReplacePath is not null)
                    return (ResourcePath: resourcePath, Text: (string?)null, Bytes: ReplaceStep.ForRealRun(root, resourcePath, resolved));

                var engine = engines.Require(resourcePath);
                var merged = engine.Merge(resolved.BasePath, resolved.PatchPathsInOrder);
                return (ResourcePath: resourcePath, Text: SecretsStep.ForRealRun(engine, resourcePath, merged, secrets), Bytes: (byte[]?)null);
            }).ToList();

            foreach (var (resourcePath, text, bytes) in finals)
            {
                var outPath = Path.Combine(outputRoot, resourcePath.Replace('/', Path.DirectorySeparatorChar));
                var outDir = Path.GetDirectoryName(outPath);
                if (!string.IsNullOrEmpty(outDir))
                    Directory.CreateDirectory(outDir);

                if (bytes is not null)
                    File.WriteAllBytes(outPath, bytes);
                else
                    File.WriteAllText(outPath, text);
                stdout.WriteLine($"Wrote '{outPath}'.");
            }

            return;
        }

        foreach (var resourcePath in owned)
        {
            var resolved = LayerChain.ResolveResource(root, chain, resourcePath);
            if (resolved.ReplacePath is not null)
            {
                stdout.WriteLine($"=== {resourcePath} ===");
                stdout.WriteLine(ReplaceStep.Preview(root, resolved, options.RevealSecrets, diff: !options.DryRun, color));
                stdout.WriteLine();
                continue;
            }

            var engine = engines.Require(resourcePath);
            var merged = engine.Merge(resolved.BasePath, resolved.PatchPathsInOrder);

            stdout.WriteLine($"=== {resourcePath} ===");
            SecretsStep.PrintReport(stdout, merged, secrets, indent: "");

            string Preview(string content) => SecretsStep.ForPreview(engine, content, secrets, options.RevealSecrets);

            if (options.DiffLayers)
            {
                var sections = LayerDiffAttribution.Compute(
                    resolved, (basePath, patches) => Preview(engine.Merge(basePath, patches)), color);
                stdout.WriteLine(sections.Count == 0 ? "(no changes)" : string.Join("\n\n", sections.Select(s => s.Diff)));
            }
            else if (options.Diff)
            {
                var baseOnly = engine.Merge(resolved.BasePath, []);
                var diff = GitDiff.Render(Preview(baseOnly), Preview(merged), color);
                stdout.WriteLine(string.IsNullOrWhiteSpace(diff) ? "(no changes)" : diff);
            }
            else
            {
                stdout.WriteLine(Preview(merged));
            }

            stdout.WriteLine();
        }
    }
}
