using System.Text.Json.Nodes;
using Xunit;

namespace ConfigTransform.Json.Tests;

/// <summary>In-memory unit tests of <see cref="JsonElemMatchResolver"/> in isolation, independent
/// of both <see cref="JsonFieldAuthor"/> (set-time) and <see cref="JsonLayerMerger"/> (merge-time)
/// -- see JsonFieldAuthorTests and JsonLayerMergerElemMatchTests for those.</summary>
public class JsonElemMatchResolverTests
{
    private static JsonArray Arr(string json) => (JsonArray)JsonNode.Parse(json)!;

    [Fact]
    public void IndicesMatching_finds_the_single_item_satisfying_all_conditions()
    {
        var array = Arr("""[ { "role": "Admin", "env": "Prod" }, { "role": "Admin", "env": "Staging" } ]""");
        var conditions = new[]
        {
            new JsonElemMatchResolver.Condition("role", JsonValue.Create("Admin")),
            new JsonElemMatchResolver.Condition("env", JsonValue.Create("Prod"))
        };

        var result = JsonElemMatchResolver.IndicesMatching(array, conditions);

        Assert.Equal([0], result);
    }

    [Fact]
    public void IndicesMatching_with_no_match_returns_empty()
    {
        var array = Arr("""[ { "role": "Admin" } ]""");
        var conditions = new[] { new JsonElemMatchResolver.Condition("role", JsonValue.Create("Viewer")) };

        Assert.Empty(JsonElemMatchResolver.IndicesMatching(array, conditions));
    }

    [Fact]
    public void IndicesMatching_against_a_null_array_returns_empty()
    {
        var conditions = new[] { new JsonElemMatchResolver.Condition("role", JsonValue.Create("Admin")) };
        Assert.Empty(JsonElemMatchResolver.IndicesMatching(null, conditions));
    }

    [Fact]
    public void IndicesMatching_compares_typed_values_not_strings()
    {
        // "true" (the CLI's raw string) must have already been converted to a real JSON boolean
        // by the caller -- IndicesMatching itself does typed DeepEquals, not string comparison.
        var array = Arr("""[ { "enabled": true }, { "enabled": false } ]""");
        var conditions = new[] { new JsonElemMatchResolver.Condition("enabled", JsonValue.Create(true)) };

        Assert.Equal([0], JsonElemMatchResolver.IndicesMatching(array, conditions));
    }

    [Fact]
    public void ResolveIndexOrAppend_returns_the_append_position_when_nothing_matches()
    {
        var array = Arr("""[ { "id": "a" } ]""");
        var conditions = new[] { new JsonElemMatchResolver.Condition("id", JsonValue.Create("b")) };

        Assert.Equal(1, JsonElemMatchResolver.ResolveIndexOrAppend(array, conditions, "Items"));
    }

    [Fact]
    public void ResolveIndexOrAppend_against_a_null_array_returns_zero()
    {
        var conditions = new[] { new JsonElemMatchResolver.Condition("id", JsonValue.Create("a")) };
        Assert.Equal(0, JsonElemMatchResolver.ResolveIndexOrAppend(null, conditions, "Items"));
    }

    [Fact]
    public void ResolveIndexOrAppend_throws_and_lists_candidates_when_ambiguous()
    {
        var array = Arr("""[ { "id": "a", "x": 1 }, { "id": "a", "x": 2 } ]""");
        var conditions = new[] { new JsonElemMatchResolver.Condition("id", JsonValue.Create("a")) };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            JsonElemMatchResolver.ResolveIndexOrAppend(array, conditions, "Items"));

