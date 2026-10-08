using ConfigTransform.Core.Tests.TestSupport;
using Xunit;

namespace ConfigTransform.Core.Tests;

/// <summary>
/// What <see cref="LayerManifestLoader"/> accepts and rejects for Key Vault entries
/// (docs/KEYVAULT_SECRETS_DESIGN.md): every form of <c>secrets</c> entry, <c>as</c>, and a
/// <c>keyvault://</c> <c>replace</c> — and that a layer written back keeps each entry's form.
/// </summary>
public class KeyVaultLayerManifestLoaderTests
{
    [Fact]
    public void Loads_every_form_of_secrets_entry_and_a_vault_replace()
    {
        using var dir = new TempDirectory();
        var path = Write(dir, """
            {
              "secrets": [
                "keyvault://kv-ra-prod-ca",
                "keyvault://kv-ra-prod-ca/CFSECRET-SMTP-PASSWORD",
                "keyvault://kv-ra-prod-ca/notifications-secrets",
                { "from": "keyvault://kv-ra-prod-ca/legacy-api-key", "as": "CFSECRET_LEGACY_API_KEY" },
                ".configtransform/Clients/CA/Production/legacy.secret.env"
              ],
              "resources": [
                { "path": "app/firebase.json", "replace": "keyvault://kv-ra-prod-ca/firebase-service-account" }
              ]
            }
            """);

        var manifest = LayerManifestLoader.Load(path);

        Assert.Equal(
            [
                new SecretsEntry("keyvault://kv-ra-prod-ca"),
                new SecretsEntry("keyvault://kv-ra-prod-ca/CFSECRET-SMTP-PASSWORD"),
                new SecretsEntry("keyvault://kv-ra-prod-ca/notifications-secrets"),
                new SecretsEntry("keyvault://kv-ra-prod-ca/legacy-api-key", "CFSECRET_LEGACY_API_KEY"),
                new SecretsEntry(".configtransform/Clients/CA/Production/legacy.secret.env"),
            ],
            manifest.Secrets);
        Assert.Equal("keyvault://kv-ra-prod-ca/firebase-service-account", manifest.Resources[0].Replace);
    }

    [Theory]
    [InlineData("""{ "from": "keyvault://kv-ra-prod-ca", "as": "CFSECRET_X" }""", "a whole vault")]
    [InlineData("""{ "from": ".configtransform/E/x.secret.env", "as": "CFSECRET_X" }""", "only applies to a single Key Vault secret")]
    [InlineData("""{ "from": "keyvault://kv-ra-prod-ca/legacy", "as": "LEGACY_KEY" }""", "must be a placeholder name")]
    [InlineData("""{ "from": "keyvault://kv-ra-prod-ca/legacy", "as": "CFSECRET_Legacy" }""", "capital letters")]
    [InlineData("\"keyvault://kv\"", "isn't a valid Key Vault name")]
    [InlineData("\"keyvault://kv-ra-prod-ca/x/y\"", "never a URL")]
    [InlineData("\"https://kv-ra-prod-ca.vault.azure.net/\"", "never a URL")]
    public void Rejects_a_malformed_vault_entry_saying_why(string entry, string expected)
    {
        using var dir = new TempDirectory();
        var path = Write(dir, $$"""{ "secrets": [ {{entry}} ], "resources": [] }""");

        var ex = Assert.Throws<InvalidOperationException>(() => LayerManifestLoader.Load(path));

        Assert.Contains(expected, ex.Message);
        Assert.Contains("Try:", ex.Message);
    }

    [Fact]
    public void An_unknown_field_in_an_object_entry_is_reported_like_any_other_unknown_field()
    {
        using var dir = new TempDirectory();
        var path = Write(dir, """{ "secrets": [ { "from": "keyvault://kv-ra-prod-ca/x", "format": "env" } ], "resources": [] }""");

        var ex = Assert.Throws<InvalidOperationException>(() => LayerManifestLoader.Load(path));

        Assert.Contains("doesn't recognize", ex.Message);
        Assert.Contains("'format'", ex.Message);
    }

    [Fact]
    public void A_vault_replace_skips_the_file_naming_rules_but_must_name_one_secret()
    {
        using var dir = new TempDirectory();
        var named = Write(dir, """{ "resources": [ { "path": "app/firebase.json", "replace": "keyvault://kv-ra-prod-ca/firebase" } ] }""");
        Assert.Equal("keyvault://kv-ra-prod-ca/firebase", LayerManifestLoader.Load(named).Resources[0].Replace);

        var wholeVault = Write(dir, """{ "resources": [ { "path": "app/firebase.json", "replace": "keyvault://kv-ra-prod-ca" } ] }""");
        var ex = Assert.Throws<InvalidOperationException>(() => LayerManifestLoader.Load(wholeVault));
        Assert.Contains("names one secret", ex.Message);
    }

    [Fact]
    public void Writing_a_layer_back_keeps_strings_as_strings_and_objects_as_objects()
    {
        // set and init rewrite configtransform.json; an "as" entry must survive that.
        var manifest = new LayerManifest(null, [],
        [
            new SecretsEntry("keyvault://kv-ra-prod-ca"),
            new SecretsEntry("keyvault://kv-ra-prod-ca/legacy-api-key", "CFSECRET_LEGACY_API_KEY"),
        ]);

        var json = System.Text.Json.JsonSerializer.Serialize(manifest, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });

        Assert.Contains("\"keyvault://kv-ra-prod-ca\",", json);
        Assert.Contains("\"from\": \"keyvault://kv-ra-prod-ca/legacy-api-key\"", json);
        Assert.Contains("\"as\": \"CFSECRET_LEGACY_API_KEY\"", json);

        using var dir = new TempDirectory();
        Assert.Equal(manifest.Secrets, LayerManifestLoader.Load(Write(dir, json)).Secrets);
    }

    private static string Write(TempDirectory dir, string json)
    {
        var path = Path.Combine(dir.Path, $"{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }
}
