using Xunit;

namespace ConfigTransform.Env.Tests;

/// <summary>
/// Direct tests of the `.env` grammar itself (see EnvFile.cs's own remarks for the full rule
/// list) -- kept separate from EnvLayerMergerTests/EnvFieldAuthorTests, which exercise the
/// grammar only incidentally through real merge/set scenarios.
/// </summary>
public class EnvFileTests
{
    [Fact]
    public void Blank_lines_and_whole_line_comments_are_ignored()
    {
        var pairs = EnvFile.Parse("""
            # a comment
            FOO=bar

            # another comment
            BAZ=qux
            """);

        Assert.Equal(
            [new("FOO", "bar"), new("BAZ", "qux")],
            pairs);
    }

    [Fact]
    public void Export_prefix_is_stripped()
    {
        var pairs = EnvFile.Parse("export FOO=bar");
        Assert.Equal([new("FOO", "bar")], pairs);
    }

    [Theory]
    [InlineData("FOO=\"bar baz\"", "bar baz")]
    [InlineData("FOO='bar baz'", "bar baz")]
    [InlineData("FOO=bar", "bar")]
    public void Matching_quotes_are_stripped_with_no_escape_processing(string line, string expectedValue)
    {
        var pairs = EnvFile.Parse(line);
        Assert.Equal("FOO", pairs[0].Key);
        Assert.Equal(expectedValue, pairs[0].Value);
    }

    [Fact]
    public void Inline_trailing_comment_is_not_stripped_only_whole_line_comments_are()
    {
        // Real .env tooling disagrees on this exact point; this tool treats '#' as a comment
        // marker only when it's the first non-whitespace character on the line -- otherwise a
        // value like a password containing '#' would be silently truncated.
        var pairs = EnvFile.Parse("PASSWORD=abc#123");
        Assert.Equal("abc#123", pairs[0].Value);
    }

    [Theory]
    [InlineData("1FOO=bar")]
    [InlineData("FOO-BAR=baz")]
    [InlineData("FOO BAR=baz")]
    public void An_invalid_key_is_rejected(string line)
    {
        Assert.Throws<InvalidOperationException>(() => EnvFile.Parse(line));
    }

    [Fact]
    public void A_line_with_no_equals_sign_is_malformed()
    {
        Assert.Throws<InvalidOperationException>(() => EnvFile.Parse("NOT_A_VALID_LINE"));
    }

    [Fact]
    public void A_later_duplicate_key_in_the_same_content_wins_but_keeps_its_first_position()
    {
        var pairs = EnvFile.Parse("FOO=1\nBAR=2\nFOO=3");
        Assert.Equal([new("FOO", "3"), new("BAR", "2")], pairs);
    }

    [Fact]
    public void Serialize_leaves_a_plain_value_unquoted()
    {
        var result = EnvFile.Serialize([new("FOO", "bar")]);
        Assert.Equal("FOO=bar\n", result);
    }

    [Theory]
    [InlineData("bar baz", "FOO=\"bar baz\"\n")]
    [InlineData("", "FOO=\"\"\n")]
    [InlineData("abc#123", "FOO=\"abc#123\"\n")]
    public void Serialize_quotes_only_when_the_value_needs_it(string value, string expected)
    {
        var result = EnvFile.Serialize([new("FOO", value)]);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ValidateKey_throws_for_an_invalid_key_and_accepts_a_valid_one()
    {
        Assert.Throws<InvalidOperationException>(() => EnvFile.ValidateKey("1BAD"));
        var exception = Record.Exception(() => EnvFile.ValidateKey("_valid_KEY9"));
        Assert.Null(exception);
    }

    [Fact]
    public void A_value_containing_a_literal_equals_sign_keeps_everything_after_the_first_one()
    {
        // IndexOf('=') finds only the first '=' -- a connection-string-shaped value with its own
        // '=' characters (e.g. a query string) must not be truncated at the second one.
        var pairs = EnvFile.Parse("URL=https://example.com/?a=1&b=2");
        Assert.Equal("https://example.com/?a=1&b=2", pairs[0].Value);
    }

    [Fact]
    public void An_empty_value_round_trips_through_Parse()
    {
        var pairs = EnvFile.Parse("FOO=");
        Assert.Equal("", pairs[0].Value);
    }

    [Fact]
    public void An_empty_quoted_value_round_trips_through_Parse()
    {
        var pairs = EnvFile.Parse("FOO=\"\"");
        Assert.Equal("", pairs[0].Value);
    }

    [Fact]
    public void Incidental_whitespace_around_the_key_and_value_is_trimmed()
    {
        var pairs = EnvFile.Parse("  FOO  =  bar  ");
        Assert.Equal("FOO", pairs[0].Key);
        Assert.Equal("bar", pairs[0].Value);
    }

    [Fact]
    public void Windows_line_endings_are_handled_the_same_as_Unix_ones()
    {
        var pairs = EnvFile.Parse("FOO=bar\r\nBAZ=qux\r\n");
        Assert.Equal([new("FOO", "bar"), new("BAZ", "qux")], pairs);
    }

    [Fact]
    public void A_file_with_no_trailing_newline_parses_the_same_as_one_with_it()
    {
        var pairs = EnvFile.Parse("FOO=bar\nBAZ=qux");
        Assert.Equal([new("FOO", "bar"), new("BAZ", "qux")], pairs);
    }

    [Fact]
    public void Export_is_case_sensitive_and_ordinal_a_capitalized_Export_is_not_recognized()
    {
        // "Export FOO=bar" doesn't match the literal "export " prefix, so it's parsed as a plain
        // line -- and "Export FOO" (with the space) fails key validation, since a real shell's
        // own `export` keyword is lowercase-only too.
        Assert.Throws<InvalidOperationException>(() => EnvFile.Parse("Export FOO=bar"));
    }
}
