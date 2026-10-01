namespace ConfigTransform.Core;

/// <summary>Signature of a format's layered merge: XmlLayerMerger.Merge / JsonLayerMerger.Merge /
/// EnvLayerMerger.Merge / YamlLayerMerger.Merge.</summary>
public delegate string LayerMerge(string basePath, IReadOnlyList<string> patchPathsInOrder);

/// <summary>Signature of a format's `set` field authoring: XmlFieldAuthor.Author / JsonFieldAuthor.Author /
/// EnvFieldAuthor.Author / YamlFieldAuthor.Author.</summary>
public delegate string FieldAuthor(
    string precedingContent,
    string? existingTargetContent,
    bool isBaseTarget,
    IReadOnlyList<MatchSpec> matches,
    IReadOnlyList<MatchSpec> setFields);

/// <summary>
/// Signature of a format's secret substitution (docs/SECRETS_DESIGN.md): replaces
/// <c>{{CFSECRET_…}}</c> placeholders inside the <i>values</i> of already-merged
/// <paramref name="content"/> — never inside keys, and never by editing the raw text, so the
/// format's own writer escapes each substituted value. <paramref name="resolve"/> returns a
/// secret's value, or null to leave that placeholder as written. Content with no placeholder must
/// come back unchanged.
/// </summary>
public delegate string SecretSubstitution(string content, Func<string, string?> resolve);

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
    FieldAuthor Author,
    SecretSubstitution? SubstituteSecrets = null)
{
    public bool Handles(string resourcePath) =>
        Extensions.Contains(Path.GetExtension(resourcePath), StringComparer.OrdinalIgnoreCase);
}
