using Xunit;

namespace ConfigTransform.Core.Tests;

public class GitDiffTests
{
    [Fact]
    public void Render_returns_empty_for_identical_content()
    {
        var diff = GitDiff.Render("same content\n", "same content\n");

        Assert.True(string.IsNullOrWhiteSpace(diff));
    }

    [Fact]
    public void Render_shows_the_actual_changed_content()
    {
        var diff = GitDiff.Render(
            "line one\nold value\nline three\n",
            "line one\nnew value\nline three\n");

        Assert.Contains("old value", diff);
        Assert.Contains("new value", diff);
    }
}
