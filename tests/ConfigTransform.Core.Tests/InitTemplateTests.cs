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

    [Fact]
    public void Hosts_variant_adds_exactly_one_host_patch_and_one_host_layer_to_the_default_plan()
    {
        using var root = new TempDirectory();

        var defaultFiles = InitTemplate.BuildPlan(root.Path);
        var hostsFiles = InitTemplate.BuildHostsPlan(root.Path);

        Assert.Equal(defaultFiles.Count + 2, hostsFiles.Count);
        Assert.All(defaultFiles, f => Assert.Contains(hostsFiles, h => h.RepoRelativePath == f.RepoRelativePath && h.Content == f.Content));
    }

    [Fact]
    public void Hosts_variant_host_layer_extends_client_a_production_and_overrides_message()
    {
        using var root = new TempDirectory();

        var files = InitTemplate.BuildHostsPlan(root.Path);
        var hostLayer = Assert.Single(files, f =>
            f.RepoRelativePath == $".configtransform/Clients/Client-A/Production/Hosts/{InitTemplate.HostName}/configtransform.json");

        var manifest = LayerManifestLoader.Load(WriteAndReturn(hostLayer));
        Assert.Equal(".configtransform/Clients/Client-A/Production/configtransform.json", manifest.Extends);
        var entry = Assert.Single(manifest.Resources);
        Assert.Equal(InitTemplate.ResourcePath, entry.Path);

        var patch = Assert.Single(files, f => f.RepoRelativePath == entry.Patch);
        Assert.Contains($"from Client-A Production {InitTemplate.HostName} config", patch.Content);
    }

    private static string WriteAndReturn(InitFile file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file.FullPath)!);
        File.WriteAllText(file.FullPath, file.Content);
        return file.FullPath;
    }

    [Fact]
    public void The_default_and_hosts_variants_contain_no_secrets()
    {
        // Secrets are opt-in: only --template secrets writes *.secret.* files or placeholders.
        using var root = new TempDirectory();

        foreach (var files in new[] { InitTemplate.BuildPlan(root.Path), InitTemplate.BuildHostsPlan(root.Path) })
        {
            Assert.DoesNotContain(files, f => f.RepoRelativePath.Contains(".secret."));
            Assert.DoesNotContain(files, f => f.Content.Contains("CFSECRET_"));
        }
    }

    [Fact]
    public void Secrets_variant_adds_secrets_files_placeholders_and_a_whole_file_secret_to_the_default_tree()
    {
        using var root = new TempDirectory();

        var files = InitTemplate.BuildSecretsPlan(root.Path);
        var byPath = files.ToDictionary(f => f.RepoRelativePath, f => f.Content);

        // 13 default files + 2 environment secrets files + 1 client secrets file + the credentials base + its replace file
        Assert.Equal(18, files.Count);
        Assert.Equal(InitTemplate.BaseContent, byPath[InitTemplate.ResourcePath]); // the base file stays secret-free
        Assert.Equal(InitTemplate.CredentialsBaseContent, byPath[InitTemplate.CredentialsResourcePath]);
        Assert.Contains("\"apiKey\": \"{{CFSECRET_DEMO_API_KEY}}\"", byPath[".configtransform/Environments/Production/patch-configtransform-template.json"]);
        Assert.Equal("CFSECRET_DEMO_API_KEY=not-a-real-secret-test\n", byPath[".configtransform/Environments/Test/demo.secret.env"]);
        Assert.Equal("CFSECRET_DEMO_API_KEY=not-a-real-secret-client-a-production\n", byPath[".configtransform/Clients/Client-A/Production/demo.secret.env"]);
        Assert.Contains("\"replace\": \".configtransform/Clients/Client-A/Production/credentials.secret.json\"",
            byPath[".configtransform/Clients/Client-A/Production/configtransform.json"]);
        Assert.DoesNotContain(files, f => f.RepoRelativePath.EndsWith(".gitattributes")); // encryption stays a deliberate step
    }
}
