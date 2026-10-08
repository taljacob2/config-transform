using System.Collections.Concurrent;
using Azure.Core;
using Azure.Identity;

namespace ConfigTransform.Secrets.AzureKeyVault;

/// <summary>
/// How the tool signs in to Azure (docs/KEYVAULT_SECRETS_DESIGN.md, "Signing in"): never
/// interactively, and never with <c>DefaultAzureCredential</c>'s long list. In order, the first that
/// works:
/// <list type="number">
/// <item>the Azure CLI's sign-in (<c>az login</c> on a machine; <c>azure/login</c> in GitHub Actions;
/// <c>az login --identity</c> on a build agent running in Azure);</item>
/// <item>a service principal or workload identity from <c>AZURE_*</c> environment variables.</item>
/// </list>
/// No managed-identity step of its own: off Azure, probing for one takes about 25 seconds (the OS's
/// connect timeout to the metadata address, which no SDK timeout shortens), so every "not signed in"
/// would take that long. <c>az login --identity</c> covers agents in Azure without the probe.
/// Tokens are only for the tenant that sign-in is for: Azure.Identity's tenant discovery would
/// otherwise request a token for whatever tenant a vault's challenge names, so a layer file naming a
/// vault elsewhere could make the tool ask for tokens there. A vault in another tenant is reported as
/// such instead (<see cref="KeyVaultErrors"/>).
/// </summary>
internal static class SignIn
{
    public static TokenCredential Create()
    {
        AppContext.SetSwitch("Azure.Identity.DisableTenantDiscovery", true);

        return new CachingTokenCredential(new ChainedTokenCredential(
            new AzureCliCredential(),
            new EnvironmentCredential(),
            new WorkloadIdentityCredential()));
    }
}

/// <summary>
/// One token per scope and tenant for the whole run. Each vault's client asks for its own token, and
/// the Azure CLI credential starts an <c>az</c> process every time it's asked — without this, every
/// vault in a chain would cost a second of <c>az</c> start-up, and a failed sign-in would be retried
/// once per vault.
/// </summary>
internal sealed class CachingTokenCredential(TokenCredential inner) : TokenCredential
{
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<string, Lazy<AccessToken>> _tokens = new();

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        var key = $"{string.Join(" ", requestContext.Scopes)}|{requestContext.TenantId}|{requestContext.Claims}";

        // A cached token close to expiring is fetched again, once.
        for (var attempt = 0; ; attempt++)
        {
            var cached = _tokens.GetOrAdd(key, _ => new Lazy<AccessToken>(() => inner.GetToken(requestContext, cancellationToken)));
            var token = cached.Value;
            if (attempt > 0 || token.ExpiresOn > DateTimeOffset.UtcNow + RefreshMargin)
                return token;
            _tokens.TryRemove(new KeyValuePair<string, Lazy<AccessToken>>(key, cached));
        }
    }

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        new(GetToken(requestContext, cancellationToken));
}
