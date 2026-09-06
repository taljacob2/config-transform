using ConfigTransform.Core;
using Xunit;

namespace ConfigTransform.Json.Tests;

/// <summary>
/// Direct tests of the "set" command's decision logic for JSON (docs/FIELD_AUTHORING_DESIGN.md),
/// against in-memory JSON strings: the plain-field path, and the element-match
/// (<c>$elemMatch</c>) path for matching/creating an item inside an array of objects. See
/// JsonElemMatchResolverTests for the resolver's own unit tests in isolation, and
/// JsonLayerMergerElemMatchTests for merge-time (as opposed to set-time) resolution.
/// </summary>
public class JsonFieldAuthorTests
{
    private const string DotNetCoreBase = """
        {
          "Logging": { "LogLevel": { "Default": "Information" } },
          "ApiUrl": "https://dev.example.com"
        }
        """;

    [Fact]
    public void Updates_an_existing_top_level_key()
    {
        var result = JsonFieldAuthor.Author(
            DotNetCoreBase, existingTargetJson: null, isBaseTarget: false,
            matches: [new MatchSpec("key", "ApiUrl", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "https://new.example.com", WasDefaulted: false)]);

        Assert.Contains("\"ApiUrl\": \"https://new.example.com\"", result);
    }

    [Fact]
    public void Updates_an_existing_nested_key_via_colon_separated_path()
    {
        var result = JsonFieldAuthor.Author(
            DotNetCoreBase, existingTargetJson: null, isBaseTarget: false,
            matches: [new MatchSpec("key", "Logging:LogLevel:Default", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "Warning", WasDefaulted: false)]);

        Assert.Contains("\"Default\": \"Warning\"", result);
    }

    [Fact]
    public void Creates_a_brand_new_nested_key_unlike_XMLs_Insert_gap()
    {
        var result = JsonFieldAuthor.Author(
            DotNetCoreBase, existingTargetJson: null, isBaseTarget: false,
            matches: [new MatchSpec("key", "Features:EnableBeta", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "true", WasDefaulted: false)]);

        Assert.Contains("\"Features\"", result);
        Assert.Contains("\"EnableBeta\": true", result);
    }

    [Fact]
    public void Infers_bool_type_the_same_way_a_merge_would()
    {
        var result = JsonFieldAuthor.Author(
            DotNetCoreBase, existingTargetJson: null, isBaseTarget: false,
            matches: [new MatchSpec("key", "Enabled", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "false", WasDefaulted: false)]);

        Assert.Contains("\"Enabled\": false", result);
        Assert.DoesNotContain("\"Enabled\": \"false\"", result);
    }

    [Fact]
    public void Re_running_set_for_the_same_key_updates_it_in_place()
    {
        var first = JsonFieldAuthor.Author(
            DotNetCoreBase, existingTargetJson: null, isBaseTarget: false,
            matches: [new MatchSpec("key", "ApiUrl", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "https://v1.example.com", WasDefaulted: false)]);

        var second = JsonFieldAuthor.Author(
            DotNetCoreBase, existingTargetJson: first, isBaseTarget: false,
            matches: [new MatchSpec("key", "ApiUrl", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "https://v2.example.com", WasDefaulted: false)]);

        Assert.Contains("https://v2.example.com", second);
        Assert.DoesNotContain("https://v1.example.com", second);
    }

    [Fact]
    public void Adding_to_an_existing_overlay_preserves_its_other_content()
    {
        var withApiUrl = JsonFieldAuthor.Author(
            DotNetCoreBase, existingTargetJson: null, isBaseTarget: false,
            matches: [new MatchSpec("key", "ApiUrl", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "https://new.example.com", WasDefaulted: false)]);

        var withBoth = JsonFieldAuthor.Author(
            DotNetCoreBase, existingTargetJson: withApiUrl, isBaseTarget: false,
            matches: [new MatchSpec("key", "Logging:LogLevel:Default", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "Warning", WasDefaulted: false)]);

        Assert.Contains("https://new.example.com", withBoth);
        Assert.Contains("Warning", withBoth);
    }

    [Fact]
    public void Literal_key_containing_a_colon_is_matched_without_splitting_it()
    {
        const string baseJson = """{ "Logging:LogLevel:Default": "Old" }""";

        var result = JsonFieldAuthor.Author(
            baseJson, existingTargetJson: null, isBaseTarget: false,
            matches: [new MatchSpec("literal-key", "Logging:LogLevel:Default", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "New", WasDefaulted: false)]);

        Assert.Contains("\"Logging:LogLevel:Default\": \"New\"", result);
    }

