using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ConfigTransform.Json;

/// <summary>
/// Applies an arbitrary-length chain of JSON overlays, in order, to a JSON config file by merging
/// each one into the base document's own tree (docs/TREE_MERGE_DESIGN.md). Under
/// docs/SELF_DESCRIBING_OVERLAYS_DESIGN.md a chain's length varies with how deep its `extends`
/// nesting goes. Format-generic by design: no appsettings.json-specific logic here (see CLAUDE.md).
///
/// Merge rules — the same ones Microsoft.Extensions.Configuration's own layering applies, which
/// this engine used to delegate to, minus the side effects of flattening everything to strings:
/// - Objects merge key by key, recursively. Keys match case-insensitively (as IConfiguration
///   does), preferring an exact-case match; a matched key keeps the base's spelling and position,
///   and a new key is appended after the existing ones, in the patch's order.
/// - Arrays merge by index: an overlay array only overrides the indices it specifies, and any
///   trailing base items beyond that survive untouched. An object whose keys are all array
///   indices (<c>{"1": ...}</c>) addresses individual items of an existing array, the
///   IConfiguration idiom for overriding one element — and the shape
///   <see cref="JsonElemMatchResolver.Rewrite"/> resolves <c>$elemMatch</c> patches to.
/// - Anything else — a scalar, <c>null</c>, or a different kind of node than the one it lands on —
///   replaces what was there, exactly as written in the patch.
///
/// Every value keeps the type and text it was written with: <c>"007"</c> stays a string,
/// <c>1.50</c> stays <c>1.50</c>, <c>null</c>/<c>{}</c>/<c>[]</c> survive. The old
/// flatten-to-strings-and-guess-the-type round trip turned <c>"007"</c> into <c>7</c>, sorted every
/// key alphabetically, and dropped empty containers. Comments and trailing commas are accepted on
/// input, as IConfiguration accepts them; comments are not carried into the output.
///
/// A patch containing <c>$elemMatch</c>-shaped array-of-objects overlays
/// (docs/FIELD_AUTHORING_DESIGN.md) is first resolved by <see cref="JsonElemMatchResolver.Rewrite"/>
/// against the document as merged so far — the base plus every earlier patch, never the base
/// alone — the same progressive order in which <c>XmlLayerMerger</c> applies each transform.
/// </summary>
public static class JsonLayerMerger
{
    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string Merge(string basePath, IReadOnlyList<string> patchPathsInOrder)
    {
        var document = Parse(basePath);

        foreach (var patchPath in patchPathsInOrder)
        {
            // A missing patch file is skipped, not an error -- the same tolerance AddJsonFile's
            // optional: true gave it (a *declared* patch that's missing is already caught earlier,
            // by LayerChain.ResolveResource).
            if (!File.Exists(patchPath))
                continue;

            var patch = Parse(patchPath);
            if (JsonElemMatchResolver.ContainsElemMatch(patch))
                patch = JsonElemMatchResolver.Rewrite(patch, document);

            document = MergeNode(document, patch);
        }

        return document?.ToJsonString(JsonWriteOptions.Indented) ?? "null";
    }

    private static JsonNode? Parse(string path) =>
        JsonNode.Parse(File.ReadAllText(path), documentOptions: ReadOptions);

    /// <returns>
    /// The node that now belongs at <paramref name="target"/>'s position: <paramref name="target"/>
    /// itself, merged into in place, or a copy of <paramref name="patch"/> that replaces it.
    /// </returns>
    private static JsonNode? MergeNode(JsonNode? target, JsonNode? patch)
    {
        switch (target, patch)
        {
            case (JsonObject targetObject, JsonObject patchObject):
                MergeObject(targetObject, patchObject);
                return targetObject;
            case (JsonArray targetArray, JsonArray patchArray):
                for (var i = 0; i < patchArray.Count; i++)
                    MergeArrayItem(targetArray, i, patchArray[i]);
                return targetArray;
            case (JsonArray targetArray, JsonObject patchObject) when TryGetIndexKeys(patchObject, out var indexed):
                foreach (var (index, value) in indexed)
                {
                    if (index > targetArray.Count)
                        throw new InvalidOperationException(
                            $"A patch addresses item {index} of an array that has only {targetArray.Count} item(s) -- " +
                            $"an index-keyed patch can update an existing item or append the next one ({targetArray.Count}), " +
                            "not leave a gap.");
                    MergeArrayItem(targetArray, index, value);
                }
                return targetArray;
            default:
                return patch?.DeepClone();
        }
    }

    private static void MergeObject(JsonObject target, JsonObject patch)
    {
        foreach (var (key, patchValue) in patch)
        {
            var existingKey = FindKey(target, key);
            if (existingKey is null)
            {
                target[key] = patchValue?.DeepClone();
                continue;
            }

            var current = target[existingKey];
            var merged = MergeNode(current, patchValue);
            if (!ReferenceEquals(merged, current))
                target[existingKey] = merged;
        }
    }

    private static void MergeArrayItem(JsonArray target, int index, JsonNode? patchValue)
    {
        if (index == target.Count)
        {
            target.Add(patchValue?.DeepClone());
            return;
        }

        var current = target[index];
        var merged = MergeNode(current, patchValue);
        if (!ReferenceEquals(merged, current))
            target[index] = merged;
    }

    /// <summary>Exact-case match first, then the first case-insensitive one -- see the class remarks.</summary>
    private static string? FindKey(JsonObject target, string key)
    {
        if (target.ContainsKey(key))
            return key;

        foreach (var (existingKey, _) in target)
        {
            if (string.Equals(existingKey, key, StringComparison.OrdinalIgnoreCase))
                return existingKey;
        }

        return null;
    }

    /// <summary>True when every key is a canonical array index ("0", "12" -- not "01" or "-1"); the pairs come back in ascending index order.</summary>
    private static bool TryGetIndexKeys(JsonObject patch, out List<(int Index, JsonNode? Value)> indexed)
    {
        indexed = [];
        foreach (var (key, value) in patch)
        {
            if (!int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var index) ||
                index.ToString(CultureInfo.InvariantCulture) != key)
                return false;
            indexed.Add((index, value));
        }

        indexed.Sort((a, b) => a.Index.CompareTo(b.Index));
        return indexed.Count > 0;
    }

    /// <summary>
    /// Type inference for a value typed on the command line, which arrives as plain text: used by
    /// <see cref="JsonFieldAuthor"/> (`set`) and <see cref="JsonElemMatchResolver"/>'s condition
    /// parsing, so <c>--set true</c> writes a JSON <c>true</c> and <c>--match enabled=true</c>
    /// compares against one. Never applied to values read from a file -- those keep their own type.
    /// </summary>
    internal static JsonNode? ToJsonValue(string? value)
    {
        if (value is null)
            return null;

        if (bool.TryParse(value, out var boolValue))
            return JsonValue.Create(boolValue);

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue))
            return JsonValue.Create(longValue);

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
            return JsonValue.Create(doubleValue);

        return JsonValue.Create(value);
    }
}
