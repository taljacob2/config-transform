using ConfigTransform.Xml.Tests.TestSupport;
using Xunit;

namespace ConfigTransform.Xml.Tests;

/// <summary>
/// End-to-end tests of the "set" verb through <see cref="XmlCliRunner"/> — manifest resolution,
/// target-file selection (base/Environment/Client), writing, and the auto-diff. Decision logic
/// itself (match/ambiguity/defaults) is covered directly in <see cref="XmlFieldAuthorTests"/>.
/// </summary>
public class XmlSetCommandCliTests
{
    [Fact]
    public void Set_writes_a_new_client_overlay_and_prints_the_effective_diff()
    {
        using var workspace = new TempCliWorkspace();
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = XmlCliRunner.Run(new[]
        {
            "set", "--manifest", workspace.ManifestPath,
            "--client", "Globex", "--environment", "Production",
            "--match", "ApiUrl", "--set", "https://globex.example.com"
        }, stdout, stderr);

        Assert.Equal(0, exitCode);
        Assert.Empty(stderr.ToString());

        var overlayPath = Path.Combine(workspace.RootPath, ".configtransform", "ProjectA", "App.config",
            "Clients", "Globex", "Production.config");
        Assert.True(File.Exists(overlayPath));
        var overlayContent = File.ReadAllText(overlayPath);
        Assert.Contains("xdt:Transform=\"SetAttributes\"", overlayContent);
        Assert.Contains("xdt:Locator=\"Match(key)\"", overlayContent);
        Assert.Contains("https://globex.example.com", overlayContent);

        Assert.Contains("dev.example.com", stdout.ToString());
        Assert.Contains("globex.example.com", stdout.ToString());
    }

    [Fact]
    public void Set_dry_run_prints_the_would_be_overlay_content_and_writes_nothing()
    {
        using var workspace = new TempCliWorkspace();
        var before = Snapshot(workspace.RootPath);

        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = XmlCliRunner.Run(new[]
        {
            "set", "--manifest", workspace.ManifestPath,
            "--client", "Globex", "--environment", "Production",
            "--match", "ApiUrl", "--set", "https://globex.example.com", "--dry-run"
        }, stdout, stderr);

        Assert.Equal(0, exitCode);
        Assert.Contains("globex.example.com", stdout.ToString());
        Assert.Equal(before, Snapshot(workspace.RootPath));
    }

    [Fact]
    public void Set_with_only_environment_writes_the_environment_overlay()
    {
        using var workspace = new TempCliWorkspace();
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = XmlCliRunner.Run(new[]
        {
            "set", "--manifest", workspace.ManifestPath,
            "--environment", "Staging",
            "--match", "key=ApiUrl", "--set", "value=https://staging.example.com"
        }, stdout, stderr);

        Assert.Equal(0, exitCode);
        var overlayPath = Path.Combine(workspace.RootPath, ".configtransform", "ProjectA", "App.config",
            "Environments", "Staging.config");
        Assert.True(File.Exists(overlayPath));
        Assert.Contains("https://staging.example.com", File.ReadAllText(overlayPath));
    }

    [Fact]
    public void Set_with_no_client_or_environment_edits_the_base_file_directly()
    {
        using var workspace = new TempCliWorkspace();
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = XmlCliRunner.Run(new[]
        {
            "set", "--manifest", workspace.ManifestPath,
            "--match", "key=ApiUrl", "--set", "value=https://everyone.example.com"
        }, stdout, stderr);

        Assert.Equal(0, exitCode);
        var basePath = Path.Combine(workspace.RootPath, "Project", "App.config");
        var baseContent = File.ReadAllText(basePath);
        Assert.Contains("https://everyone.example.com", baseContent);
        Assert.DoesNotContain("xdt:", baseContent);
    }

    [Fact]
    public void Set_with_client_but_no_environment_fails_with_an_actionable_error()
    {
        using var workspace = new TempCliWorkspace();
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = XmlCliRunner.Run(new[]
        {
            "set", "--manifest", workspace.ManifestPath,
            "--client", "Globex", "--match", "key=ApiUrl", "--set", "value=X"
        }, stdout, stderr);

        Assert.Equal(1, exitCode);
        Assert.Contains("--client requires --environment", stderr.ToString());
    }

    [Fact]
    public void Set_targeting_a_key_that_does_not_exist_fails_rather_than_guessing()
    {
        using var workspace = new TempCliWorkspace();
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = XmlCliRunner.Run(new[]
        {
            "set", "--manifest", workspace.ManifestPath,
            "--client", "Globex", "--environment", "Production",
            "--match", "key=BrandNewKey", "--set", "value=X"
        }, stdout, stderr);

        Assert.Equal(1, exitCode);
        Assert.Contains("No element found", stderr.ToString());
    }

    [Fact]
    public void Set_re_run_against_an_existing_overlay_updates_it_in_place()
    {
        using var workspace = new TempCliWorkspace();
        var stdout1 = new StringWriter();
        var exitCode1 = XmlCliRunner.Run(new[]
        {
            "set", "--manifest", workspace.ManifestPath,
            "--client", "Globex", "--environment", "Production",
            "--match", "ApiUrl", "--set", "https://v1.example.com"
        }, stdout1, new StringWriter());
        Assert.Equal(0, exitCode1);

        var stdout2 = new StringWriter();
        var exitCode2 = XmlCliRunner.Run(new[]
        {
            "set", "--manifest", workspace.ManifestPath,
            "--client", "Globex", "--environment", "Production",
            "--match", "ApiUrl", "--set", "https://v2.example.com"
        }, stdout2, new StringWriter());
        Assert.Equal(0, exitCode2);

        var overlayPath = Path.Combine(workspace.RootPath, ".configtransform", "ProjectA", "App.config",
            "Clients", "Globex", "Production.config");
        var overlayContent = File.ReadAllText(overlayPath);
        Assert.Contains("https://v2.example.com", overlayContent);
        Assert.DoesNotContain("https://v1.example.com", overlayContent);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(overlayContent, "<add "));
    }

    private static Dictionary<string, DateTime> Snapshot(string root) =>
        Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(f => f, File.GetLastWriteTimeUtc);
}
