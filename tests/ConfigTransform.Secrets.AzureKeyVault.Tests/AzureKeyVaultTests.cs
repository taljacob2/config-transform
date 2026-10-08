using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using ConfigTransform.Core;
using Xunit;

namespace ConfigTransform.Secrets.AzureKeyVault.Tests;

/// <summary>A secret's state from its properties, and the run-wide token cache — the parts of the Azure side that need no network.</summary>
public class AzureKeyVaultTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    public static TheoryData<bool?, DateTimeOffset?, DateTimeOffset?, KeyVaultSecretState> States => new()
    {
        { true, null, null, KeyVaultSecretState.Enabled },
        { null, null, null, KeyVaultSecretState.Enabled },
        { false, null, null, KeyVaultSecretState.Disabled },
        { true, Now.AddDays(-1), null, KeyVaultSecretState.Expired },
        { true, null, Now.AddDays(1), KeyVaultSecretState.NotYetValid },
        { true, Now.AddDays(1), Now.AddDays(-1), KeyVaultSecretState.Enabled },
        { false, Now.AddDays(-1), null, KeyVaultSecretState.Disabled }, // disabled wins: it's what to fix first
    };

    [Theory]
    [MemberData(nameof(States))]
    public void A_secret_counts_only_when_enabled_and_within_its_validity_dates(
        bool? enabled, DateTimeOffset? expiresOn, DateTimeOffset? notBefore, KeyVaultSecretState expected)
    {
        var vault = new AzureKeyVault(new CountingCredential(), () => Now);
        var properties = new SecretProperties("CFSECRET-DB") { Enabled = enabled, ExpiresOn = expiresOn, NotBefore = notBefore };

        Assert.Equal(new KeyVaultSecretInfo("CFSECRET-DB", expected), vault.Info(properties));
    }

    [Fact]
    public void The_token_cache_asks_once_per_run_and_again_only_near_expiry()
    {
        var inner = new CountingCredential { ExpiresIn = TimeSpan.FromHours(1) };
        var cache = new CachingTokenCredential(inner);
        var context = new TokenRequestContext(["https://vault.azure.net/.default"]);

        cache.GetToken(context, default);
        cache.GetToken(context, default);
        Assert.Equal(1, inner.Calls);

        var nearExpiry = new CountingCredential { ExpiresIn = TimeSpan.FromMinutes(1) };
        var nearExpiryCache = new CachingTokenCredential(nearExpiry);
        nearExpiryCache.GetToken(context, default);
        nearExpiryCache.GetToken(context, default);
        Assert.Equal(3, nearExpiry.Calls); // the second call found it near expiry and fetched once more -- never looping
    }

    [Fact]
    public void A_failed_sign_in_is_not_retried_for_every_vault()
    {
        var inner = new CountingCredential { Fail = true };
        var cache = new CachingTokenCredential(inner);
        var context = new TokenRequestContext(["https://vault.azure.net/.default"]);

        Assert.Throws<CredentialUnavailableException>(() => cache.GetToken(context, default));
        Assert.Throws<CredentialUnavailableException>(() => cache.GetToken(context, default));
        Assert.Equal(1, inner.Calls);
    }

    private sealed class CountingCredential : TokenCredential
    {
        public int Calls { get; private set; }
        public TimeSpan ExpiresIn { get; init; } = TimeSpan.FromHours(1);
        public bool Fail { get; init; }

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            Calls++;
            if (Fail)
                throw new CredentialUnavailableException("not signed in");
            return new AccessToken("token", DateTimeOffset.UtcNow + ExpiresIn);
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new(GetToken(requestContext, cancellationToken));
    }
}
