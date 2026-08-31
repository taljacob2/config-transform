using ConfigTransform.Core;
using ConfigTransform.Xml;

try
{
    var options = CliOptionsParser.Parse(args);

    if (options.DryRun || options.Diff)
    {
        Console.Error.WriteLine("--dry-run and --diff are not yet implemented. See docs/ROADMAP.md.");
        return 1;
    }

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
        Console.WriteLine(line);

    var merged = XmlLayerMerger.Merge(
        resolution.BasePath, resolution.EnvironmentOverlayPath, resolution.ClientOverlayPath);

    var outputPath = options.Output
        ?? throw new InvalidOperationException("--output was not set for a real run.");
    var outputDir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
    if (!string.IsNullOrEmpty(outputDir))
        Directory.CreateDirectory(outputDir);

    File.WriteAllText(outputPath, merged);
    Console.WriteLine($"Wrote merged result to '{outputPath}'.");

    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}
