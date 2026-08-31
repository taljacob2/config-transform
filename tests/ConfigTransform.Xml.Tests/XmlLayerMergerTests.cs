using System.Xml.Linq;
using ConfigTransform.Core;
using Xunit;

namespace ConfigTransform.Xml.Tests;

public class XmlLayerMergerTests
{
    private static string FixturesRoot => Path.Combine(AppContext.BaseDirectory, "Fixtures", "DotNetFramework");

    [Fact]
    public void Merges_base_environment_and_client_layers_end_to_end()
    {
        var projectDir = Path.Combine(FixturesRoot, "Project");
        var overlayRoot = Path.Combine(FixturesRoot, "Overlay");

        var resolution = LayerResolution.Resolve(projectDir, "App.config", overlayRoot, "ClientA", "Production");
        var merged = XmlLayerMerger.Merge(resolution.BasePath, resolution.EnvironmentOverlayPath, resolution.ClientOverlayPath);

        var doc = XDocument.Parse(merged);
        var appSettings = doc.Root!.Element("appSettings")!;

        Assert.Equal("https://clienta.example.com", GetAppSetting(appSettings, "ApiUrl"));
        Assert.Equal("60", GetAppSetting(appSettings, "Timeout"));

        var connectionString = doc.Root!.Element("connectionStrings")!
            .Elements("add").Single(e => (string)e.Attribute("name")! == "Main")
            .Attribute("connectionString")!.Value;
        Assert.Equal("Server=clienta-prod-db;Database=App;", connectionString);
    }

    [Fact]
    public void Applies_only_base_when_no_overlays_match()
    {
        var projectDir = Path.Combine(FixturesRoot, "Project");
        var overlayRoot = Path.Combine(FixturesRoot, "Overlay");

        // Neither Environments/Staging.config nor Clients/ClientB/Staging.config exist.
        var resolution = LayerResolution.Resolve(projectDir, "App.config", overlayRoot, "ClientB", "Staging");
        var merged = XmlLayerMerger.Merge(resolution.BasePath, resolution.EnvironmentOverlayPath, resolution.ClientOverlayPath);

        Assert.Null(resolution.EnvironmentOverlayPath);
        Assert.Null(resolution.ClientOverlayPath);

        var doc = XDocument.Parse(merged);
        var appSettings = doc.Root!.Element("appSettings")!;

        Assert.Equal("https://dev.example.com", GetAppSetting(appSettings, "ApiUrl"));
        Assert.Equal("30", GetAppSetting(appSettings, "Timeout"));
    }

    [Fact]
    public void Applies_environment_layer_only_when_client_has_no_override()
    {
        var projectDir = Path.Combine(FixturesRoot, "Project");
        var overlayRoot = Path.Combine(FixturesRoot, "Overlay");

        // Environments/Production.config exists; Clients/ClientB/Production.config does not.
        var resolution = LayerResolution.Resolve(projectDir, "App.config", overlayRoot, "ClientB", "Production");
        var merged = XmlLayerMerger.Merge(resolution.BasePath, resolution.EnvironmentOverlayPath, resolution.ClientOverlayPath);

        Assert.NotNull(resolution.EnvironmentOverlayPath);
        Assert.Null(resolution.ClientOverlayPath);

        var doc = XDocument.Parse(merged);
        var appSettings = doc.Root!.Element("appSettings")!;

        // ApiUrl is untouched (only ClientA's overlay changes it); Timeout picks up the
        // environment-wide value shared across every client in Production.
        Assert.Equal("https://dev.example.com", GetAppSetting(appSettings, "ApiUrl"));
        Assert.Equal("60", GetAppSetting(appSettings, "Timeout"));
    }

    private static string GetAppSetting(XElement appSettings, string key) =>
        appSettings.Elements("add").Single(e => (string)e.Attribute("key")! == key).Attribute("value")!.Value;
}
