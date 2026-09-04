using System.Text.Json;
using ConfigTransform.Core;
using Xunit;

namespace ConfigTransform.Json.Tests;

public class JsonLayerMergerTests
{
    private static string FixturesRoot => Path.Combine(AppContext.BaseDirectory, "Fixtures", "DotNetCore");

    [Fact]
    public void Merges_base_environment_and_client_layers_end_to_end()
    {
        var projectDir = Path.Combine(FixturesRoot, "Project");
        var overlayRoot = Path.Combine(FixturesRoot, "Overlay");

        var resolution = LayerResolution.Resolve(projectDir, "appsettings.json", overlayRoot, "ClientA", "Production");
        var merged = Merge(resolution);

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
        var projectDir = Path.Combine(FixturesRoot, "Project");
        var overlayRoot = Path.Combine(FixturesRoot, "Overlay");

        var resolution = LayerResolution.Resolve(projectDir, "appsettings.json", overlayRoot, "ClientA", "Production");
        var merged = Merge(resolution);

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
        var projectDir = Path.Combine(FixturesRoot, "Project");
        var overlayRoot = Path.Combine(FixturesRoot, "Overlay");

        var resolution = LayerResolution.Resolve(projectDir, "appsettings.json", overlayRoot, "ClientA", "Production");
        var merged = Merge(resolution);

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
        var projectDir = Path.Combine(FixturesRoot, "Project");
        var overlayRoot = Path.Combine(FixturesRoot, "Overlay");

        var resolution = LayerResolution.Resolve(projectDir, "appsettings.json", overlayRoot, "ClientB", "Staging");
        var merged = Merge(resolution);

        Assert.Null(resolution.EnvironmentOverlayPath);
        Assert.Null(resolution.ClientOverlayPath);

        using var doc = JsonDocument.Parse(merged);
        var root = doc.RootElement;

        Assert.Equal("https://dev.example.com", root.GetProperty("ApiUrl").GetString());
        Assert.Equal(3, root.GetProperty("RetryCount").GetInt32());
        Assert.Equal("Information", root.GetProperty("Logging").GetProperty("LogLevel").GetProperty("Default").GetString());
    }

    /// <summary>Adapts a fixed-slot LayerResolutionResult to JsonLayerMerger's arbitrary-length chain signature.</summary>
    private static string Merge(LayerResolutionResult resolution) =>
        JsonLayerMerger.Merge(resolution.BasePath, new[] { resolution.EnvironmentOverlayPath, resolution.ClientOverlayPath }
            .Where(p => p is not null).Select(p => p!).ToList());
}
