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
            "--manifest", workspace.ManifestPath,
            "--client", "ClientA", "--environment", "Production", "--dry-run"
        }, stdout, stderr);

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
            "--manifest", workspace.ManifestPath,
            "--client", "ClientA", "--environment", "Production", "--diff"
        }, stdout, stderr);

        Assert.Equal(0, exitCode);
        Assert.Contains("dev.example.com", stdout.ToString());
        Assert.Contains("clienta.example.com", stdout.ToString());
        Assert.Equal(before, Snapshot(workspace.RootPath));
    }

    [Fact]
    public void Diff_reports_no_changes_when_neither_layer_overrides_anything()
    {
        using var workspace = new TempCliWorkspace();

        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = XmlCliRunner.Run(new[]
        {
            "--manifest", workspace.ManifestPath,
            "--client", "ClientB", "--environment", "Staging", "--diff"
        }, stdout, stderr);

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
            "--manifest", workspace.ManifestPath,
            "--client", "ClientA", "--environment", "Production",
            "--output", outputPath
        }, stdout, stderr);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(outputPath));
        Assert.Contains("https://clienta.example.com", File.ReadAllText(outputPath));

        var baseContent = File.ReadAllText(Path.Combine(workspace.RootPath, "Project", "App.config"));
        Assert.Contains("https://dev.example.com", baseContent);
    }

    [Fact]
    public void List_prints_available_environments_and_clients_without_client_or_environment_or_output()
    {
        using var workspace = new TempCliWorkspace();
        var before = Snapshot(workspace.RootPath);

        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = XmlCliRunner.Run(new[]
        {
            "--manifest", workspace.ManifestPath, "--list"
        }, stdout, stderr);

        Assert.Equal(0, exitCode);
        Assert.Contains("App.config (xml)", stdout.ToString());
        Assert.Contains("Production", stdout.ToString());
        Assert.Contains("ClientA", stdout.ToString());
        Assert.Equal(before, Snapshot(workspace.RootPath));
        Assert.Empty(stderr.ToString());
    }

    [Fact]
    public void Manifest_is_auto_discovered_when_omitted_and_exactly_one_candidate_exists()
    {
        using var workspace = new TempCliWorkspace();

        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = XmlCliRunner.Run(new[]
        {
            "--client", "ClientA", "--environment", "Production", "--dry-run"
        }, stdout, stderr, workspace.RootPath);

        Assert.Equal(0, exitCode);
        Assert.Contains("https://clienta.example.com", stdout.ToString());
        Assert.Empty(stderr.ToString());
    }

    [Fact]
    public void Missing_manifest_and_no_dot_configtransform_directory_fails_with_an_actionable_error()
    {
        var emptyDir = Directory.CreateTempSubdirectory("configtransform-cli-tests-empty-").FullName;
        try
        {
            var stdout = new StringWriter();
            var stderr = new StringWriter();

            var exitCode = XmlCliRunner.Run(new[]
            {
                "--client", "ClientA", "--environment", "Production", "--dry-run"
            }, stdout, stderr, emptyDir);

            Assert.Equal(1, exitCode);
            Assert.Contains("--manifest/-m", stderr.ToString());
        }
        finally
        {
            Directory.Delete(emptyDir, recursive: true);
        }
    }

    [Fact]
    public void Short_flags_work_the_same_as_the_long_forms()
    {
        using var workspace = new TempCliWorkspace();

        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = XmlCliRunner.Run(new[]
        {
            "-m", workspace.ManifestPath, "-c", "ClientA", "-e", "Production", "--diff"
        }, stdout, stderr);

        Assert.Equal(0, exitCode);
        Assert.Contains("clienta.example.com", stdout.ToString());
    }

    private static Dictionary<string, DateTime> Snapshot(string root) =>
        Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(f => f, File.GetLastWriteTimeUtc);
}
