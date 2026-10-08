using ConfigTransform.Cli.Tests.TestSupport;
using ConfigTransform.Core;
using Xunit;

namespace ConfigTransform.Cli.Tests;

/// <summary>
/// Azure Key Vault sources end to end through <see cref="CliRunner"/>, against
/// <see cref="FakeKeyVault"/> (docs/KEYVAULT_SECRETS_DESIGN.md): every form of entry, every state the
/// report shows, precedence with files and environment variables, a vault <c>replace</c>, and the
/// rules about what the tool must never do — print a value, read a value for a preview, or contact a
/// vault when nothing needs it. The real Azure calls are checked by docs/KEYVAULT_VERIFICATION.md.
/// </summary>
public class KeyVaultCliTests
{
    private const string Env = ".configtransform/Environments/Production";
    private const string Client = ".configtransform/Clients/CA/Production";

    private sealed class Workspace : IDisposable
    {
        private readonly TempDirectory _dir = new();
        public string Root => _dir.Path;
        public FakeKeyVault Vault { get; } = new();

        public Workspace()
        {
            Write("app/appsettings.json", """{ "Db": "{{CFSECRET_DB_PASSWORD}}", "Smtp": "{{CFSECRET_SMTP_PASSWORD}}" }""");
        }

        public Workspace Write(string relativePath, string content)
        {
            var full = Path.Combine(Root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
            return this;
        }

        /// <summary>The Environment layer, with <paramref name="secrets"/> as the inside of its "secrets" array.</summary>
        public Workspace EnvLayer(string secrets, string resources = """{ "path": "app/appsettings.json" }""") =>
            Write($"{Env}/configtransform.json", $$"""{ "secrets": [ {{secrets}} ], "resources": [ {{resources}} ] }""");

        /// <summary>The CA client layer, extending the Environment layer.</summary>
        public Workspace ClientLayer(string secrets, string resources = "") =>
            Write($"{Client}/configtransform.json",
                $$"""{ "extends": "{{Env}}/configtransform.json", "secrets": [ {{secrets}} ], "resources": [ {{resources}} ] }""");

        public (int ExitCode, string Stdout, string Stderr) Run(params string[] args) => RunWith(null, Vault, args);

        public (int ExitCode, string Stdout, string Stderr) RunWith(
            IReadOnlyDictionary<string, string>? environment, IKeyVault? keyVault, params string[] args)
        {
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var exitCode = CliRunner.Run(args, stdout, stderr, FormatEngines.All, Root,
                environmentVariables: name => environment?.GetValueOrDefault(name), keyVault: keyVault);
            return (exitCode, stdout.ToString(), stderr.ToString());
        }

        public string Out(string name) => Path.Combine(Root, "out", name);

        public void Dispose() => _dir.Dispose();
    }

    private static string Lines(params string[] lines) => string.Join(Environment.NewLine, lines);

    [Fact]
    public void A_whole_vaults_values_show_at_their_layer_and_a_preview_reads_no_value()
    {
        using var workspace = new Workspace();
        workspace.Vault.Add("kv-shared", "CFSECRET-DB-PASSWORD", "db-value-7781").Add("kv-shared", "CFSECRET-SMTP-PASSWORD", "smtp-value-7782");
        workspace.EnvLayer("\"keyvault://kv-shared\"");

        var (exitCode, stdout, _) = workspace.Run("-r", "app/appsettings.json", "-e", "Production", "--dry-run");

        Assert.Equal(0, exitCode);
        Assert.Contains(Lines(
            "      CFSECRET_DB_PASSWORD     resolved",
            "        used in: app/appsettings.json",
            "        .configtransform/Environments/Production/configtransform.json",
            "          patched in: keyvault://kv-shared/CFSECRET-DB-PASSWORD",
            "          ↓",
            "        environment variable",
            "          not patched in"), stdout);
        Assert.Contains("          patched in: keyvault://kv-shared/CFSECRET-SMTP-PASSWORD", stdout);
        Assert.Empty(workspace.Vault.ValuesRead);
        Assert.Equal(1, workspace.Vault.ListCalls);

        var (realExit, realStdout, _) = workspace.Run("-r", "app/appsettings.json", "-e", "Production", "-o", workspace.Out("appsettings.json"));

        Assert.Equal(0, realExit);
        Assert.Contains("\"Db\": \"db-value-7781\"", File.ReadAllText(workspace.Out("appsettings.json")));
        Assert.Contains("\"Smtp\": \"smtp-value-7782\"", File.ReadAllText(workspace.Out("appsettings.json")));
        Assert.DoesNotContain("value-778", stdout + realStdout);
    }

