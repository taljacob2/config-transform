using ConfigTransform.Cli.Tests.TestSupport;
using ConfigTransform.Core;
using Xunit;

namespace ConfigTransform.Cli.Tests;

/// <summary>
/// End-to-end tests of the "set" verb against a JSON resource, through the unified
/// <see cref="CliRunner"/> — layer resolution (base/Environment/Client, creating a
/// configtransform.json when it doesn't exist yet), target-patch selection, writing, and the
/// auto-diff. Decision logic itself is covered directly in <c>JsonFieldAuthorTests</c>
/// (ConfigTransform.Json.Tests).
/// </summary>
public class JsonSetCommandCliTests
{
    [Fact]
    public void Set_creates_a_new_client_layer_and_prints_the_effective_diff()
    {
        using var workspace = new TempCliWorkspace();
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = CliRunner.Run(new[]
        {
            "set", "--resource", workspace.JsonResourcePath,
            "--client", "Globex", "--environment", "Production",
            "--match", "ApiUrl", "--set", "https://globex.example.com"
        }, stdout, stderr, FormatEngines.All, workspace.RootPath);

        Assert.Equal(0, exitCode);
        Assert.Empty(stderr.ToString());

        var layerPath = Path.Combine(workspace.RootPath, ".configtransform", "Clients", "Globex", "Production", "configtransform.json");
        Assert.True(File.Exists(layerPath));
        var layer = LayerManifestLoader.Load(layerPath);
        // Exact equality, not just Contains -- `extends` must be repo-root-relative, not the
        // absolute path LayerPathResolver itself works with internally (Settled decisions #4:
        // every path in the file is repo-root-relative, no exceptions).
        Assert.Equal(".configtransform/Environments/Production/configtransform.json", layer.Extends);
        Assert.Equal(workspace.JsonResourcePath, layer.Resources.Single().Path);
        Assert.Equal(".configtransform/Clients/Globex/Production/patch-Project-appsettings.json", layer.Resources.Single().Patch);

        var patchPath = Path.Combine(workspace.RootPath, ".configtransform", "Clients", "Globex", "Production", "patch-Project-appsettings.json");
        Assert.True(File.Exists(patchPath));
        Assert.Contains("https://globex.example.com", File.ReadAllText(patchPath));

        Assert.Contains("dev.example.com", stdout.ToString());
        Assert.Contains("globex.example.com", stdout.ToString());
    }

    [Fact]
    public void Set_dry_run_prints_the_would_be_overlay_content_and_writes_nothing()
    {
        using var workspace = new TempCliWorkspace();
        var before = Snapshot(workspace.RootPath);

        var stdout = new StringWriter();
        var exitCode = CliRunner.Run(new[]
        {
            "set", "--resource", workspace.JsonResourcePath,
            "--client", "Globex", "--environment", "Production",
            "--match", "ApiUrl", "--set", "https://globex.example.com", "--dry-run"
        }, stdout, new StringWriter(), FormatEngines.All, workspace.RootPath);

        Assert.Equal(0, exitCode);
        Assert.Contains("globex.example.com", stdout.ToString());
        Assert.Equal(before, Snapshot(workspace.RootPath));
    }

    [Fact]
    public void Set_with_only_environment_creates_a_new_environment_layer()
    {
        using var workspace = new TempCliWorkspace();
        var exitCode = CliRunner.Run(new[]
        {
            "set", "--resource", workspace.JsonResourcePath,
            "--environment", "Staging",
            "--match", "key=ApiUrl", "--set", "value=https://staging.example.com"
        }, new StringWriter(), new StringWriter(), FormatEngines.All, workspace.RootPath);

        Assert.Equal(0, exitCode);
        var layerPath = Path.Combine(workspace.RootPath, ".configtransform", "Environments", "Staging", "configtransform.json");
        Assert.True(File.Exists(layerPath));
        Assert.DoesNotContain("extends", File.ReadAllText(layerPath));

        var patchPath = Path.Combine(workspace.RootPath, ".configtransform", "Environments", "Staging", "patch-Project-appsettings.json");
        Assert.True(File.Exists(patchPath));
        Assert.Contains("https://staging.example.com", File.ReadAllText(patchPath));
    }

