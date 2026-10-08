namespace ConfigTransform.Core;

/// <summary>
/// Reads Azure Key Vault for a layer's <c>keyvault://</c> sources (docs/KEYVAULT_SECRETS_DESIGN.md).
/// Implemented by <c>ConfigTransform.Secrets.AzureKeyVault</c> and registered by the CLI, the same way
/// it registers the format engines — Core itself has no Azure dependency. Every method either
/// returns or throws <see cref="KeyVaultUnavailableException"/>, whose message the implementation
/// composes itself and which never contains a value or an Azure SDK message.
/// </summary>
public interface IKeyVault
{
    /// <summary>Every secret in the vault: names and state only, never a value.</summary>
    /// <exception cref="KeyVaultUnavailableException">The vault can't be listed.</exception>
    IReadOnlyList<KeyVaultSecretInfo> ListSecrets(string vault);

    /// <summary>One secret's metadata (its current version), or null when the vault has no secret by that name. Never reads the value.</summary>
    /// <exception cref="KeyVaultUnavailableException">The secret's metadata can't be read.</exception>
    KeyVaultSecretInfo? GetSecretInfo(string vault, string secret);

    /// <summary>One secret's current value.</summary>
    /// <exception cref="KeyVaultUnavailableException">The value can't be read.</exception>
    KeyVaultSecretValue GetSecretValue(string vault, string secret);
}

public enum KeyVaultSecretState
{
    Enabled,
    Disabled,
    Expired,
    NotYetValid,
}

public sealed record KeyVaultSecretInfo(string Name, KeyVaultSecretState State);

/// <summary>A secret's value. Deliberately not a record: a record's generated ToString would print the value if anything ever formatted one.</summary>
public sealed class KeyVaultSecretValue(string value, string? contentType)
{
    public string Value { get; } = value;

    /// <summary>The secret's content type; <c>application/x-pkcs12</c> means a certificate whose value is base64.</summary>
    public string? ContentType { get; } = contentType;

    public override string ToString() => $"(secret value not shown, content type {ContentType ?? "none"})";
}

/// <summary>
/// A vault, or a secret in it, can't be read. <see cref="Exception.Message"/> is a phrase completing
/// "keyvault://… <i>message</i>" — e.g. <c>can't be read (403 ForbiddenByRbac: no access)</c> — and
/// is composed by the implementation from status codes alone, so it's safe to print.
/// </summary>
public sealed class KeyVaultUnavailableException(string problem) : Exception(problem);
