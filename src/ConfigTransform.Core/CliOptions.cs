namespace ConfigTransform.Core;

/// <summary>Parsed CLI arguments, shared shape for both ConfigTransform.Xml and ConfigTransform.Json — see docs/USAGE.md.</summary>
public sealed record CliOptions(
    string ManifestPath,
    string? File,
    string? Client,
    string? Environment,
    string? Output,
    bool DryRun,
    bool Diff,
    bool List);
