using System.Text.Json;
using ConfigTransform.Core;
using Xunit;

namespace ConfigTransform.Json.Tests;

/// <summary>
/// Exercises JsonLayerMerger against a made-up, non-standard schema and a base filename
/// unlike appsettings.json — proving there is no appsettings.json-specific logic anywhere in
/// the merge path. See docs/CONFIGTRANSFORM_TOOL_DESIGN.md §3.2.
/// </summary>
public class JsonLayerMergerGenericJsonTests
{
    private static string FixturesRoot => Path.Combine(AppContext.BaseDirectory, "Fixtures", "GenericJson");
    private const string ResourcePath = "Project/custom-settings.json";

    [Fact]
    public void Merges_an_arbitrary_schema()
    {
        var resolved = Resolve("ClientA", "Production");
        var merged = JsonLayerMerger.Merge(resolved.BasePath, resolved.PatchPathsInOrder);

        using var doc = JsonDocument.Parse(merged);
        var primary = doc.RootElement.GetProperty("endpoints").GetProperty("primary");

        Assert.Equal(60, primary.GetProperty("timeoutSeconds").GetInt32()); // environment layer
        Assert.Equal("https://clienta.example.com/api", primary.GetProperty("url").GetString()); // client layer
        Assert.True(doc.RootElement.GetProperty("flags").GetProperty("betaEnabled").GetBoolean()); // client layer
    }

    [Fact]
    public void Environment_layer_alone_leaves_client_specific_values_untouched()
    {
        // ClientB's own configtransform.json declares only `extends` (no resources of its own)
        // -- the "accepted cost" workaround the design doc names explicitly: unlike the old
        // fixed base->Environments->Clients rule, a Client layer must still exist on disk (even
        // resource-less) for the Environment layer's content to flow through to it at all.
        var resolved = Resolve("ClientB", "Production");
        Assert.Single(resolved.PatchPathsInOrder);

        var merged = JsonLayerMerger.Merge(resolved.BasePath, resolved.PatchPathsInOrder);

        using var doc = JsonDocument.Parse(merged);
        var primary = doc.RootElement.GetProperty("endpoints").GetProperty("primary");

        Assert.Equal(60, primary.GetProperty("timeoutSeconds").GetInt32()); // environment-wide
        Assert.Equal("https://dev.example.com/api", primary.GetProperty("url").GetString()); // untouched base
        Assert.False(doc.RootElement.GetProperty("flags").GetProperty("betaEnabled").GetBoolean()); // untouched base
    }

    private static ResolvedResource Resolve(string client, string environment)
    {
        var chain = LayerChain.Build(FixturesRoot, LayerPathResolver.Resolve(FixturesRoot, client, environment));
        return LayerChain.ResolveResource(FixturesRoot, chain, ResourcePath);
    }
}
