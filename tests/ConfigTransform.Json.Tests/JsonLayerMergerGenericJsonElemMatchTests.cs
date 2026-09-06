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
    private const string ResourcePath = "Project/custom-settings.json";

    private static ResolvedResource Resolve(string? client, string? environment)
    {
        var chain = LayerChain.Build(FixturesRoot, LayerPathResolver.Resolve(FixturesRoot, client, environment));
        return LayerChain.ResolveResource(FixturesRoot, chain, ResourcePath);
    }

    [Fact]
    public void Environment_layer_elemMatch_resolves_against_the_base_array()
    {
        var resolved = Resolve(client: null, "Production");
        var merged = JsonLayerMerger.Merge(resolved.BasePath, resolved.PatchPathsInOrder);

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
        var resolved = Resolve("ClientA", "Production");
        var merged = JsonLayerMerger.Merge(resolved.BasePath, resolved.PatchPathsInOrder);

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
        var resolved = Resolve("ClientA", "Production");
        var merged = JsonLayerMerger.Merge(resolved.BasePath, resolved.PatchPathsInOrder);

        using var doc = JsonDocument.Parse(merged);
        var tags = doc.RootElement.GetProperty("tags");

        Assert.Equal(2, tags.GetArrayLength());
        Assert.Equal("gamma", tags[0].GetString());
        Assert.Equal("beta", tags[1].GetString());
    }
}
