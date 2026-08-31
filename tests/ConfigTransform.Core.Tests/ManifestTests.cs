using System.Text.Json;
using Xunit;

namespace ConfigTransform.Core.Tests;

public class ManifestTests
{
    [Fact]
    public void Deserializes_manifest_matching_the_documented_schema()
    {
        const string json = """
            {
              "project": "services/billing/ProjectB.Core/ProjectB.Core.csproj",
              "files": [
                { "relativeToProject": "appsettings.json", "type": "json" }
              ]
            }
            """;

        var manifest = JsonSerializer.Deserialize<Manifest>(json);

        Assert.NotNull(manifest);
        Assert.Equal("services/billing/ProjectB.Core/ProjectB.Core.csproj", manifest.Project);
        Assert.Single(manifest.Files);
        Assert.Equal("appsettings.json", manifest.Files[0].RelativeToProject);
        Assert.Equal("json", manifest.Files[0].Type);
        Assert.Null(manifest.Files[0].Name);
    }

    [Theory]
    [InlineData("App.config", null, "App.config")]
    [InlineData("app.config", null, "app.config")]
    [InlineData("Sub1/settings.config", "settings.config-sub1", "settings.config-sub1")]
    public void OverlayFolderName_derives_from_filename_unless_explicitly_overridden(
        string relativeToProject, string? explicitName, string expected)
    {
        var entry = new ManifestFileEntry(relativeToProject, "xml", explicitName);

        Assert.Equal(expected, entry.OverlayFolderName);
    }
}
