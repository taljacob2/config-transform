using Xunit;

namespace ConfigTransform.Yaml.Tests;

/// <summary>
/// Direct tests of quirks specific to the NetEscapades.Configuration.Yaml/Microsoft.Extensions.
/// Configuration-based merge approach -- verified empirically (not assumed) before this format
/// engine was written; see YamlLayerMerger's own doc comment for the reasoning behind each one.
/// </summary>
public class YamlLayerMergerGrammarTests
{
    [Fact]
    public void An_originally_empty_map_or_sequence_round_trips_as_absent()
    {
        using var dir = new TempDir();
        var basePath = dir.WriteFile("base.yaml", """
            Real: value
            Empty: {}
            EmptyList: []
            """);

        var merged = YamlLayerMerger.Merge(basePath, []);

        Assert.Contains("Real: value", merged);
        Assert.DoesNotContain("Empty", merged);
        Assert.DoesNotContain("EmptyList", merged);
    }

    [Fact]
    public void A_key_that_differs_only_by_case_from_a_sibling_throws_a_real_error()
    {
        // YAML itself is case-sensitive, but Microsoft.Extensions.Configuration is not -- a real
        // NetEscapades.Configuration.Yaml limitation, not a bug in this engine. Documented in
        // CONFIG_MANAGEMENT.md §5.6 rather than silently hit later.
        using var dir = new TempDir();
        var basePath = dir.WriteFile("base.yaml", """
            Foo: one
            foo: two
            """);

        Assert.ThrowsAny<Exception>(() => YamlLayerMerger.Merge(basePath, []));
    }

    [Fact]
    public void Nested_maps_and_sequences_merge_across_layers_identically_to_JSONs_documented_behavior()
    {
        using var dir = new TempDir();
        var basePath = dir.WriteFile("base.yaml", """
            Numbers:
              - a
              - b
            """);
        var patchPath = dir.WriteFile("patch.yaml", """
            Numbers:
              - z
            """);

        var merged = YamlLayerMerger.Merge(basePath, [patchPath]);

        Assert.Contains("- z", merged); // overridden by the patch layer
        Assert.Contains("- b", merged); // untouched, survives from the base layer
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
