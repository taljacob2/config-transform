using System.Globalization;
using ConfigTransform.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

namespace ConfigTransform.Yaml;

/// <summary>
/// The YAML array-of-objects half of the <c>set</c> command (docs/FIELD_AUTHORING_DESIGN.md, "YAML
/// array-of-objects matching"): matching an existing sequence item by field conditions, or creating
/// one, without ever addressing it by index -- including in the persisted overlay file. Ported from,
/// not shared with, <c>ConfigTransform.Json</c>'s <c>JsonElemMatchResolver</c> (CLAUDE.md's "Repo
/// structure"), working on YamlDotNet's node tree. Two call sites, one algorithm:
/// <list type="bullet">
/// <item><see cref="Probe"/> -- <see cref="YamlFieldAuthor"/>'s eager, set-time-only UX check.</item>
/// <item><see cref="Rewrite"/> -- <see cref="YamlLayerMerger"/>'s real, merge-time resolution,
/// against the document as merged so far.</item>
/// </list>
/// The overlay shape both consume: a sequence of patches under the array's key, each a map with an
/// <c>$elemMatch</c> map of conditions plus the fields to write.
///
/// <b>Conditions compare by text, not type</b> -- the one deliberate difference from JSON. An
/// unquoted YAML scalar's type depends on the reader (YAML 1.1 reads <c>NO</c> as a boolean,
/// YAML 1.2 doesn't), so there's no single type to compare against: a condition matches a scalar
/// whose text is exactly equal, whatever its quoting. Maps and sequences compare structurally by
/// the same rule (<see cref="NodesEqual"/>).
/// </summary>
public static class YamlElemMatchResolver
{
    private const string ElemMatchKey = "$elemMatch";

    public readonly record struct Condition(string Field, YamlNode Value);

    /// <summary>True if <paramref name="sequence"/> is the patch-list shape: non-empty, every item a map carrying an <c>$elemMatch</c> key.</summary>
    public static bool IsPatchList(YamlSequenceNode sequence) =>
        sequence.Children.Count > 0 &&
        sequence.Children.All(item => item is YamlMappingNode map && Get(map, ElemMatchKey) is not null);

    /// <summary>Whether <paramref name="node"/> contains a patch list anywhere -- a tree walk, never a text scan.</summary>
    public static bool ContainsElemMatch(YamlNode? node) => node switch
    {
        YamlSequenceNode sequence => IsPatchList(sequence) || sequence.Children.Any(ContainsElemMatch),
        YamlMappingNode map => map.Children.Any(kvp => ContainsElemMatch(kvp.Value)),
        _ => false,
    };

    /// <summary>Every index in <paramref name="sequence"/> whose item is a map satisfying every condition.</summary>
    public static IReadOnlyList<int> IndicesMatching(YamlSequenceNode? sequence, IReadOnlyList<Condition> conditions)
    {
        if (sequence is null)
            return [];

        var result = new List<int>();
        for (var i = 0; i < sequence.Children.Count; i++)
        {
            if (sequence.Children[i] is YamlMappingNode item &&
                conditions.All(c => Get(item, c.Field) is { } value && NodesEqual(value, c.Value)))
                result.Add(i);
        }
        return result;
    }

    /// <summary>One match -&gt; that index. Zero -&gt; the append position (an upsert). More than one -&gt; throws, listing every candidate.</summary>
    public static int ResolveIndexOrAppend(YamlSequenceNode? sequence, IReadOnlyList<Condition> conditions, string pathDescription)
    {
        var matches = IndicesMatching(sequence, conditions);
        return matches.Count switch
        {
            0 => sequence?.Children.Count ?? 0,
            1 => matches[0],
            _ => throw new InvalidOperationException(AmbiguousMessage(pathDescription, sequence!, matches, conditions)),
        };
    }

    /// <summary>
    /// Set-time-only eager check, not authoritative: navigates the already-resolved preceding
    /// document to the array; absent is fine (means create), present-but-not-a-sequence throws, and
    /// an ambiguous match surfaces before anything is written.
    /// </summary>
    public static void Probe(YamlNode? precedingRoot, IReadOnlyList<string> segments, IReadOnlyList<MatchSpec> conditionSpecs)
    {
        var pathDescription = string.Join(":", segments);
        var located = Navigate(precedingRoot, segments);

        if (located is not null and not YamlSequenceNode)
            throw new InvalidOperationException($"\"{pathDescription}\" exists but is not a YAML sequence -- cannot match an item inside it.");

        ResolveIndexOrAppend(located as YamlSequenceNode, ToConditions(conditionSpecs), pathDescription);
    }

