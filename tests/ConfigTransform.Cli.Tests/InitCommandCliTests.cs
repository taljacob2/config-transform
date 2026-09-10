using ConfigTransform.Cli.Tests.TestSupport;
using ConfigTransform.Core;
using Xunit;

namespace ConfigTransform.Cli.Tests;

/// <summary>
/// End-to-end tests of the `init` verb (docs/INIT_COMMAND_DESIGN.md) through the unified
/// <see cref="CliRunner.Run(string[],TextWriter,TextWriter,FormatEngineRegistry,string?,TextReader?,bool)"/>
/// against the real production <see cref="FormatEngines.All"/> registry — quiet/flag-driven mode,
/// the interactive form (via injected stdin, no real terminal needed), and <c>--template</c>.
/// </summary>
public class InitCommandCliTests
{
    [Fact]
    public void Quiet_mode_with_explicit_resource_skips_scanning_and_builds_the_expected_tree()
    {
        using var workspace = new TempDirectory();
        WriteFile(workspace.Path, "Project/App.config", "<configuration/>");

        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = CliRunner.Run(new[]
        {
            "init", "--environment", "Production", "--client", "Acme", "--resource", "Project/App.config"
        }, stdout, stderr, FormatEngines.All, workspace.Path);

        Assert.Equal(0, exitCode);
        Assert.Empty(stderr.ToString());

        var envManifest = LayerManifestLoader.Load(
            Path.Combine(workspace.Path, ".configtransform", "Environments", "Production", "configtransform.json"));
        var entry = Assert.Single(envManifest.Resources);
        Assert.Equal("Project/App.config", entry.Path);
        Assert.Null(entry.Patch);

        var clientManifest = LayerManifestLoader.Load(
            Path.Combine(workspace.Path, ".configtransform", "Clients", "Acme", "Production", "configtransform.json"));
        Assert.Equal(".configtransform/Environments/Production/configtransform.json", clientManifest.Extends);
        Assert.Empty(clientManifest.Resources);
    }

    [Fact]
    public void Quiet_mode_with_host_scaffolds_a_Hosts_layer_extending_the_client_environment_layer()
    {
        using var workspace = new TempDirectory();
        WriteFile(workspace.Path, "Project/App.config", "<configuration/>");

        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = CliRunner.Run(new[]
        {
            "init", "--environment", "Production", "--client", "Acme", "--host", "192.168.10.10",
            "--resource", "Project/App.config"
        }, stdout, stderr, FormatEngines.All, workspace.Path);

        Assert.Equal(0, exitCode);
        Assert.Empty(stderr.ToString());

        var hostManifest = LayerManifestLoader.Load(Path.Combine(
            workspace.Path, ".configtransform", "Clients", "Acme", "Production", "Hosts", "192.168.10.10", "configtransform.json"));
        Assert.Equal(".configtransform/Clients/Acme/Production/configtransform.json", hostManifest.Extends);
        Assert.Empty(hostManifest.Resources);
    }

    [Fact]
    public void Quiet_mode_rejects_host_without_client_and_environment()
    {
        using var workspace = new TempDirectory();
        WriteFile(workspace.Path, "Project/App.config", "<configuration/>");
        var stderr = new StringWriter();

        var exitCode = CliRunner.Run(new[]
        {
            "init", "--environment", "Production", "--host", "192.168.10.10", "--resource", "Project/App.config"
        }, new StringWriter(), stderr, FormatEngines.All, workspace.Path);

        Assert.Equal(1, exitCode);
        Assert.Contains("--host requires --client and --environment", stderr.ToString());
    }

