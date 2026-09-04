using ConfigTransform.Core.Tests.TestSupport;
using Xunit;

namespace ConfigTransform.Core.Tests;

public class LayerChainTests
{
    [Fact]
    public void Build_returns_empty_chain_for_null_target()
    {
        using var root = new TempDirectory();

        var chain = LayerChain.Build(root.Path, null);

        Assert.Empty(chain);
    }

    [Fact]
    public void Build_returns_empty_chain_when_target_does_not_exist()
    {
        using var root = new TempDirectory();

        var chain = LayerChain.Build(root.Path, ".configtransform/Environments/Production/configtransform.json");

        Assert.Empty(chain);
    }

    [Fact]
    public void Build_walks_extends_outermost_first()
    {
        using var root = new TempDirectory();
        WriteLayer(root.Path, ".configtransform/Environments/Production/configtransform.json",
            extends: null, resources: """[ { "path": "Project/App.config" } ]""");
        WriteLayer(root.Path, ".configtransform/Clients/Acme/Production/configtransform.json",
            extends: ".configtransform/Environments/Production/configtransform.json",
            resources: """[ { "path": "Project/App.config" } ]""");

        var chain = LayerChain.Build(root.Path, ".configtransform/Clients/Acme/Production/configtransform.json");

        Assert.Equal(2, chain.Count);
        Assert.EndsWith(Path.Combine("Environments", "Production", "configtransform.json"), chain[0].Path);
        Assert.EndsWith(Path.Combine("Clients", "Acme", "Production", "configtransform.json"), chain[1].Path);
    }

    [Fact]
    public void Build_stops_gracefully_when_extends_target_does_not_exist()
    {
        using var root = new TempDirectory();
        WriteLayer(root.Path, ".configtransform/Clients/Acme/Production/configtransform.json",
            extends: ".configtransform/Environments/Production/configtransform.json",
            resources: """[ { "path": "Project/App.config" } ]""");

        var chain = LayerChain.Build(root.Path, ".configtransform/Clients/Acme/Production/configtransform.json");

        Assert.Single(chain);
        Assert.EndsWith(Path.Combine("Clients", "Acme", "Production", "configtransform.json"), chain[0].Path);
    }

    [Fact]
    public void Build_throws_on_a_cycle()
    {
        using var root = new TempDirectory();
        WriteLayer(root.Path, ".configtransform/Environments/A/configtransform.json",
            extends: ".configtransform/Environments/B/configtransform.json", resources: "[]");
        WriteLayer(root.Path, ".configtransform/Environments/B/configtransform.json",
            extends: ".configtransform/Environments/A/configtransform.json", resources: "[]");

        Assert.Throws<InvalidOperationException>(() =>
            LayerChain.Build(root.Path, ".configtransform/Environments/A/configtransform.json"));
    }

    [Fact]
    public void ResolveResource_collects_patches_in_extends_order_across_three_layers()
    {
        using var root = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(root.Path, "Project"));
        File.WriteAllText(Path.Combine(root.Path, "Project", "App.config"), "base");

        WriteLayer(root.Path, ".configtransform/Environments/Production/configtransform.json",
            extends: null,
            resources: """[ { "path": "Project/App.config", "patch": ".configtransform/Environments/Production/env.xml" } ]""");
        File.WriteAllText(Path.Combine(root.Path, ".configtransform/Environments/Production/env.xml"), "env");

        WriteLayer(root.Path, ".configtransform/Clients/Acme/Production/configtransform.json",
            extends: ".configtransform/Environments/Production/configtransform.json",
            resources: """[ { "path": "Project/App.config", "patch": ".configtransform/Clients/Acme/Production/client.xml" } ]""");
        File.WriteAllText(Path.Combine(root.Path, ".configtransform/Clients/Acme/Production/client.xml"), "client");

        var chain = LayerChain.Build(root.Path, ".configtransform/Clients/Acme/Production/configtransform.json");
        var resolved = LayerChain.ResolveResource(root.Path, chain, "Project/App.config");

