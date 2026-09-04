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
/// <see cref="Help"/> is set by no arguments at all, a leading bare <c>help</c>, or <c>--help</c>/
/// <c>-h</c> anywhere in the arguments — it always wins over every other flag (no other
/// validation runs), and is the default when the tool is invoked with nothing else to go on.
/// <see cref="Init"/> is the "init" verb (docs/INIT_COMMAND_DESIGN.md) — scaffolds a tree instead
/// of resolving one; <see cref="InitEnvironments"/>/<see cref="InitClients"/>/
/// <see cref="InitResources"/> are its own repeatable environment/client/resource lists, distinct
/// from <see cref="Client"/>/<see cref="Environment"/>/<see cref="Resource"/> (which target one
/// existing layer, not declare several new ones) even though they're parsed from the same
/// <c>--environment</c>/<c>--client</c>/<c>--resource</c> flags. <see cref="Template"/> selects
/// the one canned starter tree instead of scanning/prompting/flags, mutually exclusive with every
/// other init-specific flag.
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
    bool Help,
    IReadOnlyList<string> Match,
    IReadOnlyList<string> SetFields,
    bool Init,
    IReadOnlyList<string> InitEnvironments,
    IReadOnlyList<string> InitClients,
    IReadOnlyList<string> InitResources,
    string? ScanRoot,
    bool Yes,
    bool NoScan,
    bool Template);
