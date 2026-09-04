namespace ConfigTransform.Core;

/// <summary>Signature of a format's layered merge: XmlLayerMerger.Merge / JsonLayerMerger.Merge.</summary>
public delegate string LayerMerge(string basePath, IReadOnlyList<string> patchPathsInOrder);

/// <summary>Signature of a format's `set` field authoring: XmlFieldAuthor.Author / JsonFieldAuthor.Author.</summary>
public delegate string FieldAuthor(
    string precedingContent,
    string? existingTargetContent,
    bool isBaseTarget,
    IReadOnlyList<MatchSpec> matches,
    IReadOnlyList<MatchSpec> setFields);

/// <summary>
/// One merge engine plus the file extensions it owns — what makes the otherwise format-agnostic
/// orchestration in <see cref="CliRunner"/>/<see cref="SetRunner"/> concrete for a given resource.
/// Registered by the CLI entry point (docs/SELF_DESCRIBING_OVERLAYS_DESIGN.md "Settled decisions"
/// #2/#7); Core itself never names XML or JSON.
/// </summary>
public sealed record FormatEngine(
    string DisplayName,
    IReadOnlyList<string> Extensions,
    string PatchExtension,
    LayerMerge Merge,
    FieldAuthor Author)
{
    public bool Handles(string resourcePath) =>
        Extensions.Contains(Path.GetExtension(resourcePath), StringComparer.OrdinalIgnoreCase);
}
