using System.Text.Json.Serialization;

namespace ConfigTransform.Core;

/// <summary>
/// One per layer directory (docs/SELF_DESCRIBING_OVERLAYS_DESIGN.md) — replaces manifest.json.
/// <see cref="Extends"/>, every <see cref="ResourceEntry.Path"/>/<see cref="ResourceEntry.Patch"/>
/// and every <see cref="Secrets"/> entry are repo-root-relative, uniformly, no exceptions
/// ("Settled decisions" #4). <see cref="Secrets"/> lists the <c>*.secret.env</c> files and Azure Key
/// Vault sources this layer contributes (docs/SECRETS_DESIGN.md, docs/KEYVAULT_SECRETS_DESIGN.md) —
/// null when the layer has none. A resource entry's <see cref="ResourceEntry.Replace"/> names a
/// whole-file secret — a file, or a <c>keyvault://</c> secret — that replaces the resource byte for
/// byte instead of being merged — never alongside <see cref="ResourceEntry.Patch"/> in one entry.
/// </summary>
public sealed record LayerManifest(
    [property: JsonPropertyName("extends")] string? Extends,
    [property: JsonPropertyName("resources")] IReadOnlyList<ResourceEntry> Resources,
    [property: JsonPropertyName("secrets")] IReadOnlyList<SecretsEntry>? Secrets = null);

public sealed record ResourceEntry(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("patch")] string? Patch = null,
    [property: JsonPropertyName("replace")] string? Replace = null);