    [Fact]
    public void Set_with_no_client_or_environment_edits_the_base_file_directly_and_preserves_other_keys()
    {
        using var workspace = new TempCliWorkspace();
        File.WriteAllText(workspace.JsonProjectFilePath,
            """{ "ApiUrl": "https://dev.example.com", "Logging": { "LogLevel": { "Default": "Information" } } }""");

        var exitCode = CliRunner.Run(new[]
        {
            "set", "--resource", workspace.JsonResourcePath,
            "--match", "key=ApiUrl", "--set", "value=https://everyone.example.com"
        }, new StringWriter(), new StringWriter(), FormatEngines.All, workspace.RootPath);

        Assert.Equal(0, exitCode);
        var baseContent = File.ReadAllText(workspace.JsonProjectFilePath);
        Assert.Contains("https://everyone.example.com", baseContent);
        // Regression test: a base-target write must not drop the rest of the document.
        Assert.Contains("\"Default\": \"Information\"", baseContent);
    }

    [Fact]
    public void Set_creates_a_brand_new_key_unlike_XMLs_Insert_gap()
    {
        using var workspace = new TempCliWorkspace();
        var exitCode = CliRunner.Run(new[]
        {
            "set", "--resource", workspace.JsonResourcePath,
            "--client", "Globex", "--environment", "Production",
            "--match", "key=Features:EnableBeta", "--set", "value=true"
        }, new StringWriter(), new StringWriter(), FormatEngines.All, workspace.RootPath);

        Assert.Equal(0, exitCode);
        var patchPath = Path.Combine(workspace.RootPath, ".configtransform", "Clients", "Globex", "Production", "patch-Project-appsettings.json");
        Assert.Contains("\"EnableBeta\": true", File.ReadAllText(patchPath));
    }

    [Fact]
    public void Set_with_client_but_no_environment_fails_with_an_actionable_error()
    {
        using var workspace = new TempCliWorkspace();
        var stderr = new StringWriter();
        var exitCode = CliRunner.Run(new[]
        {
            "set", "--resource", workspace.JsonResourcePath,
            "--client", "Globex", "--match", "key=ApiUrl", "--set", "value=X"
        }, new StringWriter(), stderr, FormatEngines.All, workspace.RootPath);

        Assert.Equal(1, exitCode);
        Assert.Contains("--client requires --environment", stderr.ToString());
    }

    [Fact]
    public void Set_element_match_writes_the_elemMatch_overlay_and_diff_shows_the_resolved_value()
    {
        using var workspace = new TempCliWorkspace();
        File.WriteAllText(workspace.JsonProjectFilePath, """
            { "ApiUrl": "https://dev.example.com",
              "Rules": [ { "role": "Admin", "enabled": false } ] }
            """);

        var stdout = new StringWriter();
        var exitCode = CliRunner.Run(new[]
        {
            "set", "--resource", workspace.JsonResourcePath,
            "--client", "Globex", "--environment", "Production",
            "--match", "key=Rules", "--match", "role=Admin", "--set", "enabled=true"
        }, stdout, new StringWriter(), FormatEngines.All, workspace.RootPath);

        Assert.Equal(0, exitCode);
        var patchPath = Path.Combine(workspace.RootPath, ".configtransform", "Clients", "Globex", "Production", "patch-Project-appsettings.json");
        var patchContent = File.ReadAllText(patchPath);
        Assert.Contains("$elemMatch", patchContent);

        // The auto-diff reflects the real, resolved value -- not the raw $elemMatch overlay text.
        var diff = stdout.ToString();
        Assert.DoesNotContain("$elemMatch", diff);
        Assert.Contains("true", diff);
    }

