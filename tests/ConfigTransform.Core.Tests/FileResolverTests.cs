using ConfigTransform.Core.Tests.TestSupport;
using Xunit;

namespace ConfigTransform.Core.Tests;

public class FileResolverTests
{
    [Fact]
    public void TryResolveCaseInsensitive_finds_exact_match()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(Path.Combine(dir.Path, "App.config"), "<configuration/>");

        var result = FileResolver.TryResolveCaseInsensitive(dir.Path, "App.config");

        Assert.Equal(Path.Combine(dir.Path, "App.config"), result);
    }

    [Fact]
    public void TryResolveCaseInsensitive_finds_differently_cased_match()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(Path.Combine(dir.Path, "app.config"), "<configuration/>");

        var result = FileResolver.TryResolveCaseInsensitive(dir.Path, "App.config");

        Assert.Equal(Path.Combine(dir.Path, "app.config"), result);
    }

    [Fact]
    public void TryResolveCaseInsensitive_returns_null_when_no_match()
    {
        using var dir = new TempDirectory();

        var result = FileResolver.TryResolveCaseInsensitive(dir.Path, "App.config");

        Assert.Null(result);
    }

    [Fact]
    public void TryResolveCaseInsensitive_returns_null_when_directory_does_not_exist()
    {
        using var dir = new TempDirectory();
        var missingDirectory = Path.Combine(dir.Path, "does-not-exist");

        var result = FileResolver.TryResolveCaseInsensitive(missingDirectory, "App.config");

        Assert.Null(result);
    }

    [SkippableFact]
    public void TryResolveCaseInsensitive_throws_when_ambiguous()
    {
        // Two files differing only by case can only coexist on a case-sensitive filesystem
        // (Linux). On Windows/macOS's default case-insensitive filesystems, the second
        // File.WriteAllText call overwrites the first — there is no way to construct real
        // ambiguity there, so this test is only meaningful on Linux. This discrepancy is
        // exactly the hazard FileResolver exists to handle — see CONFIG_MANAGEMENT.md §5.4.
        Skip.IfNot(OperatingSystem.IsLinux(), "Case ambiguity can only exist on a case-sensitive filesystem.");

        using var dir = new TempDirectory();
        File.WriteAllText(Path.Combine(dir.Path, "App.config"), "a");
        File.WriteAllText(Path.Combine(dir.Path, "app.config"), "b");

        Assert.Throws<InvalidOperationException>(() =>
            FileResolver.TryResolveCaseInsensitive(dir.Path, "App.config"));
    }

    [Fact]
    public void ResolveCaseInsensitiveRequired_returns_path_when_found()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(Path.Combine(dir.Path, "App.config"), "<configuration/>");

        var result = FileResolver.ResolveCaseInsensitiveRequired(dir.Path, "App.config");

        Assert.Equal(Path.Combine(dir.Path, "App.config"), result);
    }

    [Fact]
    public void ResolveCaseInsensitiveRequired_throws_FileNotFoundException_when_missing()
    {
        using var dir = new TempDirectory();

        Assert.Throws<FileNotFoundException>(() =>
            FileResolver.ResolveCaseInsensitiveRequired(dir.Path, "App.config"));
    }
}
