using ConfigTransform.Core;
using ConfigTransform.Xml.Tests.TestSupport;
using Xunit;

namespace ConfigTransform.Xml.Tests;

/// <summary>
/// End-to-end tests of the "set" verb through <see cref="XmlCliRunner"/> — layer resolution
/// (base/Environment/Client, creating a configtransform.json when it doesn't exist yet),
/// target-patch selection, writing, and the auto-diff. Decision logic itself (match/ambiguity/
/// defaults) is covered directly in <see cref="XmlFieldAuthorTests"/>.
/// </summary>
public class XmlSetCommandCliTests
{
    [Fact]
    public void Set_creates_a_new_client_layer_and_prints_the_effective_diff()
    {
        using var workspace = new TempCliWorkspace();
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = XmlCliRunner.Run(new[]
        {
            "set", "--resource", workspace.ResourcePath,
            "--client", "Globex", "--environment", "Production",
            "--match", "ApiUrl", "--set", "https://globex.example.com"
        }, stdout, stderr, workspace.RootPath);

        Assert.Equal(0, exitCode);
        Assert.Empty(stderr.ToString());

        var layerPath = Path.Combine(workspace.RootPath, ".configtransform", "Clients", "Globex", "Production", "configtransform.json");
        Assert.True(File.Exists(layerPath));
        var layer = LayerManifestLoader.Load(layerPath);
        // Exact equality, not just Contains -- `extends` must be repo-root-relative, not the
        // absolute path LayerPathResolver itself works with internally (Settled decisions #4:
        // every path in the file is repo-root-relative, no exceptions).
        Assert.Equal(".configtransform/Environments/Production/configtransform.json", layer.Extends);
        Assert.Equal(workspace.ResourcePath, layer.Resources.Single().Path);
        Assert.Equal(".configtransform/Clients/Globex/Production/patch-Project-App.config.xml", layer.Resources.Single().Patch);

        var patchPath = Path.Combine(workspace.RootPath, ".configtransform", "Clients", "Globex", "Production", "patch-Project-App.config.xml");
        Assert.True(File.Exists(patchPath));
        var patchContent = File.ReadAllText(patchPath);
        Assert.Contains("xdt:Transform=\"SetAttributes\"", patchContent);
        Assert.Contains("xdt:Locator=\"Match(key)\"", patchContent);
        Assert.Contains("https://globex.example.com", patchContent);

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
            "set", "--resource", workspace.ResourcePath,
            "--client", "Globex", "--environment", "Production",
            "--match", "ApiUrl", "--set", "https://globex.example.com", "--dry-run"
        }, stdout, stderr, workspace.RootPath);

        Assert.Equal(0, exitCode);
        Assert.Contains("globex.example.com", stdout.ToString());
        Assert.Equal(before, Snapshot(workspace.RootPath));
    }

    [Fact]
    public void Set_with_only_environment_creates_a_new_environment_layer()
    {
        using var workspace = new TempCliWorkspace();
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = XmlCliRunner.Run(new[]
        {
            "set", "--resource", workspace.ResourcePath,
            "--environment", "Staging",
            "--match", "key=ApiUrl", "--set", "value=https://staging.example.com"
        }, stdout, stderr, workspace.RootPath);

        Assert.Equal(0, exitCode);

        var layerPath = Path.Combine(workspace.RootPath, ".configtransform", "Environments", "Staging", "configtransform.json");
        Assert.True(File.Exists(layerPath));
        Assert.DoesNotContain("extends", File.ReadAllText(layerPath));

        var patchPath = Path.Combine(workspace.RootPath, ".configtransform", "Environments", "Staging", "patch-Project-App.config.xml");
        Assert.True(File.Exists(patchPath));
        Assert.Contains("https://staging.example.com", File.ReadAllText(patchPath));
    }

    [Fact]
    public void Set_with_no_client_or_environment_edits_the_base_file_directly()
    {
        using var workspace = new TempCliWorkspace();
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = XmlCliRunner.Run(new[]
        {
            "set", "--resource", workspace.ResourcePath,
            "--match", "key=ApiUrl", "--set", "value=https://everyone.example.com"
        }, stdout, stderr, workspace.RootPath);

        Assert.Equal(0, exitCode);
        var baseContent = File.ReadAllText(workspace.ProjectFilePath);
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
            "set", "--resource", workspace.ResourcePath,
            "--client", "Globex", "--match", "key=ApiUrl", "--set", "value=X"
        }, stdout, stderr, workspace.RootPath);

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
            "set", "--resource", workspace.ResourcePath,
            "--client", "Globex", "--environment", "Production",
            "--match", "key=BrandNewKey", "--set", "value=X"
        }, stdout, stderr, workspace.RootPath);

        Assert.Equal(1, exitCode);
        Assert.Contains("No element found", stderr.ToString());
    }

    [Fact]
    public void Set_re_run_against_an_existing_patch_updates_it_in_place()
    {
        using var workspace = new TempCliWorkspace();
        var stdout1 = new StringWriter();
        var exitCode1 = XmlCliRunner.Run(new[]
        {
            "set", "--resource", workspace.ResourcePath,
            "--client", "Globex", "--environment", "Production",
            "--match", "ApiUrl", "--set", "https://v1.example.com"
        }, stdout1, new StringWriter(), workspace.RootPath);
        Assert.Equal(0, exitCode1);

        var stdout2 = new StringWriter();
        var exitCode2 = XmlCliRunner.Run(new[]
        {
            "set", "--resource", workspace.ResourcePath,
            "--client", "Globex", "--environment", "Production",
            "--match", "ApiUrl", "--set", "https://v2.example.com"
        }, stdout2, new StringWriter(), workspace.RootPath);
        Assert.Equal(0, exitCode2);

        var patchPath = Path.Combine(workspace.RootPath, ".configtransform", "Clients", "Globex", "Production", "patch-Project-App.config.xml");
        var patchContent = File.ReadAllText(patchPath);
        Assert.Contains("https://v2.example.com", patchContent);
        Assert.DoesNotContain("https://v1.example.com", patchContent);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(patchContent, "<add "));

        var layerPath = Path.Combine(workspace.RootPath, ".configtransform", "Clients", "Globex", "Production", "configtransform.json");
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(layerPath), "\"path\""));
    }

    private static Dictionary<string, DateTime> Snapshot(string root) =>
        Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(f => f, File.GetLastWriteTimeUtc);
}
