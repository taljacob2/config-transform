using System.Text.Json.Serialization;

namespace ConfigTransform.Core;

/// <summary>
/// One per layer directory (docs/SELF_DESCRIBING_OVERLAYS_DESIGN.md) — replaces manifest.json.
/// <see cref="Extends"/> and every <see cref="ResourceEntry.Path"/>/<see cref="ResourceEntry.Patch"/>
/// are repo-root-relative, uniformly, no exceptions ("Settled decisions" #4).
/// </summary>
public sealed record LayerManifest(
    [property: JsonPropertyName("extends")] string? Extends,
    [property: JsonPropertyName("resources")] IReadOnlyList<ResourceEntry> Resources);

public sealed record ResourceEntry(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("patch")] string? Patch = null);
