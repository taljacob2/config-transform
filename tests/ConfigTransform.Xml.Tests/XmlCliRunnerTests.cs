using ConfigTransform.Xml.Tests.TestSupport;
using Xunit;

namespace ConfigTransform.Xml.Tests;

public class XmlCliRunnerTests
{
    [Fact]
    public void DryRun_prints_merged_content_and_writes_nothing_to_disk()
    {
        using var workspace = new TempCliWorkspace();
        var before = Snapshot(workspace.RootPath);

        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = XmlCliRunner.Run(new[]
        {
            "--resource", workspace.ResourcePath,
            "--client", "ClientA", "--environment", "Production", "--dry-run"
        }, stdout, stderr, workspace.RootPath);

        Assert.Equal(0, exitCode);
        Assert.Contains("https://clienta.example.com", stdout.ToString());
        Assert.Equal(before, Snapshot(workspace.RootPath));
        Assert.Empty(stderr.ToString());
    }

    [Fact]
    public void Diff_prints_a_diff_and_writes_nothing_to_disk()
    {
        using var workspace = new TempCliWorkspace();
        var before = Snapshot(workspace.RootPath);

        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = XmlCliRunner.Run(new[]
        {
            "--resource", workspace.ResourcePath,
            "--client", "ClientA", "--environment", "Production", "--diff"
        }, stdout, stderr, workspace.RootPath);

        Assert.Equal(0, exitCode);
        Assert.Contains("dev.example.com", stdout.ToString());
        Assert.Contains("clienta.example.com", stdout.ToString());
        Assert.Equal(before, Snapshot(workspace.RootPath));
    }

    [Fact]
    public void Diff_reports_no_changes_for_a_layer_that_does_not_exist_on_disk()
    {
        // Missing overlay/layer is never fatal (CONFIG_MANAGEMENT.md §5.1) -- a client/environment
        // combination with no configtransform.json anywhere in its chain just falls through to
        // the raw base file, the same tolerance a missing overlay always had.
        using var workspace = new TempCliWorkspace();

        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = XmlCliRunner.Run(new[]
        {
            "--resource", workspace.ResourcePath,
            "--client", "ClientB", "--environment", "Staging", "--diff"
        }, stdout, stderr, workspace.RootPath);

        Assert.Equal(0, exitCode);
        Assert.Contains("(no changes)", stdout.ToString());
    }

    [Fact]
    public void Real_run_writes_only_to_the_explicit_output_path_and_leaves_the_base_file_untouched()
    {
        using var workspace = new TempCliWorkspace();
        var outputPath = Path.Combine(workspace.RootPath, "out", "App.config");

        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = XmlCliRunner.Run(new[]
        {
            "--resource", workspace.ResourcePath,
            "--client", "ClientA", "--environment", "Production",
            "--output", outputPath
        }, stdout, stderr, workspace.RootPath);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(outputPath));
        Assert.Contains("https://clienta.example.com", File.ReadAllText(outputPath));

        var baseContent = File.ReadAllText(workspace.ProjectFilePath);
        Assert.Contains("https://dev.example.com", baseContent);
    }

    [Fact]
    public void Omitting_resource_processes_every_resource_the_layer_touches_for_dry_run()
    {
        using var workspace = new TempCliWorkspace();

        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = XmlCliRunner.Run(new[]
        {
            "--client", "ClientA", "--environment", "Production", "--dry-run"
        }, stdout, stderr, workspace.RootPath);

        Assert.Equal(0, exitCode);
        Assert.Contains($"=== {workspace.ResourcePath} ===", stdout.ToString());
        Assert.Contains("https://clienta.example.com", stdout.ToString());
    }

    [Fact]
    public void Omitting_resource_for_a_real_run_writes_one_file_per_resource_under_the_output_directory()
    {
        using var workspace = new TempCliWorkspace();
        var outputDir = Path.Combine(workspace.RootPath, "publish");

        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = XmlCliRunner.Run(new[]
        {
            "--client", "ClientA", "--environment", "Production", "--output", outputDir
        }, stdout, stderr, workspace.RootPath);

        Assert.Equal(0, exitCode);
        var writtenPath = Path.Combine(outputDir, "Project", "App.config");
        Assert.True(File.Exists(writtenPath));
        Assert.Contains("https://clienta.example.com", File.ReadAllText(writtenPath));
    }

    [Fact]
    public void List_shows_the_layers_resources_and_what_it_inherits_via_extends()
    {
        using var workspace = new TempCliWorkspace();
        var before = Snapshot(workspace.RootPath);

        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = XmlCliRunner.Run(new[]
        {
            "--list", "--client", "ClientA", "--environment", "Production"
        }, stdout, stderr, workspace.RootPath);

        Assert.Equal(0, exitCode);
        var output = stdout.ToString();
        Assert.Contains("extends:", output);
        Assert.Contains(workspace.ResourcePath, output);
        Assert.Contains("patched here:", output);
        Assert.Equal(before, Snapshot(workspace.RootPath));
        Assert.Empty(stderr.ToString());
    }

    [Fact]
    public void List_with_resource_is_a_reverse_lookup_across_the_whole_tree()
    {
        using var workspace = new TempCliWorkspace();

        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = XmlCliRunner.Run(new[]
        {
            "--list", "--resource", workspace.ResourcePath
        }, stdout, stderr, workspace.RootPath);

        Assert.Equal(0, exitCode);
        var output = stdout.ToString();
        Assert.Contains("Environments/Production/configtransform.json", output);
        Assert.Contains("Clients/ClientA/Production/configtransform.json", output);
    }

    [Fact]
    public void Short_flags_work_the_same_as_the_long_forms()
    {
        using var workspace = new TempCliWorkspace();

        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = XmlCliRunner.Run(new[]
        {
            "-r", workspace.ResourcePath, "-c", "ClientA", "-e", "Production", "--diff"
        }, stdout, stderr, workspace.RootPath);

        Assert.Equal(0, exitCode);
        Assert.Contains("clienta.example.com", stdout.ToString());
    }

    private static Dictionary<string, DateTime> Snapshot(string root) =>
        Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(f => f, File.GetLastWriteTimeUtc);
}
