using System.Text.RegularExpressions;

namespace ConfigTransform.Core;

/// <summary>What a Key Vault <c>secrets</c> entry supplies (docs/KEYVAULT_SECRETS_DESIGN.md, "How a layer uses Key Vault").</summary>
public enum KeyVaultSourceKind
{
    /// <summary><c>keyvault://kv</c>: every <c>CFSECRET-…</c> secret in the vault, one value each.</summary>
    WholeVault,

    /// <summary><c>keyvault://kv/CFSECRET-X</c>, or any name with <c>as</c>: that one secret, one value.</summary>
    SingleSecret,

    /// <summary><c>keyvault://kv/any-other-name</c>: that one secret's text, read as a <c>.env</c> file.</summary>
    EnvText,
}

/// <summary>
/// A parsed <c>keyvault://&lt;vault&gt;[/&lt;secret&gt;]</c> reference (docs/KEYVAULT_SECRETS_DESIGN.md).
/// Only ever a vault name — never a URL: the tool sends Azure access tokens to the vault, so it
/// always builds the host itself (<c>https://&lt;vault&gt;.vault.azure.net/</c>), and a layer file,
/// which anyone opening a pull request can edit, can't point a token anywhere else.
/// </summary>
public sealed record KeyVaultReference(string Vault, string? Secret)
{
    public const string Scheme = "keyvault://";

    /// <summary>The prefix a vault secret's name carries when it holds one value (<c>_</c> in a placeholder's name is <c>-</c> in a vault's).</summary>
    public const string SecretPrefix = "CFSECRET-";

    // Azure's own rules: a vault name is 3-24 letters, digits and '-', starting with a letter, ending
    // with a letter or digit, with no '--'; a secret name is 1-127 letters, digits and '-'.
    private static readonly Regex VaultName = new("^[A-Za-z](?!.*--)[A-Za-z0-9-]{1,22}[A-Za-z0-9]$", RegexOptions.Compiled);
    private static readonly Regex SecretName = new("^[A-Za-z0-9-]{1,127}$", RegexOptions.Compiled);

    public static bool IsKeyVault(string text) => text.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase);

    /// <exception cref="FormatException">Not a valid reference; the message says why, without guessing intent.</exception>
    public static KeyVaultReference Parse(string text)
    {
        if (!IsKeyVault(text))
            throw new FormatException($"'{text}' doesn't start with '{Scheme}'.");

        var rest = text[Scheme.Length..];
        var parts = rest.Split('/');
        if (parts.Length > 2 || (parts.Length == 2 && parts[1].Length == 0))
            throw new FormatException(
                $"'{text}' isn't '{Scheme}<vault>' or '{Scheme}<vault>/<secret>' -- a vault name, optionally one secret's name, and nothing else (never a URL).");

        if (!VaultName.IsMatch(parts[0]))
            throw new FormatException(
                $"'{text}' names the vault '{parts[0]}', which isn't a valid Key Vault name (3-24 letters, digits and '-', " +
                "starting with a letter, ending with a letter or digit, no '--').");

        var secret = parts.Length == 2 ? parts[1] : null;
        if (secret is not null && !SecretName.IsMatch(secret))
            throw new FormatException(
                $"'{text}' names the secret '{secret}', which isn't a valid Key Vault secret name (1-127 letters, digits and '-').");

        return new KeyVaultReference(parts[0], secret);
    }

    /// <summary>
    /// What this reference supplies, given the entry's <c>as</c>: a named secret's own name decides
    /// between one value (<c>CFSECRET-…</c>) and <c>.env</c> text (any other name) — known from the layer
    /// file alone, before contacting Azure.
    /// </summary>
    public KeyVaultSourceKind KindWith(string? @as) =>
        Secret is null ? KeyVaultSourceKind.WholeVault
        : @as is not null || Secret.StartsWith(SecretPrefix, StringComparison.OrdinalIgnoreCase) ? KeyVaultSourceKind.SingleSecret
        : KeyVaultSourceKind.EnvText;

    /// <summary>
    /// Whether a vault secret named <paramref name="secretName"/> holds the placeholder
    /// <paramref name="placeholderName"/>: the same name with each <c>_</c> written as <c>-</c> (Key
    /// Vault allows no <c>_</c>), compared case-insensitively, as Key Vault itself compares names.
    /// </summary>
    public static bool NamesMatch(string placeholderName, string secretName) =>
        string.Equals(placeholderName.Replace('_', '-'), secretName, StringComparison.OrdinalIgnoreCase);

    public override string ToString() => Secret is null ? $"{Scheme}{Vault}" : $"{Scheme}{Vault}/{Secret}";
}