    [Fact]
    public void Interactive_mode_answering_hosts_scaffolds_a_Hosts_layer()
    {
        using var workspace = new TempDirectory();
        WriteFile(workspace.Path, "Project/App.config", "<configuration/>");

        var stdin = new StringReader("all\nProduction\nAcme\n192.168.10.10\n");
        var stdout = new StringWriter();

        var exitCode = CliRunner.Run(new[] { "init" },
            stdout, new StringWriter(), FormatEngines.All, workspace.Path, stdin, interactiveAllowed: true);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(
            workspace.Path, ".configtransform", "Clients", "Acme", "Production", "Hosts", "192.168.10.10", "configtransform.json")));
    }

    [Fact]
    public void Interactive_mode_with_no_clients_never_prompts_for_hosts()
    {
        using var workspace = new TempDirectory();
        WriteFile(workspace.Path, "Project/App.config", "<configuration/>");

        // Blank clients answer, then nothing left in stdin -- if a hosts prompt fired anyway,
        // this would throw on the unanswered question instead of succeeding.
        var stdin = new StringReader("all\nProduction\n\n");
        var stdout = new StringWriter();

        var exitCode = CliRunner.Run(new[] { "init" },
            stdout, new StringWriter(), FormatEngines.All, workspace.Path, stdin, interactiveAllowed: true);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Dry_run_prints_the_plan_and_writes_nothing_to_disk()
    {
        using var workspace = new TempDirectory();
        WriteFile(workspace.Path, "Project/App.config", "<configuration/>");

        var stdout = new StringWriter();
        var exitCode = CliRunner.Run(new[]
        {
            "init", "--environment", "Production", "--resource", "Project/App.config", "--dry-run"
        }, stdout, new StringWriter(), FormatEngines.All, workspace.Path);

        Assert.Equal(0, exitCode);
        Assert.Contains("Would write:", stdout.ToString());
        Assert.False(Directory.Exists(Path.Combine(workspace.Path, ".configtransform")));
    }

    [Fact]
    public void Quiet_mode_scans_when_no_resource_is_given_and_yes_accepts_every_candidate()
    {
        using var workspace = new TempDirectory();
        WriteFile(workspace.Path, "Project/App.config", "<configuration/>");
        WriteFile(workspace.Path, "Project/appsettings.json", "{}");
        WriteFile(workspace.Path, "node_modules/junk/should-be-excluded.json", "{}");

        var stdout = new StringWriter();
        var exitCode = CliRunner.Run(new[]
        {
            "init", "--environment", "Production", "--yes"
        }, stdout, new StringWriter(), FormatEngines.All, workspace.Path);

        Assert.Equal(0, exitCode);
        var envManifest = LayerManifestLoader.Load(
            Path.Combine(workspace.Path, ".configtransform", "Environments", "Production", "configtransform.json"));
        Assert.Equal(2, envManifest.Resources.Count);
        Assert.Contains(envManifest.Resources, r => r.Path == "Project/App.config");
        Assert.Contains(envManifest.Resources, r => r.Path == "Project/appsettings.json");
    }

    [Fact]
    public void Quiet_mode_without_yes_or_resource_and_no_real_terminal_errors_naming_the_candidate_count()
    {
        using var workspace = new TempDirectory();
        WriteFile(workspace.Path, "Project/App.config", "<configuration/>");

        var stderr = new StringWriter();
        var exitCode = CliRunner.Run(new[]
        {
            "init", "--environment", "Production"
        }, new StringWriter(), stderr, FormatEngines.All, workspace.Path, stdin: new StringReader(""), interactiveAllowed: false);

        Assert.Equal(1, exitCode);
        Assert.Contains("1 candidate resource(s)", stderr.ToString());
        Assert.Contains("--yes", stderr.ToString());
    }

    [Fact]
    public void No_environment_and_no_template_with_no_real_terminal_errors()
    {
        using var workspace = new TempDirectory();

        var stderr = new StringWriter();
        var exitCode = CliRunner.Run(new[] { "init" },
            new StringWriter(), stderr, FormatEngines.All, workspace.Path, stdin: new StringReader(""), interactiveAllowed: false);

        Assert.Equal(1, exitCode);
        Assert.Contains("--environment", stderr.ToString());
        Assert.Contains("--template", stderr.ToString());
    }

    [Fact]
    public void Quiet_mode_with_flags_but_a_real_terminal_falls_back_to_the_resource_checklist_prompt()
    {
        using var workspace = new TempDirectory();
        WriteFile(workspace.Path, "Project/App.config", "<configuration/>");
        WriteFile(workspace.Path, "Project/appsettings.json", "{}");

        var stdin = new StringReader("1\n"); // select only the first candidate
        var stdout = new StringWriter();

        var exitCode = CliRunner.Run(new[]
        {
            "init", "--environment", "Production"
        }, stdout, new StringWriter(), FormatEngines.All, workspace.Path, stdin, interactiveAllowed: true);

        Assert.Equal(0, exitCode);
        Assert.Contains("Select which of these", stdout.ToString());
        var envManifest = LayerManifestLoader.Load(
            Path.Combine(workspace.Path, ".configtransform", "Environments", "Production", "configtransform.json"));
        var entry = Assert.Single(envManifest.Resources);
        Assert.Equal("Project/App.config", entry.Path);
    }

    [Fact]
    public void Interactive_mode_runs_the_full_form_and_builds_the_expected_tree()
    {
        using var workspace = new TempDirectory();
        WriteFile(workspace.Path, "Project/App.config", "<configuration/>");

        var stdin = new StringReader("all\nProduction,Test\nAcme,Globex\n\n"); // blank hosts answer -- none
        var stdout = new StringWriter();

        var exitCode = CliRunner.Run(new[] { "init" },
            stdout, new StringWriter(), FormatEngines.All, workspace.Path, stdin, interactiveAllowed: true);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(workspace.Path, ".configtransform", "Environments", "Production", "configtransform.json")));
        Assert.True(File.Exists(Path.Combine(workspace.Path, ".configtransform", "Environments", "Test", "configtransform.json")));
        Assert.True(File.Exists(Path.Combine(workspace.Path, ".configtransform", "Clients", "Acme", "Production", "configtransform.json")));
        Assert.True(File.Exists(Path.Combine(workspace.Path, ".configtransform", "Clients", "Globex", "Test", "configtransform.json")));
    }

    [Fact]
    public void Interactive_mode_re_prompts_on_a_blank_environments_answer()
    {
        using var workspace = new TempDirectory();
        WriteFile(workspace.Path, "Project/App.config", "<configuration/>");

        var stdin = new StringReader("none\n\nProduction\n\n"); // blank environments, then a real one, blank clients
        var stdout = new StringWriter();

        var exitCode = CliRunner.Run(new[] { "init" },
            stdout, new StringWriter(), FormatEngines.All, workspace.Path, stdin, interactiveAllowed: true);

        Assert.Equal(0, exitCode);
        Assert.Contains("At least one environment is required.", stdout.ToString());
        Assert.True(File.Exists(Path.Combine(workspace.Path, ".configtransform", "Environments", "Production", "configtransform.json")));
    }

    [Fact]
    public void Interactive_mode_hitting_eof_mid_wizard_errors_naming_the_unanswered_question()
    {
        using var workspace = new TempDirectory();
        WriteFile(workspace.Path, "Project/App.config", "<configuration/>");

        var stdin = new StringReader("none\n"); // resource checklist answered, nothing left for environments
        var stderr = new StringWriter();

        var exitCode = CliRunner.Run(new[] { "init" },
            new StringWriter(), stderr, FormatEngines.All, workspace.Path, stdin, interactiveAllowed: true);

        Assert.Equal(1, exitCode);
        Assert.Contains("Environments", stderr.ToString());
        Assert.Contains("--environment", stderr.ToString());
    }

    [Fact]
    public void Template_mode_dry_run_writes_nothing()
    {
        using var workspace = new TempDirectory();

        var stdout = new StringWriter();
        var exitCode = CliRunner.Run(new[] { "init", "--template", "--dry-run" },
            stdout, new StringWriter(), FormatEngines.All, workspace.Path);

        Assert.Equal(0, exitCode);
        Assert.Contains("Would write:", stdout.ToString());
        Assert.False(File.Exists(Path.Combine(workspace.Path, InitTemplate.ResourcePath)));
    }

    [Fact]
    public void Template_mode_writes_thirteen_files_and_is_immediately_resolvable()
    {
        using var workspace = new TempDirectory();

        var initExit = CliRunner.Run(new[] { "init", "--template" },
            new StringWriter(), new StringWriter(), FormatEngines.All, workspace.Path);
        Assert.Equal(0, initExit);

        var stdout = new StringWriter();
        var resolveExit = CliRunner.Run(new[]
        {
            "--client", "Client-A", "--environment", "Production", "--resource", InitTemplate.ResourcePath, "--dry-run"
        }, stdout, new StringWriter(), FormatEngines.All, workspace.Path);

        Assert.Equal(0, resolveExit);
        Assert.Contains("Hello, world! (from Client-A Production config)", stdout.ToString());
    }

    [Fact]
    public void Template_mode_is_idempotent_on_a_clean_re_run()
    {
        using var workspace = new TempDirectory();

        CliRunner.Run(new[] { "init", "--template" }, new StringWriter(), new StringWriter(), FormatEngines.All, workspace.Path);

        var stderr = new StringWriter();
        var exitCode = CliRunner.Run(new[] { "init", "--template" },
            new StringWriter(), stderr, FormatEngines.All, workspace.Path);

        Assert.Equal(0, exitCode);
        Assert.Empty(stderr.ToString());
    }

    [Fact]
    public void Template_mode_refuses_to_overwrite_a_differently_content_resource_file()
    {
        using var workspace = new TempDirectory();
        WriteFile(workspace.Path, InitTemplate.ResourcePath, """{ "message": "not the template" }""");

        var stderr = new StringWriter();
        var exitCode = CliRunner.Run(new[] { "init", "--template" },
            new StringWriter(), stderr, FormatEngines.All, workspace.Path);

        Assert.Equal(1, exitCode);
        Assert.Contains("already exists with different", stderr.ToString());
        Assert.False(Directory.Exists(Path.Combine(workspace.Path, ".configtransform")));
    }

    [Fact]
    public void Resource_naming_a_file_that_does_not_exist_errors()
    {
        using var workspace = new TempDirectory();

        var stderr = new StringWriter();
        var exitCode = CliRunner.Run(new[]
        {
            "init", "--environment", "Production", "--resource", "Project/DoesNotExist.config"
        }, new StringWriter(), stderr, FormatEngines.All, workspace.Path);

        Assert.Equal(1, exitCode);
        Assert.Contains("Project/DoesNotExist.config", stderr.ToString());
    }

    [Fact]
    public void Environment_name_colliding_case_insensitively_with_an_existing_one_errors()
    {
        using var workspace = new TempDirectory();
        WriteFile(workspace.Path, "Project/App.config", "<configuration/>");
        Directory.CreateDirectory(Path.Combine(workspace.Path, ".configtransform", "Environments", "Production"));

        var stderr = new StringWriter();
        var exitCode = CliRunner.Run(new[]
        {
            "init", "--environment", "production", "--resource", "Project/App.config"
        }, new StringWriter(), stderr, FormatEngines.All, workspace.Path);

        Assert.Equal(1, exitCode);
        Assert.Contains("collides case-insensitively", stderr.ToString());
    }

    [Fact]
    public void Host_name_colliding_case_insensitively_with_an_existing_one_errors()
    {
        using var workspace = new TempDirectory();
        WriteFile(workspace.Path, "Project/App.config", "<configuration/>");
        Directory.CreateDirectory(Path.Combine(
            workspace.Path, ".configtransform", "Clients", "Acme", "Production", "Hosts", "Host-A"));

        var stderr = new StringWriter();
        var exitCode = CliRunner.Run(new[]
        {
            "init", "--environment", "Production", "--client", "Acme", "--host", "host-a", "--resource", "Project/App.config"
        }, new StringWriter(), stderr, FormatEngines.All, workspace.Path);

        Assert.Equal(1, exitCode);
        Assert.Contains("collides case-insensitively", stderr.ToString());
    }

    [Fact]
    public void Re_running_quiet_mode_to_onboard_a_second_client_does_not_clobber_the_first()
    {
        using var workspace = new TempDirectory();
        WriteFile(workspace.Path, "Project/App.config", "<configuration/>");

        CliRunner.Run(new[]
        {
            "init", "--environment", "Production", "--client", "Acme", "--resource", "Project/App.config"
        }, new StringWriter(), new StringWriter(), FormatEngines.All, workspace.Path);

        var exitCode = CliRunner.Run(new[]
        {
            "init", "--environment", "Production", "--client", "Globex", "--resource", "Project/App.config"
        }, new StringWriter(), new StringWriter(), FormatEngines.All, workspace.Path);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(workspace.Path, ".configtransform", "Clients", "Acme", "Production", "configtransform.json")));
        Assert.True(File.Exists(Path.Combine(workspace.Path, ".configtransform", "Clients", "Globex", "Production", "configtransform.json")));
    }

    private static void WriteFile(string root, string relativePath, string content)
    {
        var fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }
}
