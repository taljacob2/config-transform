using ConfigTransform.Json.Tests.TestSupport;
using Xunit;

namespace ConfigTransform.Json.Tests;

/// <summary>
/// End-to-end tests of the "set" verb through <see cref="JsonCliRunner"/> — manifest resolution,
/// target-file selection (base/Environment/Client), writing, and the auto-diff. Decision logic
/// itself is covered directly in <see cref="JsonFieldAuthorTests"/>.
/// </summary>
public class JsonSetCommandCliTests
{
    [Fact]
    public void Set_writes_a_new_client_overlay_and_prints_the_effective_diff()
    {
        using var workspace = new TempCliWorkspace();
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = JsonCliRunner.Run(new[]
        {
            "set", "--manifest", workspace.ManifestPath,
            "--client", "Globex", "--environment", "Production",
            "--match", "ApiUrl", "--set", "https://globex.example.com"
        }, stdout, stderr);

        Assert.Equal(0, exitCode);
        Assert.Empty(stderr.ToString());

        var overlayPath = Path.Combine(workspace.RootPath, ".configtransform", "ProjectA", "appsettings.json",
            "Clients", "Globex", "Production.json");
        Assert.True(File.Exists(overlayPath));
        Assert.Contains("https://globex.example.com", File.ReadAllText(overlayPath));

        Assert.Contains("dev.example.com", stdout.ToString());
        Assert.Contains("globex.example.com", stdout.ToString());
    }

    [Fact]
    public void Set_dry_run_prints_the_would_be_overlay_content_and_writes_nothing()
    {
        using var workspace = new TempCliWorkspace();
        var before = Snapshot(workspace.RootPath);

        var stdout = new StringWriter();
        var exitCode = JsonCliRunner.Run(new[]
        {
            "set", "--manifest", workspace.ManifestPath,
            "--client", "Globex", "--environment", "Production",
            "--match", "ApiUrl", "--set", "https://globex.example.com", "--dry-run"
        }, stdout, new StringWriter());

        Assert.Equal(0, exitCode);
        Assert.Contains("globex.example.com", stdout.ToString());
        Assert.Equal(before, Snapshot(workspace.RootPath));
    }

    [Fact]
    public void Set_with_only_environment_writes_the_environment_overlay()
    {
        using var workspace = new TempCliWorkspace();
        var exitCode = JsonCliRunner.Run(new[]
        {
            "set", "--manifest", workspace.ManifestPath,
            "--environment", "Staging",
            "--match", "key=ApiUrl", "--set", "value=https://staging.example.com"
        }, new StringWriter(), new StringWriter());

        Assert.Equal(0, exitCode);
        var overlayPath = Path.Combine(workspace.RootPath, ".configtransform", "ProjectA", "appsettings.json",
            "Environments", "Staging.json");
        Assert.True(File.Exists(overlayPath));
        Assert.Contains("https://staging.example.com", File.ReadAllText(overlayPath));
    }

    [Fact]
    public void Set_with_no_client_or_environment_edits_the_base_file_directly_and_preserves_other_keys()
    {
        using var workspace = new TempCliWorkspace();
        var basePath = Path.Combine(workspace.RootPath, "Project", "appsettings.json");
        File.WriteAllText(basePath, """{ "ApiUrl": "https://dev.example.com", "Logging": { "LogLevel": { "Default": "Information" } } }""");

        var exitCode = JsonCliRunner.Run(new[]
        {
            "set", "--manifest", workspace.ManifestPath,
            "--match", "key=ApiUrl", "--set", "value=https://everyone.example.com"
        }, new StringWriter(), new StringWriter());

        Assert.Equal(0, exitCode);
        var baseContent = File.ReadAllText(basePath);
        Assert.Contains("https://everyone.example.com", baseContent);
        // Regression test: a base-target write must not drop the rest of the document.
        Assert.Contains("\"Default\": \"Information\"", baseContent);
    }

    [Fact]
    public void Set_creates_a_brand_new_key_unlike_XMLs_Insert_gap()
    {
        using var workspace = new TempCliWorkspace();
        var exitCode = JsonCliRunner.Run(new[]
        {
            "set", "--manifest", workspace.ManifestPath,
            "--client", "Globex", "--environment", "Production",
            "--match", "key=Features:EnableBeta", "--set", "value=true"
        }, new StringWriter(), new StringWriter());

        Assert.Equal(0, exitCode);
        var overlayPath = Path.Combine(workspace.RootPath, ".configtransform", "ProjectA", "appsettings.json",
            "Clients", "Globex", "Production.json");
        Assert.Contains("\"EnableBeta\": true", File.ReadAllText(overlayPath));
    }

    [Fact]
    public void Set_with_client_but_no_environment_fails_with_an_actionable_error()
    {
        using var workspace = new TempCliWorkspace();
        var stderr = new StringWriter();
        var exitCode = JsonCliRunner.Run(new[]
        {
            "set", "--manifest", workspace.ManifestPath,
            "--client", "Globex", "--match", "key=ApiUrl", "--set", "value=X"
        }, new StringWriter(), stderr);

        Assert.Equal(1, exitCode);
        Assert.Contains("--client requires --environment", stderr.ToString());
    }

