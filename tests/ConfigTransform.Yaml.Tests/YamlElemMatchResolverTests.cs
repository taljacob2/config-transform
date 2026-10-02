using YamlDotNet.RepresentationModel;
using Xunit;

namespace ConfigTransform.Yaml.Tests;

/// <summary>
/// <see cref="YamlElemMatchResolver"/> directly, and the merge-time edge cases a realistic fixture
/// doesn't contain (docs/FIELD_AUTHORING_DESIGN.md, "YAML array-of-objects matching").
/// </summary>
public class YamlElemMatchResolverTests
{
    private static YamlNode Parse(string yaml)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        return stream.Documents[0].RootNode;
    }

    [Fact]
    public void A_patch_list_is_a_non_empty_sequence_of_maps_that_all_carry_elemMatch()
    {
        Assert.True(YamlElemMatchResolver.IsPatchList((YamlSequenceNode)Parse("- $elemMatch: {role: Admin}\n  enabled: true\n")));
        Assert.False(YamlElemMatchResolver.IsPatchList((YamlSequenceNode)Parse("- role: Admin\n")));
        Assert.False(YamlElemMatchResolver.IsPatchList((YamlSequenceNode)Parse("- $elemMatch: {role: Admin}\n- role: Viewer\n")));
        Assert.False(YamlElemMatchResolver.IsPatchList((YamlSequenceNode)Parse("[]")));
    }

    [Fact]
    public void Text_that_merely_contains_elemMatch_is_not_a_patch_list()
    {
        Assert.False(YamlElemMatchResolver.ContainsElemMatch(Parse("Note: \"use $elemMatch to match\"\n")));
        Assert.True(YamlElemMatchResolver.ContainsElemMatch(Parse("Deep:\n  Rules:\n  - $elemMatch: {role: Admin}\n    on: true\n")));
    }

    [Fact]
    public void Conditions_compare_by_text_whatever_the_quoting()
    {
        // The one deliberate difference from JSON: an unquoted YAML scalar's type depends on the
        // reader, so `enabled: true` and `enabled: "true"` both match the condition enabled=true.
        var sequence = (YamlSequenceNode)Parse("- {name: a, enabled: true}\n- {name: b, enabled: \"true\"}\n- {name: c, enabled: false}\n");
        var condition = new YamlElemMatchResolver.Condition("enabled", new YamlScalarNode("true"));

        Assert.Equal([0, 1], YamlElemMatchResolver.IndicesMatching(sequence, [condition]));
    }

    [Fact]
    public void More_than_one_candidate_throws_listing_every_one()
    {
        using var dir = new TempDir();
        var basePath = dir.WriteFile("base.yaml", "Rules:\n- {role: Admin, env: Prod}\n- {role: Admin, env: Test}\n");
        var patchPath = dir.WriteFile("patch.yaml", "Rules:\n- $elemMatch: {role: Admin}\n  enabled: true\n");

        var ex = Assert.Throws<InvalidOperationException>(() => YamlLayerMerger.Merge(basePath, [patchPath]));

        Assert.Contains("More than one item in \"Rules\" matches role=Admin", ex.Message);
        Assert.Contains("{role: Admin, env: Prod}", ex.Message);
        Assert.Contains("{role: Admin, env: Test}", ex.Message);
    }

    [Fact]
    public void Two_patches_resolving_to_the_same_item_throw()
    {
        using var dir = new TempDir();
        var basePath = dir.WriteFile("base.yaml", "Rules:\n- {role: Admin, env: Prod}\n");
        var patchPath = dir.WriteFile("patch.yaml",
            "Rules:\n- $elemMatch: {role: Admin}\n  a: 1\n- $elemMatch: {env: Prod}\n  b: 2\n");

        var ex = Assert.Throws<InvalidOperationException>(() => YamlLayerMerger.Merge(basePath, [patchPath]));

        Assert.Contains("both resolve to the same item", ex.Message);
    }

    [Fact]
    public void A_patch_list_against_an_existing_non_sequence_value_throws()
    {
        using var dir = new TempDir();
        var basePath = dir.WriteFile("base.yaml", "Rules: just text\n");
        var patchPath = dir.WriteFile("patch.yaml", "Rules:\n- $elemMatch: {role: Admin}\n  enabled: true\n");

        var ex = Assert.Throws<InvalidOperationException>(() => YamlLayerMerger.Merge(basePath, [patchPath]));

        Assert.Contains("not a YAML sequence", ex.Message);
    }

    [Fact]
    public void Against_a_missing_sequence_every_patch_creates_an_item_in_order()
    {
        using var dir = new TempDir();
        var basePath = dir.WriteFile("base.yaml", "Other: 1\n");
        var patchPath = dir.WriteFile("patch.yaml",
            "Rules:\n- $elemMatch: {role: Admin}\n  enabled: true\n- $elemMatch: {role: Viewer}\n  enabled: false\n");

        var merged = YamlLayerMerger.Merge(basePath, [patchPath]).Replace("\r\n", "\n");

        Assert.Equal("Other: 1\nRules:\n- role: Admin\n  enabled: true\n- role: Viewer\n  enabled: false\n", merged);
    }

    private sealed class TempDir : IDisposable
    {
        private readonly string _path = Directory.CreateTempSubdirectory("configtransform-yaml-tests-").FullName;

        public string WriteFile(string name, string content)
        {
            var path = Path.Combine(_path, name);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(_path))
                Directory.Delete(_path, recursive: true);
        }
    }
}