        Assert.Equal(Path.Combine(root.Path, "Project", "App.config"), resolved.BasePath);
        Assert.Equal(2, resolved.PatchPathsInOrder.Count);
        Assert.EndsWith("env.xml", resolved.PatchPathsInOrder[0]);
        Assert.EndsWith("client.xml", resolved.PatchPathsInOrder[1]);
        Assert.All(resolved.Report, line => Assert.DoesNotContain("not listed", line));
    }

    [Fact]
    public void ResolveResource_reports_layers_that_do_not_list_the_resource()
    {
        using var root = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(root.Path, "Project"));
        File.WriteAllText(Path.Combine(root.Path, "Project", "App.config"), "base");

        WriteLayer(root.Path, ".configtransform/Clients/Acme/Production/configtransform.json",
            extends: null, resources: "[]");

        var chain = LayerChain.Build(root.Path, ".configtransform/Clients/Acme/Production/configtransform.json");
        var resolved = LayerChain.ResolveResource(root.Path, chain, "Project/App.config");

        Assert.Empty(resolved.PatchPathsInOrder);
        Assert.Contains(resolved.Report, line => line.Contains("not listed"));
    }

    [Fact]
    public void ResolveResource_reports_a_resource_listed_with_no_patch()
    {
        using var root = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(root.Path, "Project"));
        File.WriteAllText(Path.Combine(root.Path, "Project", "App.config"), "base");

        WriteLayer(root.Path, ".configtransform/Clients/Acme/Production/configtransform.json",
            extends: null, resources: """[ { "path": "Project/App.config" } ]""");

        var chain = LayerChain.Build(root.Path, ".configtransform/Clients/Acme/Production/configtransform.json");
        var resolved = LayerChain.ResolveResource(root.Path, chain, "Project/App.config");

        Assert.Empty(resolved.PatchPathsInOrder);
        Assert.Contains(resolved.Report, line => line.Contains("listed with no patch"));
    }

    [Fact]
    public void ResolveResource_throws_when_a_declared_patch_file_does_not_exist_on_disk()
    {
        using var root = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(root.Path, "Project"));
        File.WriteAllText(Path.Combine(root.Path, "Project", "App.config"), "base");

        WriteLayer(root.Path, ".configtransform/Clients/Acme/Production/configtransform.json",
            extends: null,
            resources: """[ { "path": "Project/App.config", "patch": ".configtransform/Clients/Acme/Production/missing.xml" } ]""");

        var chain = LayerChain.Build(root.Path, ".configtransform/Clients/Acme/Production/configtransform.json");

        Assert.Throws<FileNotFoundException>(() => LayerChain.ResolveResource(root.Path, chain, "Project/App.config"));
    }

    [Fact]
    public void ResolveResource_throws_when_base_file_is_missing()
    {
        using var root = new TempDirectory();

        Assert.Throws<FileNotFoundException>(() =>
            LayerChain.ResolveResource(root.Path, [], "Project/App.config"));
    }

    [Fact]
    public void ResolveAllResources_unions_and_dedupes_across_layers()
    {
        using var root = new TempDirectory();
        WriteLayer(root.Path, ".configtransform/Environments/Production/configtransform.json",
            extends: null,
            resources: """[ { "path": "OrderProcessor.Framework/App.config" }, { "path": "BillingApi.Core/appsettings.json" } ]""");
        WriteLayer(root.Path, ".configtransform/Clients/Acme/Production/configtransform.json",
            extends: ".configtransform/Environments/Production/configtransform.json",
            resources: """[ { "path": "OrderProcessor.Framework/App.config" } ]""");

        var chain = LayerChain.Build(root.Path, ".configtransform/Clients/Acme/Production/configtransform.json");
        var all = LayerChain.ResolveAllResources(chain);

        Assert.Equal(2, all.Count);
        Assert.Contains("OrderProcessor.Framework/App.config", all);
        Assert.Contains("BillingApi.Core/appsettings.json", all);
    }

    [Fact]
    public void ReverseLookup_finds_every_layer_that_patches_the_resource()
    {
        using var root = new TempDirectory();
        WriteLayer(root.Path, ".configtransform/Environments/Production/configtransform.json",
            extends: null,
            resources: """[ { "path": "OrderProcessor.Framework/App.config", "patch": ".configtransform/Environments/Production/env.xml" } ]""");
        WriteLayer(root.Path, ".configtransform/Clients/Acme/Production/configtransform.json",
            extends: ".configtransform/Environments/Production/configtransform.json",
            resources: """[ { "path": "OrderProcessor.Framework/App.config", "patch": ".configtransform/Clients/Acme/Production/client.xml" } ]""");
        WriteLayer(root.Path, ".configtransform/Environments/Staging/configtransform.json",
            extends: null, resources: "[]");

        var found = LayerChain.ReverseLookup(root.Path, "OrderProcessor.Framework/App.config");

        Assert.Equal(2, found.Count);
        Assert.Contains(found, e => e.LayerPath.Contains("Environments/Production") && e.Extends is null);
        Assert.Contains(found, e => e.LayerPath.Contains("Clients/Acme/Production") && e.Extends is not null);
    }

    [Fact]
    public void ReverseLookup_returns_empty_when_dot_configtransform_does_not_exist()
    {
        using var root = new TempDirectory();

        var found = LayerChain.ReverseLookup(root.Path, "Project/App.config");

        Assert.Empty(found);
    }

    private static void WriteLayer(string root, string relativePath, string? extends, string resources)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var extendsJson = extends is null ? "null" : $"\"{extends}\"";
        File.WriteAllText(path, $$"""{ "extends": {{extendsJson}}, "resources": {{resources}} }""");
    }
}
