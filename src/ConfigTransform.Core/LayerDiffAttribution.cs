using System.Text.RegularExpressions;

namespace ConfigTransform.Core;

/// <summary>One layer's contribution to a `--diff-layers` run -- see docs/DIFF_LAYERS_DESIGN.md.</summary>
public sealed record LayerDiffSection(string Label, string Diff);

/// <summary>
/// Computes one diff per layer that actually changes a resource (docs/DIFF_LAYERS_DESIGN.md),
/// instead of `--diff`'s single base-vs-merged comparison -- and, when a later layer re-touches a
/// line an earlier layer already changed, tags it with which earlier layer it overrides.
///
/// No format-engine changes are needed: <see cref="LayerMerge"/> (any of the four engines' own
/// delegate) already accepts an arbitrary prefix of the ordered patch list, so "the content after
/// every earlier layer" is just one more <c>Merge</c> call per patched layer -- see "How this is
/// computed" in the design doc. Attribution itself reuses <see cref="GitDiff.Render"/>'s own
/// unified-diff hunk headers for line-position bookkeeping rather than a second diff engine.
/// </summary>
public static class LayerDiffAttribution
{
    // Matches a (possibly ANSI-colored, already-stripped-to-plain-text-by-caller) unified-diff
    // hunk header -- e.g. "@@ -12,3 +12,4 @@". Only the start lines are used (to catch up owner
    // bookkeeping across lines the diff doesn't show at all); the counts are never needed, since
    // every line actually shown in a hunk is walked one at a time by its own leading marker.
    private static readonly Regex HunkHeader = new(@"^@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@", RegexOptions.Compiled);

    /// <param name="resolved">
    /// The resource's already-resolved chain (<see cref="LayerChain.ResolveResource"/>). Its
    /// <see cref="ResolvedResource.Steps"/> give the per-layer labels in order (a step with a
    /// null <see cref="ChainStep.PatchPath"/> has no override at this layer and is skipped, the
    /// same tolerance <c>--diff</c> has); <see cref="ResolvedResource.PatchPathsInOrder"/> gives
    /// the *real*, absolute patch paths <paramref name="merge"/> needs -- deliberately not
    /// <see cref="ChainStep.PatchPath"/> itself, which is only the repo-relative path used for
    /// display (<see cref="LayerChain.PrintChain"/>), not a path <c>Merge</c> can open.
    /// </param>
    /// <param name="merge">The resource's format engine's own merge delegate (e.g. <c>XmlLayerMerger.Merge</c>).</param>
    public static IReadOnlyList<LayerDiffSection> Compute(ResolvedResource resolved, LayerMerge merge)
    {
        var sections = new List<LayerDiffSection>();
        var appliedPatches = new List<string>();
        var previousContent = merge(resolved.BasePath, appliedPatches);

        // ownerByLine[i] names the layer that last touched previousContent's (i+1)-th line, or
        // null when that line traces straight back to base (never touched by any layer yet).
        var ownerByLine = new string?[CountLines(previousContent)];

        var patchIndex = 0;
        foreach (var step in resolved.Steps)
        {
            if (step.PatchPath is null)
                continue; // no override at this layer -- nothing to diff, same tolerance --diff has.

            appliedPatches.Add(resolved.PatchPathsInOrder[patchIndex]);
            patchIndex++;

            var currentContent = merge(resolved.BasePath, appliedPatches);
            if (currentContent == previousContent)
                continue; // a declared patch that happened to change nothing observable.

            var rawDiff = GitDiff.Render(previousContent, currentContent);
            var (annotated, newOwnerByLine) = Annotate(rawDiff, ownerByLine, step.Label);

            sections.Add(new LayerDiffSection(step.Label, annotated));

            previousContent = currentContent;
            ownerByLine = newOwnerByLine;
        }

        return sections;
    }

