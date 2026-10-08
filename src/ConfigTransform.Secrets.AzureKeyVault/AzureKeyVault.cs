using System.Collections.Concurrent;
using Azure;
using Azure.Core;
using Azure.Security.KeyVault.Secrets;
using ConfigTransform.Core;

namespace ConfigTransform.Secrets.AzureKeyVault;

/// <summary>
/// <see cref="IKeyVault"/> over Azure Key Vault (docs/KEYVAULT_SECRETS_DESIGN.md), registered by the
/// CLI. Constructing it contacts nothing; each vault gets one client on first use, all sharing one
/// sign-in (<see cref="SignIn"/>). Every failure becomes a <see cref="KeyVaultUnavailableException"/>
/// worded by <see cref="KeyVaultErrors"/> — an Azure SDK exception or its message never leaves this
/// class, since either can carry a whole response body.
/// </summary>
public sealed class AzureKeyVault : IKeyVault
{
    private readonly TokenCredential _credential;
    private readonly Func<DateTimeOffset> _now;
    private readonly ConcurrentDictionary<string, SecretClient> _clients = new(StringComparer.OrdinalIgnoreCase);

    public AzureKeyVault()
        : this(SignIn.Create(), () => DateTimeOffset.UtcNow)
    {
    }

    internal AzureKeyVault(TokenCredential credential, Func<DateTimeOffset> now)
    {
        _credential = credential;
        _now = now;
    }

    public IReadOnlyList<KeyVaultSecretInfo> ListSecrets(string vault) =>
        Call(() => Client(vault).GetPropertiesOfSecrets().Select(Info).ToList());

    public KeyVaultSecretInfo? GetSecretInfo(string vault, string secret) =>
        Call(() =>
        {
            try
            {
                // Metadata only: listing a secret's versions needs no permission to read its value.
                // The current version -- the one a read without a version returns -- is the newest.
                var current = Client(vault).GetPropertiesOfSecretVersions(secret).MaxBy(version => version.CreatedOn);
                return current is null ? null : Info(current);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        });

    public KeyVaultSecretValue GetSecretValue(string vault, string secret) =>
        Call(() =>
        {
            var value = Client(vault).GetSecret(secret).Value;
            return new KeyVaultSecretValue(value.Value, value.Properties.ContentType);
        });

    internal KeyVaultSecretInfo Info(SecretProperties properties)
    {
        var now = _now();
        var state =
            properties.Enabled == false ? KeyVaultSecretState.Disabled
            : properties.ExpiresOn <= now ? KeyVaultSecretState.Expired
            : properties.NotBefore > now ? KeyVaultSecretState.NotYetValid
            : KeyVaultSecretState.Enabled;
        return new KeyVaultSecretInfo(properties.Name, state);
    }

    /// <summary>Runs one call; any failure becomes our own wording, with nothing of the original attached.</summary>
    private static T Call<T>(Func<T> call)
    {
        try
        {
            return call();
        }
        catch (Exception ex) when (ex is not KeyVaultUnavailableException)
        {
            throw new KeyVaultUnavailableException(KeyVaultErrors.Describe(ex));
        }
    }

    // The vault name was validated by Core (letters, digits, '-'), so it's safe in a host name -- and
    // the host is always *.vault.azure.net: tokens never go anywhere a layer file could choose.
    private SecretClient Client(string vault) =>
        _clients.GetOrAdd(vault, name => new SecretClient(new Uri($"https://{name}.vault.azure.net/"), _credential, Options()));

    private static SecretClientOptions Options()
    {
        var options = new SecretClientOptions();
        options.Retry.MaxRetries = 2;
        options.Retry.Delay = TimeSpan.FromMilliseconds(500);
        options.Diagnostics.ApplicationId = "configtransform";
        return options;
    }
}
