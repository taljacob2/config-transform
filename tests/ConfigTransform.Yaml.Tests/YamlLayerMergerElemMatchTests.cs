using ConfigTransform.Core;
using Xunit;

namespace ConfigTransform.Yaml.Tests;

/// <summary>
/// Merge-time resolution of YAML <c>$elemMatch</c> overlays (docs/FIELD_AUTHORING_DESIGN.md, "YAML
/// array-of-objects matching") -- the port of JSON's. The fixture pairs are built the same way as
/// JSON's: the Client layer's patch targets an item the Environment layer itself just created, so
/// they prove progressive resolution (a Client patch resolves against base + Environment, not the
/// base alone), alongside a sibling plain positional sequence overlay in the same file. One set
/// indents its sequences under their key, the other doesn't, so both layouts are pinned.
/// </summary>
public class YamlLayerMergerElemMatchTests
{
    [Theory]
    [InlineData("DotNetCore", "Project/appsettings.yaml")]
    [InlineData("GenericYaml", "Project/custom-settings.yaml")]
    public void Merged_output_matches_the_expected_file_exactly(string fixtureSet, string resourcePath)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixtureSet, "ElemMatch");
        var resolved = Resolve(root, "ClientA", "Production", resourcePath);

        var merged = YamlLayerMerger.Merge(resolved.BasePath, resolved.PatchPathsInOrder);

        Assert.Equal(File.ReadAllText(Path.Combine(root, "Expected", "ClientA-Production.yaml")), merged);
    }

    [Fact]
    public void An_environment_layer_patch_resolves_against_the_base_alone()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Fixtures", "DotNetCore", "ElemMatch");
        var resolved = Resolve(root, client: null, "Production", "Project/appsettings.yaml");

        var merged = YamlLayerMerger.Merge(resolved.BasePath, resolved.PatchPathsInOrder).Replace("\r\n", "\n");

        Assert.Contains("  - role: Admin\n    env: Production\n    enabled: false\n", merged); // untouched
        Assert.Contains("  - role: Viewer\n    env: Production\n    enabled: true\n", merged); // matched, updated
        Assert.Contains("  - role: Auditor\n    enabled: true\n    env: Production\n", merged); // no match: created
    }

    private static ResolvedResource Resolve(string root, string? client, string environment, string resourcePath)
    {
        var chain = LayerChain.Build(root, LayerPathResolver.Resolve(root, client, environment));
        return LayerChain.ResolveResource(root, chain, resourcePath);
    }
}
