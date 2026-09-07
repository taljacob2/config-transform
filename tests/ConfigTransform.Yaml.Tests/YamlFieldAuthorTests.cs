using ConfigTransform.Core;
using Xunit;

namespace ConfigTransform.Yaml.Tests;

/// <summary>
/// Direct tests of the "set" command's decision logic for YAML (docs/FIELD_AUTHORING_DESIGN.md):
/// the plain-field path, ported from JsonFieldAuthorTests' equivalent cases. No element-match
/// (array-of-objects) cases -- deliberately not implemented for YAML's first version, see
/// YamlFieldAuthor's own doc comment.
/// </summary>
public class YamlFieldAuthorTests
{
    private const string DotNetCoreBase = """
        Logging:
          LogLevel:
            Default: Information
        ApiUrl: https://dev.example.com
        """;

    [Fact]
    public void Updates_an_existing_top_level_key()
    {
        var result = YamlFieldAuthor.Author(
            DotNetCoreBase, existingTargetYaml: null, isBaseTarget: false,
            matches: [new MatchSpec("key", "ApiUrl", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "https://new.example.com", WasDefaulted: false)]);

        Assert.Contains("ApiUrl: https://new.example.com", result);
    }

    [Fact]
    public void Updates_an_existing_nested_key_via_colon_separated_path()
    {
        var result = YamlFieldAuthor.Author(
            DotNetCoreBase, existingTargetYaml: null, isBaseTarget: false,
            matches: [new MatchSpec("key", "Logging:LogLevel:Default", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "Warning", WasDefaulted: false)]);

        Assert.Contains("Default: Warning", result);
    }

    [Fact]
    public void Creates_a_brand_new_nested_key()
    {
        var result = YamlFieldAuthor.Author(
            DotNetCoreBase, existingTargetYaml: null, isBaseTarget: false,
            matches: [new MatchSpec("key", "Features:EnableBeta", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "true", WasDefaulted: false)]);

        Assert.Contains("Features:", result);
        Assert.Contains("EnableBeta: true", result);
    }

    [Fact]
    public void Infers_bool_type_the_same_way_a_merge_would()
    {
        var result = YamlFieldAuthor.Author(
            DotNetCoreBase, existingTargetYaml: null, isBaseTarget: false,
            matches: [new MatchSpec("key", "Enabled", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "false", WasDefaulted: false)]);

        Assert.Contains("Enabled: false", result);
        Assert.DoesNotContain("Enabled: 'false'", result);
    }

    [Fact]
    public void Re_running_set_for_the_same_key_updates_it_in_place()
    {
        var first = YamlFieldAuthor.Author(
            DotNetCoreBase, existingTargetYaml: null, isBaseTarget: false,
            matches: [new MatchSpec("key", "ApiUrl", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "https://v1.example.com", WasDefaulted: false)]);

        var second = YamlFieldAuthor.Author(
            DotNetCoreBase, existingTargetYaml: first, isBaseTarget: false,
            matches: [new MatchSpec("key", "ApiUrl", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "https://v2.example.com", WasDefaulted: false)]);

        Assert.Contains("ApiUrl: https://v2.example.com", second);
        Assert.DoesNotContain("v1.example.com", second);
    }

    [Fact]
    public void A_literal_key_containing_a_colon_is_resolved_via_literal_key()
    {
        const string baseWithColonKey = """
            "Weird:Key": value
            """;

        var result = YamlFieldAuthor.Author(
            baseWithColonKey, existingTargetYaml: null, isBaseTarget: false,
            matches: [new MatchSpec("literal-key", "Weird:Key", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "new-value", WasDefaulted: false)]);

        Assert.Contains("new-value", result);
    }

    [Fact]
    public void Ambiguous_nested_path_vs_literal_key_refuses_and_shows_both_alternatives()
    {
        const string ambiguousBase = """
            Logging:
              LogLevel: nested-value
            "Logging:LogLevel": literal-value
            """;

        var ex = Assert.Throws<InvalidOperationException>(() => YamlFieldAuthor.Author(
            ambiguousBase, existingTargetYaml: null, isBaseTarget: false,
            matches: [new MatchSpec("key", "Logging:LogLevel", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "x", WasDefaulted: false)]));

        Assert.Contains("--match key=", ex.Message);
        Assert.Contains("--match literal-key=", ex.Message);
    }

    [Fact]
    public void Element_match_shape_more_than_one_match_is_refused_as_not_yet_supported()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => YamlFieldAuthor.Author(
            DotNetCoreBase, existingTargetYaml: null, isBaseTarget: false,
            matches: [new MatchSpec("key", "Rules", WasDefaulted: false), new MatchSpec("role", "Admin", WasDefaulted: false)],
            setFields: [new MatchSpec("enabled", "true", WasDefaulted: false)]));

        Assert.Contains("not yet supported", ex.Message);
    }

    [Fact]
    public void Bad_match_attribute_is_rejected()
    {
        Assert.Throws<InvalidOperationException>(() => YamlFieldAuthor.Author(
            DotNetCoreBase, existingTargetYaml: null, isBaseTarget: false,
            matches: [new MatchSpec("tag", "ApiUrl", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "x", WasDefaulted: false)]));
    }

    [Fact]
    public void Bad_set_attribute_is_rejected()
    {
        Assert.Throws<InvalidOperationException>(() => YamlFieldAuthor.Author(
            DotNetCoreBase, existingTargetYaml: null, isBaseTarget: false,
            matches: [new MatchSpec("key", "ApiUrl", WasDefaulted: false)],
            setFields: [new MatchSpec("other", "x", WasDefaulted: false)]));
    }
}
