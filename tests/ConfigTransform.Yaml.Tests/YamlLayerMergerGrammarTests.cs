using Xunit;

namespace ConfigTransform.Yaml.Tests;

/// <summary>
/// Direct tests of the tree merge's own rules (docs/TREE_MERGE_DESIGN.md) against small inline
/// documents -- the edge cases a realistic fixture doesn't naturally contain. The fixture-backed
/// tests (YamlLayerMergerTests, YamlLayerMergerGenericYamlTests, YamlLayerMergerGoldenTests) cover
/// the realistic shapes.
/// </summary>
public class YamlLayerMergerGrammarTests
{
    [Fact]
    public void An_empty_map_or_sequence_and_a_null_survive()
    {
        // The old IConfiguration-based merge dropped all three.
        using var dir = new TempDir();
        var basePath = dir.WriteFile("base.yaml", """
            Real: value
            Empty: {}
            EmptyList: []
            Nothing: ~
            """);

        var merged = YamlLayerMerger.Merge(basePath, []);

        Assert.Contains("Real: value", merged);
        Assert.Contains("Empty: {}", merged);
        Assert.Contains("EmptyList: []", merged);
        Assert.Contains("Nothing: ~", merged);
    }

    [Fact]
    public void Keys_differing_only_by_case_within_one_file_are_kept_as_written()
    {
        // YAML itself is case-sensitive; only the cross-layer match is case-insensitive. The old
        // NetEscapades/IConfiguration-based merge threw on this.
        using var dir = new TempDir();
        var basePath = dir.WriteFile("base.yaml", """
            Foo: one
            foo: two
            """);
        var patchPath = dir.WriteFile("patch.yaml", """
            foo: three
            """);

        var merged = YamlLayerMerger.Merge(basePath, [patchPath]);

        Assert.Contains("Foo: one", merged);
        Assert.Contains("foo: three", merged); // the exact-case match wins
    }

    [Fact]
    public void Scalars_keep_their_quoting_and_block_style_exactly()
    {
        using var dir = new TempDir();
        var basePath = dir.WriteFile("base.yaml", """
            Pin: "007"
            Plain: 007
            Cron: '0 * * * *'
            Flow: [a, b]
            Script: |
              echo one
              echo two
            Last: x
            """);
        var patchPath = dir.WriteFile("patch.yaml", """
            Cron: '*/15 * * * *'
            """);

        var merged = YamlLayerMerger.Merge(basePath, [patchPath]);

        Assert.Contains("Pin: \"007\"", merged);
        Assert.Contains("Plain: 007", merged);
        Assert.Contains("Cron: '*/15 * * * *'", merged);
        Assert.Contains("Flow: [a, b]", merged);
        Assert.Contains("Script: |", merged);
        Assert.DoesNotContain("...", merged); // YamlStream's document-end marker is stripped
    }

    [Theory]
    [InlineData("Root:\n    Child: 1\nList:\n    - a\n", "Root:\n    Child: 1\nList:\n    - a\n")]
    [InlineData("Root:\n  Child: 1\nList:\n- a\n", "Root:\n  Child: 1\nList:\n- a\n")]
    public void The_base_files_indentation_and_sequence_style_are_kept(string baseYaml, string expected)
    {
        using var dir = new TempDir();
        var basePath = dir.WriteFile("base.yaml", baseYaml);

        var merged = YamlLayerMerger.Merge(basePath, []);

        Assert.Equal(expected, merged.Replace("\r\n", "\n"));
    }

    [Fact]
    public void A_later_layer_replaces_a_value_of_a_different_kind_outright()
    {
        using var dir = new TempDir();
        var basePath = dir.WriteFile("base.yaml", """
            WasMap:
              Inner: 1
            WasScalar: 5
            """);
        var patchPath = dir.WriteFile("patch.yaml", """
            WasMap: now a string
            WasScalar:
              Inner: 2
            """);

        var merged = YamlLayerMerger.Merge(basePath, [patchPath]).Replace("\r\n", "\n");

        Assert.Contains("WasMap: now a string", merged);
        Assert.Contains("WasScalar:\n  Inner: 2", merged);
    }

    [Fact]
    public void An_index_keyed_map_updates_one_sequence_item_and_a_gap_throws()
    {
        using var dir = new TempDir();
        var basePath = dir.WriteFile("base.yaml", """
            Items:
              - a
              - b
            """);
        var patchPath = dir.WriteFile("patch.yaml", """
            Items:
              "1": z
            """);
        var gapPatchPath = dir.WriteFile("gap.yaml", """
            Items:
              "5": z
            """);

        var merged = YamlLayerMerger.Merge(basePath, [patchPath]);
        var ex = Assert.Throws<InvalidOperationException>(() => YamlLayerMerger.Merge(basePath, [gapPatchPath]));

        Assert.Contains("- a", merged);
        Assert.Contains("- z", merged);
        Assert.DoesNotContain("- b", merged);
        Assert.Contains("item 5", ex.Message);
    }

    [Fact]
    public void A_file_with_more_than_one_document_throws()
    {
        using var dir = new TempDir();
        var basePath = dir.WriteFile("base.yaml", """
            A: 1
            ---
            B: 2
            """);

        var ex = Assert.Throws<InvalidOperationException>(() => YamlLayerMerger.Merge(basePath, []));

        Assert.Contains("2 YAML documents", ex.Message);
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

    [Fact]
    public void A_patch_key_differing_from_an_existing_key_only_by_case_is_an_error_naming_the_real_spelling()
    {
        using var dir = new TempDir();
        var basePath = dir.WriteFile("base.yaml", """
            Reporting:
              Schedule: "0 * * * *"
            """);
        var patchPath = dir.WriteFile("patch.yaml", """
            Reporting:
              schedule: "*/15 * * * *"
            """);

        var ex = Assert.Throws<InvalidOperationException>(() => YamlLayerMerger.Merge(basePath, [patchPath]));

        Assert.Contains(patchPath, ex.Message);
        Assert.Contains("\"Reporting:schedule\"", ex.Message);
        Assert.Contains("\"Reporting:Schedule\"", ex.Message);
        Assert.Contains("Try: spell it \"Schedule\"", ex.Message);
    }

    [Theory]
    [InlineData("\n", true)]
    [InlineData("\r\n", true)]
    [InlineData("\n", false)]
    public void Output_uses_the_base_files_line_endings_and_final_newline(string newLine, bool finalNewLine)
    {
        using var dir = new TempDir();
        var basePath = dir.WriteFile("base.yaml", $"A: 1{newLine}B:{newLine}  C: 2" + (finalNewLine ? newLine : ""));
        var patchPath = dir.WriteFile("patch.yaml", "D: 3\n");

        var merged = YamlLayerMerger.Merge(basePath, [patchPath]);

        Assert.Equal($"A: 1{newLine}B:{newLine}  C: 2{newLine}D: 3" + (finalNewLine ? newLine : ""), merged);
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