    /// <summary>
    /// Merge-time resolution: a copy of <paramref name="overlayNode"/> in which every patch list is
    /// replaced by real positions, resolved against the corresponding sequence in
    /// <paramref name="precedingNode"/> -- an index-keyed map (<c>"1": {...}</c>) for an existing
    /// sequence, which <see cref="YamlLayerMerger"/> merges item by item, or a real sequence when the
    /// target sequence doesn't exist yet. Never mutates either input.
    /// </summary>
    public static YamlNode Rewrite(YamlNode overlayNode, YamlNode? precedingNode)
    {
        switch (overlayNode)
        {
            case YamlMappingNode map:
            {
                var result = new YamlMappingNode { Style = map.Style, Tag = map.Tag };
                foreach (var (key, value) in map.Children)
                {
                    var keyText = (key as YamlScalarNode)?.Value;
                    var precedingChild = keyText is not null && precedingNode is YamlMappingNode precedingMap ? Get(precedingMap, keyText) : null;
                    if (value is YamlSequenceNode sequence && IsPatchList(sequence))
                    {
                        if (precedingChild is not null and not YamlSequenceNode)
                            throw new InvalidOperationException($"\"{keyText}\" exists but is not a YAML sequence -- cannot match an item inside it.");
                        result.Children.Add(YamlLayerMerger.Clone(key), RewritePatchList(sequence, precedingChild as YamlSequenceNode, keyText ?? "?"));
                    }
                    else
                    {
                        result.Children.Add(YamlLayerMerger.Clone(key), Rewrite(value, precedingChild));
                    }
                }
                return result;
            }
            case YamlSequenceNode sequence:
            {
                var result = new YamlSequenceNode { Style = sequence.Style, Tag = sequence.Tag };
                for (var i = 0; i < sequence.Children.Count; i++)
                {
                    var precedingElement = precedingNode is YamlSequenceNode precedingSequence && i < precedingSequence.Children.Count
                        ? precedingSequence.Children[i]
                        : null;
                    result.Children.Add(Rewrite(sequence.Children[i], precedingElement));
                }
                return result;
            }
            default:
                return YamlLayerMerger.Clone(overlayNode);
        }
    }

    private static YamlNode RewritePatchList(YamlSequenceNode patchList, YamlSequenceNode? precedingSequence, string pathDescription)
    {
        var resolved = new List<(int Index, YamlMappingNode Fields)>();
        var usedIndices = new HashSet<int>();
        var appendCursor = precedingSequence?.Children.Count ?? 0;

        foreach (var patchNode in patchList.Children)
        {
            var patch = (YamlMappingNode)patchNode;
            var elemMatch = Get(patch, ElemMatchKey) as YamlMappingNode
                ?? throw new InvalidOperationException($"\"{ElemMatchKey}\" in \"{pathDescription}\" must be a map of field: value conditions.");

            var conditions = elemMatch.Children
                .Select(kvp => new Condition(((YamlScalarNode)kvp.Key).Value ?? "", kvp.Value))
                .ToList();
            var matches = IndicesMatching(precedingSequence, conditions);

            int index;
            if (matches.Count == 0)
                index = appendCursor++;
            else if (matches.Count == 1)
                index = matches[0];
            else
                throw new InvalidOperationException(AmbiguousMessage(pathDescription, precedingSequence!, matches, conditions));

            if (!usedIndices.Add(index))
                throw new InvalidOperationException(
                    $"Two patches for \"{pathDescription}\" in this overlay both resolve to the same item " +
                    $"(index {index}) -- merge them into one patch, or adjust the conditions so they target different items.");

            var precedingItem = precedingSequence is not null && index < precedingSequence.Children.Count
                ? precedingSequence.Children[index] as YamlMappingNode
                : null;
            var fields = new YamlMappingNode();

            // Upsert: a new item's identity comes from the $elemMatch conditions themselves --
            // MongoDB's own upsert semantics, the same as JSON's.
            if (matches.Count == 0)
                foreach (var condition in conditions)
                    Set(fields, condition.Field, YamlLayerMerger.Clone(condition.Value));

            foreach (var (key, value) in patch.Children)
            {
                var keyText = (key as YamlScalarNode)?.Value;
                if (keyText == ElemMatchKey)
                    continue;
                var precedingFieldValue = keyText is not null && precedingItem is not null ? Get(precedingItem, keyText) : null;
                Set(fields, keyText ?? "", Rewrite(value, precedingFieldValue));
            }

            resolved.Add((index, fields));
        }

        if (precedingSequence is null)
        {
            // No sequence yet: every patch was an append at 0, 1, 2... in order -- a real sequence.
            var created = new YamlSequenceNode();
            foreach (var (_, fields) in resolved)
                created.Children.Add(fields);
            return created;
        }

        var indexed = new YamlMappingNode();
        foreach (var (index, fields) in resolved)
            indexed.Children.Add(new YamlScalarNode(index.ToString(CultureInfo.InvariantCulture)), fields);
        return indexed;
    }

