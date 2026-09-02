using ConfigTransform.Core.Tests.TestSupport;
using Xunit;

namespace ConfigTransform.Core.Tests;

public class ManifestDiscoveryTests
{
    [Fact]
    public void Finds_the_only_manifest_under_dot_configtransform()
    {
        using var dir = new TempDirectory();
        var manifestDir = Path.Combine(dir.Path, ".configtransform", "ProjectA");
        Directory.CreateDirectory(manifestDir);
        var manifestPath = Path.Combine(manifestDir, "manifest.json");
        File.WriteAllText(manifestPath, "{}");

        var discovered = ManifestDiscovery.Discover(dir.Path);

        Assert.Equal(Path.GetFullPath(manifestPath), discovered);
    }

    [Fact]
    public void Throws_when_no_dot_configtransform_directory_exists()
    {
        using var dir = new TempDirectory();

        var ex = Assert.Throws<ArgumentException>(() => ManifestDiscovery.Discover(dir.Path));
        Assert.Contains("--manifest/-m", ex.Message);
    }

    [Fact]
    public void Throws_when_dot_configtransform_has_no_manifest_json_anywhere()
    {
        using var dir = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(dir.Path, ".configtransform", "ProjectA"));

        Assert.Throws<ArgumentException>(() => ManifestDiscovery.Discover(dir.Path));
    }

    [Fact]
    public void Throws_and_lists_candidates_when_more_than_one_manifest_exists()
    {
        using var dir = new TempDirectory();
        foreach (var name in new[] { "ProjectA", "ProjectB" })
        {
            var manifestDir = Path.Combine(dir.Path, ".configtransform", name);
            Directory.CreateDirectory(manifestDir);
            File.WriteAllText(Path.Combine(manifestDir, "manifest.json"), "{}");
        }

        var ex = Assert.Throws<ArgumentException>(() => ManifestDiscovery.Discover(dir.Path));
        Assert.Contains("ProjectA", ex.Message);
        Assert.Contains("ProjectB", ex.Message);
    }
}