    [Fact]
    public void A_client_vault_overrides_an_environment_file_and_the_tree_shows_both()
    {
        using var workspace = new Workspace();
        workspace.Write($"{Env}/db.secret.env", "CFSECRET_DB_PASSWORD=from-file\nCFSECRET_SMTP_PASSWORD=smtp-from-file\n");
        workspace.EnvLayer($"\"{Env}/db.secret.env\"");
        workspace.Vault.Add("kv-ca", "CFSECRET-DB-PASSWORD", "from-vault");
        workspace.ClientLayer("\"keyvault://kv-ca\"");

        var (_, preview, _) = workspace.Run("-r", "app/appsettings.json", "-c", "CA", "-e", "Production", "--dry-run");
        var (exitCode, _, _) = workspace.Run("-r", "app/appsettings.json", "-c", "CA", "-e", "Production", "-o", workspace.Out("a.json"));

        Assert.Contains(Lines(
            "        .configtransform/Environments/Production/configtransform.json",
            "          patched in: .configtransform/Environments/Production/db.secret.env",
            "          ↓",
            "        .configtransform/Clients/CA/Production/configtransform.json",
            "          patched in: keyvault://kv-ca/CFSECRET-DB-PASSWORD",
            "          ↓",
            "        environment variable",
            "          not patched in"), preview);
        Assert.Equal(0, exitCode);
        Assert.Contains("\"Db\": \"from-vault\"", File.ReadAllText(workspace.Out("a.json")));
    }

    [Fact]
    public void Named_secrets_and_as_fill_only_their_own_placeholder_without_listing_the_vault()
    {
        using var workspace = new Workspace();
        workspace.Vault.Add("kv-ca", "CFSECRET-DB-PASSWORD", "db").Add("kv-ca", "legacy-smtp", "smtp");
        workspace.EnvLayer("""
            "keyvault://kv-ca/CFSECRET-DB-PASSWORD",
            { "from": "keyvault://kv-ca/legacy-smtp", "as": "CFSECRET_SMTP_PASSWORD" }
            """);

        var (_, preview, _) = workspace.Run("-r", "app/appsettings.json", "-e", "Production", "--dry-run");
        var (exitCode, _, _) = workspace.Run("-r", "app/appsettings.json", "-e", "Production", "-o", workspace.Out("a.json"));

        Assert.Contains("          patched in: keyvault://kv-ca/CFSECRET-DB-PASSWORD", preview);
        Assert.Contains("          patched in: keyvault://kv-ca/legacy-smtp", preview);
        Assert.Equal(0, workspace.Vault.ListCalls); // named secrets need no permission to list the vault
        Assert.Equal(0, exitCode);
        Assert.Contains("\"Db\": \"db\"", File.ReadAllText(workspace.Out("a.json")));
        Assert.Contains("\"Smtp\": \"smtp\"", File.ReadAllText(workspace.Out("a.json")));
    }

