using ConfigTransform.Core;
using Xunit;

namespace ConfigTransform.Env.Tests;

/// <summary>
/// Exercises EnvLayerMerger against an arbitrary, made-up set of keys unrelated to any real
/// application -- proving there is no app-specific logic anywhere in the merge path, the same
/// role GenericXml/GenericJson play for their formats. See docs/CONFIGTRANSFORM_TOOL_DESIGN.md §3.2.
/// </summary>
public class EnvLayerMergerGenericEnvTests
{
    private static string FixturesRoot => Path.Combine(AppContext.BaseDirectory, "Fixtures", "GenericEnv");
    private const string ResourcePath = "Project/settings.env";

    [Fact]
    public void Merges_an_arbitrary_set_of_keys()
    {
        var resolved = Resolve("ClientA", "Production");
        var merged = EnvFile.Parse(EnvLayerMerger.Merge(resolved.BasePath, resolved.PatchPathsInOrder))
            .ToDictionary(p => p.Key, p => p.Value);

        Assert.Equal("red", merged["WIDGET_COLOR"]); // client layer
        Assert.Equal("manual", merged["SPROCKET_MODE"]); // environment layer
        Assert.Equal("42", merged["WIDGET_SIZE"]); // untouched base value
    }

    [Fact]
    public void Environment_layer_alone_leaves_client_specific_values_untouched()
    {
        // ClientB's own configtransform.json declares only `extends` (no resources of its own)
        // -- the "accepted cost" workaround the design doc names explicitly: a Client layer must
        // still exist on disk (even resource-less) for the Environment layer's content to flow
        // through to it at all.
        var resolved = Resolve("ClientB", "Production");
        Assert.Single(resolved.PatchPathsInOrder);

        var merged = EnvFile.Parse(EnvLayerMerger.Merge(resolved.BasePath, resolved.PatchPathsInOrder))
            .ToDictionary(p => p.Key, p => p.Value);

        Assert.Equal("manual", merged["SPROCKET_MODE"]); // environment-wide
        Assert.Equal("blue", merged["WIDGET_COLOR"]); // untouched base
    }

    private static ResolvedResource Resolve(string client, string environment)
    {
        var chain = LayerChain.Build(FixturesRoot, LayerPathResolver.Resolve(FixturesRoot, client, environment));
        return LayerChain.ResolveResource(FixturesRoot, chain, ResourcePath);
    }
}