    /// <summary>Command-line conditions, typed the same way `set` writes values (<see cref="YamlCliValue"/>); matching compares text, so the typing only affects how a new item's identity fields are written.</summary>
    internal static IReadOnlyList<Condition> ToConditions(IReadOnlyList<MatchSpec> specs) =>
        specs.Select(s => new Condition(s.Attribute, YamlCliValue.From(s.Value))).ToList();

    /// <summary>Scalars by text (quoting ignored); maps by the same keys with equal values; sequences item by item.</summary>
    internal static bool NodesEqual(YamlNode? a, YamlNode? b) => (a, b) switch
    {
        (YamlScalarNode x, YamlScalarNode y) => x.Value == y.Value,
        (YamlMappingNode x, YamlMappingNode y) =>
            x.Children.Count == y.Children.Count &&
            x.Children.All(kvp => kvp.Key is YamlScalarNode { Value: { } k } && Get(y, k) is { } other && NodesEqual(kvp.Value, other)),
        (YamlSequenceNode x, YamlSequenceNode y) =>
            x.Children.Count == y.Children.Count && x.Children.Zip(y.Children).All(pair => NodesEqual(pair.First, pair.Second)),
        _ => false,
    };

    /// <summary>The value under the scalar key spelled exactly <paramref name="key"/>, or null.</summary>
    internal static YamlNode? Get(YamlMappingNode map, string key) =>
        map.Children.FirstOrDefault(kvp => kvp.Key is YamlScalarNode { Value: { } text } && text == key).Value;

    /// <summary>Sets the scalar key spelled exactly <paramref name="key"/> -- in place when it exists, appended otherwise.</summary>
    internal static void Set(YamlMappingNode map, string key, YamlNode value)
    {
        var existing = map.Children.Keys.FirstOrDefault(k => k is YamlScalarNode { Value: { } text } && text == key);
        if (existing is not null)
            map.Children[existing] = value;
        else
            map.Children.Add(new YamlScalarNode(key), value);
    }

    internal static YamlNode? Navigate(YamlNode? node, IReadOnlyList<string> segments)
    {
        var current = node;
        foreach (var segment in segments)
        {
            if (current is not YamlMappingNode map || Get(map, segment) is not { } next)
                return null;
            current = next;
        }
        return current;
    }

    private static string AmbiguousMessage(string pathDescription, YamlSequenceNode sequence, IReadOnlyList<int> indices, IReadOnlyList<Condition> conditions)
    {
        var description = string.Join(", ", conditions.Select(c => $"{c.Field}={Render(c.Value)}"));
        var listing = string.Join("\n", indices.Select(i => "  " + Render(sequence.Children[i])));
        return $"More than one item in \"{pathDescription}\" matches {description} -- add another --match to narrow it down:\n{listing}";
    }

    /// <summary>A compact, single-line rendering for error messages (flow style).</summary>
    private static string Render(YamlNode node) => node switch
    {
        YamlScalarNode scalar => scalar.Value ?? "~",
        YamlMappingNode map => "{" + string.Join(", ", map.Children.Select(kvp => $"{Render(kvp.Key)}: {Render(kvp.Value)}")) + "}",
        YamlSequenceNode sequence => "[" + string.Join(", ", sequence.Children.Select(Render)) + "]",
        _ => node.ToString(),
    };

    /// <summary>A map that's about to receive keys gets block style, not an empty flow map's <c>{}</c>.</summary>
    internal static void UseBlockStyleIfEmpty(YamlMappingNode map)
    {
        if (map.Children.Count == 0)
            map.Style = MappingStyle.Block;
    }
}
