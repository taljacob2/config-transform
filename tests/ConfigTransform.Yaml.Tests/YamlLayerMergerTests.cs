using ConfigTransform.Core;
using Xunit;

namespace ConfigTransform.Yaml.Tests;

public class YamlLayerMergerTests
{
    private static string FixturesRoot => Path.Combine(AppContext.BaseDirectory, "Fixtures", "DotNetCore");
    private const string ResourcePath = "Project/appsettings.yaml";

    [Fact]
    public void Merges_base_environment_and_client_layers_end_to_end()
    {
        var resolved = Resolve("ClientA", "Production");
        var merged = YamlLayerMerger.Merge(resolved.BasePath, resolved.PatchPathsInOrder);

        Assert.Contains("ApiUrl: https://clienta.example.com", merged); // client layer
        Assert.Contains("EnableBeta: true", merged); // client layer
        Assert.Contains("Default: Warning", merged); // env layer
        Assert.Contains("AllowedHosts:", merged); // untouched base value
    }

    [Fact]
    public void Preserves_yaml_scalar_types_rather_than_flattening_everything_to_strings()
    {
        // IConfiguration stores every leaf as a plain string internally; a naive round-trip
        // would turn `RetryCount: 5` into `RetryCount: "5"`. This pins that it doesn't.
        var resolved = Resolve("ClientA", "Production");
        var merged = YamlLayerMerger.Merge(resolved.BasePath, resolved.PatchPathsInOrder);

        Assert.Contains("RetryCount: 5", merged);
        Assert.DoesNotContain("RetryCount: '5'", merged);
        Assert.DoesNotContain("RetryCount: \"5\"", merged);
        Assert.Contains("EnableBeta: true", merged);
        Assert.DoesNotContain("EnableBeta: 'true'", merged);
    }

    [Fact]
    public void An_overlay_array_overrides_by_index_not_wholesale()
    {
        // The environment overlay's AllowedOrigins has one element; the base has two. Same
        // documented behavior as JsonLayerMerger, for the same underlying reason: both engines
        // flatten through Microsoft.Extensions.Configuration ("AllowedOrigins:0",
        // "AllowedOrigins:1", ...), so an overlay array only overrides the indices it specifies.
        var resolved = Resolve("ClientA", "Production");
        var merged = YamlLayerMerger.Merge(resolved.BasePath, resolved.PatchPathsInOrder);

        Assert.Contains("https://prod.example.com", merged); // overridden by the environment layer
        Assert.Contains("https://b.example.com", merged);    // untouched, survives from the base layer
    }

    [Fact]
    public void Applies_only_base_when_no_overlays_match()
    {
        var resolved = Resolve("ClientB", "Staging");
        Assert.Empty(resolved.PatchPathsInOrder);

        var merged = YamlLayerMerger.Merge(resolved.BasePath, resolved.PatchPathsInOrder);

        Assert.Contains("ApiUrl: https://dev.example.com", merged);
        Assert.Contains("RetryCount: 3", merged);
        Assert.Contains("Default: Information", merged);
    }

    private static ResolvedResource Resolve(string client, string environment)
    {
        var chain = LayerChain.Build(FixturesRoot, LayerPathResolver.Resolve(FixturesRoot, client, environment));
        return LayerChain.ResolveResource(FixturesRoot, chain, ResourcePath);
    }
}
