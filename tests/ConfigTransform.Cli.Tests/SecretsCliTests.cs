using ConfigTransform.Cli.Tests.TestSupport;
using ConfigTransform.Core;
using Xunit;

namespace ConfigTransform.Cli.Tests;

/// <summary>
/// Secrets end to end through <see cref="CliRunner"/> (docs/SECRETS_DESIGN.md, "What each mode
/// does"): previews keep placeholders and report status, <c>--reveal-secrets</c> substitutes, a real
/// run substitutes everything or writes nothing, and no mode ever prints a value it wasn't asked to.
/// </summary>
public class SecretsCliTests
{
    private const string DbPassword = "Pa5\"5&<x>";
    private const string EscapedDbPassword = "Pa5&quot;5&amp;&lt;x&gt;";

    private static readonly byte[] GitCryptHeader =
        [0x00, (byte)'G', (byte)'I', (byte)'T', (byte)'C', (byte)'R', (byte)'Y', (byte)'P', (byte)'T', 0x00, 0x42];

    /// <summary>
    /// Project/App.config uses CFSECRET_DB and gets Mode=prod from the Environment patch;
    /// Project/appsettings.json uses CFSECRET_API. db.secret.env always defines CFSECRET_DB;
    /// CFSECRET_API only when <paramref name="defineApi"/>.
    /// </summary>
    private sealed class Workspace : IDisposable
    {
        private readonly TempDirectory _dir = new();
        public string Root => _dir.Path;
        public string SecretsFile => Path.Combine(Root, ".configtransform", "Environments", "Production", "db.secret.env");

        public Workspace(bool defineApi = false, string xmlExtra = "")
        {
            Write("Project/App.config", $$$"""
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <appSettings>
                    <add key="Mode" value="dev" />
                  </appSettings>
                  <connectionStrings>
                    <add name="Db" connectionString="Server=x;Password={{CFSECRET_DB}}" />
                  </connectionStrings>{{{xmlExtra}}}
                </configuration>
                """);
            Write("Project/appsettings.json", """{ "ApiKey": "{{CFSECRET_API}}" }""");
            Write(".configtransform/Environments/Production/patch-Project-App.config.xml", """
                <?xml version="1.0" encoding="utf-8"?>
                <configuration xmlns:xdt="http://schemas.microsoft.com/XML-Document-Transform">
                  <appSettings>
                    <add key="Mode" value="prod" xdt:Transform="SetAttributes" xdt:Locator="Match(key)" />
                  </appSettings>
                </configuration>
                """);
            Write(".configtransform/Environments/Production/db.secret.env",
                $"CFSECRET_DB={DbPassword}\n" + (defineApi ? "CFSECRET_API=api-value\n" : ""));
            Write(".configtransform/Environments/Production/configtransform.json", """
                {
                  "secrets": [ ".configtransform/Environments/Production/db.secret.env" ],
                  "resources": [
                    { "path": "Project/App.config", "patch": ".configtransform/Environments/Production/patch-Project-App.config.xml" },
                    { "path": "Project/appsettings.json" }
                  ]
                }
                """);
        }

        public void Write(string relativePath, string content)
        {
            var full = Path.Combine(Root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }

        public (int ExitCode, string Stdout, string Stderr) Run(
            IReadOnlyDictionary<string, string>? environment = null, params string[] args)
        {
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var exitCode = CliRunner.Run(args, stdout, stderr, FormatEngines.All, Root,
                environmentVariables: name => environment?.GetValueOrDefault(name));
            return (exitCode, stdout.ToString(), stderr.ToString());
        }

        /// <summary>An absolute path under the workspace's own out/ -- a relative --output would resolve against the test process's directory.</summary>
        public string Out(string relativePath) => Path.Combine(Root, "out", relativePath);

        public void Dispose() => _dir.Dispose();
    }

    /// <summary>Expected output lines, joined the way StringWriter.WriteLine ends them.</summary>
    private static string Lines(params string[] lines) => string.Join(Environment.NewLine, lines);

