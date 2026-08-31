using ConfigTransform.Core;

namespace ConfigTransform.Xml;

/// <summary>
/// The full CLI orchestration for ConfigTransform.Xml, factored out of Program.cs so it can be
/// exercised directly in tests (including "does this really never write to disk" checks for
/// --dry-run/--diff) without spawning a subprocess.
/// </summary>
public static class XmlCliRunner
{
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
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

            var merged = XmlLayerMerger.Merge(
                resolution.BasePath, resolution.EnvironmentOverlayPath, resolution.ClientOverlayPath);

            if (options.Diff)
            {
                var baseOnly = XmlLayerMerger.Merge(resolution.BasePath, environmentOverlayPath: null, clientOverlayPath: null);
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
