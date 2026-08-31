namespace ConfigTransform.Core;

/// <summary>
/// Shared CLI orchestration for both ConfigTransform.Xml and ConfigTransform.Json: parse args,
/// load the manifest, resolve layers, merge (via the format-specific <paramref name="merge"/>
/// delegate), and either print (--dry-run/--diff) or write (--output) the result. This class
/// knows nothing about XML or JSON specifically — only the shape both tools share.
/// </summary>
public static class CliRunner
{
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr,
        Func<string, string?, string?, string> merge)
    {
        try
        {
            var options = CliOptionsParser.Parse(args);

            var manifestFullPath = Path.GetFullPath(options.ManifestPath);
            var manifest = ManifestLoader.Load(manifestFullPath);
            var entry = ManifestEntrySelector.Select(manifest, options.File);

            var manifestDir = Path.GetDirectoryName(manifestFullPath)
                ?? throw new InvalidOperationException($"Could not determine the directory of '{manifestFullPath}'.");
            var overlayRoot = Path.Combine(manifestDir, entry.OverlayFolderName);
            var projectDir = Path.GetDirectoryName(Path.GetFullPath(manifest.Project))
                ?? throw new InvalidOperationException($"Could not determine the directory of '{manifest.Project}'.");

            var resolution = LayerResolution.Resolve(
                projectDir, entry.RelativeToProject, overlayRoot, options.Client, options.Environment);

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
