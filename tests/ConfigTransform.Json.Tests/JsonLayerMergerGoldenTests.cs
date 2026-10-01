using ConfigTransform.Core;
using Xunit;

namespace ConfigTransform.Json.Tests;

/// <summary>
/// The merged output for each fixture set must equal a hand-checked expected file exactly, so
/// key order, key spelling, value types and text, and empty containers are all pinned at once
/// (docs/TREE_MERGE_DESIGN.md) -- not just the handful of values the other tests look up. Line
/// endings are normalized, since output uses the platform's newline.
/// </summary>
public class JsonLayerMergerGoldenTests
{
    [Theory]
    [InlineData("DotNetCore", "Project/appsettings.json")]
    [InlineData("GenericJson", "Project/custom-settings.json")]
    public void Merged_output_matches_the_expected_file_exactly(string fixtureSet, string resourcePath)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixtureSet);
        var chain = LayerChain.Build(root, LayerPathResolver.Resolve(root, "ClientA", "Production"));
        var resolved = LayerChain.ResolveResource(root, chain, resourcePath);

        var merged = JsonLayerMerger.Merge(resolved.BasePath, resolved.PatchPathsInOrder);

        var expected = File.ReadAllText(Path.Combine(root, "Expected", "ClientA-Production.json"));
        Assert.Equal(Normalize(expected), Normalize(merged));
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n").TrimEnd('\n');
}
