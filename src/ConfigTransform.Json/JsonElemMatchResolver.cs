using System.Globalization;
using System.Text.Json.Nodes;
using ConfigTransform.Core;

namespace ConfigTransform.Json;

/// <summary>
/// Resolves the JSON array-of-objects half of the <c>set</c> command
/// (docs/FIELD_AUTHORING_DESIGN.md): matching an existing array item by field conditions, or
/// creating one, without ever addressing it by index anywhere -- including in the persisted
/// overlay file. Two call sites, one algorithm:
/// <list type="bullet">
/// <item><see cref="Probe"/> -- <see cref="JsonFieldAuthor"/>'s eager, set-time-only UX check
/// (ambiguous/non-array errors surface immediately, mirroring XML's). Not authoritative.</item>
/// <item><see cref="Rewrite"/> -- <see cref="JsonLayerMerger"/>'s real, merge-time resolution,
/// re-run every time against whatever the actual document looks like at that point (a
/// hand-written overlay was never checked by <c>set</c> at all, and a Client-layer overlay must
/// resolve against the base+Environment-merged state, not the base alone).</item>
/// </list>
/// The overlay shape both consume: a <b>list</b> of patches under an array's key, each an
/// <c>$elemMatch</c> condition object plus the fields to write -- a list even for a single
/// condition set, so there is exactly one shape to parse, and so more than one <c>set</c> call
/// against the same array in the same overlay file has somewhere to go:
/// <code>{ "Rules": [ { "$elemMatch": { "role": "Admin" }, "enabled": true } ] }</code>
/// <see cref="Rewrite"/> turns each patch into a real position, expressed as a
/// <see cref="JsonObject"/> keyed by numeric-string index (<c>{"1": {...}}</c>) rather than a
/// <see cref="JsonArray"/> literal -- a real array can't say "leave every other index alone,
/// touch only this one" without emitting placeholder nulls for the skipped indices, and those
/// nulls would themselves flatten to real <c>IConfiguration</c> keys and clobber the base
/// layer's actual values there (the same hazard <see cref="JsonLayerMerger"/>'s own doc comment
/// already warns about for plain arrays). A numeric-string object key has no such constraint,
/// and is proven (empirically, both via file and via stream input) to flatten to the exact same
/// <c>IConfiguration</c> path as a real array element at that index.
/// </summary>
public static class JsonElemMatchResolver
{
    private const string ElemMatchKey = "$elemMatch";

    public readonly record struct Condition(string Field, JsonNode? Value);

    /// <summary>True if <paramref name="array"/> is the patch-list shape: non-empty, every
    /// element a <see cref="JsonObject"/> carrying an <c>"$elemMatch"</c> key. An empty array,
    /// or one whose elements are ordinary business objects, is left alone as a plain array --
    /// this is what keeps a sibling positional-array overlay in the same file untouched.</summary>
    public static bool IsPatchList(JsonArray array) =>
        array.Count > 0 && array.All(item => item is JsonObject obj && obj.ContainsKey(ElemMatchKey));

    /// <summary>Recursive tree walk (never a raw-text scan, so a config value that happens to
    /// literally contain the text "$elemMatch" can never false-positive) for whether
    /// <paramref name="node"/> contains a patch-list anywhere.</summary>
    public static bool ContainsElemMatch(JsonNode? node) => node switch
    {
        JsonArray array => IsPatchList(array) || array.Any(ContainsElemMatch),
        JsonObject obj => obj.Any(kvp => ContainsElemMatch(kvp.Value)),
        _ => false
    };

    /// <summary>Every index in <paramref name="array"/> whose item satisfies every condition
    /// (typed equality via <see cref="JsonNode.DeepEquals(JsonNode?, JsonNode?)"/>, so
    /// <c>--match enabled=true</c> compares against a real JSON <c>true</c>, not the string
    /// <c>"true"</c>).</summary>
    public static IReadOnlyList<int> IndicesMatching(JsonArray? array, IReadOnlyList<Condition> conditions)
    {
        if (array is null)
            return [];

        var result = new List<int>();
        for (var i = 0; i < array.Count; i++)
        {
            if (array[i] is JsonObject item &&
                conditions.All(c => item.TryGetPropertyValue(c.Field, out var value) && JsonNode.DeepEquals(value, c.Value)))
            {
                result.Add(i);
            }
        }
        return result;
    }

    /// <summary>One match -&gt; that index. Zero -&gt; the append position
    /// (<c>array?.Count ?? 0</c>) -- the upsert case, decided here rather than baked into the
    /// overlay file. More than one -&gt; throws, naming every candidate.</summary>
    public static int ResolveIndexOrAppend(JsonArray? array, IReadOnlyList<Condition> conditions, string pathDescription)
    {
        var matches = IndicesMatching(array, conditions);
        return matches.Count switch
        {
            0 => array?.Count ?? 0,
            1 => matches[0],
            _ => throw new InvalidOperationException(AmbiguousMessage(pathDescription, array!, matches, conditions))
        };
    }

    private static string AmbiguousMessage(string pathDescription, JsonArray array, IReadOnlyList<int> indices, IReadOnlyList<Condition> conditions)
    {
        var description = string.Join(", ", conditions.Select(c => $"{c.Field}={c.Value?.ToJsonString() ?? "null"}"));
        var listing = string.Join("\n", indices.Select(i => "  " + array[i]!.ToJsonString()));
        return $"More than one item in \"{pathDescription}\" matches {description} -- add another --match to narrow it down:\n{listing}";
    }

