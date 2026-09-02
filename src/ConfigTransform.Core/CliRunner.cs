namespace ConfigTransform.Core;

/// <summary>
/// Shared CLI orchestration for both ConfigTransform.Xml and ConfigTransform.Json: parse args,
/// load the manifest, resolve layers, merge (via the format-specific <paramref name="merge"/>
/// delegate), and either print (--dry-run/--diff) or write (--output) the result. --list is a
/// separate, earlier branch: pure manifest introspection, no merge and no --client/--environment
/// needed. This class knows nothing about XML or JSON specifically — only the shape both tools
/// share.
/// </summary>
public static class CliRunner
{
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr,
        Func<string, string?, string?, string> merge, string? workingDirectory = null)
    {
        try
        {
            var options = CliOptionsParser.Parse(args);
            var workDir = workingDirectory ?? Directory.GetCurrentDirectory();

            var manifestPath = options.ManifestPath ?? ManifestDiscovery.Discover(workDir);
            var manifestFullPath = Path.GetFullPath(manifestPath, workDir);
            var manifest = ManifestLoader.Load(manifestFullPath);

            var manifestDir = Path.GetDirectoryName(manifestFullPath)
                ?? throw new InvalidOperationException($"Could not determine the directory of '{manifestFullPath}'.");

            if (options.List)
            {
                ManifestLister.List(manifest, options.File, manifestDir, stdout);
                return 0;
            }

            var entry = ManifestEntrySelector.Select(manifest, options.File);
            var overlayRoot = Path.Combine(manifestDir, entry.OverlayFolderName);
            var directory = Path.GetFullPath(manifest.Directory, workDir);
            var client = options.Client ?? throw new InvalidOperationException("--client was not set for a real run.");
            var environment = options.Environment ?? throw new InvalidOperationException("--environment was not set for a real run.");

            var resolution = LayerResolution.Resolve(
                directory, entry.RelativeToDirectory, overlayRoot, client, environment);

            foreach (var line in resolution.Report)
                stdout.WriteLine(line);

            var merged = merge(resolution.BasePath, resolution.EnvironmentOverlayPath, resolution.ClientOverlayPath);

            if (options.Diff)
            {
                var baseOnly = merge(resolution.BasePath, null, null);
                var diff = GitDiff.Render(baseOnly, merged);
                stdout.WriteLine(string.IsNullOrWhiteSpace(diff) ? "(no changes)" : diff);
                return 0;
            }

            if (options.DryRun)
            {
                stdout.WriteLine(merged);
                return 0;
            }

            var outputPath = options.Output
                ?? throw new InvalidOperationException("--output was not set for a real run.");
            var outputDir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!string.IsNullOrEmpty(outputDir))
                Directory.CreateDirectory(outputDir);

            File.WriteAllText(outputPath, merged);
            stdout.WriteLine($"Wrote merged result to '{outputPath}'.");

            return 0;
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}
