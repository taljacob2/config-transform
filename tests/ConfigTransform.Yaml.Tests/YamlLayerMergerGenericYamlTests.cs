using ConfigTransform.Core;
using Xunit;

namespace ConfigTransform.Yaml.Tests;

/// <summary>
/// Exercises YamlLayerMerger against a made-up, non-standard schema and a base filename unlike
/// appsettings.yaml — proving there is no appsettings.yaml-specific logic anywhere in the merge
/// path, the same role GenericXml/GenericJson/GenericEnv play for their formats. See
/// docs/CONFIGTRANSFORM_TOOL_DESIGN.md §3.2.
/// </summary>
public class YamlLayerMergerGenericYamlTests
{
    private static string FixturesRoot => Path.Combine(AppContext.BaseDirectory, "Fixtures", "GenericYaml");
    private const string ResourcePath = "Project/custom-settings.yaml";

    [Fact]
    public void Merges_an_arbitrary_schema()
    {
        var resolved = Resolve("ClientA", "Production");
        var merged = YamlLayerMerger.Merge(resolved.BasePath, resolved.PatchPathsInOrder);

        Assert.Contains("timeoutSeconds: 60", merged); // environment layer
        Assert.Contains("url: https://clienta.example.com/api", merged); // client layer
        Assert.Contains("betaEnabled: true", merged); // client layer
    }

    [Fact]
    public void Environment_layer_alone_leaves_client_specific_values_untouched()
    {
        // ClientB's own configtransform.json declares only `extends` (no resources of its own)
        // -- a Client layer must still exist on disk (even resource-less) for the Environment
        // layer's content to flow through to it at all.
        var resolved = Resolve("ClientB", "Production");
        Assert.Single(resolved.PatchPathsInOrder);

        var merged = YamlLayerMerger.Merge(resolved.BasePath, resolved.PatchPathsInOrder);

        Assert.Contains("timeoutSeconds: 60", merged); // environment-wide
        Assert.Contains("url: https://dev.example.com/api", merged); // untouched base
        Assert.Contains("betaEnabled: false", merged); // untouched base
    }

    private static ResolvedResource Resolve(string client, string environment)
    {
        var chain = LayerChain.Build(FixturesRoot, LayerPathResolver.Resolve(FixturesRoot, client, environment));
        return LayerChain.ResolveResource(FixturesRoot, chain, ResourcePath);
    }
}
