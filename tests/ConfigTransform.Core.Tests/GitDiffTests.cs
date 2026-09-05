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

    [Fact]
    public void Render_omits_gits_own_file_identity_header_lines()
    {
        // Reported against the published tool: `a`/`b` here are always throwaway OS temp file
        // paths (Render's own leftPath/rightPath), so git's 4-line "diff --git a/tmp... b/tmp...",
        // "index ...", "--- a/tmp...", "+++ b/tmp..." header is meaningless noise, not real file
        // identity -- the caller already shows the actual resource path.
        var diff = GitDiff.Render(
            "line one\nold value\nline three\n",
            "line one\nnew value\nline three\n");

        Assert.DoesNotContain("diff --git", diff);
        Assert.DoesNotContain("index ", diff);
        Assert.DoesNotContain("--- ", diff);
        Assert.DoesNotContain("+++ ", diff);

        // The actual hunk content is untouched.
        Assert.Contains("@@", diff);
        Assert.Contains("old value", diff);
        Assert.Contains("new value", diff);
    }

    [Fact]
    public void Render_preserves_the_no_newline_at_end_of_file_marker()
    {
        var diff = GitDiff.Render("{}", "{ }");

        Assert.Contains("No newline at end of file", diff);
    }
}
