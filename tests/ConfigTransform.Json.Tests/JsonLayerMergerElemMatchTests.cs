using System.Text.Json;
using ConfigTransform.Core;
using Xunit;

namespace ConfigTransform.Json.Tests;

/// <summary>
/// Merge-time (not set-time) resolution of <c>$elemMatch</c> array-of-objects overlays
/// (docs/FIELD_AUTHORING_DESIGN.md) through <see cref="JsonLayerMerger.Merge"/> -- the fixture
/// pair here is deliberately built so a Client-layer patch targets an item the Environment layer
/// itself just created, proving the progressive resolution these overlays require (an
/// Environment-layer patch must resolve against the base array; a Client-layer patch against the
/// base+Environment-*merged* array, not the base alone -- see JsonElemMatchResolver's remarks).
/// The fixtures also carry a sibling plain positional-array overlay (AllowedOrigins) alongside
/// the $elemMatch content, to prove the rewrite pass leaves it untouched.
/// </summary>
public class JsonLayerMergerElemMatchTests
{
    private static string FixturesRoot => Path.Combine(AppContext.BaseDirectory, "Fixtures", "DotNetCore", "ElemMatch");
    private const string ResourcePath = "Project/appsettings.json";

    private static ResolvedResource Resolve(string? client, string? environment)
    {
        var chain = LayerChain.Build(FixturesRoot, LayerPathResolver.Resolve(FixturesRoot, client, environment));
        return LayerChain.ResolveResource(FixturesRoot, chain, ResourcePath);
    }

