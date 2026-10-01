using System.Text.Json;
using Xunit;

namespace ConfigTransform.Json.Tests;

/// <summary>
/// Direct tests of the tree merge's own rules (docs/TREE_MERGE_DESIGN.md) against small inline
/// documents -- the edge cases a realistic fixture doesn't naturally contain. The fixture-backed
/// tests (JsonLayerMergerTests, JsonLayerMergerGenericJsonTests, JsonLayerMergerGoldenTests) cover
/// the realistic shapes.
/// </summary>
public class JsonLayerMergerGrammarTests
{
    [Fact]
    public void Comments_and_trailing_commas_are_accepted_in_base_and_patch()
    {
        using var dir = new TempDir();
        var basePath = dir.WriteFile("base.json", """
            {
              // a comment, as appsettings.json often has
              "A": 1,
              "B": 2, /* another */
            }
            """);
        var patchPath = dir.WriteFile("patch.json", """
            { "B": 3, // patched
            }
            """);

        var merged = JsonLayerMerger.Merge(basePath, [patchPath]);

        using var doc = JsonDocument.Parse(merged);
        Assert.Equal(1, doc.RootElement.GetProperty("A").GetInt32());
        Assert.Equal(3, doc.RootElement.GetProperty("B").GetInt32());
        Assert.DoesNotContain("comment", merged);
    }

    [Fact]
    public void Number_text_is_written_exactly_as_in_the_source()
    {
        using var dir = new TempDir();
        var basePath = dir.WriteFile("base.json", """
            { "Big": 12345678901234567890, "Price": 1.50, "Exp": 1e5, "Pin": "007" }
            """);

        var merged = JsonLayerMerger.Merge(basePath, []);

        Assert.Contains("\"Big\": 12345678901234567890", merged);
        Assert.Contains("\"Price\": 1.50", merged);
        Assert.Contains("\"Exp\": 1e5", merged);
        Assert.Contains("\"Pin\": \"007\"", merged);
    }

