using System.Text.Json;

namespace ConfigTransform.Core;

public static class ManifestLoader
{
    public static Manifest Load(string manifestPath)
    {
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException($"Manifest not found: '{manifestPath}'");

        var json = File.ReadAllText(manifestPath);

        Manifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<Manifest>(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Failed to parse manifest '{manifestPath}': {ex.Message}", ex);
        }

        return manifest ?? throw new InvalidOperationException($"Manifest '{manifestPath}' deserialized to null.");
    }
}
