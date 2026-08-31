using ConfigTransform.Core.Tests.TestSupport;
using Xunit;

namespace ConfigTransform.Core.Tests;

public class ManifestLoaderTests
{
    [Fact]
    public void Loads_a_valid_manifest()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "manifest.json");
        File.WriteAllText(path, """
            { "directory": "Project", "files": [ { "relativeToDirectory": "App.config", "type": "xml" } ] }
            """);

        var manifest = ManifestLoader.Load(path);

        Assert.Equal("Project", manifest.Directory);
        Assert.Single(manifest.Files);
    }

    [Fact]
    public void Missing_file_throws_FileNotFoundException()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "manifest.json");

        Assert.Throws<FileNotFoundException>(() => ManifestLoader.Load(path));
    }

    [Fact]
    public void Malformed_json_throws_InvalidOperationException()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "manifest.json");
        File.WriteAllText(path, "{ this is not valid json");

        Assert.Throws<InvalidOperationException>(() => ManifestLoader.Load(path));
    }
}
