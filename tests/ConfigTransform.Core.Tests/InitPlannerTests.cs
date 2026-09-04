using ConfigTransform.Core.Tests.TestSupport;
using Xunit;

namespace ConfigTransform.Core.Tests;

public class InitPlannerTests
{
    [Fact]
    public void Environment_layer_lists_every_selected_resource_with_no_patch()
    {
        using var root = new TempDirectory();

        var files = InitPlanner.BuildPlan(
            root.Path, resources: ["Project/App.config", "Project/appsettings.json"],
            environments: ["Production"], clients: []);

        var envFile = Assert.Single(files, f => f.RepoRelativePath == ".configtransform/Environments/Production/configtransform.json");
        Assert.DoesNotContain("\"patch\"", envFile.Content);
        Assert.Contains("Project/App.config", envFile.Content);
        Assert.Contains("Project/appsettings.json", envFile.Content);
        Assert.DoesNotContain("\"extends\"", envFile.Content);
    }

    [Fact]
    public void Client_layer_declares_extends_only_with_an_empty_resources_list()
    {
        using var root = new TempDirectory();

        var files = InitPlanner.BuildPlan(
            root.Path, resources: ["Project/App.config"], environments: ["Production"], clients: ["Acme"]);

        var clientFile = Assert.Single(files, f => f.RepoRelativePath == ".configtransform/Clients/Acme/Production/configtransform.json");
        var manifest = LayerManifestLoader.Load(WriteAndReturn(clientFile));
        Assert.Equal(".configtransform/Environments/Production/configtransform.json", manifest.Extends);
        Assert.Empty(manifest.Resources);
    }

    [Fact]
    public void Builds_the_full_cross_product_of_resources_environments_and_clients()
    {
        using var root = new TempDirectory();

        var files = InitPlanner.BuildPlan(
            root.Path, resources: ["Project/App.config"],
            environments: ["Production", "Test"], clients: ["Acme", "Globex"]);

        // 2 environment layers + (2 environments x 2 clients) client layers.
        Assert.Equal(6, files.Count);
        Assert.Contains(files, f => f.RepoRelativePath == ".configtransform/Environments/Production/configtransform.json");
        Assert.Contains(files, f => f.RepoRelativePath == ".configtransform/Environments/Test/configtransform.json");
        Assert.Contains(files, f => f.RepoRelativePath == ".configtransform/Clients/Acme/Production/configtransform.json");
        Assert.Contains(files, f => f.RepoRelativePath == ".configtransform/Clients/Acme/Test/configtransform.json");
        Assert.Contains(files, f => f.RepoRelativePath == ".configtransform/Clients/Globex/Production/configtransform.json");
        Assert.Contains(files, f => f.RepoRelativePath == ".configtransform/Clients/Globex/Test/configtransform.json");
    }

    [Fact]
    public void Re_running_against_an_existing_environment_layer_keeps_existing_patch_references()
    {
        using var root = new TempDirectory();
        var envDir = Path.Combine(root.Path, ".configtransform", "Environments", "Production");
        Directory.CreateDirectory(envDir);
        File.WriteAllText(Path.Combine(envDir, "configtransform.json"), """
            { "resources": [
                { "path": "Project/App.config", "patch": ".configtransform/Environments/Production/patch-Project-App.config.xml" }
            ] }
            """);

        var files = InitPlanner.BuildPlan(
            root.Path, resources: ["Project/App.config", "Project/appsettings.json"],
            environments: ["Production"], clients: []);

        var envFile = Assert.Single(files, f => f.RepoRelativePath == ".configtransform/Environments/Production/configtransform.json");
        var manifest = LayerManifestLoader.Load(WriteAndReturn(envFile));
        var appConfigEntry = Assert.Single(manifest.Resources, r => r.Path == "Project/App.config");
        Assert.Equal(".configtransform/Environments/Production/patch-Project-App.config.xml", appConfigEntry.Patch);
        var appSettingsEntry = Assert.Single(manifest.Resources, r => r.Path == "Project/appsettings.json");
        Assert.Null(appSettingsEntry.Patch);
    }

    [Fact]
    public void Re_running_does_not_duplicate_an_already_listed_resource()
    {
        using var root = new TempDirectory();
        var envDir = Path.Combine(root.Path, ".configtransform", "Environments", "Production");
        Directory.CreateDirectory(envDir);
        File.WriteAllText(Path.Combine(envDir, "configtransform.json"), """{ "resources": [ { "path": "Project/App.config" } ] }""");

        var files = InitPlanner.BuildPlan(
            root.Path, resources: ["Project/App.config"], environments: ["Production"], clients: []);

        var envFile = Assert.Single(files, f => f.RepoRelativePath == ".configtransform/Environments/Production/configtransform.json");
        var manifest = LayerManifestLoader.Load(WriteAndReturn(envFile));
        Assert.Single(manifest.Resources);
    }

    [Fact]
    public void Re_running_does_not_overwrite_an_already_set_extends()
    {
        using var root = new TempDirectory();
        var clientDir = Path.Combine(root.Path, ".configtransform", "Clients", "Acme", "Production");
        Directory.CreateDirectory(clientDir);
        File.WriteAllText(Path.Combine(clientDir, "configtransform.json"), """
            { "extends": ".configtransform/Environments/CustomBase/configtransform.json", "resources": [] }
            """);

        var files = InitPlanner.BuildPlan(
            root.Path, resources: ["Project/App.config"], environments: ["Production"], clients: ["Acme"]);

        var clientFile = Assert.Single(files, f => f.RepoRelativePath == ".configtransform/Clients/Acme/Production/configtransform.json");
        var manifest = LayerManifestLoader.Load(WriteAndReturn(clientFile));
        Assert.Equal(".configtransform/Environments/CustomBase/configtransform.json", manifest.Extends);
    }

    private static string WriteAndReturn(InitFile file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file.FullPath)!);
        File.WriteAllText(file.FullPath, file.Content);
        return file.FullPath;
    }
}
