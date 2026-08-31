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

    [Fact]
    public void Merges_an_arbitrary_schema()
    {
        var projectDir = Path.Combine(FixturesRoot, "Project");
        var overlayRoot = Path.Combine(FixturesRoot, "Overlay");

        var resolution = LayerResolution.Resolve(projectDir, "custom-settings.json", overlayRoot, "ClientA", "Production");
        var merged = JsonLayerMerger.Merge(resolution.BasePath, resolution.EnvironmentOverlayPath, resolution.ClientOverlayPath);

        using var doc = JsonDocument.Parse(merged);
        var primary = doc.RootElement.GetProperty("endpoints").GetProperty("primary");

        Assert.Equal(60, primary.GetProperty("timeoutSeconds").GetInt32()); // environment layer
        Assert.Equal("https://clienta.example.com/api", primary.GetProperty("url").GetString()); // client layer
        Assert.True(doc.RootElement.GetProperty("flags").GetProperty("betaEnabled").GetBoolean()); // client layer
    }

    [Fact]
    public void Environment_layer_alone_leaves_client_specific_values_untouched()
    {
        var projectDir = Path.Combine(FixturesRoot, "Project");
        var overlayRoot = Path.Combine(FixturesRoot, "Overlay");

        var resolution = LayerResolution.Resolve(projectDir, "custom-settings.json", overlayRoot, "ClientB", "Production");
        Assert.Null(resolution.ClientOverlayPath);

        var merged = JsonLayerMerger.Merge(resolution.BasePath, resolution.EnvironmentOverlayPath, resolution.ClientOverlayPath);

        using var doc = JsonDocument.Parse(merged);
        var primary = doc.RootElement.GetProperty("endpoints").GetProperty("primary");

        Assert.Equal(60, primary.GetProperty("timeoutSeconds").GetInt32()); // environment-wide
        Assert.Equal("https://dev.example.com/api", primary.GetProperty("url").GetString()); // untouched base
        Assert.False(doc.RootElement.GetProperty("flags").GetProperty("betaEnabled").GetBoolean()); // untouched base
    }
}