    [Fact]
    public void A_later_layer_replaces_a_value_of_a_different_kind_outright()
    {
        // The old IConfiguration-based merge kept the object and silently dropped the scalar,
        // whichever layer came last.
        using var dir = new TempDir();
        var basePath = dir.WriteFile("base.json", """
            { "WasObject": { "Inner": 1 }, "WasScalar": 5, "WasArray": [ 1, 2 ] }
            """);
        var patchPath = dir.WriteFile("patch.json", """
            { "WasObject": "now a string", "WasScalar": { "Inner": 2 }, "WasArray": null }
            """);

        var merged = JsonLayerMerger.Merge(basePath, [patchPath]);

        using var doc = JsonDocument.Parse(merged);
        Assert.Equal("now a string", doc.RootElement.GetProperty("WasObject").GetString());
        Assert.Equal(2, doc.RootElement.GetProperty("WasScalar").GetProperty("Inner").GetInt32());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("WasArray").ValueKind);
    }

    [Fact]
    public void An_index_keyed_object_updates_one_array_item_and_can_append_the_next()
    {
        using var dir = new TempDir();
        var basePath = dir.WriteFile("base.json", """
            { "Items": [ { "Name": "a", "On": false }, { "Name": "b", "On": false } ] }
            """);
        var patchPath = dir.WriteFile("patch.json", """
            { "Items": { "1": { "On": true }, "2": { "Name": "c", "On": true } } }
            """);

        var merged = JsonLayerMerger.Merge(basePath, [patchPath]);

        using var doc = JsonDocument.Parse(merged);
        var items = doc.RootElement.GetProperty("Items");
        Assert.Equal(3, items.GetArrayLength());
        Assert.False(items[0].GetProperty("On").GetBoolean());
        Assert.Equal("b", items[1].GetProperty("Name").GetString()); // merged into, not replaced
        Assert.True(items[1].GetProperty("On").GetBoolean());
        Assert.Equal("c", items[2].GetProperty("Name").GetString());
    }

    [Fact]
    public void An_index_keyed_object_that_would_leave_a_gap_throws()
    {
        using var dir = new TempDir();
        var basePath = dir.WriteFile("base.json", """{ "Items": [ "a" ] }""");
        var patchPath = dir.WriteFile("patch.json", """{ "Items": { "3": "d" } }""");

        var ex = Assert.Throws<InvalidOperationException>(() => JsonLayerMerger.Merge(basePath, [patchPath]));

        Assert.Contains("item 3", ex.Message);
    }

    [Fact]
    public void An_empty_patch_object_or_array_changes_nothing_already_there()
    {
        using var dir = new TempDir();
        var basePath = dir.WriteFile("base.json", """{ "Obj": { "A": 1 }, "Arr": [ 1, 2 ] }""");
        var patchPath = dir.WriteFile("patch.json", """{ "Obj": {}, "Arr": [] }""");

        var merged = JsonLayerMerger.Merge(basePath, [patchPath]);

        using var doc = JsonDocument.Parse(merged);
        Assert.Equal(1, doc.RootElement.GetProperty("Obj").GetProperty("A").GetInt32());
        Assert.Equal(2, doc.RootElement.GetProperty("Arr").GetArrayLength());
    }

    [Fact]
    public void Keys_differing_only_by_case_within_one_file_are_kept_as_written()
    {
        // JSON itself is case-sensitive; only the cross-layer match is case-insensitive.
        using var dir = new TempDir();
        var basePath = dir.WriteFile("base.json", """{ "Foo": 1, "foo": 2 }""");
        var patchPath = dir.WriteFile("patch.json", """{ "foo": 3 }""");

        var merged = JsonLayerMerger.Merge(basePath, [patchPath]);

        using var doc = JsonDocument.Parse(merged);
        Assert.Equal(1, doc.RootElement.GetProperty("Foo").GetInt32());
        Assert.Equal(3, doc.RootElement.GetProperty("foo").GetInt32()); // the exact-case match wins
    }

    [Fact]
    public void An_elemMatch_patch_onto_a_missing_array_creates_a_real_array()
    {
        using var dir = new TempDir();
        var basePath = dir.WriteFile("base.json", """{ "Other": 1 }""");
        var patchPath = dir.WriteFile("patch.json", """
            { "Rules": [ { "$elemMatch": { "role": "Admin" }, "enabled": true } ] }
            """);

        var merged = JsonLayerMerger.Merge(basePath, [patchPath]);

        using var doc = JsonDocument.Parse(merged);
        var rules = doc.RootElement.GetProperty("Rules");
        Assert.Equal(JsonValueKind.Array, rules.ValueKind);
        Assert.Equal("Admin", rules[0].GetProperty("role").GetString());
        Assert.True(rules[0].GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public void A_patch_key_differing_from_an_existing_key_only_by_case_is_an_error_naming_the_real_spelling()
    {
        // Keys are case-sensitive, but writing a second key that differs only by case is never
        // what was meant -- and .NET's configuration loader refuses to load such a file.
        using var dir = new TempDir();
        var basePath = dir.WriteFile("base.json", """{ "Billing": { "ApiUrl": "https://dev.example.com" } }""");
        var patchPath = dir.WriteFile("patch.json", """{ "Billing": { "apiUrl": "https://prod.example.com" } }""");

        var ex = Assert.Throws<InvalidOperationException>(() => JsonLayerMerger.Merge(basePath, [patchPath]));

        Assert.Contains(patchPath, ex.Message);
        Assert.Contains("\"Billing:apiUrl\"", ex.Message);
        Assert.Contains("\"Billing:ApiUrl\"", ex.Message);
        Assert.Contains("Try: spell it \"ApiUrl\"", ex.Message);
    }

    [Fact]
    public void A_case_only_mismatch_inside_an_array_item_names_the_item()
    {
        using var dir = new TempDir();
        var basePath = dir.WriteFile("base.json", """{ "Rules": [ { "enabled": false } ] }""");
        var patchPath = dir.WriteFile("patch.json", """{ "Rules": [ { "Enabled": true } ] }""");

        var ex = Assert.Throws<InvalidOperationException>(() => JsonLayerMerger.Merge(basePath, [patchPath]));

        Assert.Contains("\"Rules:0:Enabled\"", ex.Message);
    }

    private sealed class TempDir : IDisposable
    {
        private readonly string _path = Directory.CreateTempSubdirectory("configtransform-json-tests-").FullName;

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