    [Fact]
    public void Single_segment_key_is_not_treated_as_ambiguous_with_itself()
    {
        // A bare, colon-free key IS its own literal key -- "nested" and "literal" aren't two
        // competing readings here, they're the same one. Regression test for a real bug caught
        // during manual smoke-testing.
        var result = JsonFieldAuthor.Author(
            DotNetCoreBase, existingTargetJson: null, isBaseTarget: false,
            matches: [new MatchSpec("key", "ApiUrl", WasDefaulted: true)],
            setFields: [new MatchSpec("value", "https://new.example.com", WasDefaulted: true)]);

        Assert.Contains("https://new.example.com", result);
    }

    [Fact]
    public void Genuine_collision_between_a_nested_path_and_a_literal_key_is_ambiguous()
    {
        // Only reachable in practice via a base-target write against a hand-edited file --
        // Microsoft.Extensions.Configuration.Json itself refuses to load a file shaped like this
        // (duplicate flattened key), so an Environment/Client-target `set` (which merges through
        // JsonLayerMerger.Merge, i.e. through IConfiguration) never reaches this code path at
        // all; IConfiguration's own load failure is the actual defense there. See
        // docs/FIELD_AUTHORING_DESIGN.md's "Open items".
        const string colliding = """
            {
              "Logging": { "LogLevel": { "Default": "Information" } },
              "Logging:LogLevel:Default": "LiteralCollision"
            }
            """;

        var ex = Assert.Throws<InvalidOperationException>(() => JsonFieldAuthor.Author(
            colliding, existingTargetJson: null, isBaseTarget: false,
            matches: [new MatchSpec("key", "Logging:LogLevel:Default", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "X", WasDefaulted: false)]));

        Assert.Contains("Ambiguous", ex.Message);
        Assert.Contains("--match literal-key=Logging:LogLevel:Default", ex.Message);
    }

