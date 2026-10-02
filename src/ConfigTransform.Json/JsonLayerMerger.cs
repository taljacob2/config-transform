using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using ConfigTransform.Core;

namespace ConfigTransform.Json;

/// <summary>
/// Applies an arbitrary-length chain of JSON overlays, in order, to a JSON config file by merging
/// each one into the base document's own tree (docs/TREE_MERGE_DESIGN.md). Under
/// docs/SELF_DESCRIBING_OVERLAYS_DESIGN.md a chain's length varies with how deep its `extends`
/// nesting goes. Format-generic by design: no appsettings.json-specific logic here (see CLAUDE.md).
///
/// Merge rules — the ones Microsoft.Extensions.Configuration's own layering applies, which this
/// engine used to delegate to, minus the side effects of flattening everything to strings, and
/// with case-sensitive key matching:
/// - Objects merge key by key, recursively. Keys match exactly -- JSON is case-sensitive, and so
///   are most of the ecosystems whose config this tool merges. A matched key keeps its position;
///   a new key is appended after the existing ones, in the patch's order. A patch key that matches
///   an existing key <i>only by case</i> is an error, not a new key (see <see cref="CaseOnlyMismatch"/>).
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
/// input, as IConfiguration accepts them; comments are not carried into the output. The output uses
/// the base file's line endings and ends with a newline exactly when the base does
/// (<see cref="TextLayout"/>).
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
        var baseText = File.ReadAllText(basePath);
        var layout = TextLayout.Of(baseText);
        var document = Parse(baseText);

        foreach (var patchPath in patchPathsInOrder)
        {
            // A missing patch file is skipped, not an error -- the same tolerance AddJsonFile's
            // optional: true gave it (a *declared* patch that's missing is already caught earlier,
            // by LayerChain.ResolveResource).
            if (!File.Exists(patchPath))
                continue;

            var patch = Parse(File.ReadAllText(patchPath));
            if (JsonElemMatchResolver.ContainsElemMatch(patch))
                patch = JsonElemMatchResolver.Rewrite(patch, document);

            document = MergeNode(document, patch, patchPath, "");
        }

        return layout.Apply(document?.ToJsonString(JsonWriteOptions.Indented) ?? "null");
    }

    private static JsonNode? Parse(string text) =>
        JsonNode.Parse(text, documentOptions: ReadOptions);

    /// <returns>
    /// The node that now belongs at <paramref name="target"/>'s position: <paramref name="target"/>
    /// itself, merged into in place, or a copy of <paramref name="patch"/> that replaces it.
    /// </returns>
    private static JsonNode? MergeNode(JsonNode? target, JsonNode? patch, string patchPath, string path)
    {
        switch (target, patch)
        {
            case (JsonObject targetObject, JsonObject patchObject):
                MergeObject(targetObject, patchObject, patchPath, path);
                return targetObject;
            case (JsonArray targetArray, JsonArray patchArray):
                for (var i = 0; i < patchArray.Count; i++)
                    MergeArrayItem(targetArray, i, patchArray[i], patchPath, path);
                return targetArray;
            case (JsonArray targetArray, JsonObject patchObject) when TryGetIndexKeys(patchObject, out var indexed):
                foreach (var (index, value) in indexed)
                {
                    if (index > targetArray.Count)
                        throw new InvalidOperationException(
                            $"A patch addresses item {index} of an array that has only {targetArray.Count} item(s) -- " +
                            $"an index-keyed patch can update an existing item or append the next one ({targetArray.Count}), " +
                            "not leave a gap.");
                    MergeArrayItem(targetArray, index, value, patchPath, path);
                }
                return targetArray;
            default:
                return patch?.DeepClone();
        }
    }

    private static void MergeObject(JsonObject target, JsonObject patch, string patchPath, string path)
    {
        foreach (var (key, patchValue) in patch)
        {
            var keyPath = path.Length == 0 ? key : $"{path}:{key}";
            if (!target.ContainsKey(key))
            {
                var caseVariant = target.Select(kvp => kvp.Key)
                    .FirstOrDefault(existing => string.Equals(existing, key, StringComparison.OrdinalIgnoreCase));
                if (caseVariant is not null)
                    throw CaseOnlyMismatch(patchPath, keyPath, path.Length == 0 ? caseVariant : $"{path}:{caseVariant}", caseVariant);

                target[key] = patchValue?.DeepClone();
                continue;
            }

            var current = target[key];
            var merged = MergeNode(current, patchValue, patchPath, keyPath);
            if (!ReferenceEquals(merged, current))
                target[key] = merged;
        }
    }

    private static void MergeArrayItem(JsonArray target, int index, JsonNode? patchValue, string patchPath, string path)
    {
        if (index == target.Count)
        {
            target.Add(patchValue?.DeepClone());
            return;
        }

        var current = target[index];
        var merged = MergeNode(current, patchValue, patchPath, $"{path}:{index}");
        if (!ReferenceEquals(merged, current))
            target[index] = merged;
    }

    /// <summary>
    /// A patch key that differs from an existing key only by case. Writing it as a second key
    /// would almost never be what was meant -- and for a .NET consumer it's a deploy-time time
    /// bomb, since <c>Microsoft.Extensions.Configuration</c> reads keys case-insensitively and
    /// refuses to load a file with two such keys. Silently overriding the existing key instead (the
    /// old behavior) is wrong for every case-sensitive consumer. So: stop, and say which spelling
    /// exists. docs/TREE_MERGE_DESIGN.md's "Key matching is case-sensitive".
    /// </summary>
    private static InvalidOperationException CaseOnlyMismatch(string patchPath, string keyPath, string existingPath, string existingKey) =>
        new($"'{patchPath}' sets \"{keyPath}\", but the existing key is \"{existingPath}\" -- they differ only by case. " +
            "Keys are case-sensitive, so this would add a second key instead of overriding the existing one " +
            $"(and .NET's configuration loader rejects keys that differ only by case).\nTry: spell it \"{existingKey}\" in the patch.");

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
}
