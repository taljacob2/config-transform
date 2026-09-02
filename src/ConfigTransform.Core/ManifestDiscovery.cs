namespace ConfigTransform.Core;

/// <summary>
/// Infers --manifest when it's omitted: if <paramref name="workingDirectory"/> contains a
/// ".configtransform" folder holding exactly one "*/manifest.json" (the layout
/// docs/GETTING_STARTED.md sets up — ".configtransform/&lt;ProjectName&gt;/manifest.json"), that's
/// the one the user meant. Zero or more than one candidate is left to the user to resolve
/// explicitly with --manifest/-m — this never guesses between candidates, the same way the rest
/// of the tool never guesses between an ambiguous multi-file manifest without --file.
/// </summary>
public static class ManifestDiscovery
{
    public static string Discover(string workingDirectory)
    {
        var root = Path.Combine(workingDirectory, ".configtransform");

        if (!Directory.Exists(root))
            throw new ArgumentException(
                "--manifest/-m was not given and no '.configtransform' directory was found in " +
                $"'{workingDirectory}'. Pass --manifest/-m explicitly, or run from the directory " +
                "that contains '.configtransform'.");

        var candidates = Directory.GetDirectories(root)
            .Select(dir => Path.Combine(dir, "manifest.json"))
            .Where(File.Exists)
            .Select(Path.GetFullPath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (candidates.Count == 0)
            throw new ArgumentException(
                $"--manifest/-m was not given and no '.configtransform/*/manifest.json' was found " +
                $"under '{root}'. Pass --manifest/-m explicitly.");

        if (candidates.Count > 1)
            throw new ArgumentException(
                "--manifest/-m was not given and more than one manifest was found under " +
                $"'{root}': {string.Join(", ", candidates)}. Pass --manifest/-m explicitly to pick one.");

        return candidates[0];
    }
}