    [Fact]
    public void Set_element_match_writes_the_elemMatch_overlay_and_diff_shows_the_resolved_value()
    {
        using var workspace = new TempCliWorkspace();
        var basePath = Path.Combine(workspace.RootPath, "Project", "appsettings.json");
        File.WriteAllText(basePath, """
            { "ApiUrl": "https://dev.example.com",
              "Rules": [ { "role": "Admin", "enabled": false } ] }
            """);

        var stdout = new StringWriter();
        var exitCode = JsonCliRunner.Run(new[]
        {
            "set", "--manifest", workspace.ManifestPath,
            "--client", "Globex", "--environment", "Production",
            "--match", "key=Rules", "--match", "role=Admin", "--set", "enabled=true"
        }, stdout, new StringWriter());

        Assert.Equal(0, exitCode);
        var overlayPath = Path.Combine(workspace.RootPath, ".configtransform", "ProjectA", "appsettings.json",
            "Clients", "Globex", "Production.json");
        var overlayContent = File.ReadAllText(overlayPath);
        Assert.Contains("$elemMatch", overlayContent);

        // The auto-diff reflects the real, resolved value -- not the raw $elemMatch overlay text.
        var diff = stdout.ToString();
        Assert.DoesNotContain("$elemMatch", diff);
        Assert.Contains("true", diff);
    }

    [Fact]
    public void Set_element_match_second_call_appends_a_second_patch_to_the_same_overlay()
    {
        using var workspace = new TempCliWorkspace();
        var basePath = Path.Combine(workspace.RootPath, "Project", "appsettings.json");
        File.WriteAllText(basePath, """
            { "Rules": [
              { "role": "Admin", "enabled": false },
              { "role": "Viewer", "enabled": false }
            ] }
            """);

        foreach (var role in new[] { "Admin", "Viewer" })
        {
            var exitCode = JsonCliRunner.Run(new[]
            {
                "set", "--manifest", workspace.ManifestPath,
                "--client", "Globex", "--environment", "Production",
                "--match", "key=Rules", "--match", $"role={role}", "--set", "enabled=true"
            }, new StringWriter(), new StringWriter());
            Assert.Equal(0, exitCode);
        }

        var overlayPath = Path.Combine(workspace.RootPath, ".configtransform", "ProjectA", "appsettings.json",
            "Clients", "Globex", "Production.json");
        var overlayContent = File.ReadAllText(overlayPath);
        Assert.Contains("\"role\": \"Admin\"", overlayContent);
        Assert.Contains("\"role\": \"Viewer\"", overlayContent);
    }

    [Fact]
    public void Set_element_match_dry_run_prints_the_patch_list_without_writing()
    {
        using var workspace = new TempCliWorkspace();
        var basePath = Path.Combine(workspace.RootPath, "Project", "appsettings.json");
        File.WriteAllText(basePath, """{ "Rules": [ { "role": "Admin", "enabled": false } ] }""");
        var before = Snapshot(workspace.RootPath);

        var stdout = new StringWriter();
        var exitCode = JsonCliRunner.Run(new[]
        {
            "set", "--manifest", workspace.ManifestPath,
            "--client", "Globex", "--environment", "Production",
            "--match", "key=Rules", "--match", "role=Admin", "--set", "enabled=true", "--dry-run"
        }, stdout, new StringWriter());

        Assert.Equal(0, exitCode);
        Assert.Contains("$elemMatch", stdout.ToString());
        Assert.Equal(before, Snapshot(workspace.RootPath));
    }

    [Fact]
    public void Set_element_match_base_target_writes_directly_into_the_base_files_real_array()
    {
        using var workspace = new TempCliWorkspace();
        var basePath = Path.Combine(workspace.RootPath, "Project", "appsettings.json");
        File.WriteAllText(basePath, """{ "Rules": [ { "role": "Admin", "enabled": false } ] }""");

        var exitCode = JsonCliRunner.Run(new[]
        {
            "set", "--manifest", workspace.ManifestPath,
            "--match", "key=Rules", "--match", "role=Admin", "--set", "enabled=true"
        }, new StringWriter(), new StringWriter());

        Assert.Equal(0, exitCode);
        var baseContent = File.ReadAllText(basePath);
        Assert.DoesNotContain("$elemMatch", baseContent);
        Assert.Contains("\"enabled\": true", baseContent);
    }

    [Fact]
    public void Set_re_run_against_an_existing_overlay_updates_it_in_place()
    {
        using var workspace = new TempCliWorkspace();
        var exitCode1 = JsonCliRunner.Run(new[]
        {
            "set", "--manifest", workspace.ManifestPath,
            "--client", "Globex", "--environment", "Production",
            "--match", "ApiUrl", "--set", "https://v1.example.com"
        }, new StringWriter(), new StringWriter());
        Assert.Equal(0, exitCode1);

        var exitCode2 = JsonCliRunner.Run(new[]
        {
            "set", "--manifest", workspace.ManifestPath,
            "--client", "Globex", "--environment", "Production",
            "--match", "ApiUrl", "--set", "https://v2.example.com"
        }, new StringWriter(), new StringWriter());
        Assert.Equal(0, exitCode2);

        var overlayPath = Path.Combine(workspace.RootPath, ".configtransform", "ProjectA", "appsettings.json",
            "Clients", "Globex", "Production.json");
        var overlayContent = File.ReadAllText(overlayPath);
        Assert.Contains("https://v2.example.com", overlayContent);
        Assert.DoesNotContain("https://v1.example.com", overlayContent);
    }

    private static Dictionary<string, DateTime> Snapshot(string root) =>
        Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(f => f, File.GetLastWriteTimeUtc);
}
