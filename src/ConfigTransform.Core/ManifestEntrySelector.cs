namespace ConfigTransform.Core;

/// <summary>
/// Selects the manifest file entry a CLI invocation targets. When --file is omitted, the
/// manifest must have exactly one entry — an ambiguous manifest without --file is an error,
/// not a silent guess.
/// </summary>
public static class ManifestEntrySelector
{
    public static ManifestFileEntry Select(Manifest manifest, string? fileArg)
    {
        if (fileArg is null)
        {
            return manifest.Files.Count switch
            {
                0 => throw new InvalidOperationException("Manifest has no file entries."),
                1 => manifest.Files[0],
                _ => throw new ArgumentException(
                    "--file is required: the manifest has more than one file entry.")
            };
        }

        var match = manifest.Files.FirstOrDefault(f =>
            f.RelativeToProject == fileArg ||
            string.Equals(f.OverlayFolderName, fileArg, StringComparison.OrdinalIgnoreCase));

        return match ?? throw new ArgumentException($"No manifest entry matches --file '{fileArg}'.");
    }
}
