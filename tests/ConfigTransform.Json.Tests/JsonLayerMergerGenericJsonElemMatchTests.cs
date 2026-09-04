using System.Text.Json;
using ConfigTransform.Core;
using Xunit;

namespace ConfigTransform.Json.Tests;

/// <summary>
/// Same scenarios as <see cref="JsonLayerMergerElemMatchTests"/>, re-run against a deliberately
/// non-appsettings-shaped, made-up schema (docs/CONFIGTRANSFORM_TOOL_DESIGN.md §3.2's
/// GenericJson purpose, per CLAUDE.md: prove there is no hardcoded key/field name anywhere in
/// <see cref="JsonElemMatchResolver"/>). The array here lives one level deeper
/// (<c>endpoints:routes</c>) than the DotNetCore fixture's top-level <c>Rules</c>.
/// </summary>
public class JsonLayerMergerGenericJsonElemMatchTests
{
    private static string FixturesRoot => Path.Combine(AppContext.BaseDirectory, "Fixtures", "GenericJson", "ElemMatch");

    private static (string BasePath, string? EnvPath, string? ClientPath) Resolve(string client, string environment)
    {
        var projectDir = Path.Combine(FixturesRoot, "Project");
        var overlayRoot = Path.Combine(FixturesRoot, "Overlay");
        var resolution = LayerResolution.Resolve(projectDir, "custom-settings.json", overlayRoot, client, environment);
        return (resolution.BasePath, resolution.EnvironmentOverlayPath, resolution.ClientOverlayPath);
    }

    [Fact]
    public void Environment_layer_elemMatch_resolves_against_the_base_array()
    {
        var (basePath, envPath, _) = Resolve("ClientA", "Production");
        var merged = Merge(basePath, envPath, null);

        using var doc = JsonDocument.Parse(merged);
        var routes = doc.RootElement.GetProperty("endpoints").GetProperty("routes");

        Assert.Equal(3, routes.GetArrayLength());
        Assert.Equal("secondary", routes[1].GetProperty("name").GetString());
        Assert.True(routes[1].GetProperty("active").GetBoolean());
        Assert.Equal("tertiary", routes[2].GetProperty("name").GetString());
    }

    [Fact]
    public void Client_layer_elemMatch_resolves_against_base_plus_environment_merged_not_base_alone()
    {
        var (basePath, envPath, clientPath) = Resolve("ClientA", "Production");
        var merged = Merge(basePath, envPath, clientPath);

        using var doc = JsonDocument.Parse(merged);
        var routes = doc.RootElement.GetProperty("endpoints").GetProperty("routes");

        var tertiaries = Enumerable.Range(0, routes.GetArrayLength())
            .Select(i => routes[i])
            .Where(r => r.GetProperty("name").GetString() == "tertiary")
            .ToList();
        Assert.Single(tertiaries);
        Assert.False(tertiaries[0].GetProperty("active").GetBoolean());

        var primary = Enumerable.Range(0, routes.GetArrayLength())
            .Select(i => routes[i])
            .Single(r => r.GetProperty("name").GetString() == "primary");
        Assert.True(primary.GetProperty("active").GetBoolean());
    }

    [Fact]
    public void Sibling_plain_array_at_a_different_key_is_unaffected()
    {
        var (basePath, envPath, clientPath) = Resolve("ClientA", "Production");
        var merged = Merge(basePath, envPath, clientPath);

        using var doc = JsonDocument.Parse(merged);
        var tags = doc.RootElement.GetProperty("tags");

        Assert.Equal(2, tags.GetArrayLength());
        Assert.Equal("gamma", tags[0].GetString());
        Assert.Equal("beta", tags[1].GetString());
    }

    /// <summary>Adapts fixed-slot (env, client) arguments to JsonLayerMerger's arbitrary-length chain signature.</summary>
    private static string Merge(string basePath, params string?[] patches) =>
        JsonLayerMerger.Merge(basePath, patches.Where(p => p is not null).Select(p => p!).ToList());
}
