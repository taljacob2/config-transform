using ConfigTransform.Core.Tests.TestSupport;
using Xunit;

namespace ConfigTransform.Core.Tests;

public class InitScannerTests
{
    private static readonly FormatEngineRegistry Engines = new(
    [
        new FormatEngine("XML", [".config", ".xml"], "xml", DummyMerge, DummyAuthor),
        new FormatEngine("JSON", [".json"], "json", DummyMerge, DummyAuthor),
    ]);

    [Fact]
    public void Finds_every_file_whose_extension_a_registered_engine_handles()
    {
        using var root = new TempDirectory();
        WriteFile(root.Path, "Project/App.config", "<configuration/>");
        WriteFile(root.Path, "Project/appsettings.json", "{}");
        WriteFile(root.Path, "Project/notes.txt", "not a resource");

        var candidates = InitScanner.Scan(root.Path, root.Path, Engines);

        Assert.Equal(["Project/App.config", "Project/appsettings.json"], candidates);
    }

    [Theory]
    [InlineData(".git")]
    [InlineData(".configtransform")]
    [InlineData("bin")]
    [InlineData("obj")]
    [InlineData("node_modules")]
    public void Excludes_structural_noise_directories_at_any_depth(string excludedDir)
    {
        using var root = new TempDirectory();
        WriteFile(root.Path, "Project/App.config", "<configuration/>");
        WriteFile(root.Path, $"nested/{excludedDir}/junk.json", "{}");

        var candidates = InitScanner.Scan(root.Path, root.Path, Engines);

        Assert.Equal(["Project/App.config"], candidates);
    }

    [Fact]
    public void Excluded_directory_names_are_case_insensitive()
    {
        using var root = new TempDirectory();
        WriteFile(root.Path, "Project/App.config", "<configuration/>");
        WriteFile(root.Path, "NODE_MODULES/junk.json", "{}");

        var candidates = InitScanner.Scan(root.Path, root.Path, Engines);

        Assert.Equal(["Project/App.config"], candidates);
    }

    [Theory]
    [InlineData(".config/dotnet-tools.json")]
    [InlineData("nuget.config")]
    public void Excludes_universal_dotnet_tooling_manifests_by_name(string excludedFile)
    {
        using var root = new TempDirectory();
        WriteFile(root.Path, "Project/App.config", "<configuration/>");
        WriteFile(root.Path, excludedFile, "{}");

        var candidates = InitScanner.Scan(root.Path, root.Path, Engines);

        Assert.Equal(["Project/App.config"], candidates);
    }

    [Fact]
    public void Excluded_file_names_are_case_insensitive()
    {
        using var root = new TempDirectory();
        WriteFile(root.Path, "Project/App.config", "<configuration/>");
        WriteFile(root.Path, "NuGet.Config", "{}");

        var candidates = InitScanner.Scan(root.Path, root.Path, Engines);

        Assert.Equal(["Project/App.config"], candidates);
    }

    [Fact]
    public void Returns_repo_root_relative_paths_even_when_scan_root_is_a_subdirectory()
    {
        using var root = new TempDirectory();
        WriteFile(root.Path, "Sub/Project/App.config", "<configuration/>");

        var candidates = InitScanner.Scan(root.Path, Path.Combine(root.Path, "Sub"), Engines);

        Assert.Equal(["Sub/Project/App.config"], candidates);
    }

    [Fact]
    public void Returns_empty_when_scan_root_does_not_exist()
    {
        using var root = new TempDirectory();

        var candidates = InitScanner.Scan(root.Path, Path.Combine(root.Path, "DoesNotExist"), Engines);

        Assert.Empty(candidates);
    }

    [Fact]
    public void Returns_sorted_results()
    {
        using var root = new TempDirectory();
        WriteFile(root.Path, "Zebra/App.config", "<configuration/>");
        WriteFile(root.Path, "Apple/appsettings.json", "{}");

        var candidates = InitScanner.Scan(root.Path, root.Path, Engines);

        Assert.Equal(["Apple/appsettings.json", "Zebra/App.config"], candidates);
    }

    private static void WriteFile(string root, string relativePath, string content)
    {
        var fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    private static string DummyMerge(string basePath, IReadOnlyList<string> patchPathsInOrder) =>
        throw new NotImplementedException("Not invoked -- InitScanner never calls into an engine's Merge/Author.");

    private static string DummyAuthor(
        string precedingContent, string? existingTargetContent, bool isBaseTarget,
        IReadOnlyList<MatchSpec> matches, IReadOnlyList<MatchSpec> setFields) =>
        throw new NotImplementedException("Not invoked -- InitScanner never calls into an engine's Merge/Author.");
}
