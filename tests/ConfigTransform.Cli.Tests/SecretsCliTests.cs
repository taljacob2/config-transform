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

    [Fact]
    public void A_dry_run_keeps_placeholders_and_reports_each_secrets_status_but_never_its_value()
    {
        using var workspace = new Workspace();

        var (exitCode, stdout, _) = workspace.Run(null, "-r", "Project/App.config", "-e", "Production", "--dry-run");

        Assert.Equal(0, exitCode);
        Assert.Contains("Password={{CFSECRET_DB}}", stdout);
        Assert.Contains("secrets", stdout);
        Assert.Contains("CFSECRET_DB   resolved   .configtransform/Environments/Production/db.secret.env", stdout);
        Assert.DoesNotContain("Pa5", stdout);
    }

    [Fact]
    public void A_dry_run_reports_a_name_nothing_defines_as_missing()
    {
        using var workspace = new Workspace();

        var (exitCode, stdout, _) = workspace.Run(null, "-r", "Project/appsettings.json", "-e", "Production", "--dry-run");

        Assert.Equal(0, exitCode); // a preview with a missing secret is still a preview, not an error
        Assert.Contains("CFSECRET_API   MISSING", stdout);
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

        Assert.Contains("CFSECRET_DB   unknown", preview);
        Assert.Contains("git-crypt unlock", preview);
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

        Assert.Contains("CFSECRET_API   resolved   environment variable", preview);
        Assert.Equal(0, exitCode);
        Assert.Contains("from-ci", File.ReadAllText(Path.Combine(workspace.Root, "out", "appsettings.json")));
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
    public void List_shows_the_chains_secrets_files_but_never_their_contents()
    {
        using var workspace = new Workspace();

        var (exitCode, stdout, _) = workspace.Run(null, "--list", "-e", "Production");

        Assert.Equal(0, exitCode);
        Assert.Contains("  secrets:", stdout);
        Assert.Contains(".configtransform/Environments/Production/db.secret.env", stdout);
        Assert.DoesNotContain("Pa5", stdout);
    }
}
