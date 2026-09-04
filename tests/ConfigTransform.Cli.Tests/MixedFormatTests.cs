using ConfigTransform.Cli.Tests.TestSupport;
using ConfigTransform.Core;
using Xunit;

namespace ConfigTransform.Cli.Tests;

/// <summary>
/// Coverage that was structurally impossible before CLI unification: a single `configtransform`
/// call touching resources of more than one format, an extension no registered engine handles,
/// and `set` dispatching to the right engine within one shared layer file — the two-tool split
/// meant no single test process could previously reach any of these, since each tool only ever
/// saw resources of its own format.
/// </summary>
public class MixedFormatTests
{
    [Fact]
    public void Resource_with_an_unregistered_extension_fails_and_names_the_supported_formats()
    {
        using var workspace = new TempCliWorkspace();
        var yamlPath = Path.Combine(workspace.RootPath, "Project", "config.yaml");
        File.WriteAllText(yamlPath, "placeholder: true");

        var stderr = new StringWriter();
        var exitCode = CliRunner.Run(new[]
        {
            "--resource", "Project/config.yaml",
            "--client", "ClientA", "--environment", "Production", "--dry-run"
        }, new StringWriter(), stderr, FormatEngines.All, workspace.RootPath);

        Assert.Equal(1, exitCode);
        Assert.Contains("no format engine handles", stderr.ToString());
        Assert.Contains(".config", stderr.ToString());
        Assert.Contains(".xml", stderr.ToString());
        Assert.Contains(".json", stderr.ToString());
    }

    [Fact]
    public void Omitting_resource_notes_an_unregistered_extension_on_stderr_and_still_processes_the_rest()
    {
        using var workspace = new TempCliWorkspace();
        var yamlPath = Path.Combine(workspace.RootPath, "Project", "config.yaml");
        File.WriteAllText(yamlPath, "placeholder: true");

        var environmentLayer = LayerManifestLoader.Load(workspace.EnvironmentLayerPath);
        var updated = environmentLayer with
        {
            Resources = [.. environmentLayer.Resources, new ResourceEntry("Project/config.yaml", null)],
        };
        File.WriteAllText(workspace.EnvironmentLayerPath, System.Text.Json.JsonSerializer.Serialize(updated));

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exitCode = CliRunner.Run(new[]
        {
            "--client", "ClientA", "--environment", "Production", "--dry-run"
        }, stdout, stderr, FormatEngines.All, workspace.RootPath);

        Assert.Equal(0, exitCode);
        Assert.Contains("Skipped 1 resource(s)", stderr.ToString());

        // Never silently dropped: the two real resources still resolve despite the one unhandled one.
        var output = stdout.ToString();
        Assert.Contains($"=== {workspace.XmlResourcePath} ===", output);
        Assert.Contains($"=== {workspace.JsonResourcePath} ===", output);
        Assert.Contains("https://clienta.example.com", output);
    }

    [Fact]
    public void Set_dispatches_to_the_right_engine_by_resource_extension_within_one_shared_layer()
    {
        using var workspace = new TempCliWorkspace();

        var xmlExit = CliRunner.Run(new[]
        {
            "set", "--resource", workspace.XmlResourcePath,
            "--client", "Globex", "--environment", "Production",
            "--match", "ApiUrl", "--set", "https://globex-xml.example.com"
        }, new StringWriter(), new StringWriter(), FormatEngines.All, workspace.RootPath);
        Assert.Equal(0, xmlExit);

        var jsonExit = CliRunner.Run(new[]
        {
            "set", "--resource", workspace.JsonResourcePath,
            "--client", "Globex", "--environment", "Production",
            "--match", "key=ApiUrl", "--set", "value=https://globex-json.example.com"
        }, new StringWriter(), new StringWriter(), FormatEngines.All, workspace.RootPath);
        Assert.Equal(0, jsonExit);

        var layerPath = Path.Combine(workspace.RootPath, ".configtransform", "Clients", "Globex", "Production", "configtransform.json");
        var layer = LayerManifestLoader.Load(layerPath);

        // One shared configtransform.json now lists both resources -- set on the second call
        // appended to it rather than clobbering the first's entry.
        Assert.Equal(2, layer.Resources.Count);

        var xmlEntry = layer.Resources.Single(r => r.Path == workspace.XmlResourcePath);
        Assert.EndsWith(".xml", xmlEntry.Patch);
        var xmlPatchContent = File.ReadAllText(Path.Combine(workspace.RootPath, xmlEntry.Patch!));
        Assert.Contains("xdt:Transform=\"SetAttributes\"", xmlPatchContent);
        Assert.Contains("https://globex-xml.example.com", xmlPatchContent);

        var jsonEntry = layer.Resources.Single(r => r.Path == workspace.JsonResourcePath);
        Assert.EndsWith(".json", jsonEntry.Patch);
        var jsonPatchContent = File.ReadAllText(Path.Combine(workspace.RootPath, jsonEntry.Patch!));
        Assert.Contains("https://globex-json.example.com", jsonPatchContent);
        Assert.DoesNotContain("xdt:", jsonPatchContent);
    }
}
