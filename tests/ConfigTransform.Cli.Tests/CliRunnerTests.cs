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
        Assert.Contains("patched here:", output);
        Assert.Equal(before, Snapshot(workspace.RootPath));
        Assert.Empty(stderr.ToString());
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
