using ConfigTransform.Core;
using Xunit;

namespace ConfigTransform.Env.Tests;

public class EnvLayerMergerTests
{
    private static string FixturesRoot => Path.Combine(AppContext.BaseDirectory, "Fixtures", "WebService");
    private const string ResourcePath = "Project/.env";

    [Fact]
    public void Merges_base_environment_and_client_layers_end_to_end()
    {
        var resolved = Resolve("ClientA", "Production");
        var merged = EnvFile.Parse(EnvLayerMerger.Merge(resolved.BasePath, resolved.PatchPathsInOrder))
            .ToDictionary(p => p.Key, p => p.Value);

        Assert.Equal("postgres://prod-db:5432/app", merged["DATABASE_URL"]); // environment layer
        Assert.Equal("warn", merged["LOG_LEVEL"]); // environment layer
        Assert.Equal("clienta-prod-key", merged["API_KEY"]); // client layer
        Assert.Equal("true", merged["FEATURE_FLAG_X"]); // client layer
        Assert.Equal("3000", merged["PORT"]); // untouched base value
    }

    [Fact]
    public void A_brand_new_key_introduced_by_an_overlay_is_appended_after_the_base_keys()
    {
        var resolved = Resolve("ClientA", "Production");
        var merged = EnvLayerMerger.Merge(resolved.BasePath, resolved.PatchPathsInOrder);

        var lines = merged.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("REGION=us-east-1", lines[^1]); // REGION doesn't exist in the base -- appended last
    }

    [Fact]
    public void Comments_and_blank_lines_in_the_base_file_do_not_survive_a_merge()
    {
        // Matches JSON's own existing behavior: Microsoft.Extensions.Configuration's JSON
        // provider already drops comments/formatting on rebuild, so .env is consistent with an
        // existing precedent here, not introducing a new gap.
        var resolved = Resolve("ClientA", "Production");
        var merged = EnvLayerMerger.Merge(resolved.BasePath, resolved.PatchPathsInOrder);

        Assert.DoesNotContain('#', merged);
        Assert.DoesNotContain("\n\n", merged);
    }

    [Fact]
    public void Applies_only_base_when_no_overlays_match()
    {
        var resolved = Resolve("ClientB", "Staging");
        Assert.Empty(resolved.PatchPathsInOrder);

        var merged = EnvFile.Parse(EnvLayerMerger.Merge(resolved.BasePath, resolved.PatchPathsInOrder))
            .ToDictionary(p => p.Key, p => p.Value);

        Assert.Equal("postgres://localhost:5432/dev", merged["DATABASE_URL"]);
        Assert.Equal("info", merged["LOG_LEVEL"]);
        Assert.Equal("false", merged["FEATURE_FLAG_X"]);
    }

    private static ResolvedResource Resolve(string client, string environment)
    {
        var chain = LayerChain.Build(FixturesRoot, LayerPathResolver.Resolve(FixturesRoot, client, environment));
        return LayerChain.ResolveResource(FixturesRoot, chain, ResourcePath);
    }
}
