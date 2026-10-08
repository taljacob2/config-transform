using System.Text.RegularExpressions;

namespace ConfigTransform.Core;

/// <summary>
/// The <c>{{CFSECRET_NAME}}</c> placeholder syntax (docs/SECRETS_DESIGN.md): <c>{{</c>, then a name in
/// upper snake case — the <c>CFSECRET_</c> prefix followed by one or more of <c>[A-Z0-9_]</c> — then
/// <c>}}</c>. The name — prefix included — is the same string used as the <c>*.secret.env</c> key and
/// the environment-variable override, and (with <c>-</c> for <c>_</c>) the Key Vault secret name.
/// Format engines call <see cref="Replace"/> on individual values (never on whole output text, which
/// would break escaping); finding which names a file uses works on the text.
///
/// Upper case only, because the sources disagree about case: a <c>*.secret.env</c> key matches
/// exactly, an environment variable matches exactly on Linux but not on Windows, and Key Vault ignores
/// case. A lower-case letter is therefore an error (<see cref="Names"/>) rather than a placeholder
/// that works on one machine and not in CI.
/// </summary>
public static class SecretPlaceholders
{
    public const string Prefix = "CFSECRET_";

    /// <summary>What every placeholder starts with — anything containing it that isn't a well-formed placeholder is malformed.</summary>
    public const string Marker = "{{" + Prefix;

    private static readonly Regex NamePattern = new("^CFSECRET_[A-Z0-9_]+$", RegexOptions.Compiled);

    private static readonly Regex Pattern = new(@"\{\{(CFSECRET_[A-Z0-9_]+)\}\}", RegexOptions.Compiled);

    // A placeholder in any case -- to catch {{CFSECRET_Db}} and {{cfsecret_db}} instead of leaving them as text.
    private static readonly Regex AnyCase = new(@"\{\{(CFSECRET_[A-Z0-9_]+)\}\}", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Every distinct placeholder name in <paramref name="text"/>, in order of first appearance.</summary>
    /// <param name="where">What <paramref name="text"/> is (a resource or file path), for the error message.</param>
    /// <exception cref="InvalidOperationException">A placeholder isn't in upper snake case.</exception>
    public static IReadOnlyList<string> Names(string text, string where)
    {
        if (AnyCase.Matches(text).Select(m => m.Groups[1].Value).FirstOrDefault(name => !NamePattern.IsMatch(name)) is { } invalid)
            throw new InvalidOperationException(
                $"'{where}' uses {{{{{invalid}}}}}, but a secret's name is upper snake case -- capital letters, digits and '_' -- " +
                "so that it matches the same way in a *.secret.env file, an environment variable on every OS, and Key Vault " +
                $"(docs/SECRETS_DESIGN.md).\nTry: {{{{{invalid.ToUpperInvariant()}}}}}");

        return Pattern.Matches(text).Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).ToList();
    }

    public static bool ContainsMarker(string text) => text.Contains(Marker, StringComparison.Ordinal);

    /// <summary>Whether <paramref name="name"/> is a placeholder name (what goes between <c>{{</c> and <c>}}</c>): upper snake case, with the prefix.</summary>
    public static bool IsName(string name) => NamePattern.IsMatch(name);

    /// <summary>
    /// Substitutes every placeholder in one value. <paramref name="resolve"/> returns the secret's
    /// value, or null to leave that placeholder as written (a preview, or an unresolved name).
    /// A single pass: a substituted value is never scanned again.
    /// </summary>
    public static string Replace(string value, Func<string, string?> resolve) =>
        Pattern.Replace(value, m => resolve(m.Groups[1].Value) ?? m.Value);
}
