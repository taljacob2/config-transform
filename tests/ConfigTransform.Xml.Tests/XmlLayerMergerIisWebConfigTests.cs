using System.Xml.Linq;
using ConfigTransform.Core;
using Xunit;

namespace ConfigTransform.Xml.Tests;

/// <summary>
/// Exercises XmlLayerMerger against Web.config-shaped fixtures deliberately containing
/// structures App.config never has — &lt;location&gt;-wrapped elements and nested
/// system.webServer/rewrite/rules — to prove Locator matching works beyond flat appSettings.
/// See docs/CONFIGTRANSFORM_TOOL_DESIGN.md §3.1.
/// </summary>
public class XmlLayerMergerIisWebConfigTests
{
    private static string FixturesRoot => Path.Combine(AppContext.BaseDirectory, "Fixtures", "IisWebConfig");

    [Fact]
    public void Merges_environment_and_client_layers_including_location_wrapped_elements()
    {
        var projectDir = Path.Combine(FixturesRoot, "Project");
        var overlayRoot = Path.Combine(FixturesRoot, "Overlay");

        var resolution = LayerResolution.Resolve(projectDir, "Web.config", overlayRoot, "ClientA", "Production");
        var merged = XmlLayerMerger.Merge(resolution.BasePath, resolution.EnvironmentOverlayPath, resolution.ClientOverlayPath);

        var doc = XDocument.Parse(merged);

        var systemWeb = doc.Root!.Element("system.web")!;
        Assert.Equal("false", systemWeb.Element("compilation")!.Attribute("debug")!.Value);
        Assert.Equal("RemoteOnly", systemWeb.Element("customErrors")!.Attribute("mode")!.Value);

        var rule = doc.Root!.Element("system.webServer")!.Element("rewrite")!.Element("rules")!
            .Elements("rule").Single(r => (string)r.Attribute("name")! == "HTTPS Redirect");
        Assert.Equal("true", rule.Attribute("enabled")!.Value);

        var authorization = doc.Root!.Elements("location")
            .Single(l => (string)l.Attribute("path")! == "Admin")
            .Element("system.web")!.Element("authorization")!;

        Assert.Contains(authorization.Elements("allow"), e => (string)e.Attribute("users")! == "clienta-admin");
        Assert.Contains(authorization.Elements("deny"), e => (string)e.Attribute("users")! == "?");
    }

    [Fact]
    public void Environment_layer_alone_does_not_touch_the_location_wrapped_authorization()
    {
        var projectDir = Path.Combine(FixturesRoot, "Project");
        var overlayRoot = Path.Combine(FixturesRoot, "Overlay");

        // ClientB has no override — only the environment-wide Production layer applies.
        var resolution = LayerResolution.Resolve(projectDir, "Web.config", overlayRoot, "ClientB", "Production");
        var merged = XmlLayerMerger.Merge(resolution.BasePath, resolution.EnvironmentOverlayPath, resolution.ClientOverlayPath);

        Assert.Null(resolution.ClientOverlayPath);

        var doc = XDocument.Parse(merged);
        var authorization = doc.Root!.Elements("location")
            .Single(l => (string)l.Attribute("path")! == "Admin")
            .Element("system.web")!.Element("authorization")!;

        Assert.Empty(authorization.Elements("allow"));
        Assert.Single(authorization.Elements("deny"));
    }
}
