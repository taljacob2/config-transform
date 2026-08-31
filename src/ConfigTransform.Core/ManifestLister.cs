namespace ConfigTransform.Core;

/// <summary>
/// Implements --list: prints a manifest's file entries and, for each, which Environments/
/// Clients overlays actually exist on disk. Purely an introspection command -- unlike a real
/// resolve, an ambiguous manifest (multiple file entries, no --file) is not an error here; it
/// just lists all of them, since the whole point is not having to already know the answer.
/// </summary>
public static class ManifestLister
{
    public static void List(Manifest manifest, string? fileArg, string manifestDir, TextWriter stdout)
    {
        var entries = fileArg is null
            ? manifest.Files
            : manifest.Files.Where(f =>
                    f.RelativeToDirectory == fileArg ||
                    string.Equals(f.OverlayFolderName, fileArg, StringComparison.OrdinalIgnoreCase))
                .ToList();

        if (entries.Count == 0)
            throw new ArgumentException($"No manifest entry matches --file '{fileArg}'.");

        stdout.WriteLine($"directory: {manifest.Directory}");

        foreach (var entry in entries)
        {
            stdout.WriteLine();
            stdout.WriteLine($"{entry.RelativeToDirectory} ({entry.Type})");

            var overlayRoot = Path.Combine(manifestDir, entry.OverlayFolderName);

            var environments = FindOverlayNames(Path.Combine(overlayRoot, "Environments"), entry.RelativeToDirectory);
            stdout.WriteLine($"  environments: {Describe(environments)}");

            var clientsRoot = Path.Combine(overlayRoot, "Clients");
            var clientNames = Directory.Exists(clientsRoot)
                ? Directory.GetDirectories(clientsRoot)
                    .Select(d => Path.GetFileName(d)!)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : new List<string>();

            if (clientNames.Count == 0)
            {
                stdout.WriteLine("  clients: (none)");
                continue;
            }

            stdout.WriteLine("  clients:");
            foreach (var client in clientNames)
            {
                var clientEnvironments = FindOverlayNames(Path.Combine(clientsRoot, client), entry.RelativeToDirectory);
                stdout.WriteLine($"    {client}: {Describe(clientEnvironments)}");
            }
        }
    }

    /// <summary>
    /// Environment names found as overlay files directly under <paramref name="directory"/>,
    /// matching the base file's own extension (the same convention LayerResolution resolves
    /// against: "{environment}{extension}").
    /// </summary>
    private static List<string> FindOverlayNames(string directory, string relativeToDirectory)
    {
        if (!Directory.Exists(directory))
            return new List<string>();

        var extension = Path.GetExtension(relativeToDirectory);

        return Directory.EnumerateFiles(directory)
            .Where(f => string.Equals(Path.GetExtension(f), extension, StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name is not null)
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string Describe(IReadOnlyCollection<string> names) =>
        names.Count == 0 ? "(none)" : string.Join(", ", names);
}
