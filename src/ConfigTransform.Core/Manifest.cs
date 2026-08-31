using System.Text.Json.Serialization;

namespace ConfigTransform.Core;

/// <summary>
/// Matches the manifest.json schema in CONFIG_MANAGEMENT.md §4 — one per project under
/// .configtransform/&lt;Project&gt;/manifest.json.
/// </summary>
public sealed record Manifest(
    [property: JsonPropertyName("project")] string Project,
    [property: JsonPropertyName("files")] IReadOnlyList<ManifestFileEntry> Files);

public sealed record ManifestFileEntry(
    [property: JsonPropertyName("relativeToProject")] string RelativeToProject,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("name")] string? Name = null)
{
    /// <summary>
    /// The overlay subfolder name under .configtransform/&lt;Project&gt;/ — <see cref="Name"/>
    /// when explicitly set (only needed to disambiguate two base files sharing a filename in
    /// different subdirectories of the same project), otherwise derived automatically from
    /// <see cref="RelativeToProject"/>'s own filename per CONFIG_MANAGEMENT.md §4.
    /// </summary>
    public string OverlayFolderName => Name ?? Path.GetFileName(RelativeToProject);
}
