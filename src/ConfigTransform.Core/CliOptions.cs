namespace ConfigTransform.Core;

/// <summary>
/// Parsed CLI arguments, shared shape for both ConfigTransform.Xml and ConfigTransform.Json —
/// see docs/USAGE.md. <see cref="Resource"/> is the repo-root-relative path to one project's
/// base file (docs/SELF_DESCRIBING_OVERLAYS_DESIGN.md "Settled decisions" #6/#7) — replaces the
/// old <c>--manifest</c>/<c>--file</c> pair. Null means "every resource the resolved layer
/// touches" for a resolve/dry-run/diff/real-run, or "list this whole layer" for <c>--list</c>;
/// it's required for <c>set</c>, which always targets exactly one resource.
/// <see cref="Set"/> is the "set" verb (docs/FIELD_AUTHORING_DESIGN.md) — a different mode from
/// the resolve/list flow the other flags govern; when true, <see cref="Client"/>/
/// <see cref="Environment"/> are optional (they choose which layer --set writes: the base file,
/// the Environment layer, or the Client layer) rather than required, and <see cref="Match"/>/
/// <see cref="SetFields"/> carry the raw, not-yet-parsed --match/--set argument strings (see
/// <see cref="MatchSpec"/>).
/// </summary>
public sealed record CliOptions(
    string? Resource,
    string? Client,
    string? Environment,
    string? Output,
    bool DryRun,
    bool Diff,
    bool List,
    bool Set,
    IReadOnlyList<string> Match,
    IReadOnlyList<string> SetFields);
