using Xunit;

namespace ConfigTransform.Core.Tests;

public class SecretPlaceholdersTests
{
    [Fact]
    public void Names_finds_each_distinct_name_in_order_of_first_appearance()
    {
        var names = SecretPlaceholders.Names(
            "a={{CFSECRET_B}} b={{CFSECRET_A_1}} c={{CFSECRET_B}}", "x.config");

        Assert.Equal(["CFSECRET_B", "CFSECRET_A_1"], names);
    }

    [Theory]
    [InlineData("{{CFSECRET_}}")]    // a name needs at least one character after the prefix
    [InlineData("{{CFSECRET_A-B}}")] // '-' isn't a name character
    [InlineData("{{ CFSECRET_X }}")] // no spaces inside
    [InlineData("{{name}}")]         // other template syntax is never ours
    public void Names_ignores_anything_that_is_not_a_well_formed_placeholder(string text)
    {
        Assert.Empty(SecretPlaceholders.Names(text, "x.config"));
    }

    [Theory]
    [InlineData("{{CFSECRET_Db_Password}}", "{{CFSECRET_DB_PASSWORD}}")]
    [InlineData("{{cfsecret_db_password}}", "{{CFSECRET_DB_PASSWORD}}")] // a lower-case prefix too: never silently left as text
    public void A_placeholder_that_is_not_upper_snake_case_is_an_error_naming_the_fix(string placeholder, string fix)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => SecretPlaceholders.Names($"Password={placeholder}", "app/App.config"));

        Assert.Contains($"'app/App.config' uses {placeholder}", ex.Message);
        Assert.Contains("upper snake case", ex.Message);
        Assert.Contains($"Try: {fix}", ex.Message);
    }

    [Theory]
    [InlineData("CFSECRET_DB_PASSWORD", true)]
    [InlineData("CFSECRET_A1", true)]
    [InlineData("CFSECRET_Db", false)]
    [InlineData("DB_PASSWORD", false)]
    [InlineData("CFSECRET_", false)]
    public void IsName_accepts_only_upper_snake_case_with_the_prefix(string name, bool expected)
    {
        Assert.Equal(expected, SecretPlaceholders.IsName(name));
    }

    [Fact]
    public void Replace_substitutes_partial_and_repeated_placeholders_and_leaves_unresolved_ones()
    {
        var values = new Dictionary<string, string> { ["CFSECRET_PW"] = "p@ss" };

        var result = SecretPlaceholders.Replace(
            "Server=x;Password={{CFSECRET_PW}};Again={{CFSECRET_PW}};Other={{CFSECRET_NOPE}}",
            name => values.GetValueOrDefault(name));

        Assert.Equal("Server=x;Password=p@ss;Again=p@ss;Other={{CFSECRET_NOPE}}", result);
    }

    [Fact]
    public void Replace_is_a_single_pass_so_a_value_containing_a_placeholder_is_not_expanded_again()
    {
        var values = new Dictionary<string, string>
        {
            ["CFSECRET_A"] = "{{CFSECRET_B}}",
            ["CFSECRET_B"] = "should-not-appear",
        };

        var result = SecretPlaceholders.Replace("{{CFSECRET_A}}", name => values.GetValueOrDefault(name));

        Assert.Equal("{{CFSECRET_B}}", result);
    }

    [Theory]
    [InlineData("x {{CFSECRET_A}}", true)]
    [InlineData("x {{CFSECRET_bad-name}}", true)] // malformed, but still ours -- a real run must not ship it
    [InlineData("x {CFSECRET_A}", false)]
    public void ContainsMarker_flags_anything_that_starts_like_a_placeholder(string text, bool expected)
    {
        Assert.Equal(expected, SecretPlaceholders.ContainsMarker(text));
    }
}