    [Fact]
    public void A_dry_run_keeps_placeholders_and_shows_each_secrets_tree_after_the_chain_but_never_its_value()
    {
        using var workspace = new Workspace();

        var (exitCode, stdout, _) = workspace.Run(null, "-r", "Project/App.config", "-e", "Production", "--dry-run");

        Assert.Equal(0, exitCode);
        Assert.Contains("Password={{CFSECRET_DB}}", stdout);
        Assert.Contains(Lines(
            "      patched in: .configtransform/Environments/Production/patch-Project-App.config.xml",
            "",
            "    secrets",
            "      CFSECRET_DB   resolved",
            "        used in: Project/App.config",
            "        .configtransform/Environments/Production/configtransform.json",
            "          patched in: .configtransform/Environments/Production/db.secret.env",
            "          ↓",
            "        environment variable",
            "          not patched in",
            "",
            "<?xml"), stdout);
        Assert.DoesNotContain("Pa5", stdout);
    }

    [Fact]
    public void The_tree_shows_every_layer_that_sets_a_value_and_the_last_one_wins()
    {
        using var workspace = new Workspace();
        WriteClientLayer(workspace, secretsFile: "CFSECRET_DB=client-value");

        var (_, preview, _) = workspace.Run(null, "-r", "Project/App.config", "-c", "Acme", "-e", "Production", "--dry-run");
        var (exitCode, _, _) = workspace.Run(null, "-r", "Project/App.config", "-c", "Acme", "-e", "Production", "-o", workspace.Out("App.config"));

        Assert.Contains(Lines(
            "      CFSECRET_DB   resolved",
            "        used in: Project/App.config",
            "        .configtransform/Environments/Production/configtransform.json",
            "          patched in: .configtransform/Environments/Production/db.secret.env",
            "          ↓",
            "        .configtransform/Clients/Acme/Production/configtransform.json",
            "          patched in: .configtransform/Clients/Acme/Production/db.secret.env",
            "          ↓",
            "        environment variable",
            "          not patched in"), preview);
        Assert.DoesNotContain("client-value", preview);
        Assert.Equal(0, exitCode);
        Assert.Contains("Password=client-value", File.ReadAllText(workspace.Out("App.config")));
    }

    [Fact]
    public void Used_in_names_the_patch_that_writes_a_placeholder_and_a_layer_that_sets_nothing_says_so()
    {
        using var workspace = new Workspace();
        WriteClientLayer(workspace, secretsFile: null, appsettingsPatch: """{ "Extra": "{{CFSECRET_DB}}" }""");

        var (_, stdout, _) = workspace.Run(null, "-r", "Project/appsettings.json", "-c", "Acme", "-e", "Production", "--dry-run");

        Assert.Contains(Lines(
            "    secrets",
            "      CFSECRET_API   MISSING",
            "        used in: Project/appsettings.json",
            "        .configtransform/Environments/Production/configtransform.json",
            "          not patched in",
            "          ↓",
            "        .configtransform/Clients/Acme/Production/configtransform.json",
            "          not patched in",
            "          ↓",
            "        environment variable",
            "          not patched in",
            "",
            "      CFSECRET_DB    resolved",
            "        used in: .configtransform/Clients/Acme/Production/patch-Project-appsettings.json",
            "        .configtransform/Environments/Production/configtransform.json",
            "          patched in: .configtransform/Environments/Production/db.secret.env",
            "          ↓",
            "        .configtransform/Clients/Acme/Production/configtransform.json",
            "          not patched in",
            "          ↓",
            "        environment variable",
            "          not patched in"), stdout);
    }

    [Fact]
    public void The_every_resource_preview_shows_the_tree_under_each_resource()
    {
        using var workspace = new Workspace();

        var (exitCode, stdout, _) = workspace.Run(null, "-e", "Production", "--dry-run");

        Assert.Equal(0, exitCode);
        Assert.Contains(Lines(
            "=== Project/App.config ===",
            "secrets",
            "  CFSECRET_DB   resolved",
            "    used in: Project/App.config",
            "    .configtransform/Environments/Production/configtransform.json",
            "      patched in: .configtransform/Environments/Production/db.secret.env",
            "      ↓",
            "    environment variable",
            "      not patched in",
            "",
            "<?xml"), stdout);
        Assert.DoesNotContain("Pa5", stdout);
    }