    [Fact]
    public void A_secret_holding_env_text_fills_every_name_in_it_and_is_never_printed()
    {
        using var workspace = new Workspace();
        workspace.Vault.Add("kv-ca", "notifications-secrets", "# shared\nCFSECRET_DB_PASSWORD=bundle-db-4410\nCFSECRET_SMTP_PASSWORD=bundle-smtp-4411\n");
        workspace.EnvLayer("\"keyvault://kv-ca/notifications-secrets\"");

        var (_, preview, _) = workspace.Run("-r", "app/appsettings.json", "-e", "Production", "--dry-run");
        var (exitCode, real, _) = workspace.Run("-r", "app/appsettings.json", "-e", "Production", "-o", workspace.Out("a.json"));

        Assert.Equal(2, CountOf(preview, "          patched in: keyvault://kv-ca/notifications-secrets"));
        Assert.DoesNotContain("bundle-", preview + real);
        Assert.Equal(0, exitCode);
        Assert.Contains("\"Db\": \"bundle-db-4410\"", File.ReadAllText(workspace.Out("a.json")));
        Assert.Contains("\"Smtp\": \"bundle-smtp-4411\"", File.ReadAllText(workspace.Out("a.json")));
    }

    [Fact]
    public void The_whole_vault_form_uses_only_prefixed_secrets_and_matches_names_in_any_case()
    {
        using var workspace = new Workspace();
        workspace.Vault.Add("kv-shared", "DB-PASSWORD", "unrelated-app").Add("kv-shared", "cfsecret-smtp-password", "smtp");
        workspace.EnvLayer("\"keyvault://kv-shared\"");

        var (_, stdout, _) = workspace.Run("-r", "app/appsettings.json", "-e", "Production", "--dry-run");

        Assert.Contains("      CFSECRET_DB_PASSWORD     MISSING", stdout);
        Assert.Contains("          patched in: keyvault://kv-shared/cfsecret-smtp-password", stdout);
    }

    [Fact]
    public void A_disabled_secret_is_shown_as_not_counting_and_why()
    {
        using var workspace = new Workspace();
        workspace.Vault.Add("kv-shared", "CFSECRET-DB-PASSWORD", "x", KeyVaultSecretState.Disabled);
        workspace.EnvLayer("\"keyvault://kv-shared\"");

        var (_, stdout, _) = workspace.Run("-r", "app/appsettings.json", "-e", "Production", "--dry-run");

        Assert.Contains("      CFSECRET_DB_PASSWORD     MISSING", stdout);
        Assert.Contains("          not patched in (keyvault://kv-shared/CFSECRET-DB-PASSWORD is disabled)", stdout);
    }

    [Fact]
    public void An_unreadable_vault_is_unknown_and_a_real_run_writes_nothing()
    {
        using var workspace = new Workspace();
        workspace.Vault.Unreadable("kv-shared", "can't be read (403 ForbiddenByRbac: no access)");
        workspace.EnvLayer("\"keyvault://kv-shared\"");

        var (_, preview, _) = workspace.Run("-r", "app/appsettings.json", "-e", "Production", "--dry-run");
        var (exitCode, _, stderr) = workspace.Run("-r", "app/appsettings.json", "-e", "Production", "-o", workspace.Out("a.json"));

        Assert.Contains(Lines(
            "      CFSECRET_DB_PASSWORD     unknown",
            "        used in: app/appsettings.json",
            "        .configtransform/Environments/Production/configtransform.json",
            "          unknown: keyvault://kv-shared can't be read (403 ForbiddenByRbac: no access)"), preview);
        Assert.Equal(1, exitCode);
        Assert.Contains("keyvault://kv-shared can't be read (403 ForbiddenByRbac: no access)", stderr);
        Assert.Contains("az login", stderr);
        Assert.False(File.Exists(workspace.Out("a.json")));
    }

    [Fact]
    public void An_unreadable_named_secret_leaves_every_other_name_certain()
    {
        using var workspace = new Workspace();
        workspace.Write($"{Env}/smtp.secret.env", "CFSECRET_SMTP_PASSWORD=smtp\n");
        workspace.Vault.Unreadable("kv-ca", "can't be read (403 ForbiddenByRbac: no access)");
        workspace.EnvLayer($"\"keyvault://kv-ca/CFSECRET-DB-PASSWORD\", \"{Env}/smtp.secret.env\"");

        var (_, stdout, _) = workspace.Run("-r", "app/appsettings.json", "-e", "Production", "--dry-run");

        Assert.Contains("      CFSECRET_DB_PASSWORD     unknown", stdout);
        Assert.Contains("      CFSECRET_SMTP_PASSWORD   resolved", stdout);
    }

