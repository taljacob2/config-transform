using Xunit;

namespace ConfigTransform.Core.Tests;

public class ManifestEntrySelectorTests
{
    [Fact]
    public void Single_entry_manifest_does_not_need_file_arg()
    {
        var manifest = new Manifest("Project/Project.csproj", new[]
        {
            new ManifestFileEntry("App.config", "xml")
        });

        var entry = ManifestEntrySelector.Select(manifest, fileArg: null);

        Assert.Equal("App.config", entry.RelativeToProject);
    }

    [Fact]
    public void Multi_entry_manifest_without_file_arg_throws()
    {
        var manifest = new Manifest("Project/Project.csproj", new[]
        {
            new ManifestFileEntry("App.config", "xml"),
            new ManifestFileEntry("NLog.config", "xml")
        });

        Assert.Throws<ArgumentException>(() => ManifestEntrySelector.Select(manifest, fileArg: null));
    }

    [Fact]
    public void Matches_by_overlay_folder_name()
    {
        var manifest = new Manifest("Project/Project.csproj", new[]
        {
            new ManifestFileEntry("App.config", "xml"),
            new ManifestFileEntry("NLog.config", "xml")
        });

        var entry = ManifestEntrySelector.Select(manifest, "NLog.config");

        Assert.Equal("NLog.config", entry.RelativeToProject);
    }

    [Fact]
    public void No_match_throws()
    {
        var manifest = new Manifest("Project/Project.csproj", new[]
        {
            new ManifestFileEntry("App.config", "xml")
        });

        Assert.Throws<ArgumentException>(() => ManifestEntrySelector.Select(manifest, "DoesNotExist.config"));
    }

    [Fact]
    public void Empty_manifest_throws()
    {
        var manifest = new Manifest("Project/Project.csproj", Array.Empty<ManifestFileEntry>());

        Assert.Throws<InvalidOperationException>(() => ManifestEntrySelector.Select(manifest, fileArg: null));
    }
}