    /// <summary>
    /// Adds .configtransform/Clients/Acme/Production, extending the Environment layer: with a
    /// db.secret.env of <paramref name="secretsFile"/> when given, and a patch for
    /// Project/appsettings.json when <paramref name="appsettingsPatch"/> is given.
    /// </summary>
    private static void WriteClientLayer(Workspace workspace, string? secretsFile, string? appsettingsPatch = null)
    {
        const string Dir = ".configtransform/Clients/Acme/Production";
        if (secretsFile is not null)
            workspace.Write($"{Dir}/db.secret.env", secretsFile);
        if (appsettingsPatch is not null)
            workspace.Write($"{Dir}/patch-Project-appsettings.json", appsettingsPatch);

        var secrets = secretsFile is null ? "" : $"\"secrets\": [ \"{Dir}/db.secret.env\" ],";
        var resources = appsettingsPatch is null ? "" : $"{{ \"path\": \"Project/appsettings.json\", \"patch\": \"{Dir}/patch-Project-appsettings.json\" }}";
        workspace.Write($"{Dir}/configtransform.json",
            $"{{ \"extends\": \".configtransform/Environments/Production/configtransform.json\", {secrets} \"resources\": [ {resources} ] }}");
    }

    [Fact]
    public void A_dry_run_reports_a_name_nothing_defines_as_missing()
    {
        using var workspace = new Workspace();

        var (exitCode, stdout, _) = workspace.Run(null, "-r", "Project/appsettings.json", "-e", "Production", "--dry-run");

        Assert.Equal(0, exitCode); // a preview with a missing secret is still a preview, not an error
        Assert.Contains(Lines(
            "      CFSECRET_API   MISSING",
            "        used in: Project/appsettings.json",
            "        .configtransform/Environments/Production/configtransform.json",
            "          not patched in",
            "          ↓",
            "        environment variable",
            "          not patched in"), stdout);
    }

    [Fact]
    public void Reveal_secrets_substitutes_real_values_into_a_dry_run()
    {
        using var workspace = new Workspace();

        var (exitCode, stdout, _) = workspace.Run(null, "-r", "Project/App.config", "-e", "Production", "--dry-run", "--reveal-secrets");

        Assert.Equal(0, exitCode);
        Assert.Contains($"Password={EscapedDbPassword}", stdout);
        Assert.DoesNotContain("{{CFSECRET_DB}}", stdout);
    }

    [Fact]
    public void A_diff_never_shows_a_value_and_a_revealed_diff_never_shows_a_placeholder_turning_into_one()
    {
        using var workspace = new Workspace();

        var (_, plain, _) = workspace.Run(null, "-r", "Project/App.config", "-e", "Production", "--diff");
        var (_, revealed, _) = workspace.Run(null, "-r", "Project/App.config", "-e", "Production", "--diff", "--reveal-secrets");

        Assert.DoesNotContain("Pa5", plain);
        // Every side resolves with the full chain, so only the real change (Mode) is a changed line.
        Assert.Contains("+    <add key=\"Mode\" value=\"prod\" />", revealed);
        Assert.DoesNotContain("-    <add name=\"Db\"", revealed);
        Assert.DoesNotContain("+    <add name=\"Db\"", revealed);
    }

    [Fact]
    public void A_real_run_writes_every_value_escaped_by_the_format_and_prints_none()
    {
        using var workspace = new Workspace(defineApi: true);

        var (exitCode, stdout, stderr) = workspace.Run(null, "-e", "Production", "-o", workspace.Out(""));

        Assert.Equal(0, exitCode);
        Assert.Empty(stderr);
        Assert.DoesNotContain("Pa5", stdout);
        Assert.Contains($"Password={EscapedDbPassword}", File.ReadAllText(Path.Combine(workspace.Root, "out", "Project", "App.config")));
        Assert.Contains("\"ApiKey\": \"api-value\"", File.ReadAllText(Path.Combine(workspace.Root, "out", "Project", "appsettings.json")));
    }

    [Fact]
    public void A_real_run_with_any_unresolved_secret_writes_nothing_at_all()
    {
        // App.config's secret resolves, appsettings.json's doesn't -- neither file may be written.
        using var workspace = new Workspace(defineApi: false);

        var (exitCode, _, stderr) = workspace.Run(null, "-e", "Production", "-o", workspace.Out(""));

        Assert.Equal(1, exitCode);
        Assert.Contains("CFSECRET_API", stderr);
        Assert.Contains("MISSING", stderr);
        Assert.Contains("nothing was written", stderr);
        Assert.False(Directory.Exists(Path.Combine(workspace.Root, "out")));
    }

