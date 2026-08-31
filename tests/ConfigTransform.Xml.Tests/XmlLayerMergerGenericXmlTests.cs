using System.Xml.Linq;
using ConfigTransform.Core;
using Xunit;

namespace ConfigTransform.Xml.Tests;

/// <summary>
/// Exercises XmlLayerMerger against a made-up, non-standard schema and a non-".config"
/// extension (.xml) — proving there is no App.config-, Web.config-, or ".config"-specific
/// logic anywhere in the merge path. If this passes using the exact same code as the
/// DotNetFramework/IisWebConfig fixtures, the "format-generic by design" claim (CLAUDE.md,
/// CONFIG_MANAGEMENT.md §5.2) is demonstrated, not just asserted. See
/// docs/CONFIGTRANSFORM_TOOL_DESIGN.md §3.1.
/// </summary>
public class XmlLayerMergerGenericXmlTests
{
    private static string FixturesRoot => Path.Combine(AppContext.BaseDirectory, "Fixtures", "GenericXml");

    [Fact]
    public void Merges_an_arbitrary_schema_with_a_non_config_extension()
    {
        var projectDir = Path.Combine(FixturesRoot, "Project");
        var overlayRoot = Path.Combine(FixturesRoot, "Overlay");

        var resolution = LayerResolution.Resolve(projectDir, "settings.custom.xml", overlayRoot, "ClientA", "Production");

        // The overlay file name is derived from the base file's own extension (.xml here, not
        // .config) — confirms LayerResolution never hardcodes ".config".
        Assert.EndsWith("Production.xml", resolution.EnvironmentOverlayPath);
        Assert.EndsWith("Production.xml", resolution.ClientOverlayPath);

        var merged = XmlLayerMerger.Merge(resolution.BasePath, resolution.EnvironmentOverlayPath, resolution.ClientOverlayPath);
        var doc = XDocument.Parse(merged);

        var endpoint = doc.Root!.Element("Endpoints")!.Element("Endpoint")!;
        Assert.Equal("60", endpoint.Attribute("timeoutSeconds")!.Value);
        Assert.Equal("https://clienta.example.com/api", endpoint.Attribute("url")!.Value);

        var flag = doc.Root!.Element("FeatureFlags")!.Element("Flag")!;
        Assert.Equal("true", flag.Attribute("enabled")!.Value);
    }

    [Fact]
    public void Environment_layer_alone_leaves_client_specific_values_untouched()
    {
        var projectDir = Path.Combine(FixturesRoot, "Project");
        var overlayRoot = Path.Combine(FixturesRoot, "Overlay");

        var resolution = LayerResolution.Resolve(projectDir, "settings.custom.xml", overlayRoot, "ClientB", "Production");
        Assert.Null(resolution.ClientOverlayPath);

        var merged = XmlLayerMerger.Merge(resolution.BasePath, resolution.EnvironmentOverlayPath, resolution.ClientOverlayPath);
        var doc = XDocument.Parse(merged);

        var endpoint = doc.Root!.Element("Endpoints")!.Element("Endpoint")!;
        Assert.Equal("60", endpoint.Attribute("timeoutSeconds")!.Value); // environment-wide
        Assert.Equal("https://dev.example.com/api", endpoint.Attribute("url")!.Value); // untouched base value

        var flag = doc.Root!.Element("FeatureFlags")!.Element("Flag")!;
        Assert.Equal("false", flag.Attribute("enabled")!.Value); // untouched base value
    }
}
