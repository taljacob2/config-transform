using Xunit;

namespace ConfigTransform.Env.Tests;

/// <summary>
/// Direct tests of merge-specific behaviors not covered by the fixture-backed
/// EnvLayerMergerTests.cs -- a duplicate key within one real file exercised through the merger
/// (not just EnvFile.Parse directly), and key case-sensitivity across layers. Uses ad hoc temp
/// files rather than the shared fixture tree, mirroring YamlLayerMergerGrammarTests.cs's own
/// pattern for the same reason: these scenarios don't need a full layer chain, just two files.
/// </summary>
public class EnvLayerMergerGrammarTests
{
    [Fact]
    public void A_duplicate_key_within_one_real_file_keeps_only_the_last_value()
    {
        using var dir = new TempDir();
        var basePath = dir.WriteFile("base.env", "FOO=1\nBAR=2\nFOO=3\n");

        var merged = EnvFile.Parse(EnvLayerMerger.Merge(basePath, []))
            .ToDictionary(p => p.Key, p => p.Value);

        Assert.Equal("3", merged["FOO"]);
        Assert.Equal("2", merged["BAR"]);
    }

    [Fact]
    public void Keys_differing_only_by_case_across_layers_are_treated_as_independent_keys()
    {
        // A real, deliberate consequence of using ordinal (case-sensitive) key comparison
        // throughout this engine, matching real POSIX/shell env-var semantics where case matters
        // -- FOO and foo are two different environment variables, not the same one twice.
        using var dir = new TempDir();
        var basePath = dir.WriteFile("base.env", "FOO=base-value\n");
        var patchPath = dir.WriteFile("patch.env", "foo=overlay-value\n");

        var merged = EnvFile.Parse(EnvLayerMerger.Merge(basePath, [patchPath]))
            .ToDictionary(p => p.Key, p => p.Value);

        Assert.Equal(2, merged.Count);
        Assert.Equal("base-value", merged["FOO"]);
        Assert.Equal("overlay-value", merged["foo"]);
    }

    private sealed class TempDir : IDisposable
    {
        private readonly string _path = Directory.CreateTempSubdirectory("configtransform-env-tests-").FullName;

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