    [Fact]
    public void Set_element_match_second_call_appends_a_second_patch_to_the_same_overlay()
    {
        using var workspace = new TempCliWorkspace();
        File.WriteAllText(workspace.JsonProjectFilePath, """
            { "Rules": [
              { "role": "Admin", "enabled": false },
              { "role": "Viewer", "enabled": false }
            ] }
            """);

        foreach (var role in new[] { "Admin", "Viewer" })
        {
            var exitCode = CliRunner.Run(new[]
            {
                "set", "--resource", workspace.JsonResourcePath,
                "--client", "Globex", "--environment", "Production",
                "--match", "key=Rules", "--match", $"role={role}", "--set", "enabled=true"
            }, new StringWriter(), new StringWriter(), FormatEngines.All, workspace.RootPath);
            Assert.Equal(0, exitCode);
        }

        var patchPath = Path.Combine(workspace.RootPath, ".configtransform", "Clients", "Globex", "Production", "patch-Project-appsettings.json");
        var patchContent = File.ReadAllText(patchPath);
        Assert.Contains("\"role\": \"Admin\"", patchContent);
        Assert.Contains("\"role\": \"Viewer\"", patchContent);
    }

    [Fact]
    public void Set_element_match_dry_run_prints_the_patch_list_without_writing()
    {
        using var workspace = new TempCliWorkspace();
        File.WriteAllText(workspace.JsonProjectFilePath, """{ "Rules": [ { "role": "Admin", "enabled": false } ] }""");
        var before = Snapshot(workspace.RootPath);

        var stdout = new StringWriter();
        var exitCode = CliRunner.Run(new[]
        {
            "set", "--resource", workspace.JsonResourcePath,
            "--client", "Globex", "--environment", "Production",
            "--match", "key=Rules", "--match", "role=Admin", "--set", "enabled=true", "--dry-run"
        }, stdout, new StringWriter(), FormatEngines.All, workspace.RootPath);

        Assert.Equal(0, exitCode);
        Assert.Contains("$elemMatch", stdout.ToString());
        Assert.Equal(before, Snapshot(workspace.RootPath));
    }

    [Fact]
    public void Set_element_match_base_target_writes_directly_into_the_base_files_real_array()
    {
        using var workspace = new TempCliWorkspace();
        File.WriteAllText(workspace.JsonProjectFilePath, """{ "Rules": [ { "role": "Admin", "enabled": false } ] }""");

        var exitCode = CliRunner.Run(new[]
        {
            "set", "--resource", workspace.JsonResourcePath,
            "--match", "key=Rules", "--match", "role=Admin", "--set", "enabled=true"
        }, new StringWriter(), new StringWriter(), FormatEngines.All, workspace.RootPath);

        Assert.Equal(0, exitCode);
        var baseContent = File.ReadAllText(workspace.JsonProjectFilePath);
        Assert.DoesNotContain("$elemMatch", baseContent);
        Assert.Contains("\"enabled\": true", baseContent);
    }

    [Fact]
    public void Set_re_run_against_an_existing_patch_updates_it_in_place()
    {
        using var workspace = new TempCliWorkspace();
        var exitCode1 = CliRunner.Run(new[]
        {
            "set", "--resource", workspace.JsonResourcePath,
            "--client", "Globex", "--environment", "Production",
            "--match", "ApiUrl", "--set", "https://v1.example.com"
        }, new StringWriter(), new StringWriter(), FormatEngines.All, workspace.RootPath);
        Assert.Equal(0, exitCode1);

        var exitCode2 = CliRunner.Run(new[]
        {
            "set", "--resource", workspace.JsonResourcePath,
            "--client", "Globex", "--environment", "Production",
            "--match", "ApiUrl", "--set", "https://v2.example.com"
        }, new StringWriter(), new StringWriter(), FormatEngines.All, workspace.RootPath);
        Assert.Equal(0, exitCode2);

        var patchPath = Path.Combine(workspace.RootPath, ".configtransform", "Clients", "Globex", "Production", "patch-Project-appsettings.json");
        var patchContent = File.ReadAllText(patchPath);
        Assert.Contains("https://v2.example.com", patchContent);
        Assert.DoesNotContain("https://v1.example.com", patchContent);

        var layerPath = Path.Combine(workspace.RootPath, ".configtransform", "Clients", "Globex", "Production", "configtransform.json");
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(layerPath), "\"path\""));
    }

    private static Dictionary<string, DateTime> Snapshot(string root) =>
        Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(f => f, File.GetLastWriteTimeUtc);
}