    [Fact]
    public void A_locked_secrets_file_is_unknown_in_a_preview_and_an_error_on_a_real_run()
    {
        using var workspace = new Workspace(defineApi: true);
        File.WriteAllBytes(workspace.SecretsFile, GitCryptHeader);

        var (_, preview, _) = workspace.Run(null, "-r", "Project/App.config", "-e", "Production", "--dry-run");
        var (exitCode, _, stderr) = workspace.Run(null, "-r", "Project/App.config", "-e", "Production", "-o", workspace.Out("App.config"));

        Assert.Contains(Lines(
            "      CFSECRET_DB   unknown",
            "        used in: Project/App.config",
            "        .configtransform/Environments/Production/configtransform.json",
            "          unknown: .configtransform/Environments/Production/db.secret.env is locked (run git-crypt unlock)",
            "          ↓",
            "        environment variable",
            "          not patched in"), preview);
        Assert.Equal(1, exitCode);
        Assert.Contains("git-crypt unlock", stderr);
        Assert.False(File.Exists(Path.Combine(workspace.Root, "out", "App.config")));
    }

    [Fact]
    public void An_environment_variable_of_the_same_name_overrides_the_file_and_the_report_says_so()
    {
        using var workspace = new Workspace();
        var environment = new Dictionary<string, string> { ["CFSECRET_API"] = "from-ci" };

        var (_, preview, _) = workspace.Run(environment, "-r", "Project/appsettings.json", "-e", "Production", "--dry-run");
        var (exitCode, _, _) = workspace.Run(environment, "-r", "Project/appsettings.json", "-e", "Production", "-o", workspace.Out("appsettings.json"));

        Assert.Contains(Lines(
            "      CFSECRET_API   resolved",
            "        used in: Project/appsettings.json",
            "        .configtransform/Environments/Production/configtransform.json",
            "          not patched in",
            "          ↓",
            "        environment variable",
            "          patched in: $CFSECRET_API"), preview);
        Assert.DoesNotContain("from-ci", preview);
        Assert.Equal(0, exitCode);
        Assert.Contains("from-ci", File.ReadAllText(Path.Combine(workspace.Root, "out", "appsettings.json")));
    }

    [Fact]
    public void A_placeholder_that_is_not_upper_snake_case_fails_a_preview_and_a_real_run_naming_the_fix()
    {
        // Lower case would match a *.secret.env key and a Linux environment variable differently from
        // Key Vault and Windows -- so it's an error in every mode, never a silent mismatch.
        using var workspace = new Workspace(xmlExtra: "<extra value=\"{{CFSECRET_Db}}\" />");

        var (previewExit, _, previewErr) = workspace.Run(null, "-r", "Project/App.config", "-e", "Production", "--dry-run");
        var (realExit, _, realErr) = workspace.Run(null, "-r", "Project/App.config", "-e", "Production", "-o", workspace.Out("App.config"));

        Assert.Equal(1, previewExit);
        Assert.Contains("'Project/App.config' uses {{CFSECRET_Db}}", previewErr);
        Assert.Contains("Try: {{CFSECRET_DB}}", previewErr);
        Assert.Equal(1, realExit);
        Assert.Contains("upper snake case", realErr);
        Assert.False(File.Exists(workspace.Out("App.config")));
    }

    [Fact]
    public void A_placeholder_left_outside_a_value_fails_a_real_run()
    {
        // XML comments aren't substituted -- a placeholder there must not reach a deployed file.
        using var workspace = new Workspace(xmlExtra: "\n  <!-- {{CFSECRET_DB}} -->");

        var (exitCode, _, stderr) = workspace.Run(null, "-r", "Project/App.config", "-e", "Production", "-o", workspace.Out("App.config"));

        Assert.Equal(1, exitCode);
        Assert.Contains("still contains '{{CFSECRET_'", stderr);
        Assert.False(File.Exists(Path.Combine(workspace.Root, "out", "App.config")));
    }

