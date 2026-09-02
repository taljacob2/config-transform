namespace ConfigTransform.Core;

/// <summary>
/// Parsed CLI arguments, shared shape for both ConfigTransform.Xml and ConfigTransform.Json —
/// see docs/USAGE.md. <see cref="ManifestPath"/> is null when --manifest/-m was omitted; it's
/// left to <see cref="CliRunner"/> to resolve that via <see cref="ManifestDiscovery"/>, since
/// discovery needs a working directory this pure argument parser doesn't have.
/// </summary>
public sealed record CliOptions(
    string? ManifestPath,
    string? File,
    string? Client,
    string? Environment,
    string? Output,
    bool DryRun,
    bool Diff,
    bool List);
