using Xunit;

namespace ConfigTransform.Core.Tests;

public class TextLayoutTests
{
    [Theory]
    [InlineData("a\nb\n", "\n", true)]
    [InlineData("a\r\nb\r\n", "\r\n", true)]
    [InlineData("a\nb", "\n", false)]
    [InlineData("a\r\nb", "\r\n", false)]
    [InlineData("", "\n", true)] // nothing to mirror: the default
    public void Of_reads_the_line_ending_style_and_final_newline(string text, string newLine, bool endsWithNewLine)
    {
        Assert.Equal(new TextLayout(newLine, endsWithNewLine), TextLayout.Of(text));
    }

    [Theory]
    [InlineData("{\r\n  \"A\": 1\r\n}", "\n", true, "{\n  \"A\": 1\n}\n")]
    [InlineData("{\n  \"A\": 1\n}", "\r\n", true, "{\r\n  \"A\": 1\r\n}\r\n")]
    [InlineData("A: 1\nB: 2\n", "\n", false, "A: 1\nB: 2")]
    [InlineData("A: 1\r\n", "\n", true, "A: 1\n")]
    public void Apply_rewrites_line_endings_and_the_final_newline(string output, string newLine, bool endsWithNewLine, string expected)
    {
        Assert.Equal(expected, new TextLayout(newLine, endsWithNewLine).Apply(output));
    }

    [Fact]
    public void Apply_removes_at_most_one_final_newline()
    {
        // A YAML "|+" block at the end of a document keeps its trailing blank lines as content.
        Assert.Equal("Notes: |+\n  text\n\n", TextLayout.Default.Apply("Notes: |+\n  text\n\n"));
    }
}