    /// <summary>Set-time-only eager check, not authoritative. Navigates the already
    /// fully-resolved preceding document (never itself contains <c>$elemMatch</c>) down to the
    /// array; absent is fine (means create), present-but-not-an-array throws, otherwise
    /// delegates to <see cref="ResolveIndexOrAppend"/> so an ambiguous match surfaces
    /// immediately, before anything is written.</summary>
    public static void Probe(JsonNode? precedingRoot, IReadOnlyList<string> segments, IReadOnlyList<MatchSpec> conditionSpecs)
    {
        var pathDescription = string.Join(":", segments);
        var located = Navigate(precedingRoot, segments);

        if (located is not null and not JsonArray)
            throw new InvalidOperationException($"\"{pathDescription}\" exists but is not a JSON array -- cannot match an item inside it.");

        ResolveIndexOrAppend(located as JsonArray, ToConditions(conditionSpecs), pathDescription);
    }

    /// <summary>Merge-time resolution. Deep-clones and recursively rewrites
    /// <paramref name="overlayNode"/> -- every patch-list found becomes a
    /// <see cref="JsonObject"/> of numeric-string-keyed positional entries, resolved against the
    /// corresponding array in <paramref name="precedingNode"/> (the state this layer must
    /// resolve against: base alone for an Environment overlay, base+Environment-merged for a
    /// Client overlay). Never mutates either input. Everything else recurses/passes through
    /// unchanged, including a sibling plain array at a different key.</summary>
    public static JsonNode? Rewrite(JsonNode? overlayNode, JsonNode? precedingNode)
    {
        switch (overlayNode)
        {
            case JsonObject obj:
            {
                var result = new JsonObject();
                foreach (var (key, value) in obj)
                {
                    var precedingChild = precedingNode is JsonObject precedingObj ? precedingObj[key] : null;
                    if (value is JsonArray array && IsPatchList(array))
                    {
                        if (precedingChild is not null and not JsonArray)
                            throw new InvalidOperationException($"\"{key}\" exists but is not a JSON array -- cannot match an item inside it.");
                        result[key] = RewritePatchList(array, precedingChild as JsonArray, key);
                    }
                    else
                    {
                        result[key] = Rewrite(value, precedingChild);
                    }
                }
                return result;
            }
            case JsonArray array:
            {
                var result = new JsonArray();
                for (var i = 0; i < array.Count; i++)
                {
                    var precedingElement = precedingNode is JsonArray precedingArray && i < precedingArray.Count
                        ? precedingArray[i]
                        : null;
                    result.Add(Rewrite(array[i], precedingElement));
                }
                return result;
            }
            default:
                return overlayNode?.DeepClone();
        }
    }

    private static JsonObject RewritePatchList(JsonArray patchList, JsonArray? precedingArray, string pathDescription)
    {
        var result = new JsonObject();
        var usedIndices = new HashSet<int>();
        var appendCursor = precedingArray?.Count ?? 0;

        foreach (var patchNode in patchList)
        {
            var patch = (JsonObject)patchNode!;
            var elemMatch = patch[ElemMatchKey] as JsonObject
                ?? throw new InvalidOperationException($"\"{ElemMatchKey}\" in \"{pathDescription}\" must be a JSON object of field=value conditions.");

            var conditions = elemMatch.Select(kvp => new Condition(kvp.Key, kvp.Value)).ToList();
            var matches = IndicesMatching(precedingArray, conditions);

            int index;
            if (matches.Count == 0)
            {
                index = appendCursor;
                appendCursor++;
            }
            else if (matches.Count == 1)
            {
                index = matches[0];
            }
            else
            {
                throw new InvalidOperationException(AmbiguousMessage(pathDescription, precedingArray!, matches, conditions));
            }

            if (!usedIndices.Add(index))
                throw new InvalidOperationException(
                    $"Two patches for \"{pathDescription}\" in this overlay both resolve to the same item " +
                    $"(index {index}) -- merge them into one patch, or adjust the conditions so they target different items.");

            var precedingItem = precedingArray is not null && index < precedingArray.Count ? precedingArray[index] : null;
            var fields = new JsonObject();

            // Upsert: a newly-created item's identity comes from the $elemMatch conditions
            // themselves (the same fields that would have found it, had it already existed) --
            // mirrors MongoDB's own upsert semantics. An updated item needs none of this; its
            // identity fields are already there.
            if (matches.Count == 0)
                foreach (var condition in conditions)
                    fields[condition.Field] = condition.Value?.DeepClone();

            foreach (var (key, value) in patch)
            {
                if (key == ElemMatchKey)
                    continue;
                var precedingFieldValue = precedingItem is JsonObject precedingItemObj ? precedingItemObj[key] : null;
                fields[key] = Rewrite(value, precedingFieldValue);
            }

            result[index.ToString(CultureInfo.InvariantCulture)] = fields;
        }

        return result;
    }

    private static JsonNode? Navigate(JsonNode? node, IReadOnlyList<string> segments)
    {
        var current = node;
        foreach (var segment in segments)
        {
            if (current is not JsonObject obj || !obj.TryGetPropertyValue(segment, out var next))
                return null;
            current = next;
        }
        return current;
    }

    public static List<Condition> ToConditions(IReadOnlyList<MatchSpec> specs) =>
        specs.Select(s => new Condition(s.Attribute, JsonLayerMerger.ToJsonValue(s.Value))).ToList();
}
