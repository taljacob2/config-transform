using ConfigTransform.Core;
using Xunit;

namespace ConfigTransform.Json.Tests;

/// <summary>
/// Direct tests of the "set" command's decision logic for JSON (docs/FIELD_AUTHORING_DESIGN.md),
/// against in-memory JSON strings. Array-of-objects matching is out of scope (see
/// docs/FIELD_AUTHORING_DESIGN.md's "Open items") and covered by a rejection test, not a
/// happy-path one.
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
            DotNetCoreBase, existingTargetJson: null,
            matches: [new MatchSpec("key", "ApiUrl", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "https://new.example.com", WasDefaulted: false)]);

        Assert.Contains("\"ApiUrl\": \"https://new.example.com\"", result);
    }

    [Fact]
    public void Updates_an_existing_nested_key_via_colon_separated_path()
    {
        var result = JsonFieldAuthor.Author(
            DotNetCoreBase, existingTargetJson: null,
            matches: [new MatchSpec("key", "Logging:LogLevel:Default", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "Warning", WasDefaulted: false)]);

        Assert.Contains("\"Default\": \"Warning\"", result);
    }

    [Fact]
    public void Creates_a_brand_new_nested_key_unlike_XMLs_Insert_gap()
    {
        var result = JsonFieldAuthor.Author(
            DotNetCoreBase, existingTargetJson: null,
            matches: [new MatchSpec("key", "Features:EnableBeta", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "true", WasDefaulted: false)]);

        Assert.Contains("\"Features\"", result);
        Assert.Contains("\"EnableBeta\": true", result);
    }

    [Fact]
    public void Infers_bool_type_the_same_way_a_merge_would()
    {
        var result = JsonFieldAuthor.Author(
            DotNetCoreBase, existingTargetJson: null,
            matches: [new MatchSpec("key", "Enabled", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "false", WasDefaulted: false)]);

        Assert.Contains("\"Enabled\": false", result);
        Assert.DoesNotContain("\"Enabled\": \"false\"", result);
    }

    [Fact]
    public void Re_running_set_for_the_same_key_updates_it_in_place()
    {
        var first = JsonFieldAuthor.Author(
            DotNetCoreBase, existingTargetJson: null,
            matches: [new MatchSpec("key", "ApiUrl", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "https://v1.example.com", WasDefaulted: false)]);

        var second = JsonFieldAuthor.Author(
            DotNetCoreBase, existingTargetJson: first,
            matches: [new MatchSpec("key", "ApiUrl", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "https://v2.example.com", WasDefaulted: false)]);

        Assert.Contains("https://v2.example.com", second);
        Assert.DoesNotContain("https://v1.example.com", second);
    }

    [Fact]
    public void Adding_to_an_existing_overlay_preserves_its_other_content()
    {
        var withApiUrl = JsonFieldAuthor.Author(
            DotNetCoreBase, existingTargetJson: null,
            matches: [new MatchSpec("key", "ApiUrl", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "https://new.example.com", WasDefaulted: false)]);

        var withBoth = JsonFieldAuthor.Author(
            DotNetCoreBase, existingTargetJson: withApiUrl,
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
            baseJson, existingTargetJson: null,
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
            DotNetCoreBase, existingTargetJson: null,
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
            colliding, existingTargetJson: null,
            matches: [new MatchSpec("key", "Logging:LogLevel:Default", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "X", WasDefaulted: false)]));

        Assert.Contains("Ambiguous", ex.Message);
        Assert.Contains("--match literal-key=Logging:LogLevel:Default", ex.Message);
    }

    [Fact]
    public void Not_found_literal_key_throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => JsonFieldAuthor.Author(
            DotNetCoreBase, existingTargetJson: null,
            matches: [new MatchSpec("literal-key", "DoesNotExist", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "X", WasDefaulted: false)]));

        Assert.Contains("No literal key named", ex.Message);
    }

    [Fact]
    public void More_than_one_match_is_rejected_as_array_matching_not_supported()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => JsonFieldAuthor.Author(
            DotNetCoreBase, existingTargetJson: null,
            matches:
            [
                new MatchSpec("key", "ConnectionStrings", WasDefaulted: false),
                new MatchSpec("name", "Prod", WasDefaulted: false)
            ],
            setFields: [new MatchSpec("value", "X", WasDefaulted: false)]));

        Assert.Contains("array of objects", ex.Message);
        Assert.Contains("not yet implemented", ex.Message);
    }

    [Fact]
    public void An_unrecognized_match_attribute_is_rejected()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => JsonFieldAuthor.Author(
            DotNetCoreBase, existingTargetJson: null,
            matches: [new MatchSpec("name", "Prod", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "X", WasDefaulted: false)]));

        Assert.Contains("not a valid --match for JSON", ex.Message);
    }

    [Fact]
    public void A_non_value_set_attribute_is_rejected()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => JsonFieldAuthor.Author(
            DotNetCoreBase, existingTargetJson: null,
            matches: [new MatchSpec("key", "ApiUrl", WasDefaulted: false)],
            setFields: [new MatchSpec("connectionString", "X", WasDefaulted: false)]));

        Assert.Contains("single scalar value", ex.Message);
    }

    [Fact]
    public void GenericJson_arbitrary_schema_works_with_no_special_casing()
    {
        const string genericJson = """{ "widgets": { "primary": { "tier": "basic" } } }""";

        var result = JsonFieldAuthor.Author(
            genericJson, existingTargetJson: null,
            matches: [new MatchSpec("key", "widgets:primary:tier", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "premium", WasDefaulted: false)]);

        Assert.Contains("\"tier\": \"premium\"", result);
    }
}
