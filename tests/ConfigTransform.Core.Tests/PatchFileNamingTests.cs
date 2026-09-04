using Xunit;

namespace ConfigTransform.Core.Tests;

public class PatchFileNamingTests
{
    [Fact]
    public void Appends_the_patch_extension_when_the_resource_extension_differs()
    {
        var name = PatchFileNaming.BuildFileName("Project/App.config", "xml");

        Assert.Equal("patch-Project-App.config.xml", name);
    }

    [Fact]
    public void Does_not_stutter_when_the_resource_extension_already_matches_the_patch_extension()
    {
        var name = PatchFileNaming.BuildFileName("configtransform-template.json", "json");

        Assert.Equal("patch-configtransform-template.json", name);
    }

    [Fact]
    public void Match_check_is_case_insensitive()
    {
        var name = PatchFileNaming.BuildFileName("Settings.JSON", "json");

        Assert.Equal("patch-Settings.JSON", name);
    }

    [Fact]
    public void Sanitizes_slashes_to_dashes()
    {
        var name = PatchFileNaming.BuildFileName("Nested/Project/appsettings.json", "json");

        Assert.Equal("patch-Nested-Project-appsettings.json", name);
    }
}