    /// <summary>
    /// Walks one hop's unified diff (already meta-line-stripped by <see cref="GitDiff.Render"/>),
    /// prefixing each hunk with a bracket tag naming <paramref name="currentLabel"/> (plus which
    /// earlier layer it overrides, when every removed line in that hunk shares one prior owner),
    /// and returns the owner map for <paramref name="currentLabel"/>'s own content so the next hop
    /// can look prior ownership up in turn.
    /// </summary>
    private static (string Annotated, string?[] OwnerByLine) Annotate(
        string rawDiff, string?[] previousOwnerByLine, string currentLabel)
    {
        var output = new List<string>();
        var newOwnerByLine = new List<string?>();

        var oldPos = 0; // count of previousOwnerByLine entries already accounted for
        var newPos = 0; // count of newOwnerByLine entries already appended

        var inHunk = false;
        var hunkBody = new List<(string Line, bool Removed, string? PriorOwner)>();

        void FlushHunk()
        {
            if (!inHunk)
                return;

            var removedOwners = hunkBody
                .Where(l => l.Removed && l.PriorOwner is not null)
                .Select(l => l.PriorOwner)
                .Distinct()
                .ToList();

            // A single shared prior owner across every removed line -> name it once, on the
            // header, and skip per-line notes (the common case). Anything else -- no prior
            // owner at all (genuinely new content), or more than one -- gets a plain header,
            // with per-line notes added below only in the "more than one" case (see docs/
            // DIFF_LAYERS_DESIGN.md "The overrides annotation" for both examples).
            output.Add(removedOwners.Count == 1
                ? $"[{currentLabel} overrides {removedOwners[0]}]"
                : $"[{currentLabel}]");

            var annotatePerLine = removedOwners.Count > 1;
            foreach (var (line, removed, priorOwner) in hunkBody)
            {
                output.Add(removed && annotatePerLine && priorOwner is not null
                    ? $"{line}    (overrides {priorOwner})"
                    : line);
            }

            hunkBody.Clear();
            inHunk = false;
        }

        foreach (var rawLine in rawDiff.Length == 0 ? [] : rawDiff.Split('\n'))
        {
            var plain = GitDiff.AnsiEscapeSequence.Replace(rawLine, "");
            var headerMatch = HunkHeader.Match(plain);

            if (headerMatch.Success)
            {
                FlushHunk();

                var headerOldStart = int.Parse(headerMatch.Groups[1].Value);
                var skip = Math.Max(0, headerOldStart - 1 - oldPos);
                for (var k = 0; k < skip; k++)
                {
                    newOwnerByLine.Add(oldPos < previousOwnerByLine.Length ? previousOwnerByLine[oldPos] : null);
                    oldPos++;
                    newPos++;
                }

                inHunk = true;
                continue;
            }

            if (!inHunk)
                continue; // nothing but hunk headers should precede the first one here.

            if (plain.StartsWith('\\'))
            {
                // "\ No newline at end of file" -- not a real content line, no position change.
                hunkBody.Add((rawLine, Removed: false, PriorOwner: null));
            }
            else if (plain.StartsWith(' '))
            {
                newOwnerByLine.Add(oldPos < previousOwnerByLine.Length ? previousOwnerByLine[oldPos] : null);
                hunkBody.Add((rawLine, Removed: false, PriorOwner: null));
                oldPos++;
                newPos++;
            }
            else if (plain.StartsWith('-'))
            {
                var priorOwner = oldPos < previousOwnerByLine.Length ? previousOwnerByLine[oldPos] : null;
                hunkBody.Add((rawLine, Removed: true, PriorOwner: priorOwner));
                oldPos++;
            }
            else if (plain.StartsWith('+'))
            {
                newOwnerByLine.Add(currentLabel);
                hunkBody.Add((rawLine, Removed: false, PriorOwner: null));
                newPos++;
            }
            // else: a blank trailing split artifact from a diff ending in '\n' -- not a real line.
        }

        FlushHunk();

        // The tail after the last hunk -- identical, unshown content -- keeps its existing owner,
        // one-for-one; both contents have exactly the same number of lines left from here on.
        for (var k = oldPos; k < previousOwnerByLine.Length; k++)
            newOwnerByLine.Add(previousOwnerByLine[k]);

        return (string.Join('\n', output), newOwnerByLine.ToArray());
    }

    private static int CountLines(string content) => content.Length == 0 ? 0 : content.Split('\n').Length;
}
