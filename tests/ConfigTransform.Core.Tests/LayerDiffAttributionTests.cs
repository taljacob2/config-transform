using Xunit;

namespace ConfigTransform.Core.Tests;

/// <summary>
/// Unit tests for <see cref="LayerDiffAttribution"/> (docs/DIFF_LAYERS_DESIGN.md) against a fake
/// <see cref="LayerMerge"/> delegate keyed purely by how many patches have been applied so far --
/// <see cref="LayerDiffAttribution.Compute"/> never reads <c>BasePath</c> or the patch paths
/// themselves, only counts and forwards them, so this exercises the real attribution algorithm
/// with no XML/JSON/file-system fixture needed. End-to-end coverage against a real multi-layer
/// XML chain lives in ConfigTransform.Cli.Tests' CliRunnerTests.
/// </summary>
public class LayerDiffAttributionTests
{
    private static LayerMerge FakeMerge(params string[] contentByPatchCount) =>
        (_, patches) => contentByPatchCount[patches.Count];

    /// <summary>
    /// Builds a <see cref="ResolvedResource"/> from just the (label, hasPatch) shape a test cares
    /// about -- a dummy, never-opened patch path per patched step, same 1:1 correspondence with
    /// <see cref="ResolvedResource.Steps"/> that <see cref="LayerChain.ResolveResource"/> itself
    /// produces (see <see cref="LayerDiffAttribution.Compute"/>'s doc comment for why that
    /// correspondence matters, not <see cref="ChainStep.PatchPath"/> itself).
    /// </summary>
    private static ResolvedResource Resolved(params (string Label, bool HasPatch)[] steps)
    {
        var chainSteps = steps
            .Select(s => new ChainStep(s.Label, s.HasPatch ? $"{s.Label}-patch-display-path" : null))
            .ToList();
        var patchPaths = steps.Where(s => s.HasPatch).Select(s => $"{s.Label}-patch-real-path").ToList();

        return new ResolvedResource("base", patchPaths, [], chainSteps);
    }

    [Fact]
    public void Second_layer_re_touching_the_same_line_is_tagged_as_overriding_the_first()
    {
        var merge = FakeMerge(
            "line1\nvalue=30\nline3\n",
            "line1\nvalue=60\nline3\n",
            "line1\nvalue=90\nline3\n");

        var resolved = Resolved(
            ("Environments/Production/configtransform.json", true),
            ("Clients/Acme/Production/configtransform.json", true));

        var sections = LayerDiffAttribution.Compute(resolved, merge);

        Assert.Equal(2, sections.Count);

        Assert.Equal("Environments/Production/configtransform.json", sections[0].Label);
        Assert.Contains("[Environments/Production/configtransform.json]", sections[0].Diff);
        Assert.DoesNotContain("overrides", sections[0].Diff);
        Assert.Contains("value=30", sections[0].Diff);
        Assert.Contains("value=60", sections[0].Diff);

        Assert.Equal("Clients/Acme/Production/configtransform.json", sections[1].Label);
        Assert.Contains(
            "[Clients/Acme/Production/configtransform.json overrides Environments/Production/configtransform.json]",
            sections[1].Diff);
        Assert.Contains("value=60", sections[1].Diff);
        Assert.Contains("value=90", sections[1].Diff);
    }

    [Fact]
    public void A_layer_that_declares_no_patch_for_the_resource_contributes_no_section()
    {
        var merge = FakeMerge("line1\n", "line1-changed\n");

        var resolved = Resolved(
            ("Environments/Production/configtransform.json", false),
            ("Clients/Acme/Production/configtransform.json", true));

        var sections = LayerDiffAttribution.Compute(resolved, merge);

        Assert.Single(sections);
        Assert.Equal("Clients/Acme/Production/configtransform.json", sections[0].Label);
    }

    [Fact]
    public void A_declared_patch_that_changes_nothing_observable_contributes_no_section()
    {
        var merge = FakeMerge("line1\n", "line1\n", "line1-changed\n");

        var resolved = Resolved(
            ("Environments/Production/configtransform.json", true),
            ("Clients/Acme/Production/configtransform.json", true));

        var sections = LayerDiffAttribution.Compute(resolved, merge);

        Assert.Single(sections);
        Assert.Equal("Clients/Acme/Production/configtransform.json", sections[0].Label);
    }

