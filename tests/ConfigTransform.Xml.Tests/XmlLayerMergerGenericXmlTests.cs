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
    private const string ResourcePath = "Project/settings.custom.xml";

    [Fact]
    public void Merges_an_arbitrary_schema_with_a_non_config_extension()
    {
        var resolved = Resolve("ClientA", "Production");

        // Two patches (Environment, then Client) confirm LayerChain never special-cases ".config".
        Assert.Equal(2, resolved.PatchPathsInOrder.Count);
        Assert.All(resolved.PatchPathsInOrder, p => Assert.EndsWith(".xml", p));

        var merged = XmlLayerMerger.Merge(resolved.BasePath, resolved.PatchPathsInOrder);
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
        // ClientB's own configtransform.json declares only `extends` (no resources of its own)
        // -- see XmlLayerMergerTests's identical comment for why this file still needs to exist.
        var resolved = Resolve("ClientB", "Production");
        Assert.Single(resolved.PatchPathsInOrder);

        var merged = XmlLayerMerger.Merge(resolved.BasePath, resolved.PatchPathsInOrder);
        var doc = XDocument.Parse(merged);

        var endpoint = doc.Root!.Element("Endpoints")!.Element("Endpoint")!;
        Assert.Equal("60", endpoint.Attribute("timeoutSeconds")!.Value); // environment-wide
        Assert.Equal("https://dev.example.com/api", endpoint.Attribute("url")!.Value); // untouched base value

        var flag = doc.Root!.Element("FeatureFlags")!.Element("Flag")!;
        Assert.Equal("false", flag.Attribute("enabled")!.Value); // untouched base value
    }

    private static ResolvedResource Resolve(string client, string environment)
    {
        var chain = LayerChain.Build(FixturesRoot, LayerPathResolver.Resolve(FixturesRoot, client, environment));
        return LayerChain.ResolveResource(FixturesRoot, chain, ResourcePath);
    }
}
