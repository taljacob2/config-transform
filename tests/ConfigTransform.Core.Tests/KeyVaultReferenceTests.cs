using Xunit;

namespace ConfigTransform.Core.Tests;

/// <summary>The <c>keyvault://</c> grammar (docs/KEYVAULT_SECRETS_DESIGN.md, "How a layer uses Key Vault").</summary>
public class KeyVaultReferenceTests
{
    [Theory]
    [InlineData("keyvault://kv-ra-prod-ca", "kv-ra-prod-ca", null)]
    [InlineData("keyvault://kv-ra-prod-ca/CFSECRET-SMTP-PASSWORD", "kv-ra-prod-ca", "CFSECRET-SMTP-PASSWORD")]
    [InlineData("keyvault://abc/notifications-secrets", "abc", "notifications-secrets")]
    [InlineData("KeyVault://kv1/x", "kv1", "x")]
    public void Parses_a_vault_and_an_optional_secret(string text, string vault, string? secret)
    {
        var reference = KeyVaultReference.Parse(text);

        Assert.Equal(vault, reference.Vault);
        Assert.Equal(secret, reference.Secret);
    }

    [Theory]
    [InlineData("keyvault://")]                                  // no vault
    [InlineData("keyvault://ab")]                                // too short
    [InlineData("keyvault://a234567890123456789012345")]         // 25 characters, too long
    [InlineData("keyvault://1kv")]                               // must start with a letter
    [InlineData("keyvault://kv-")]                               // must end with a letter or digit
    [InlineData("keyvault://kv--ab")]                            // no '--'
    [InlineData("keyvault://kv_ab")]                             // no '_'
    [InlineData("keyvault://kv-ab/")]                            // an empty secret name
    [InlineData("keyvault://kv-ab/a_b")]                         // no '_' in a secret name
    [InlineData("keyvault://kv-ab/x/y")]                         // nothing after the secret
    [InlineData("keyvault://kv-ab.vault.azure.net")]             // never a host
    [InlineData("keyvault://https://kv-ab.vault.azure.net/x")]   // never a URL
    public void Rejects_anything_but_a_vault_name_and_a_secret_name(string text)
    {
        Assert.Throws<FormatException>(() => KeyVaultReference.Parse(text));
    }

    [Theory]
    [InlineData("keyvault://kv1", null, KeyVaultSourceKind.WholeVault)]
    [InlineData("keyvault://kv1/CFSECRET-SMTP-PASSWORD", null, KeyVaultSourceKind.SingleSecret)]
    [InlineData("keyvault://kv1/cfsecret-smtp-password", null, KeyVaultSourceKind.SingleSecret)]
    [InlineData("keyvault://kv1/notifications-secrets", null, KeyVaultSourceKind.EnvText)]
    [InlineData("keyvault://kv1/legacy-api-key", "CFSECRET_LEGACY_API_KEY", KeyVaultSourceKind.SingleSecret)]
    public void A_named_secrets_own_name_decides_between_one_value_and_env_text(string text, string? @as, KeyVaultSourceKind expected)
    {
        Assert.Equal(expected, KeyVaultReference.Parse(text).KindWith(@as));
    }

    [Theory]
    [InlineData("CFSECRET_ADMIN_DB_CONNECTION", "CFSECRET-ADMIN-DB-CONNECTION", true)]
    [InlineData("CFSECRET_SMTP_PASSWORD", "cfsecret-smtp-password", true)] // Key Vault ignores case
    [InlineData("CFSECRET_SMTP_PASSWORD", "CFSECRET-SMTP", false)]
    [InlineData("CFSECRET_DB", "DB", false)]
    public void A_placeholder_matches_the_vault_secret_named_with_dashes_for_underscores(string placeholder, string secret, bool expected)
    {
        Assert.Equal(expected, KeyVaultReference.NamesMatch(placeholder, secret));
    }
}