    [Fact]
    public void A_genuinely_new_line_carries_no_overrides_note()
    {
        var merge = FakeMerge("line1\nline2\n", "line1\nline2\nline3-new\n");

        var resolved = Resolved(("Environments/Production/configtransform.json", true));

        var sections = LayerDiffAttribution.Compute(resolved, merge);

        Assert.Single(sections);
        Assert.Contains("[Environments/Production/configtransform.json]", sections[0].Diff);
        Assert.DoesNotContain("overrides", sections[0].Diff);
        Assert.Contains("line3-new", sections[0].Diff);
    }

    [Fact]
    public void A_hunk_with_two_different_prior_owners_gets_a_plain_header_and_per_line_notes()
    {
        // Environment sets Region, Client sets Cache; Host then changes both in one patch, in one
        // hunk (adjacent lines) -- docs/DIFF_LAYERS_DESIGN.md's "Mixed-owner hunks" example.
        var merge = FakeMerge(
            "lineA\nRegion=us-east-0\nCache=redis-0\nlineD\n",
            "lineA\nRegion=us-east\nCache=redis-0\nlineD\n",
            "lineA\nRegion=us-east\nCache=redis-a\nlineD\n",
            "lineA\nRegion=us-east-1\nCache=redis-a.internal:6379\nlineD\n");

        var resolved = Resolved(
            ("Environments/Production/configtransform.json", true),
            ("Clients/Acme/Production/configtransform.json", true),
            ("Clients/Acme/Production/Hosts/10.0.1.11/configtransform.json", true));

        var sections = LayerDiffAttribution.Compute(resolved, merge);

        Assert.Equal(3, sections.Count);
        var hostDiff = sections[2].Diff;

        Assert.Contains("[Clients/Acme/Production/Hosts/10.0.1.11/configtransform.json]", hostDiff);
        Assert.DoesNotContain("[Clients/Acme/Production/Hosts/10.0.1.11/configtransform.json overrides", hostDiff);
        Assert.Contains("(overrides Environments/Production/configtransform.json)", hostDiff);
        Assert.Contains("(overrides Clients/Acme/Production/configtransform.json)", hostDiff);
    }

    [Fact]
    public void Owner_carries_forward_correctly_across_a_multi_hunk_diff()
    {
        // One layer changes two far-apart lines in a single patch (two separate hunks in that
        // one diff); a later layer re-touches only the second one. Proves the owner map correctly
        // tracks position through a diff with more than one hunk, not just the single-hunk cases
        // above -- the "Multi-hunk verification" open item in docs/DIFF_LAYERS_DESIGN.md.
        var baseContent = string.Join('\n', Enumerable.Range(1, 14).Select(n => n switch
        {
            2 => "l2-old",
            13 => "l13-old",
            _ => $"l{n}"
        })) + "\n";

        var afterLayer1 = string.Join('\n', Enumerable.Range(1, 14).Select(n => n switch
        {
            2 => "l2-new1",
            13 => "l13-new1",
            _ => $"l{n}"
        })) + "\n";

        var afterLayer2 = string.Join('\n', Enumerable.Range(1, 14).Select(n => n switch
        {
            2 => "l2-new1",
            13 => "l13-new2",
            _ => $"l{n}"
        })) + "\n";

        var merge = FakeMerge(baseContent, afterLayer1, afterLayer2);

        var resolved = Resolved(
            ("Environments/Production/configtransform.json", true),
            ("Clients/Acme/Production/configtransform.json", true));

        var sections = LayerDiffAttribution.Compute(resolved, merge);

        Assert.Equal(2, sections.Count);

        var layer1Diff = sections[0].Diff;
        Assert.Contains("l2-old", layer1Diff);
        Assert.Contains("l13-old", layer1Diff);
        // Two separate hunks in the same layer's own diff -- each gets its own header, neither
        // one has a prior owner (both trace back to base).
        Assert.Equal(2, CountOccurrences(layer1Diff, "[Environments/Production/configtransform.json]"));

        var layer2Diff = sections[1].Diff;
        Assert.Contains(
            "[Clients/Acme/Production/configtransform.json overrides Environments/Production/configtransform.json]",
            layer2Diff);
        Assert.Contains("l13-new1", layer2Diff);
        Assert.Contains("l13-new2", layer2Diff);
        Assert.DoesNotContain("l2", layer2Diff); // untouched by layer 2 -- not part of its diff at all.
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
