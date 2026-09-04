using System.Text.Json;
using ConfigTransform.Core;
using Xunit;

namespace ConfigTransform.Json.Tests;

public class JsonLayerMergerTests
{
    private static string FixturesRoot => Path.Combine(AppContext.BaseDirectory, "Fixtures", "DotNetCore");
    private const string ResourcePath = "Project/appsettings.json";

    [Fact]
    public void Merges_base_environment_and_client_layers_end_to_end()
    {
        var resolved = Resolve("ClientA", "Production");
        var merged = JsonLayerMerger.Merge(resolved.BasePath, resolved.PatchPathsInOrder);

        using var doc = JsonDocument.Parse(merged);
        var root = doc.RootElement;

        Assert.Equal("https://clienta.example.com", root.GetProperty("ApiUrl").GetString()); // client layer
        Assert.True(root.GetProperty("Features").GetProperty("EnableBeta").GetBoolean()); // client layer
        Assert.Equal("Warning", root.GetProperty("Logging").GetProperty("LogLevel").GetProperty("Default").GetString()); // env layer
        Assert.Equal("*", root.GetProperty("AllowedHosts").GetString()); // untouched base value
    }

    [Fact]
    public void Preserves_json_types_rather_than_flattening_everything_to_strings()
    {
        // IConfiguration stores every leaf as a plain string internally; a naive round-trip
        // would turn `"RetryCount": 5` into `"RetryCount": "5"`. This pins that it doesn't.
        var resolved = Resolve("ClientA", "Production");
        var merged = JsonLayerMerger.Merge(resolved.BasePath, resolved.PatchPathsInOrder);

        using var doc = JsonDocument.Parse(merged);
        var root = doc.RootElement;

        Assert.Equal(JsonValueKind.Number, root.GetProperty("RetryCount").ValueKind);
        Assert.Equal(5, root.GetProperty("RetryCount").GetInt32());
        Assert.Equal(JsonValueKind.True, root.GetProperty("Features").GetProperty("EnableBeta").ValueKind);
        Assert.Equal(JsonValueKind.String, root.GetProperty("ApiUrl").ValueKind);
    }

    [Fact]
    public void An_overlay_array_overrides_by_index_not_wholesale()
    {
        // The environment overlay's AllowedOrigins has one element; the base has two. This
        // documents the real, easy-to-get-wrong behavior: Microsoft.Extensions.Configuration
        // flattens arrays to indexed keys ("AllowedOrigins:0", "AllowedOrigins:1", ...), so an
        // overlay array only overrides the indices it specifies -- it does not replace the
        // base array wholesale. Index 1 survives from the base layer untouched.
        var resolved = Resolve("ClientA", "Production");
        var merged = JsonLayerMerger.Merge(resolved.BasePath, resolved.PatchPathsInOrder);

        using var doc = JsonDocument.Parse(merged);
        var origins = doc.RootElement.GetProperty("AllowedOrigins");

        Assert.Equal(JsonValueKind.Array, origins.ValueKind);
        Assert.Equal(2, origins.GetArrayLength());
        Assert.Equal("https://prod.example.com", origins[0].GetString()); // overridden by the environment layer
        Assert.Equal("https://b.example.com", origins[1].GetString());    // untouched, survives from the base layer
    }

    [Fact]
    public void Applies_only_base_when_no_overlays_match()
    {
        var resolved = Resolve("ClientB", "Staging");
        Assert.Empty(resolved.PatchPathsInOrder);

        var merged = JsonLayerMerger.Merge(resolved.BasePath, resolved.PatchPathsInOrder);

        using var doc = JsonDocument.Parse(merged);
        var root = doc.RootElement;

        Assert.Equal("https://dev.example.com", root.GetProperty("ApiUrl").GetString());
        Assert.Equal(3, root.GetProperty("RetryCount").GetInt32());
        Assert.Equal("Information", root.GetProperty("Logging").GetProperty("LogLevel").GetProperty("Default").GetString());
    }

    private static ResolvedResource Resolve(string client, string environment)
    {
        var chain = LayerChain.Build(FixturesRoot, LayerPathResolver.Resolve(FixturesRoot, client, environment));
        return LayerChain.ResolveResource(FixturesRoot, chain, ResourcePath);
    }
}
