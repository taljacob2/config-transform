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

        Assert.Contains("ApiUrl: \"https://new.example.com\"", result);
    }

    [Fact]
    public void Updates_an_existing_nested_key_via_colon_separated_path()
    {
        var result = YamlFieldAuthor.Author(
            DotNetCoreBase, existingTargetYaml: null, isBaseTarget: false,
            matches: [new MatchSpec("key", "Logging:LogLevel:Default", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "Warning", WasDefaulted: false)]);

        Assert.Contains("Default: \"Warning\"", result);
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
    public void True_or_false_is_written_as_a_boolean()
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

        Assert.Contains("ApiUrl: \"https://v2.example.com\"", second);
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
    public void An_element_match_write_authors_an_elemMatch_patch_not_an_array_position()
    {
        var result = YamlFieldAuthor.Author(
            RulesBase, existingTargetYaml: null, isBaseTarget: false,
            matches: [Match("key", "Rules"), Match("role", "Admin")],
            setFields: [Match("enabled", "true")]);

        Assert.Equal("Rules:\n- $elemMatch:\n    role: \"Admin\"\n  enabled: true\n", result);
    }

    [Fact]
    public void Re_running_with_the_same_conditions_updates_that_patch_and_different_conditions_append_one()
    {
        var first = YamlFieldAuthor.Author(RulesBase, null, false, [Match("key", "Rules"), Match("role", "Admin")], [Match("enabled", "true")]);
        var again = YamlFieldAuthor.Author(RulesBase, first, false, [Match("key", "Rules"), Match("role", "Admin")], [Match("enabled", "false")]);
        var second = YamlFieldAuthor.Author(RulesBase, again, false, [Match("key", "Rules"), Match("role", "Viewer")], [Match("enabled", "true")]);

        Assert.Equal(
            "Rules:\n- $elemMatch:\n    role: \"Admin\"\n  enabled: false\n- $elemMatch:\n    role: \"Viewer\"\n  enabled: true\n",
            second);
    }

    [Fact]
    public void A_base_target_element_match_write_edits_the_real_item_or_appends_one()
    {
        var updated = YamlFieldAuthor.Author(RulesBase, RulesBase, true, [Match("key", "Rules"), Match("role", "Admin")], [Match("enabled", "true")]);
        var created = YamlFieldAuthor.Author(RulesBase, RulesBase, true, [Match("key", "Rules"), Match("role", "Auditor")], [Match("enabled", "true")]);

        Assert.Equal("Rules:\n  - role: Admin\n    enabled: true\n  - role: Viewer\n    enabled: false\n", updated);
        Assert.Equal("Rules:\n  - role: Admin\n    enabled: false\n  - role: Viewer\n    enabled: false\n  - role: \"Auditor\"\n    enabled: true\n", created);
    }

    [Fact]
    public void An_ambiguous_match_is_refused_before_anything_is_written()
    {
        var twoAdmins = "Rules:\n- {role: Admin, env: Prod}\n- {role: Admin, env: Test}\n";

        var ex = Assert.Throws<InvalidOperationException>(() => YamlFieldAuthor.Author(
            twoAdmins, null, false, [Match("key", "Rules"), Match("role", "Admin")], [Match("enabled", "true")]));

        Assert.Contains("More than one item", ex.Message);
    }

    [Fact]
    public void An_overlay_key_holding_other_content_is_refused()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => YamlFieldAuthor.Author(
            RulesBase, "Rules:\n- plain item\n", false, [Match("key", "Rules"), Match("role", "Admin")], [Match("enabled", "true")]));

        Assert.Contains("isn't an element-match patch list", ex.Message);
    }

    [Fact]
    public void An_element_match_condition_needs_an_explicit_field()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => YamlFieldAuthor.Author(
            RulesBase, null, false,
            [Match("key", "Rules"), new MatchSpec("key", "Admin", WasDefaulted: true)],
            [Match("enabled", "true")]));

        Assert.Contains("explicit field=value", ex.Message);
    }

    private const string RulesBase = "Rules:\n  - role: Admin\n    enabled: false\n  - role: Viewer\n    enabled: false\n";

    private static MatchSpec Match(string attribute, string value) => new(attribute, value, WasDefaulted: false);

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

    [Fact]
    public void A_key_path_existing_only_with_different_casing_is_refused_with_the_real_spelling()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => YamlFieldAuthor.Author(
            DotNetCoreBase, existingTargetYaml: null, isBaseTarget: false,
            matches: [new MatchSpec("key", "apiurl", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "https://new.example.com", WasDefaulted: false)]));

        Assert.Contains("\"ApiUrl\" does", ex.Message);
        Assert.Contains("Try: --match key=ApiUrl", ex.Message);
    }

    [Fact]
    public void Rewriting_an_existing_overlay_keeps_its_line_endings()
    {
        var result = YamlFieldAuthor.Author(
            DotNetCoreBase, existingTargetYaml: "ApiUrl: \"https://old.example.com\"\r\n", isBaseTarget: false,
            matches: [new MatchSpec("key", "ApiUrl", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "https://new.example.com", WasDefaulted: false)]);

        Assert.Equal("ApiUrl: \"https://new.example.com\"\r\n", result);
    }

    [Theory]
    [InlineData("NO", "\"NO\"")]          // the Norway problem: a boolean to YAML 1.1 readers if unquoted
    [InlineData("yes", "\"yes\"")]
    [InlineData("on", "\"on\"")]
    [InlineData("y", "\"y\"")]
    [InlineData("null", "\"null\"")]      // a real null even in YAML 1.2 if unquoted
    [InlineData("~", "\"~\"")]
    [InlineData("1:20", "\"1:20\"")]      // base-60 80 to YAML 1.1 readers
    [InlineData("0123", "\"0123\"")]      // octal to YAML 1.1 readers -- and 123 under the old rule
    [InlineData("02134", "\"02134\"")]    // a zip code -- was written as 2134
    [InlineData("007", "\"007\"")]
    [InlineData("1.10", "\"1.10\"")]      // a version -- was written as 1.1
    [InlineData("1e3", "\"1e3\"")]        // was written as 1000
    [InlineData("True", "\"True\"")]
    [InlineData("5432", "5432")]            // reads back exactly as typed: a number
    [InlineData("-12", "-12")]
    [InlineData("1.5", "1.5")]
    [InlineData("true", "true")]
    [InlineData("false", "false")]
    public void A_value_is_a_number_or_boolean_only_if_it_reads_back_exactly_otherwise_a_quoted_string(string value, string written)
    {
        var result = YamlFieldAuthor.Author(
            DotNetCoreBase, existingTargetYaml: null, isBaseTarget: false,
            matches: [new MatchSpec("key", "Value", WasDefaulted: false)],
            setFields: [new MatchSpec("value", value, WasDefaulted: false)]);

        Assert.Equal($"Value: {written}\n", result.Replace("\r\n", "\n"));
    }

    [Fact]
    public void Setting_a_value_leaves_every_other_line_of_the_file_exactly_as_it_was()
    {
        // The old implementation re-serialized the whole file, re-quoting values it never touched.
        var existing = "Zeta: 'single'\nApiUrl: \"https://old.example.com\"\nPlain: text\nList:\n  - a\n  - b\n";

        var result = YamlFieldAuthor.Author(
            DotNetCoreBase, existingTargetYaml: existing, isBaseTarget: false,
            matches: [new MatchSpec("key", "ApiUrl", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "https://new.example.com", WasDefaulted: false)]);

        Assert.Equal("Zeta: 'single'\nApiUrl: \"https://new.example.com\"\nPlain: text\nList:\n  - a\n  - b\n", result);
    }
}
