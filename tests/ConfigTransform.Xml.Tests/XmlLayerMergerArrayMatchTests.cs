using System.Xml.Linq;
using ConfigTransform.Core;
using Xunit;

namespace ConfigTransform.Xml.Tests;

/// <summary>
/// Merge-time (not set-time) proof that a compound <c>xdt:Locator="Match(a,b)"</c> overlay
/// correctly disambiguates one item among real repeated siblings sharing a tag name, through
/// <see cref="XmlLayerMerger.Merge"/> and real <c>Microsoft.Web.Xdt</c> -- the XML analogue of
/// JsonLayerMergerElemMatchTests's role for JSON's <c>$elemMatch</c>. Unlike JSON, this needs no
/// merge-time pre-processing pass of its own: <c>xdt:Locator</c> is native XDT vocabulary that
/// <see cref="XmlLayerMerger"/> already hands straight to <c>XmlTransformation.Apply</c> (see
/// docs/FIELD_AUTHORING_DESIGN.md's "Open items" -- matching an *existing* array item was
/// already mechanically answerable via the same machinery an ordinary XML element match uses;
/// this fixture is the missing proof, not new production code).
/// </summary>
public class XmlLayerMergerArrayMatchTests
{
    private static string FixturesRoot => Path.Combine(AppContext.BaseDirectory, "Fixtures", "IisWebConfig", "ArrayMatch");
    private const string ResourcePath = "Project/Web.config";

    [Fact]
    public void Environment_layer_compound_Locator_updates_only_the_matching_sibling()
    {
        var resolved = Resolve(client: null, "Production");
        var merged = XmlLayerMerger.Merge(resolved.BasePath, resolved.PatchPathsInOrder);

        var rules = XDocument.Parse(merged).Root!
            .Element("system.webServer")!.Element("rewrite")!.Element("rules")!
            .Elements("rule").ToList();

        Assert.Equal(2, rules.Count);
        var stopTrue = rules.Single(r => (string)r.Attribute("stopProcessing")! == "true");
        var stopFalse = rules.Single(r => (string)r.Attribute("stopProcessing")! == "false");
        Assert.Equal("true", stopTrue.Attribute("enabled")!.Value); // matched by the compound Locator
        Assert.Equal("false", stopFalse.Attribute("enabled")!.Value); // untouched -- proves no ambiguity leaked through
    }

    [Fact]
    public void Client_layer_compound_Locator_updates_the_other_sibling_without_colliding_with_the_environment_layer()
    {
        var resolved = Resolve("ClientA", "Production");
        var merged = XmlLayerMerger.Merge(resolved.BasePath, resolved.PatchPathsInOrder);

        var rules = XDocument.Parse(merged).Root!
            .Element("system.webServer")!.Element("rewrite")!.Element("rules")!
            .Elements("rule").ToList();

        Assert.Equal(2, rules.Count);
        // Both siblings end up enabled=true -- one via the Environment layer's Locator, the
        // other via the Client layer's -- proving each compound Locator independently resolves
        // to the correct sibling within the same repeated group, at real merge time, with real
        // Microsoft.Web.Xdt, not just at set-authoring time.
        Assert.All(rules, r => Assert.Equal("true", r.Attribute("enabled")!.Value));
    }

    private static ResolvedResource Resolve(string? client, string environment)
    {
        var chain = LayerChain.Build(FixturesRoot, LayerPathResolver.Resolve(FixturesRoot, client, environment));
        return LayerChain.ResolveResource(FixturesRoot, chain, ResourcePath);
    }
}