    [Fact]
    public void Environment_layer_elemMatch_resolves_against_base_only()
    {
        var resolved = Resolve(client: null, "Production");
        var merged = JsonLayerMerger.Merge(resolved.BasePath, resolved.PatchPathsInOrder);

        using var doc = JsonDocument.Parse(merged);
        var rules = doc.RootElement.GetProperty("Rules");

        // "Viewer" exists in the base -> updated in place, still 3 items after the env layer
        // also creates "Auditor".
        Assert.Equal(3, rules.GetArrayLength());
        Assert.Equal("Admin", rules[0].GetProperty("role").GetString());
        Assert.False(rules[0].GetProperty("enabled").GetBoolean()); // untouched by either patch
        Assert.Equal("Viewer", rules[1].GetProperty("role").GetString());
        Assert.True(rules[1].GetProperty("enabled").GetBoolean()); // updated by the env patch
        Assert.Equal("Auditor", rules[2].GetProperty("role").GetString()); // created by the env patch
        Assert.True(rules[2].GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public void Client_layer_elemMatch_resolves_against_the_base_plus_environment_merged_array_not_base_alone()
    {
        var resolved = Resolve("ClientA", "Production");
        var merged = JsonLayerMerger.Merge(resolved.BasePath, resolved.PatchPathsInOrder);

        using var doc = JsonDocument.Parse(merged);
        var rules = doc.RootElement.GetProperty("Rules");

        // If the client layer had (incorrectly) resolved "Auditor" against the base array alone,
        // it would not find it there and would append a *second*, duplicate Auditor entry
        // instead of updating the one the environment layer created. Exactly one Auditor here is
        // the progressive-layering proof.
        var auditors = Enumerable.Range(0, rules.GetArrayLength())
            .Select(i => rules[i])
            .Where(item => item.GetProperty("role").GetString() == "Auditor")
            .ToList();
        Assert.Single(auditors);
        Assert.False(auditors[0].GetProperty("enabled").GetBoolean()); // updated by the client patch

        // The client overlay's second patch (multi-item in one overlay file), against the base.
        var admin = Enumerable.Range(0, rules.GetArrayLength())
            .Select(i => rules[i])
            .Single(item => item.GetProperty("role").GetString() == "Admin");
        Assert.True(admin.GetProperty("enabled").GetBoolean());

        // "Viewer" was set by the env layer, untouched by the client layer.
        var viewer = Enumerable.Range(0, rules.GetArrayLength())
            .Select(i => rules[i])
            .Single(item => item.GetProperty("role").GetString() == "Viewer");
        Assert.True(viewer.GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public void ElemMatch_array_and_a_sibling_plain_positional_array_overlay_in_the_same_file_both_resolve_correctly()
    {
        var resolved = Resolve("ClientA", "Production");
        var merged = JsonLayerMerger.Merge(resolved.BasePath, resolved.PatchPathsInOrder);

        using var doc = JsonDocument.Parse(merged);
        var origins = doc.RootElement.GetProperty("AllowedOrigins");

        // Existing, unrelated positional-array-overlay behavior (index 0 overridden by the
        // client layer, index 1 survives from the base) -- unaffected by the $elemMatch rewrite
        // pass running over the rest of the same file.
        Assert.Equal(2, origins.GetArrayLength());
        Assert.Equal("https://prod.example.com", origins[0].GetString());
        Assert.Equal("https://b.example.com", origins[1].GetString());
    }

    [Fact]
    public void No_elemMatch_anywhere_takes_the_untouched_legacy_fast_path()
    {
        // Any client/environment combination with no $elemMatch content at all in this fixture
        // set should merge identically to plain positional-array behavior -- confirms the
        // fast-path guard in JsonLayerMerger.Merge doesn't accidentally engage for ordinary
        // content.
        var resolved = Resolve(client: null, environment: null);
        var merged = JsonLayerMerger.Merge(resolved.BasePath, resolved.PatchPathsInOrder);

        using var doc = JsonDocument.Parse(merged);
        var rules = doc.RootElement.GetProperty("Rules");
        Assert.Equal(2, rules.GetArrayLength());
        Assert.False(rules[0].GetProperty("enabled").GetBoolean());
        Assert.False(rules[1].GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public void ElemMatch_resolves_progressively_across_a_genuine_three_deep_chain()
    {
        // The old hardcoded implementation only ever had two steps (env-against-base,
        // client-against-base+env). This design's chain is unbounded -- prove the fold actually
        // recomputes the accumulated state at each of three steps, not just two.
        var basePath = WriteTempJson("""{ "Rules": [ { "role": "Admin", "enabled": false } ] }""");
        var envPatch = WriteTempJson("""{ "Rules": [ { "$elemMatch": { "role": "Viewer" }, "enabled": false } ] }""");
        var regionPatch = WriteTempJson("""
            { "Rules": [
              { "$elemMatch": { "role": "Auditor" }, "enabled": false },
              { "$elemMatch": { "role": "Viewer" }, "enabled": true }
            ] }
            """);
        var clientPatch = WriteTempJson("""{ "Rules": [ { "$elemMatch": { "role": "Auditor" }, "enabled": true } ] }""");
        try
        {
            var merged = JsonLayerMerger.Merge(basePath, [envPatch, regionPatch, clientPatch]);
            using var doc = JsonDocument.Parse(merged);
            var rules = doc.RootElement.GetProperty("Rules");

            // If the region patch had (incorrectly) resolved "Viewer" against the base alone, or
            // the client patch had resolved "Auditor" against base+env alone, each would append a
            // duplicate instead of updating the item the prior step created. Exactly one of each
            // role, all updated, is the three-deep progressive-layering proof.
            Assert.Equal(3, rules.GetArrayLength());
            var byRole = Enumerable.Range(0, rules.GetArrayLength()).Select(i => rules[i])
                .ToDictionary(r => r.GetProperty("role").GetString()!, r => r.GetProperty("enabled").GetBoolean());

            Assert.False(byRole["Admin"]); // untouched by any patch
            Assert.True(byRole["Viewer"]); // created by env, updated by region
            Assert.True(byRole["Auditor"]); // created by region, updated by client
        }
        finally
        {
            foreach (var path in new[] { basePath, envPatch, regionPatch, clientPatch })
                File.Delete(path);
        }
    }

    // --- Scenarios not naturally expressed by one fixture pair: ad hoc temp files ---

    private static string WriteTempJson(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"configtransform-elemmatch-test-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void ElemMatch_with_no_match_creates_a_new_array_item_combining_conditions_and_set_fields()
    {
        var basePath = WriteTempJson("""{ "Items": [ { "id": "a" } ] }""");
        var overlayPath = WriteTempJson("""{ "Items": [ { "$elemMatch": { "id": "b" }, "value": "new" } ] }""");
        try
        {
            var merged = Merge(basePath, overlayPath, null);
            using var doc = JsonDocument.Parse(merged);
            var items = doc.RootElement.GetProperty("Items");

            Assert.Equal(2, items.GetArrayLength());
            Assert.Equal("b", items[1].GetProperty("id").GetString());
            Assert.Equal("new", items[1].GetProperty("value").GetString());
        }
        finally
        {
            File.Delete(basePath);
            File.Delete(overlayPath);
        }
    }

    [Fact]
    public void Two_create_patches_in_one_overlay_append_to_sequential_indices_without_colliding()
    {
        var basePath = WriteTempJson("""{ "Items": [ { "id": "a" } ] }""");
        var overlayPath = WriteTempJson("""
            { "Items": [
              { "$elemMatch": { "id": "b" }, "value": "one" },
              { "$elemMatch": { "id": "c" }, "value": "two" }
            ] }
            """);
        try
        {
            var merged = Merge(basePath, overlayPath, null);
            using var doc = JsonDocument.Parse(merged);
            var items = doc.RootElement.GetProperty("Items");

            Assert.Equal(3, items.GetArrayLength());
            Assert.Equal("b", items[1].GetProperty("id").GetString());
            Assert.Equal("c", items[2].GetProperty("id").GetString());
        }
        finally
        {
            File.Delete(basePath);
            File.Delete(overlayPath);
        }
    }

    [Fact]
    public void Ambiguous_elemMatch_more_than_one_candidate_throws_naming_them()
    {
        var basePath = WriteTempJson("""
            { "Items": [ { "id": "a", "region": "US" }, { "id": "a", "region": "EU" } ] }
            """);
        var overlayPath = WriteTempJson("""{ "Items": [ { "$elemMatch": { "id": "a" }, "value": "x" } ] }""");
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => Merge(basePath, overlayPath, null));
            Assert.Contains("More than one item", ex.Message);
        }
        finally
        {
            File.Delete(basePath);
            File.Delete(overlayPath);
        }
    }

    [Fact]
    public void Two_patches_in_the_same_overlay_resolving_to_the_same_item_throws()
    {
        var basePath = WriteTempJson("""{ "Items": [ { "id": "a", "region": "US" } ] }""");
        var overlayPath = WriteTempJson("""
            { "Items": [
              { "$elemMatch": { "id": "a" }, "value": "x" },
              { "$elemMatch": { "region": "US" }, "value": "y" }
            ] }
            """);
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => Merge(basePath, overlayPath, null));
            Assert.Contains("both resolve to the same item", ex.Message);
        }
        finally
        {
            File.Delete(basePath);
            File.Delete(overlayPath);
        }
    }

    [Fact]
    public void Hand_written_elemMatch_overlay_merges_correctly_without_ever_going_through_set()
    {
        // No JsonFieldAuthor call anywhere in this test -- the overlay file is written directly,
        // the way a human editing the file by hand would, then merged via the plain public API
        // (--dry-run/--diff's own code path). Proves the overlay format is self-sufficient, not
        // an implementation detail only `set` itself understands.
        var basePath = WriteTempJson("""{ "Items": [ { "id": "a", "enabled": false } ] }""");
        var overlayPath = WriteTempJson("""{ "Items": [ { "$elemMatch": { "id": "a" }, "enabled": true } ] }""");
        try
        {
            var merged = Merge(basePath, overlayPath, null);
            using var doc = JsonDocument.Parse(merged);
            Assert.True(doc.RootElement.GetProperty("Items")[0].GetProperty("enabled").GetBoolean());
        }
        finally
        {
            File.Delete(basePath);
            File.Delete(overlayPath);
        }
    }

    [Fact]
    public void ElemMatch_against_an_existing_non_array_value_throws_at_merge_time()
    {
        var basePath = WriteTempJson("""{ "Items": "not-an-array" }""");
        var overlayPath = WriteTempJson("""{ "Items": [ { "$elemMatch": { "id": "a" }, "value": "x" } ] }""");
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => Merge(basePath, overlayPath, null));
            Assert.Contains("not a JSON array", ex.Message);
        }
        finally
        {
            File.Delete(basePath);
            File.Delete(overlayPath);
        }
    }

    /// <summary>Adapts fixed-slot (env, client) arguments to JsonLayerMerger's arbitrary-length chain signature.</summary>
    private static string Merge(string basePath, params string?[] patches) =>
        JsonLayerMerger.Merge(basePath, patches.Where(p => p is not null).Select(p => p!).ToList());
}