    [Fact]
    public void An_empty_environment_variable_is_shown_as_not_setting_the_value()
    {
        using var workspace = new Workspace();
        var environment = new Dictionary<string, string> { ["CFSECRET_API"] = "" };

        var (_, preview, _) = workspace.Run(environment, "-r", "Project/appsettings.json", "-e", "Production", "--dry-run");

        Assert.Contains("      CFSECRET_API   MISSING", preview);
        Assert.Contains("          not patched in ($CFSECRET_API is set but empty, which counts as unset)", preview);
    }

    [Fact]
    public void List_ends_with_each_secrets_tree_after_every_resources_chain_but_never_a_value()
    {
        using var workspace = new Workspace();

        var (exitCode, stdout, _) = workspace.Run(null, "--list", "-e", "Production");

        Assert.Equal(0, exitCode);
        // The Environment layer is the target and lists the file itself, so its header shows it.
        Assert.Contains(Lines(
            "  secrets:",
            "    .configtransform/Environments/Production/db.secret.env"), stdout);
        Assert.EndsWith(Lines(
            "  secrets",
            "    CFSECRET_DB    resolved",
            "      used in: Project/App.config",
            "      .configtransform/Environments/Production/configtransform.json",
            "        patched in: .configtransform/Environments/Production/db.secret.env",
            "        ↓",
            "      environment variable",
            "        not patched in",
            "",
            "    CFSECRET_API   MISSING",
            "      used in: Project/appsettings.json",
            "      .configtransform/Environments/Production/configtransform.json",
            "        not patched in",
            "        ↓",
            "      environment variable",
            "        not patched in",
            ""), stdout);
        Assert.DoesNotContain("Pa5", stdout);
    }

    [Fact]
    public void List_shows_a_secret_once_however_many_resources_use_it_and_only_the_targets_own_secrets_in_its_header()
    {
        using var workspace = new Workspace();
        workspace.Write("Project/appsettings.json", """{ "ApiKey": "{{CFSECRET_DB}}" }""");
        WriteClientLayer(workspace, secretsFile: null, appsettingsPatch: """{ "Mode": "client" }""");

        var (exitCode, stdout, _) = workspace.Run(null, "--list", "-c", "Acme", "-e", "Production");

        Assert.Equal(0, exitCode);
        // The Client layer lists no secrets itself; the Environment layer's file shows in the tree,
        // at the layer that lists it.
        Assert.DoesNotContain("  secrets:", stdout);
        Assert.Contains(Lines(
            "    CFSECRET_DB   resolved",
            "      used in: Project/App.config",
            "      used in: Project/appsettings.json",
            "      .configtransform/Environments/Production/configtransform.json",
            "        patched in: .configtransform/Environments/Production/db.secret.env",
            "        ↓",
            "      .configtransform/Clients/Acme/Production/configtransform.json",
            "        not patched in"), stdout);
        Assert.Single(stdout.Split(Environment.NewLine), line => line.TrimStart().StartsWith("CFSECRET_DB", StringComparison.Ordinal));
    }

    [Fact]
    public void List_leaves_out_placeholders_in_resources_that_are_never_substituted()
    {
        // No engine handles .txt, so a placeholder there is never substituted -- not a secret in use.
        using var workspace = new Workspace(defineApi: true);
        workspace.Write("Project/notes.txt", "{{CFSECRET_NOTES}}");
        workspace.Write(".configtransform/Environments/Production/configtransform.json", """
            {
              "secrets": [ ".configtransform/Environments/Production/db.secret.env" ],
              "resources": [
                { "path": "Project/App.config" },
                { "path": "Project/notes.txt" }
              ]
            }
            """);

        var (exitCode, stdout, _) = workspace.Run(null, "--list", "-e", "Production");

        Assert.Equal(0, exitCode);
        Assert.Contains("  Project/notes.txt", stdout);
        Assert.Contains("    CFSECRET_DB   resolved", stdout);
        Assert.DoesNotContain("CFSECRET_NOTES", stdout);
    }

    [Fact]
    public void List_with_reveal_secrets_is_an_error_since_list_never_shows_a_value()
    {
        using var workspace = new Workspace();

        var (exitCode, stdout, stderr) = workspace.Run(null, "--list", "-e", "Production", "--reveal-secrets");

        Assert.Equal(1, exitCode);
        Assert.Contains("--reveal-secrets doesn't apply to --list", stderr);
        Assert.Contains("Try:", stderr);
        Assert.Empty(stdout);
    }
}
