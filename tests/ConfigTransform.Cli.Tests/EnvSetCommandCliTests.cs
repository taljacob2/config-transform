using ConfigTransform.Cli.Tests.TestSupport;
using ConfigTransform.Core;
using Xunit;

namespace ConfigTransform.Cli.Tests;

/// <summary>
/// End-to-end tests of the "set" verb against a `.env` resource, through the unified
/// <see cref="CliRunner"/> — mirrors XmlSetCommandCliTests/JsonSetCommandCliTests, minus the
/// cases that don't apply to a flat format (no ambiguous-match case, since a `.env` key has only
/// one possible interpretation). Decision logic itself is covered directly in
/// <c>EnvFieldAuthorTests</c> (ConfigTransform.Env.Tests).
/// </summary>
public class EnvSetCommandCliTests
{
    private const string ResourcePath = "Project/.env";

    private static string CreateBaseEnvFile(TempCliWorkspace workspace)
    {
        var projectDir = Path.Combine(workspace.RootPath, "Project");
        Directory.CreateDirectory(projectDir);
        var path = Path.Combine(projectDir, ".env");
        File.WriteAllText(path, "API_URL=https://dev.example.com\n");
        return path;
    }

    [Fact]
    public void Set_creates_a_new_client_layer_and_prints_the_effective_diff()
    {
        using var workspace = new TempCliWorkspace();
        CreateBaseEnvFile(workspace);
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = CliRunner.Run(new[]
        {
            "set", "--resource", ResourcePath,
            "--client", "Globex", "--environment", "Production",
            "--match", "API_URL", "--set", "https://globex.example.com"
        }, stdout, stderr, FormatEngines.All, workspace.RootPath);

        Assert.Equal(0, exitCode);
        Assert.Empty(stderr.ToString());

        var layerPath = Path.Combine(workspace.RootPath, ".configtransform", "Clients", "Globex", "Production", "configtransform.json");
        Assert.True(File.Exists(layerPath));
        var layer = LayerManifestLoader.Load(layerPath);
        Assert.Equal(".configtransform/Environments/Production/configtransform.json", layer.Extends);
        Assert.Equal(ResourcePath, layer.Resources.Single().Path);
        Assert.Equal(".configtransform/Clients/Globex/Production/patch-Project-.env", layer.Resources.Single().Patch);

        var patchPath = Path.Combine(workspace.RootPath, ".configtransform", "Clients", "Globex", "Production", "patch-Project-.env");
        Assert.True(File.Exists(patchPath));
        Assert.Equal("API_URL=https://globex.example.com\n", File.ReadAllText(patchPath));

        Assert.Contains("dev.example.com", stdout.ToString());
        Assert.Contains("globex.example.com", stdout.ToString());
    }

    [Fact]
    public void Set_dry_run_prints_the_would_be_overlay_content_and_writes_nothing()
    {
        using var workspace = new TempCliWorkspace();
        CreateBaseEnvFile(workspace);
        var before = Snapshot(workspace.RootPath);

        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = CliRunner.Run(new[]
        {
            "set", "--resource", ResourcePath,
            "--client", "Globex", "--environment", "Production",
            "--match", "API_URL", "--set", "https://globex.example.com", "--dry-run"
        }, stdout, stderr, FormatEngines.All, workspace.RootPath);

        Assert.Equal(0, exitCode);
        Assert.Contains("globex.example.com", stdout.ToString());
        Assert.Equal(before, Snapshot(workspace.RootPath));
    }

    [Fact]
    public void Set_with_only_environment_creates_a_new_environment_layer()
    {
        using var workspace = new TempCliWorkspace();
        CreateBaseEnvFile(workspace);
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = CliRunner.Run(new[]
        {
            "set", "--resource", ResourcePath,
            "--environment", "Staging",
            "--match", "key=API_URL", "--set", "value=https://staging.example.com"
        }, stdout, stderr, FormatEngines.All, workspace.RootPath);

        Assert.Equal(0, exitCode);

        var layerPath = Path.Combine(workspace.RootPath, ".configtransform", "Environments", "Staging", "configtransform.json");
        Assert.True(File.Exists(layerPath));
        Assert.DoesNotContain("extends", File.ReadAllText(layerPath));

        var patchPath = Path.Combine(workspace.RootPath, ".configtransform", "Environments", "Staging", "patch-Project-.env");
        Assert.True(File.Exists(patchPath));
        Assert.Contains("https://staging.example.com", File.ReadAllText(patchPath));
    }

    [Fact]
    public void Set_with_no_client_or_environment_edits_the_base_file_directly()
    {
        using var workspace = new TempCliWorkspace();
        var basePath = CreateBaseEnvFile(workspace);
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = CliRunner.Run(new[]
        {
            "set", "--resource", ResourcePath,
            "--match", "key=API_URL", "--set", "value=https://everyone.example.com"
        }, stdout, stderr, FormatEngines.All, workspace.RootPath);

        Assert.Equal(0, exitCode);
        Assert.Equal("API_URL=https://everyone.example.com\n", File.ReadAllText(basePath));
    }

    [Fact]
    public void Set_targeting_an_invalid_key_name_fails_rather_than_writing_an_unusable_file()
    {
        using var workspace = new TempCliWorkspace();
        CreateBaseEnvFile(workspace);
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = CliRunner.Run(new[]
        {
            "set", "--resource", ResourcePath,
            "--client", "Globex", "--environment", "Production",
            "--match", "key=not-a-valid-key", "--set", "value=X"
        }, stdout, stderr, FormatEngines.All, workspace.RootPath);

        Assert.Equal(1, exitCode);
        Assert.Contains("not a valid .env key", stderr.ToString());
    }

    [Fact]
    public void Set_re_run_against_an_existing_patch_updates_it_in_place()
    {
        using var workspace = new TempCliWorkspace();
        CreateBaseEnvFile(workspace);

        var exitCode1 = CliRunner.Run(new[]
        {
            "set", "--resource", ResourcePath,
            "--client", "Globex", "--environment", "Production",
            "--match", "API_URL", "--set", "https://v1.example.com"
        }, new StringWriter(), new StringWriter(), FormatEngines.All, workspace.RootPath);
        Assert.Equal(0, exitCode1);

        var exitCode2 = CliRunner.Run(new[]
        {
            "set", "--resource", ResourcePath,
            "--client", "Globex", "--environment", "Production",
            "--match", "API_URL", "--set", "https://v2.example.com"
        }, new StringWriter(), new StringWriter(), FormatEngines.All, workspace.RootPath);
        Assert.Equal(0, exitCode2);

        var patchPath = Path.Combine(workspace.RootPath, ".configtransform", "Clients", "Globex", "Production", "patch-Project-.env");
        Assert.Equal("API_URL=https://v2.example.com\n", File.ReadAllText(patchPath));
    }

    private static Dictionary<string, DateTime> Snapshot(string root) =>
        Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(f => f, File.GetLastWriteTimeUtc);
}