    [Fact]
    public void Not_found_literal_key_throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => JsonFieldAuthor.Author(
            DotNetCoreBase, existingTargetJson: null, isBaseTarget: false,
            matches: [new MatchSpec("literal-key", "DoesNotExist", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "X", WasDefaulted: false)]));

        Assert.Contains("No literal key named", ex.Message);
    }

    private const string RulesBase = """
        {
          "Rules": [
            { "role": "Admin", "env": "Production", "enabled": false },
            { "role": "Viewer", "env": "Production", "enabled": false }
          ],
          "ApiUrl": "https://example.com"
        }
        """;

    private static MatchSpec Cond(string field, string value) => new(field, value, WasDefaulted: false);

    [Fact]
    public void Set_with_element_match_conditions_writes_the_elemMatch_shaped_overlay()
    {
        var result = JsonFieldAuthor.Author(
            RulesBase, existingTargetJson: null, isBaseTarget: false,
            matches: [Cond("key", "Rules"), Cond("role", "Admin"), Cond("env", "Production")],
            setFields: [Cond("enabled", "true")]);

        Assert.Contains("\"$elemMatch\"", result);
        Assert.Contains("\"role\": \"Admin\"", result);
        Assert.Contains("\"env\": \"Production\"", result);
        Assert.Contains("\"enabled\": true", result);
    }

    [Fact]
    public void Set_with_element_match_writing_multiple_fields_at_once()
    {
        var result = JsonFieldAuthor.Author(
            RulesBase, existingTargetJson: null, isBaseTarget: false,
            matches: [Cond("key", "Rules"), Cond("role", "Admin")],
            setFields: [Cond("enabled", "true"), Cond("region", "US")]);

        Assert.Contains("\"enabled\": true", result);
        Assert.Contains("\"region\": \"US\"", result);
    }

    [Fact]
    public void Set_re_running_element_match_with_the_same_conditions_updates_the_same_node_in_place()
    {
        var first = JsonFieldAuthor.Author(
            RulesBase, existingTargetJson: null, isBaseTarget: false,
            matches: [Cond("key", "Rules"), Cond("role", "Admin")],
            setFields: [Cond("enabled", "true")]);

        var second = JsonFieldAuthor.Author(
            RulesBase, existingTargetJson: first, isBaseTarget: false,
            matches: [Cond("key", "Rules"), Cond("role", "Admin")],
            setFields: [Cond("enabled", "false")]);

        Assert.Single(System.Text.Json.Nodes.JsonNode.Parse(second)!["Rules"]!.AsArray());
        Assert.Contains("\"enabled\": false", second);
    }

    [Fact]
    public void Set_a_second_patch_with_different_conditions_appends_to_the_same_overlay()
    {
        var first = JsonFieldAuthor.Author(
            RulesBase, existingTargetJson: null, isBaseTarget: false,
            matches: [Cond("key", "Rules"), Cond("role", "Admin")],
            setFields: [Cond("enabled", "true")]);

        var second = JsonFieldAuthor.Author(
            RulesBase, existingTargetJson: first, isBaseTarget: false,
            matches: [Cond("key", "Rules"), Cond("role", "Viewer")],
            setFields: [Cond("enabled", "true")]);

        Assert.Equal(2, System.Text.Json.Nodes.JsonNode.Parse(second)!["Rules"]!.AsArray().Count);
        Assert.Contains("\"role\": \"Admin\"", second);
        Assert.Contains("\"role\": \"Viewer\"", second);
    }

    [Fact]
    public void Set_element_match_against_a_non_array_location_throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => JsonFieldAuthor.Author(
            RulesBase, existingTargetJson: null, isBaseTarget: false,
            matches: [Cond("key", "ApiUrl"), Cond("role", "Admin")],
            setFields: [Cond("enabled", "true")]));

        Assert.Contains("not a JSON array", ex.Message);
    }

    [Fact]
    public void Set_element_match_ambiguous_in_preceding_document_throws_at_set_time()
    {
        const string ambiguous = """
            { "Rules": [
              { "role": "Admin", "region": "US" },
              { "role": "Admin", "region": "EU" }
            ] }
            """;

        var ex = Assert.Throws<InvalidOperationException>(() => JsonFieldAuthor.Author(
            ambiguous, existingTargetJson: null, isBaseTarget: false,
            matches: [Cond("key", "Rules"), Cond("role", "Admin")],
            setFields: [Cond("enabled", "true")]));

        Assert.Contains("More than one item", ex.Message);
    }

    [Fact]
    public void Bare_second_match_without_an_explicit_field_is_rejected_for_element_match_conditions()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => JsonFieldAuthor.Author(
            RulesBase, existingTargetJson: null, isBaseTarget: false,
            matches: [Cond("key", "Rules"), new MatchSpec("key", "Admin", WasDefaulted: true)],
            setFields: [Cond("enabled", "true")]));

        Assert.Contains("explicit field=value", ex.Message);
    }

    [Fact]
    public void Set_element_match_against_an_overlay_that_already_has_a_different_shape_is_rejected()
    {
        const string overlayWithPlainRules = """{ "Rules": [ { "role": "Auditor" } ] }""";

        var ex = Assert.Throws<InvalidOperationException>(() => JsonFieldAuthor.Author(
            RulesBase, existingTargetJson: overlayWithPlainRules, isBaseTarget: false,
            matches: [Cond("key", "Rules"), Cond("role", "Admin")],
            setFields: [Cond("enabled", "true")]));

        Assert.Contains("isn't an element-match patch list", ex.Message);
    }

    [Fact]
    public void Base_target_element_match_mutates_the_real_array_item_directly_no_elemMatch_syntax_written()
    {
        var result = JsonFieldAuthor.Author(
            RulesBase, existingTargetJson: RulesBase, isBaseTarget: true,
            matches: [Cond("key", "Rules"), Cond("role", "Admin")],
            setFields: [Cond("enabled", "true")]);

        Assert.DoesNotContain("$elemMatch", result);
        Assert.Contains("\"role\": \"Admin\"", result);
        Assert.Contains("\"enabled\": true", result);
    }

    [Fact]
    public void Base_target_element_match_with_no_match_appends_a_real_new_array_item()
    {
        var result = JsonFieldAuthor.Author(
            RulesBase, existingTargetJson: RulesBase, isBaseTarget: true,
            matches: [Cond("key", "Rules"), Cond("role", "Auditor")],
            setFields: [Cond("enabled", "true")]);

        Assert.DoesNotContain("$elemMatch", result);
        var rules = System.Text.Json.Nodes.JsonNode.Parse(result)!["Rules"]!.AsArray();
        Assert.Equal(3, rules.Count);
        Assert.Contains("\"role\": \"Auditor\"", result);
    }

    [Fact]
    public void An_unrecognized_match_attribute_is_rejected()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => JsonFieldAuthor.Author(
            DotNetCoreBase, existingTargetJson: null, isBaseTarget: false,
            matches: [new MatchSpec("name", "Prod", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "X", WasDefaulted: false)]));

        Assert.Contains("not a valid --match for JSON", ex.Message);
    }

    [Fact]
    public void A_non_value_set_attribute_is_rejected()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => JsonFieldAuthor.Author(
            DotNetCoreBase, existingTargetJson: null, isBaseTarget: false,
            matches: [new MatchSpec("key", "ApiUrl", WasDefaulted: false)],
            setFields: [new MatchSpec("connectionString", "X", WasDefaulted: false)]));

        Assert.Contains("single scalar value", ex.Message);
    }

    [Fact]
    public void GenericJson_arbitrary_schema_works_with_no_special_casing()
    {
        const string genericJson = """{ "widgets": { "primary": { "tier": "basic" } } }""";

        var result = JsonFieldAuthor.Author(
            genericJson, existingTargetJson: null, isBaseTarget: false,
            matches: [new MatchSpec("key", "widgets:primary:tier", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "premium", WasDefaulted: false)]);

        Assert.Contains("\"tier\": \"premium\"", result);
    }
}
