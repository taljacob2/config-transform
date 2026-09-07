using ConfigTransform.Cli.Tests.TestSupport;
using ConfigTransform.Core;
using Xunit;

namespace ConfigTransform.Cli.Tests;

/// <summary>
/// End-to-end tests of the "set" verb against a YAML resource, through the unified
/// <see cref="CliRunner"/> — mirrors XmlSetCommandCliTests/JsonSetCommandCliTests/
/// EnvSetCommandCliTests, minus any array-of-objects (element-match) cases, which YAML's first
/// version doesn't support (see YamlFieldAuthor's own doc comment). Decision logic itself is
/// covered directly in <c>YamlFieldAuthorTests</c> (ConfigTransform.Yaml.Tests).
/// </summary>
public class YamlSetCommandCliTests
{
    private const string ResourcePath = "Project/settings.yaml";

    private static string CreateBaseYamlFile(TempCliWorkspace workspace)
    {
        var projectDir = Path.Combine(workspace.RootPath, "Project");
        Directory.CreateDirectory(projectDir);
        var path = Path.Combine(projectDir, "settings.yaml");
        File.WriteAllText(path, "ApiUrl: https://dev.example.com\n");
        return path;
    }

    [Fact]
    public void Set_creates_a_new_client_layer_and_prints_the_effective_diff()
    {
        using var workspace = new TempCliWorkspace();
        CreateBaseYamlFile(workspace);
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = CliRunner.Run(new[]
        {
            "set", "--resource", ResourcePath,
            "--client", "Globex", "--environment", "Production",
            "--match", "ApiUrl", "--set", "https://globex.example.com"
        }, stdout, stderr, FormatEngines.All, workspace.RootPath);

        Assert.Equal(0, exitCode);
        Assert.Empty(stderr.ToString());

        var layerPath = Path.Combine(workspace.RootPath, ".configtransform", "Clients", "Globex", "Production", "configtransform.json");
        Assert.True(File.Exists(layerPath));
        var layer = LayerManifestLoader.Load(layerPath);
        Assert.Equal(".configtransform/Environments/Production/configtransform.json", layer.Extends);
        Assert.Equal(ResourcePath, layer.Resources.Single().Path);
        Assert.Equal(".configtransform/Clients/Globex/Production/patch-Project-settings.yaml", layer.Resources.Single().Patch);

        var patchPath = Path.Combine(workspace.RootPath, ".configtransform", "Clients", "Globex", "Production", "patch-Project-settings.yaml");
        Assert.True(File.Exists(patchPath));
        Assert.Contains("ApiUrl: https://globex.example.com", File.ReadAllText(patchPath));

        Assert.Contains("dev.example.com", stdout.ToString());
        Assert.Contains("globex.example.com", stdout.ToString());
    }

    [Fact]
    public void Set_dry_run_prints_the_would_be_overlay_content_and_writes_nothing()
    {
        using var workspace = new TempCliWorkspace();
        CreateBaseYamlFile(workspace);
        var before = Snapshot(workspace.RootPath);

        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = CliRunner.Run(new[]
        {
            "set", "--resource", ResourcePath,
            "--client", "Globex", "--environment", "Production",
            "--match", "ApiUrl", "--set", "https://globex.example.com", "--dry-run"
        }, stdout, stderr, FormatEngines.All, workspace.RootPath);

        Assert.Equal(0, exitCode);
        Assert.Contains("globex.example.com", stdout.ToString());
        Assert.Equal(before, Snapshot(workspace.RootPath));
    }

    [Fact]
    public void Set_with_only_environment_creates_a_new_environment_layer()
    {
        using var workspace = new TempCliWorkspace();
        CreateBaseYamlFile(workspace);
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = CliRunner.Run(new[]
        {
            "set", "--resource", ResourcePath,
            "--environment", "Staging",
            "--match", "key=ApiUrl", "--set", "value=https://staging.example.com"
        }, stdout, stderr, FormatEngines.All, workspace.RootPath);

        Assert.Equal(0, exitCode);

        var layerPath = Path.Combine(workspace.RootPath, ".configtransform", "Environments", "Staging", "configtransform.json");
        Assert.True(File.Exists(layerPath));
        Assert.DoesNotContain("extends", File.ReadAllText(layerPath));

        var patchPath = Path.Combine(workspace.RootPath, ".configtransform", "Environments", "Staging", "patch-Project-settings.yaml");
        Assert.True(File.Exists(patchPath));
        Assert.Contains("https://staging.example.com", File.ReadAllText(patchPath));
    }

    [Fact]
    public void Set_with_no_client_or_environment_edits_the_base_file_directly()
    {
        using var workspace = new TempCliWorkspace();
        var basePath = CreateBaseYamlFile(workspace);
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = CliRunner.Run(new[]
        {
            "set", "--resource", ResourcePath,
            "--match", "key=ApiUrl", "--set", "value=https://everyone.example.com"
        }, stdout, stderr, FormatEngines.All, workspace.RootPath);

        Assert.Equal(0, exitCode);
        Assert.Contains("ApiUrl: https://everyone.example.com", File.ReadAllText(basePath));
    }

    [Fact]
    public void Set_targeting_an_array_of_objects_is_refused_as_not_yet_supported()
    {
        using var workspace = new TempCliWorkspace();
        CreateBaseYamlFile(workspace);
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = CliRunner.Run(new[]
        {
            "set", "--resource", ResourcePath,
            "--client", "Globex", "--environment", "Production",
            "--match", "key=Rules", "--match", "role=Admin", "--set", "enabled=true"
        }, stdout, stderr, FormatEngines.All, workspace.RootPath);

        Assert.Equal(1, exitCode);
        Assert.Contains("not yet supported", stderr.ToString());
    }

    [Fact]
    public void Set_re_run_against_an_existing_patch_updates_it_in_place()
    {
        using var workspace = new TempCliWorkspace();
        CreateBaseYamlFile(workspace);

        var exitCode1 = CliRunner.Run(new[]
        {
            "set", "--resource", ResourcePath,
            "--client", "Globex", "--environment", "Production",
            "--match", "ApiUrl", "--set", "https://v1.example.com"
        }, new StringWriter(), new StringWriter(), FormatEngines.All, workspace.RootPath);
        Assert.Equal(0, exitCode1);

        var exitCode2 = CliRunner.Run(new[]
        {
            "set", "--resource", ResourcePath,
            "--client", "Globex", "--environment", "Production",
            "--match", "ApiUrl", "--set", "https://v2.example.com"
        }, new StringWriter(), new StringWriter(), FormatEngines.All, workspace.RootPath);
        Assert.Equal(0, exitCode2);

        var patchPath = Path.Combine(workspace.RootPath, ".configtransform", "Clients", "Globex", "Production", "patch-Project-settings.yaml");
        var content = File.ReadAllText(patchPath);
        Assert.Contains("https://v2.example.com", content);
        Assert.DoesNotContain("v1.example.com", content);
    }

    private static Dictionary<string, DateTime> Snapshot(string root) =>
        Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(f => f, File.GetLastWriteTimeUtc);
}