    [Fact]
    public void A_named_secret_that_does_not_exist_is_an_error()
    {
        using var workspace = new Workspace();
        workspace.Vault.Add("kv-ca", "CFSECRET-OTHER", "x");
        workspace.EnvLayer("\"keyvault://kv-ca/CFSECRET-DB-PASWORD\"");

        var (exitCode, _, stderr) = workspace.Run("-r", "app/appsettings.json", "-e", "Production", "--dry-run");

        Assert.Equal(1, exitCode);
        Assert.Contains("has no secret named 'CFSECRET-DB-PASWORD'", stderr);
        Assert.Contains("Try:", stderr);
    }

    [Fact]
    public void A_single_value_named_without_the_prefix_fails_as_env_text_without_showing_any_of_it()
    {
        // No CFSECRET- prefix, so it's read as .env text -- and "Sup3rKey=Secr3t" parses as a key
        // without the prefix. Neither half may appear in the error.
        using var workspace = new Workspace();
        workspace.Vault.Add("kv-ca", "db-password", "Sup3rKey=Secr3tPart");
        workspace.EnvLayer("\"keyvault://kv-ca/db-password\"");

        var (exitCode, stdout, stderr) = workspace.Run("-r", "app/appsettings.json", "-e", "Production", "--dry-run");

        Assert.Equal(1, exitCode);
        Assert.Contains("read as .env text", stderr);
        Assert.Contains("give the entry an \"as\"", stderr);
        Assert.DoesNotContain("Sup3r", stdout + stderr);
        Assert.DoesNotContain("Secr3t", stdout + stderr);
    }

    [Fact]
    public void Two_sources_in_one_layer_giving_the_same_name_is_an_error()
    {
        using var workspace = new Workspace();
        workspace.Write($"{Env}/db.secret.env", "CFSECRET_DB_PASSWORD=file\n");
        workspace.Vault.Add("kv-shared", "CFSECRET-DB-PASSWORD", "vault");
        workspace.EnvLayer($"\"{Env}/db.secret.env\", \"keyvault://kv-shared\"");

        var (exitCode, _, stderr) = workspace.Run("-r", "app/appsettings.json", "-e", "Production", "--dry-run");

        Assert.Equal(1, exitCode);
        Assert.Contains("gets \"CFSECRET_DB_PASSWORD\" from two sources", stderr);
    }

    [Fact]
    public void Nothing_contacts_a_vault_when_no_resource_uses_a_placeholder()
    {
        using var workspace = new Workspace();
        workspace.Write("app/plain.json", """{ "A": 1 }""");
        workspace.Vault.Unreadable("kv-shared", "can't be read (not signed in to Azure -- run az login)");
        workspace.EnvLayer("\"keyvault://kv-shared\"", """{ "path": "app/plain.json" }""");

        var (exitCode, _, _) = workspace.Run("-r", "app/plain.json", "-e", "Production", "-o", workspace.Out("plain.json"));
        var (listExit, _, _) = workspace.Run("--list", "-e", "Production");

        Assert.Equal(0, exitCode);
        Assert.Equal(0, listExit);
        Assert.Equal(0, workspace.Vault.Calls);
    }

    [Fact]
    public void A_value_that_cannot_be_read_stops_a_real_run_after_a_preview_said_resolved()
    {
        using var workspace = new Workspace();
        workspace.Vault.Add("kv-shared", "CFSECRET-DB-PASSWORD", "x").Add("kv-shared", "CFSECRET-SMTP-PASSWORD", "y")
            .ValueUnreadable("kv-shared", "CFSECRET-DB-PASSWORD", "can't be read (403 ForbiddenByRbac: no access)");
        workspace.EnvLayer("\"keyvault://kv-shared\"");

        var (_, preview, _) = workspace.Run("-r", "app/appsettings.json", "-e", "Production", "--dry-run");
        var (exitCode, _, stderr) = workspace.Run("-e", "Production", "-o", workspace.Out(""));

        Assert.Contains("      CFSECRET_DB_PASSWORD     resolved", preview);
        Assert.Equal(1, exitCode);
        Assert.Contains("The value of keyvault://kv-shared/CFSECRET-DB-PASSWORD can't be read (403 ForbiddenByRbac: no access)", stderr);
        Assert.Contains("Key Vault Reader", stderr);
        Assert.False(Directory.Exists(workspace.Out("")));
    }

