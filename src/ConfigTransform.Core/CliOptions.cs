namespace ConfigTransform.Core;

/// <summary>
/// Parsed CLI arguments, shared shape for both ConfigTransform.Xml and ConfigTransform.Json —
/// see docs/USAGE.md. <see cref="ManifestPath"/> is null when --manifest/-m was omitted; it's
/// left to <see cref="CliRunner"/> to resolve that via <see cref="ManifestDiscovery"/>, since
/// discovery needs a working directory this pure argument parser doesn't have.
/// <see cref="Set"/> is the "set" verb (docs/FIELD_AUTHORING_DESIGN.md) — a different mode from
/// the resolve/list flow the other flags govern; when true, <see cref="Client"/>/
/// <see cref="Environment"/> are optional (they choose which file --set writes: base,
/// Environment overlay, or Client overlay) rather than required, and <see cref="Match"/>/
/// <see cref="SetFields"/> carry the raw, not-yet-parsed --match/--set argument strings (see
/// <see cref="MatchSpec"/>).
/// </summary>
public sealed record CliOptions(
    string? ManifestPath,
    string? File,
    string? Client,
    string? Environment,
    string? Output,
    bool DryRun,
    bool Diff,
    bool List,
    bool Set,
    IReadOnlyList<string> Match,
    IReadOnlyList<string> SetFields);
