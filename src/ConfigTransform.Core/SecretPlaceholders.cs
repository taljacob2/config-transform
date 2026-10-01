using System.Text.RegularExpressions;

namespace ConfigTransform.Core;

/// <summary>
/// The <c>{{CFSECRET_NAME}}</c> placeholder syntax (docs/SECRETS_DESIGN.md): <c>{{</c>, then a name
/// that starts with the uppercase <c>CFSECRET_</c> prefix followed by one or more of
/// <c>[A-Za-z0-9_]</c>, then <c>}}</c>. The name — prefix included — is the same string used as the
/// <c>*.secret.env</c> key and the environment-variable override. Format engines call
/// <see cref="Replace"/> on individual values (never on whole output text, which would break
/// escaping); finding which names a file uses works on the text.
/// </summary>
public static class SecretPlaceholders
{
    public const string Prefix = "CFSECRET_";

    /// <summary>What every placeholder starts with — anything containing it that isn't a well-formed placeholder is malformed.</summary>
    public const string Marker = "{{" + Prefix;

    private static readonly Regex Pattern = new(@"\{\{(CFSECRET_[A-Za-z0-9_]+)\}\}", RegexOptions.Compiled);

    /// <summary>Every distinct placeholder name in <paramref name="text"/>, in order of first appearance.</summary>
    public static IReadOnlyList<string> Names(string text) =>
        Pattern.Matches(text).Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).ToList();

    public static bool ContainsMarker(string text) => text.Contains(Marker, StringComparison.Ordinal);

    /// <summary>
    /// Substitutes every placeholder in one value. <paramref name="resolve"/> returns the secret's
    /// value, or null to leave that placeholder as written (a preview, or an unresolved name).
    /// A single pass: a substituted value is never scanned again.
    /// </summary>
    public static string Replace(string value, Func<string, string?> resolve) =>
        Pattern.Replace(value, m => resolve(m.Groups[1].Value) ?? m.Value);
}