    [Fact]
    public void An_environment_variable_overrides_a_vault_value_which_is_then_never_read()
    {
        using var workspace = new Workspace();
        workspace.Vault.Add("kv-shared", "CFSECRET-DB-PASSWORD", "vault").Add("kv-shared", "CFSECRET-SMTP-PASSWORD", "smtp");
        workspace.EnvLayer("\"keyvault://kv-shared\"");
        var environment = new Dictionary<string, string> { ["CFSECRET_DB_PASSWORD"] = "from-ci" };

        var (_, preview, _) = workspace.RunWith(environment, workspace.Vault, "-r", "app/appsettings.json", "-e", "Production", "--dry-run");
        var (exitCode, _, _) = workspace.RunWith(environment, workspace.Vault, "-r", "app/appsettings.json", "-e", "Production", "-o", workspace.Out("a.json"));

        Assert.Contains("          patched in: $CFSECRET_DB_PASSWORD", preview);
        Assert.Equal(0, exitCode);
        Assert.Contains("\"Db\": \"from-ci\"", File.ReadAllText(workspace.Out("a.json")));
        Assert.Equal(["kv-shared/CFSECRET-SMTP-PASSWORD"], workspace.Vault.ValuesRead);
    }

    [Fact]
    public void A_vault_replace_previews_from_metadata_and_a_real_run_writes_the_exact_value()
    {
        using var workspace = new Workspace();
        workspace.Write("app/firebase.json", "{}\n");
        workspace.Vault.Add("kv-ca", "firebase-service-account", "{ \"type\": \"service_account\" }\n");
        workspace.EnvLayer("", """{ "path": "app/firebase.json", "replace": "keyvault://kv-ca/firebase-service-account" }""");

        var (_, preview, _) = workspace.Run("-r", "app/firebase.json", "-e", "Production", "--dry-run");
        var (_, revealed, _) = workspace.Run("-r", "app/firebase.json", "-e", "Production", "--dry-run", "--reveal-secrets");
        var valuesReadByPreviews = workspace.Vault.ValuesRead.Count;
        var (exitCode, real, _) = workspace.Run("-r", "app/firebase.json", "-e", "Production", "-o", workspace.Out("firebase.json"));

        Assert.Contains("      replaced by: keyvault://kv-ca/firebase-service-account", preview);
        Assert.Contains("(replaced by keyvault://kv-ca/firebase-service-account, not shown -- pass --reveal-secrets to see it)", preview);
        Assert.DoesNotContain("service_account", preview);
        Assert.Contains("\"type\": \"service_account\"", revealed);
        Assert.Equal(1, valuesReadByPreviews); // only the --reveal-secrets one
        Assert.Equal(0, exitCode);
        Assert.Contains("(replaced by keyvault://kv-ca/firebase-service-account)", real);
        Assert.Equal("{ \"type\": \"service_account\" }\n", File.ReadAllText(workspace.Out("firebase.json")));
    }

    [Fact]
    public void A_certificate_replace_is_written_as_its_real_bytes()
    {
        byte[] pfx = [0x30, 0x82, 0x00, 0xff, 0x01];
        using var workspace = new Workspace();
        workspace.Write("app/cert.pfx", "");
        workspace.Vault.Add("kv-ca", "api-cert", Convert.ToBase64String(pfx), contentType: "application/x-pkcs12");
        workspace.EnvLayer("", """{ "path": "app/cert.pfx", "replace": "keyvault://kv-ca/api-cert" }""");

        var (exitCode, _, _) = workspace.Run("-e", "Production", "-o", workspace.Out(""));

        Assert.Equal(0, exitCode);
        Assert.Equal(pfx, File.ReadAllBytes(Path.Combine(workspace.Out(""), "app", "cert.pfx")));
    }

