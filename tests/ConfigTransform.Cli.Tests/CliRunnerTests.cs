using ConfigTransform.Cli.Tests.TestSupport;
using ConfigTransform.Core;
using Xunit;

namespace ConfigTransform.Cli.Tests;

/// <summary>
/// End-to-end tests of the unified `configtransform` dispatcher's resolve/dry-run/diff/real-run/
/// list flow, through <see cref="CliRunner.Run(string[],TextWriter,TextWriter,FormatEngineRegistry,string?)"/>
/// against the real production <see cref="FormatEngines.All"/> registry — merges what were
/// separate <c>XmlCliRunnerTests</c>/<c>JsonCliRunnerTests</c>, since the per-resource behavior is
/// identical regardless of format (parametrized below); the omit-`--resource`/`--list` tests now
/// exercise both formats in one call, which is the actual point of unification.
/// </summary>
public class CliRunnerTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DryRun_prints_merged_content_and_writes_nothing_to_disk(bool xml)
    {
        using var workspace = new TempCliWorkspace();
        var resource = xml ? workspace.XmlResourcePath : workspace.JsonResourcePath;
        var before = Snapshot(workspace.RootPath);

        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = CliRunner.Run(new[]
        {
            "--resource", resource,
            "--client", "ClientA", "--environment", "Production", "--dry-run"
        }, stdout, stderr, FormatEngines.All, workspace.RootPath);

        Assert.Equal(0, exitCode);
        Assert.Contains("https://clienta.example.com", stdout.ToString());
        Assert.Equal(before, Snapshot(workspace.RootPath));
        Assert.Empty(stderr.ToString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DryRun_prints_a_resolution_report_with_a_blank_line_before_the_merged_content(bool xml)
    {
        using var workspace = new TempCliWorkspace();
        var resource = xml ? workspace.XmlResourcePath : workspace.JsonResourcePath;

        var stdout = new StringWriter();

        var exitCode = CliRunner.Run(new[]
        {
            "--resource", resource,
            "--client", "ClientA", "--environment", "Production", "--dry-run"
        }, stdout, new StringWriter(), FormatEngines.All, workspace.RootPath);

        Assert.Equal(0, exitCode);
        var output = stdout.ToString();

        Assert.Contains($"Resolving '{resource}'", output);
        Assert.Contains("base", output);

        var environmentIndex = output.IndexOf("Environments/Production/configtransform.json", StringComparison.Ordinal);
        var clientIndex = output.IndexOf("Clients/ClientA/Production/configtransform.json", StringComparison.Ordinal);
        Assert.True(environmentIndex >= 0 && clientIndex >= 0);
        Assert.True(environmentIndex < clientIndex);

        // A blank line separates the report from the merged content that follows.
        var lines = output.Replace("\r\n", "\n").Split('\n');
        var blankLineIndex = Array.FindIndex(lines, l => l.Length == 0);
        Assert.True(blankLineIndex > 0);
        Assert.Contains("clienta.example.com", string.Join('\n', lines.Skip(blankLineIndex + 1)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Diff_prints_a_resolution_report_with_a_blank_line_before_the_diff(bool xml)
    {
        using var workspace = new TempCliWorkspace();
        var resource = xml ? workspace.XmlResourcePath : workspace.JsonResourcePath;

        var stdout = new StringWriter();

        var exitCode = CliRunner.Run(new[]
        {
            "--resource", resource,
            "--client", "ClientA", "--environment", "Production", "--diff"
        }, stdout, new StringWriter(), FormatEngines.All, workspace.RootPath);

        Assert.Equal(0, exitCode);
        var output = stdout.ToString();

        Assert.Contains($"Resolving '{resource}'", output);

        var lines = output.Replace("\r\n", "\n").Split('\n');
        var blankLineIndex = Array.FindIndex(lines, l => l.Length == 0);
        Assert.True(blankLineIndex > 0);
        Assert.Contains("clienta.example.com", string.Join('\n', lines.Skip(blankLineIndex + 1)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DryRun_accepts_environment_alone_targeting_that_environment_layer_with_no_client(bool xml)
    {
        using var workspace = new TempCliWorkspace();
        var resource = xml ? workspace.XmlResourcePath : workspace.JsonResourcePath;

        var stdout = new StringWriter();
        var exitCode = CliRunner.Run(new[]
        {
            "--resource", resource, "--environment", "Production", "--dry-run"
        }, stdout, new StringWriter(), FormatEngines.All, workspace.RootPath);

        Assert.Equal(0, exitCode);
        // The Environment layer's own override, not the Client layer's (no --client given).
        Assert.Contains("https://prod.example.com", stdout.ToString());
        Assert.DoesNotContain("https://clienta.example.com", stdout.ToString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DryRun_accepts_neither_client_nor_environment_showing_the_raw_base_file(bool xml)
    {
        using var workspace = new TempCliWorkspace();
        var resource = xml ? workspace.XmlResourcePath : workspace.JsonResourcePath;

        var stdout = new StringWriter();
        var exitCode = CliRunner.Run(new[]
        {
            "--resource", resource, "--dry-run"
        }, stdout, new StringWriter(), FormatEngines.All, workspace.RootPath);

        Assert.Equal(0, exitCode);
        Assert.Contains("https://dev.example.com", stdout.ToString());
        Assert.DoesNotContain("https://prod.example.com", stdout.ToString());
        Assert.DoesNotContain("https://clienta.example.com", stdout.ToString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Diff_prints_a_diff_and_writes_nothing_to_disk(bool xml)
    {
        using var workspace = new TempCliWorkspace();
        var resource = xml ? workspace.XmlResourcePath : workspace.JsonResourcePath;
        var before = Snapshot(workspace.RootPath);

        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = CliRunner.Run(new[]
        {
            "--resource", resource,
            "--client", "ClientA", "--environment", "Production", "--diff"
        }, stdout, stderr, FormatEngines.All, workspace.RootPath);

        Assert.Equal(0, exitCode);
        Assert.Contains("dev.example.com", stdout.ToString());
        Assert.Contains("clienta.example.com", stdout.ToString());
        Assert.Equal(before, Snapshot(workspace.RootPath));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Diff_reports_no_changes_for_a_layer_that_does_not_exist_on_disk(bool xml)
    {
        // Missing overlay/layer is never fatal (CONFIG_MANAGEMENT.md §5.1) -- a client/environment
        // combination with no configtransform.json anywhere in its chain just falls through to
        // the raw base file, the same tolerance a missing overlay always had.
        using var workspace = new TempCliWorkspace();
        var resource = xml ? workspace.XmlResourcePath : workspace.JsonResourcePath;

        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = CliRunner.Run(new[]
        {
            "--resource", resource,
            "--client", "ClientB", "--environment", "Staging", "--diff"
        }, stdout, stderr, FormatEngines.All, workspace.RootPath);

        Assert.Equal(0, exitCode);
        Assert.Contains("(no changes)", stdout.ToString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DiffLayers_splits_the_diff_by_layer_and_tags_the_override(bool xml)
    {
        // TempCliWorkspace's ApiUrl goes dev.example.com (base) -> prod.example.com (Environment)
        // -> clienta.example.com (Client) -- the same key at every layer, so the Client layer's
        // own section should be tagged as overriding the Environment layer's.
        using var workspace = new TempCliWorkspace();
        var resource = xml ? workspace.XmlResourcePath : workspace.JsonResourcePath;

        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = CliRunner.Run(new[]
        {
            "--resource", resource,
            "--client", "ClientA", "--environment", "Production", "--diff-layers"
        }, stdout, stderr, FormatEngines.All, workspace.RootPath);

        Assert.Equal(0, exitCode);
        var output = stdout.ToString();

        Assert.Contains("[.configtransform/Environments/Production/configtransform.json]", output);
        Assert.Contains(
            "[.configtransform/Clients/ClientA/Production/configtransform.json overrides " +
            ".configtransform/Environments/Production/configtransform.json]",
            output);
        Assert.Contains("dev.example.com", output);
        Assert.Contains("prod.example.com", output);
        Assert.Contains("clienta.example.com", output);
        Assert.Empty(stderr.ToString());
    }

    [Fact]
    public void DiffLayers_reports_no_changes_for_a_layer_that_does_not_exist_on_disk()
    {
        using var workspace = new TempCliWorkspace();

        var stdout = new StringWriter();
        var exitCode = CliRunner.Run(new[]
        {
            "--resource", workspace.XmlResourcePath,
            "--client", "ClientB", "--environment", "Staging", "--diff-layers"
        }, stdout, new StringWriter(), FormatEngines.All, workspace.RootPath);

        Assert.Equal(0, exitCode);
        Assert.Contains("(no changes)", stdout.ToString());
    }

    [Fact]
    public void Diff_and_diff_layers_together_is_rejected_before_touching_the_workspace()
    {
        using var workspace = new TempCliWorkspace();

        var stderr = new StringWriter();
        var exitCode = CliRunner.Run(new[]
        {
            "--resource", workspace.XmlResourcePath,
            "--client", "ClientA", "--environment", "Production", "--diff", "--diff-layers"
        }, new StringWriter(), stderr, FormatEngines.All, workspace.RootPath);

        Assert.Equal(1, exitCode);
        Assert.Contains("--diff and --diff-layers are mutually exclusive", stderr.ToString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Real_run_writes_only_to_the_explicit_output_path_and_leaves_the_base_file_untouched(bool xml)
    {
        using var workspace = new TempCliWorkspace();
        var resource = xml ? workspace.XmlResourcePath : workspace.JsonResourcePath;
        var projectFilePath = xml ? workspace.XmlProjectFilePath : workspace.JsonProjectFilePath;
        var outputPath = Path.Combine(workspace.RootPath, "out", Path.GetFileName(projectFilePath));

        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = CliRunner.Run(new[]
        {
            "--resource", resource,
            "--client", "ClientA", "--environment", "Production",
            "--output", outputPath
        }, stdout, stderr, FormatEngines.All, workspace.RootPath);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(outputPath));
        Assert.Contains("https://clienta.example.com", File.ReadAllText(outputPath));

        var baseContent = File.ReadAllText(projectFilePath);
        Assert.Contains("https://dev.example.com", baseContent);
    }

    [Fact]
    public void Omitting_resource_processes_both_formats_in_one_call_for_dry_run()
    {
        using var workspace = new TempCliWorkspace();

        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = CliRunner.Run(new[]
        {
            "--client", "ClientA", "--environment", "Production", "--dry-run"
        }, stdout, stderr, FormatEngines.All, workspace.RootPath);

        Assert.Equal(0, exitCode);
        var output = stdout.ToString();
        Assert.Contains($"=== {workspace.XmlResourcePath} ===", output);
        Assert.Contains($"=== {workspace.JsonResourcePath} ===", output);
        Assert.Contains("https://clienta.example.com", output);
        // The actual capability win: a mixed-format layer resolves with no skip note at all.
        Assert.Empty(stderr.ToString());
    }

    [Fact]
    public void Omitting_resource_for_a_real_run_writes_one_file_per_resource_under_the_output_directory()
    {
        using var workspace = new TempCliWorkspace();
        var outputDir = Path.Combine(workspace.RootPath, "publish");

        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = CliRunner.Run(new[]
        {
            "--client", "ClientA", "--environment", "Production", "--output", outputDir
        }, stdout, stderr, FormatEngines.All, workspace.RootPath);

        Assert.Equal(0, exitCode);

        var writtenXmlPath = Path.Combine(outputDir, "Project", "App.config");
        Assert.True(File.Exists(writtenXmlPath));
        Assert.Contains("https://clienta.example.com", File.ReadAllText(writtenXmlPath));

        var writtenJsonPath = Path.Combine(outputDir, "Project", "appsettings.json");
        Assert.True(File.Exists(writtenJsonPath));
        Assert.Contains("https://clienta.example.com", File.ReadAllText(writtenJsonPath));

        Assert.Empty(stderr.ToString());
    }

    [Fact]
    public void Omitting_resource_for_a_real_run_fails_clearly_when_output_is_an_existing_file()
    {
        using var workspace = new TempCliWorkspace();
        var outputPath = Path.Combine(workspace.RootPath, "already-a-file.txt");
        File.WriteAllText(outputPath, "pre-existing content");

        var stderr = new StringWriter();
        var exitCode = CliRunner.Run(new[]
        {
            "--client", "ClientA", "--environment", "Production", "--output", outputPath
        }, new StringWriter(), stderr, FormatEngines.All, workspace.RootPath);

        Assert.Equal(1, exitCode);
        var error = stderr.ToString();
        Assert.Contains("already exists as a file", error);
        Assert.Contains("Try: add --resource", error);

        // Never touched: the pre-check fires before any resource is written.
        Assert.Equal("pre-existing content", File.ReadAllText(outputPath));
    }

    [Fact]
    public void Omitting_resource_names_the_missing_environment_when_no_configtransform_json_exists_for_it()
    {
        using var workspace = new TempCliWorkspace();

        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = CliRunner.Run(new[]
        {
            "--environment", "test", "--dry-run"
        }, stdout, stderr, FormatEngines.All, workspace.RootPath);

        Assert.Equal(0, exitCode);
        var output = stdout.ToString();
        Assert.Contains("no configtransform.json found for --environment 'test'", output);
        Assert.Contains(".configtransform/Environments/test/configtransform.json", output);
        Assert.Contains("Try: check the spelling", output);
        Assert.DoesNotContain("no resources with a registered format handler", output);
        Assert.Empty(stderr.ToString());
    }

    [Fact]
    public void Omitting_resource_names_the_missing_client_and_environment_when_no_configtransform_json_exists_for_them()
    {
        using var workspace = new TempCliWorkspace();

        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = CliRunner.Run(new[]
        {
            "--client", "Nope", "--environment", "Production", "--dry-run"
        }, stdout, stderr, FormatEngines.All, workspace.RootPath);

        Assert.Equal(0, exitCode);
        var output = stdout.ToString();
        Assert.Contains("no configtransform.json found for --client 'Nope' --environment 'Production'", output);
        Assert.Contains(".configtransform/Clients/Nope/Production/configtransform.json", output);
        Assert.Empty(stderr.ToString());
    }

    [Fact]
    public void Host_layer_overrides_the_client_layers_value_when_targeted()
    {
        using var workspace = new TempCliWorkspace();
        var hostDir = Path.Combine(workspace.RootPath, ".configtransform", "Clients", "ClientA", "Production", "Hosts", "192.168.10.10");
        Directory.CreateDirectory(hostDir);
        File.WriteAllText(Path.Combine(hostDir, "patch-Project-App.config.xml"), """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration xmlns:xdt="http://schemas.microsoft.com/XML-Document-Transform">
              <appSettings>
                <add key="ApiUrl" value="https://host-a.example.com" xdt:Transform="SetAttributes" xdt:Locator="Match(key)" />
              </appSettings>
            </configuration>
            """);
        File.WriteAllText(Path.Combine(hostDir, "configtransform.json"), """
            {
              "extends": ".configtransform/Clients/ClientA/Production/configtransform.json",
              "resources": [
                { "path": "Project/App.config", "patch": ".configtransform/Clients/ClientA/Production/Hosts/192.168.10.10/patch-Project-App.config.xml" }
              ]
            }
            """);

        var stdout = new StringWriter();
        var exitCode = CliRunner.Run(new[]
        {
            "--resource", workspace.XmlResourcePath,
            "--client", "ClientA", "--environment", "Production", "--host", "192.168.10.10", "--dry-run"
        }, stdout, new StringWriter(), FormatEngines.All, workspace.RootPath);

        Assert.Equal(0, exitCode);
        Assert.Contains("https://host-a.example.com", stdout.ToString());
    }

    [Fact]
    public void Omitting_host_still_resolves_at_the_client_layer_unaffected_by_an_unrelated_hosts_folder()
    {
        using var workspace = new TempCliWorkspace();
        var hostDir = Path.Combine(workspace.RootPath, ".configtransform", "Clients", "ClientA", "Production", "Hosts", "192.168.10.10");
        Directory.CreateDirectory(hostDir);
        File.WriteAllText(Path.Combine(hostDir, "configtransform.json"), """
            {
              "extends": ".configtransform/Clients/ClientA/Production/configtransform.json",
              "resources": []
            }
            """);

        var stdout = new StringWriter();
        var exitCode = CliRunner.Run(new[]
        {
            "--resource", workspace.XmlResourcePath,
            "--client", "ClientA", "--environment", "Production", "--dry-run"
        }, stdout, new StringWriter(), FormatEngines.All, workspace.RootPath);

        Assert.Equal(0, exitCode);
        Assert.Contains("https://clienta.example.com", stdout.ToString());
    }

    [Fact]
    public void Omitting_resource_names_the_missing_host_when_no_configtransform_json_exists_for_it()
    {
        using var workspace = new TempCliWorkspace();

        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = CliRunner.Run(new[]
        {
            "--client", "ClientA", "--environment", "Production", "--host", "192.168.10.99", "--dry-run"
        }, stdout, stderr, FormatEngines.All, workspace.RootPath);

        Assert.Equal(0, exitCode);
        var output = stdout.ToString();
        Assert.Contains("no configtransform.json found for --client 'ClientA' --environment 'Production' --host '192.168.10.99'", output);
        Assert.Contains(".configtransform/Clients/ClientA/Production/Hosts/192.168.10.99/configtransform.json", output);
        Assert.Empty(stderr.ToString());
    }

    [Fact]
    public void Omitting_resource_diff_covers_both_formats_in_one_call()
    {
        using var workspace = new TempCliWorkspace();

        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = CliRunner.Run(new[]
        {
            "--client", "ClientA", "--environment", "Production", "--diff"
        }, stdout, stderr, FormatEngines.All, workspace.RootPath);

        Assert.Equal(0, exitCode);
        var output = stdout.ToString();
        Assert.Contains($"=== {workspace.XmlResourcePath} ===", output);
        Assert.Contains($"=== {workspace.JsonResourcePath} ===", output);
        Assert.Empty(stderr.ToString());
    }

    [Fact]
    public void List_shows_both_resources_and_what_the_layer_inherits_via_extends()
    {
        using var workspace = new TempCliWorkspace();
        var before = Snapshot(workspace.RootPath);

        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = CliRunner.Run(new[]
        {
            "--list", "--client", "ClientA", "--environment", "Production"
        }, stdout, stderr, FormatEngines.All, workspace.RootPath);

        Assert.Equal(0, exitCode);
        var output = stdout.ToString();
        Assert.Contains("extends:", output);
        Assert.Contains(workspace.XmlResourcePath, output);
        Assert.Contains(workspace.JsonResourcePath, output);
        Assert.Contains("patched in", output);
        Assert.Equal(before, Snapshot(workspace.RootPath));
        Assert.Empty(stderr.ToString());
    }

    [Fact]
    public void List_shows_the_chain_in_real_application_order_base_then_environment_then_client()
    {
        using var workspace = new TempCliWorkspace();

        var stdout = new StringWriter();

        var exitCode = CliRunner.Run(new[]
        {
            "--list", "--client", "ClientA", "--environment", "Production"
        }, stdout, new StringWriter(), FormatEngines.All, workspace.RootPath);

        Assert.Equal(0, exitCode);
        var output = stdout.ToString();

        // Skip past the layer-level "extends:" header line, which names the Environment layer
        // too -- the ordering under test is within a resource's own chain block, not the header.
        var baseIndex = output.IndexOf("    base", StringComparison.Ordinal);
        var environmentIndex = output.IndexOf("Environments/Production/configtransform.json", baseIndex, StringComparison.Ordinal);
        var clientIndex = output.IndexOf("Clients/ClientA/Production/configtransform.json", baseIndex, StringComparison.Ordinal);

        Assert.True(baseIndex >= 0 && environmentIndex >= 0 && clientIndex >= 0);
        Assert.True(baseIndex < environmentIndex);
        Assert.True(environmentIndex < clientIndex);
    }

    [Fact]
    public void List_with_host_shows_the_chain_through_the_Hosts_layer()
    {
        using var workspace = new TempCliWorkspace();
        var hostDir = Path.Combine(workspace.RootPath, ".configtransform", "Clients", "ClientA", "Production", "Hosts", "192.168.10.10");
        Directory.CreateDirectory(hostDir);
        File.WriteAllText(Path.Combine(hostDir, "configtransform.json"), """
            {
              "extends": ".configtransform/Clients/ClientA/Production/configtransform.json",
              "resources": []
            }
            """);

        var stdout = new StringWriter();

        var exitCode = CliRunner.Run(new[]
        {
            "--list", "--client", "ClientA", "--environment", "Production", "--host", "192.168.10.10"
        }, stdout, new StringWriter(), FormatEngines.All, workspace.RootPath);

        Assert.Equal(0, exitCode);
        var output = stdout.ToString();
        Assert.Contains("Clients/ClientA/Production/Hosts/192.168.10.10/configtransform.json", output);
        Assert.Contains("Clients/ClientA/Production/configtransform.json", output);
    }

    [Fact]
    public void List_shows_the_same_chain_rendering_the_single_resource_report_uses_including_real_patch_paths()
    {
        using var workspace = new TempCliWorkspace();

        var listStdout = new StringWriter();
        var listExitCode = CliRunner.Run(new[]
        {
            "--list", "--client", "ClientA", "--environment", "Production"
        }, listStdout, new StringWriter(), FormatEngines.All, workspace.RootPath);

        Assert.Equal(0, listExitCode);
        var listOutput = listStdout.ToString();

        // --list shows the real patch file path per layer, not just "patched in" -- the same
        // detail the single-resource report already prints, extracted from the same
        // LayerChain.PrintChain so the two never drift into two different renderings. This is
        // the exact chain block PrintChain produces for the XML resource in this workspace.
        // StringWriter.WriteLine emits Environment.NewLine (\r\n on Windows), so the expected
        // text must join on that too, not a hardcoded '\n'.
        var expectedChain = string.Join(Environment.NewLine,
        [
            "    base",
            $"      {workspace.XmlResourcePath}",
            "      ↓",
            "    .configtransform/Environments/Production/configtransform.json",
            "      patched in: .configtransform/Environments/Production/patch-Project-App.config.xml",
            "      ↓",
            "    .configtransform/Clients/ClientA/Production/configtransform.json",
            "      patched in: .configtransform/Clients/ClientA/Production/patch-Project-App.config.xml"
        ]);
        Assert.Contains(expectedChain, listOutput);
    }

    [Fact]
    public void List_throws_the_same_way_the_single_resource_report_does_when_a_declared_patch_file_is_missing()
    {
        using var workspace = new TempCliWorkspace();
        File.Delete(Path.Combine(workspace.RootPath, ".configtransform", "Clients", "ClientA", "Production", "patch-Project-App.config.xml"));

        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = CliRunner.Run(new[]
        {
            "--list", "--client", "ClientA", "--environment", "Production"
        }, stdout, stderr, FormatEngines.All, workspace.RootPath);

        Assert.Equal(1, exitCode);
        Assert.Contains("no file exists at", stderr.ToString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void List_with_resource_is_a_reverse_lookup_across_the_whole_tree(bool xml)
    {
        using var workspace = new TempCliWorkspace();
        var resource = xml ? workspace.XmlResourcePath : workspace.JsonResourcePath;

        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = CliRunner.Run(new[]
        {
            "--list", "--resource", resource
        }, stdout, stderr, FormatEngines.All, workspace.RootPath);

        Assert.Equal(0, exitCode);
        var output = stdout.ToString();
        Assert.Contains("Environments/Production/configtransform.json", output);
        Assert.Contains("Clients/ClientA/Production/configtransform.json", output);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Short_flags_work_the_same_as_the_long_forms(bool xml)
    {
        using var workspace = new TempCliWorkspace();
        var resource = xml ? workspace.XmlResourcePath : workspace.JsonResourcePath;

        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = CliRunner.Run(new[]
        {
            "-r", resource, "-c", "ClientA", "-e", "Production", "--diff"
        }, stdout, stderr, FormatEngines.All, workspace.RootPath);

        Assert.Equal(0, exitCode);
        Assert.Contains("clienta.example.com", stdout.ToString());
    }

    private static Dictionary<string, DateTime> Snapshot(string root) =>
        Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(f => f, File.GetLastWriteTimeUtc);
}