        Assert.Contains("More than one item", ex.Message);
        Assert.Contains("\"x\":1", ex.Message);
        Assert.Contains("\"x\":2", ex.Message);
    }

    [Fact]
    public void ContainsElemMatch_detects_a_patch_list()
    {
        var node = JsonNode.Parse("""{ "Rules": [ { "$elemMatch": { "role": "Admin" }, "enabled": true } ] }""");
        Assert.True(JsonElemMatchResolver.ContainsElemMatch(node));
    }

    [Fact]
    public void ContainsElemMatch_detects_a_patch_list_nested_deeper_in_the_tree()
    {
        var node = JsonNode.Parse("""
            { "Logging": { "Rules": [ { "$elemMatch": { "logger": "X" }, "level": "Error" } ] } }
            """);
        Assert.True(JsonElemMatchResolver.ContainsElemMatch(node));
    }

    [Fact]
    public void ContainsElemMatch_ignores_a_plain_string_that_merely_contains_the_literal_text()
    {
        var node = JsonNode.Parse("""{ "Description": "See $elemMatch in the docs" }""");
        Assert.False(JsonElemMatchResolver.ContainsElemMatch(node));
    }

    [Fact]
    public void ContainsElemMatch_ignores_an_ordinary_array_of_business_objects()
    {
        var node = JsonNode.Parse("""{ "Rules": [ { "role": "Admin" }, { "role": "Viewer" } ] }""");
        Assert.False(JsonElemMatchResolver.ContainsElemMatch(node));
    }

    [Fact]
    public void IsPatchList_requires_every_element_to_carry_elemMatch()
    {
        var mixed = Arr("""[ { "$elemMatch": { "id": "a" }, "v": 1 }, { "id": "b" } ]""");
        Assert.False(JsonElemMatchResolver.IsPatchList(mixed));
    }

    [Fact]
    public void IsPatchList_is_false_for_an_empty_array()
    {
        Assert.False(JsonElemMatchResolver.IsPatchList(new JsonArray()));
    }

    [Fact]
    public void Rewrite_resolves_a_single_patch_to_its_real_index()
    {
        var preceding = JsonNode.Parse("""{ "Rules": [ { "role": "Admin", "enabled": false }, { "role": "Viewer", "enabled": false } ] }""");
        var overlay = JsonNode.Parse("""{ "Rules": [ { "$elemMatch": { "role": "Viewer" }, "enabled": true } ] }""");

        var rewritten = JsonElemMatchResolver.Rewrite(overlay, preceding);

        var rulesObj = (JsonObject)rewritten!["Rules"]!;
        Assert.True((bool)rulesObj["1"]!["enabled"]!);
        Assert.False(rulesObj.ContainsKey("0"));
    }

    [Fact]
    public void Rewrite_nested_patch_list_resolves_against_the_corresponding_nested_preceding_array()
    {
        var preceding = JsonNode.Parse("""{ "Logging": { "Rules": [ { "logger": "X", "level": "Warning" } ] } }""");
        var overlay = JsonNode.Parse("""{ "Logging": { "Rules": [ { "$elemMatch": { "logger": "X" }, "level": "Error" } ] } }""");

        var rewritten = JsonElemMatchResolver.Rewrite(overlay, preceding);

        var rulesObj = (JsonObject)rewritten!["Logging"]!["Rules"]!;
        Assert.Equal("Error", (string?)rulesObj["0"]!["level"]);
    }

    [Fact]
    public void Rewrite_never_mutates_its_inputs()
    {
        var preceding = JsonNode.Parse("""{ "Rules": [ { "role": "Admin", "enabled": false } ] }""");
        var overlay = JsonNode.Parse("""{ "Rules": [ { "$elemMatch": { "role": "Admin" }, "enabled": true } ] }""");
        var precedingBefore = preceding!.DeepClone().ToJsonString();
        var overlayBefore = overlay!.DeepClone().ToJsonString();

        JsonElemMatchResolver.Rewrite(overlay, preceding);

        Assert.Equal(precedingBefore, preceding.ToJsonString());
        Assert.Equal(overlayBefore, overlay.ToJsonString());
    }

    [Fact]
    public void Rewrite_leaves_a_sibling_plain_array_at_a_different_key_unchanged()
    {
        var preceding = JsonNode.Parse("""{ "Rules": [ { "role": "Admin", "enabled": false } ], "Tags": [ "a", "b" ] }""");
        var overlay = JsonNode.Parse("""{ "Rules": [ { "$elemMatch": { "role": "Admin" }, "enabled": true } ], "Tags": [ "z" ] }""");

        var rewritten = JsonElemMatchResolver.Rewrite(overlay, preceding);

        Assert.Equal("z", (string?)rewritten!["Tags"]![0]);
        Assert.Single(rewritten["Tags"]!.AsArray());
    }
}