    [Theory]
    [InlineData(KeyVaultSecretState.Disabled, "is disabled")]
    [InlineData(KeyVaultSecretState.Expired, "is expired")]
    public void A_vault_replace_that_does_not_count_is_noted_in_a_preview_and_stops_a_real_run(KeyVaultSecretState state, string expected)
    {
        using var workspace = new Workspace();
        workspace.Write("app/firebase.json", "{}\n");
        workspace.Vault.Add("kv-ca", "firebase", "{}", state);
        workspace.EnvLayer("", """{ "path": "app/firebase.json", "replace": "keyvault://kv-ca/firebase" }""");

        var (_, preview, _) = workspace.Run("-r", "app/firebase.json", "-e", "Production", "--dry-run");
        var (exitCode, _, stderr) = workspace.Run("-r", "app/firebase.json", "-e", "Production", "-o", workspace.Out("f.json"));

        Assert.Contains($"(replaced by keyvault://kv-ca/firebase, which {expected} -- a real run would fail)", preview);
        Assert.Equal(1, exitCode);
        Assert.Contains($"which {expected}, so nothing was written", stderr);
        Assert.False(File.Exists(workspace.Out("f.json")));
    }

    [Fact]
    public void List_shows_vault_steps_and_an_as_entry_in_the_header()
    {
        using var workspace = new Workspace();
        workspace.Vault.Add("kv-ca", "CFSECRET-DB-PASSWORD", "db").Add("kv-ca", "legacy-smtp", "smtp");
        workspace.EnvLayer("""
            "keyvault://kv-ca",
            { "from": "keyvault://kv-ca/legacy-smtp", "as": "CFSECRET_SMTP_PASSWORD" }
            """);

        var (exitCode, stdout, _) = workspace.Run("--list", "-e", "Production");

        Assert.Equal(0, exitCode);
        Assert.Contains(Lines(
            "  secrets:",
            "    keyvault://kv-ca",
            "    keyvault://kv-ca/legacy-smtp as CFSECRET_SMTP_PASSWORD"), stdout);
        Assert.Contains("        patched in: keyvault://kv-ca/CFSECRET-DB-PASSWORD", stdout);
        Assert.Contains("        patched in: keyvault://kv-ca/legacy-smtp", stdout);
        Assert.Empty(workspace.Vault.ValuesRead);
    }

    [Fact]
    public void Set_keeps_an_as_entry_when_it_rewrites_the_layer()
    {
        using var workspace = new Workspace();
        workspace.Write("app/settings.json", """{ "Mode": "dev" }""");
        workspace.EnvLayer("""{ "from": "keyvault://kv-ca/legacy-smtp", "as": "CFSECRET_SMTP_PASSWORD" }""", """{ "path": "app/appsettings.json" }""");

        var (exitCode, _, stderr) = workspace.Run("set", "--resource", "app/settings.json", "--environment", "Production", "--match", "Mode", "--set", "prod");

        Assert.True(exitCode == 0, stderr);
        Assert.Equal(
            [new SecretsEntry("keyvault://kv-ca/legacy-smtp", "CFSECRET_SMTP_PASSWORD")],
            LayerManifestLoader.Load(Path.Combine(workspace.Root, Env, "configtransform.json")).Secrets);
        Assert.Equal(0, workspace.Vault.Calls);
    }

    [Fact]
    public void Without_key_vault_support_a_vault_entry_is_an_error()
    {
        using var workspace = new Workspace();
        workspace.EnvLayer("\"keyvault://kv-shared\"");

        var (exitCode, _, stderr) = workspace.RunWith(null, null, "-r", "app/appsettings.json", "-e", "Production", "--dry-run");

        Assert.Equal(1, exitCode);
        Assert.Contains("no Azure Key Vault support", stderr);
    }

    private static int CountOf(string text, string value) =>
        (text.Length - text.Replace(value, "").Length) / value.Length;
}
