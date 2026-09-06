using ConfigTransform.Core.Tests.TestSupport;
using Xunit;

namespace ConfigTransform.Core.Tests;

public class InitTemplateTests
{
    [Fact]
    public void Builds_thirteen_files_covering_base_two_environments_and_two_clients_each()
    {
        using var root = new TempDirectory();

        var files = InitTemplate.BuildPlan(root.Path);

        Assert.Equal(13, files.Count);
    }

    [Fact]
    public void Every_layer_overrides_message_naming_itself()
    {
        using var root = new TempDirectory();

        var files = InitTemplate.BuildPlan(root.Path);

        Assert.Contains(files, f => f.RepoRelativePath == InitTemplate.ResourcePath && f.Content.Contains("from base config"));
        Assert.Contains(files, f => f.Content.Contains("from Production config"));
        Assert.Contains(files, f => f.Content.Contains("from Test config"));
        Assert.Contains(files, f => f.Content.Contains("from Client-A Production config"));
        Assert.Contains(files, f => f.Content.Contains("from Client-A Test config"));
        Assert.Contains(files, f => f.Content.Contains("from Client-B Production config"));
        Assert.Contains(files, f => f.Content.Contains("from Client-B Test config"));
    }

    [Fact]
    public void Patch_filenames_do_not_stutter_the_json_extension()
    {
        using var root = new TempDirectory();

        var files = InitTemplate.BuildPlan(root.Path);

        var envPatch = Assert.Single(files, f =>
            f.RepoRelativePath == ".configtransform/Environments/Production/patch-configtransform-template.json");
        Assert.DoesNotContain(".json.json", envPatch.RepoRelativePath);
    }

    [Fact]
    public void Environment_layers_list_the_template_resource_with_their_own_patch()
    {
        using var root = new TempDirectory();

        var files = InitTemplate.BuildPlan(root.Path);
        var envLayer = Assert.Single(files, f => f.RepoRelativePath == ".configtransform/Environments/Production/configtransform.json");

        var fullPath = WriteAndReturn(envLayer);
        var manifest = LayerManifestLoader.Load(fullPath);
        var entry = Assert.Single(manifest.Resources);
        Assert.Equal(InitTemplate.ResourcePath, entry.Path);
        Assert.Equal(".configtransform/Environments/Production/patch-configtransform-template.json", entry.Patch);
        Assert.Null(manifest.Extends);
    }

    [Fact]
    public void Client_layers_extend_the_matching_environment_layer()
    {
        using var root = new TempDirectory();

        var files = InitTemplate.BuildPlan(root.Path);
        var clientLayer = Assert.Single(files, f => f.RepoRelativePath == ".configtransform/Clients/Client-A/Test/configtransform.json");

        var manifest = LayerManifestLoader.Load(WriteAndReturn(clientLayer));
        Assert.Equal(".configtransform/Environments/Test/configtransform.json", manifest.Extends);
        var entry = Assert.Single(manifest.Resources);
        Assert.Equal(".configtransform/Clients/Client-A/Test/patch-configtransform-template.json", entry.Patch);
    }

    private static string WriteAndReturn(InitFile file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file.FullPath)!);
        File.WriteAllText(file.FullPath, file.Content);
        return file.FullPath;
    }
}
