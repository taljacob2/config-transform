using System.Text.Json;
using System.Text.Json.Nodes;
using ConfigTransform.Core;

namespace ConfigTransform.Json;

/// <summary>
/// Implements the <c>set</c> command (docs/FIELD_AUTHORING_DESIGN.md) for JSON: a plain-field
/// half (writes a nested key's value directly, with no XDT-style Transform/Locator concept —
/// JSON overlays are just plain JSON, any layer can introduce or override a key; no "update vs.
/// insert" distinction to make, unlike XML) and an element-match half (matching/creating an item
/// inside an array of objects — see <see cref="JsonElemMatchResolver"/> for the mechanism).
/// </summary>
public static class JsonFieldAuthor
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <param name="precedingJson">
    /// The document that exists immediately before this write's own layer would apply — same
    /// meaning as in <c>XmlFieldAuthor.Author</c> — used to verify a bare/defaulted --match
    /// against the real document (docs/FIELD_AUTHORING_DESIGN.md's "Defaults"), to resolve the
    /// nested-path-vs-literal-key collision case, and (for an element-match write) as the eager,
    /// non-authoritative document <see cref="JsonElemMatchResolver.Probe"/> checks against.
    /// </param>
    /// <param name="existingTargetJson">
    /// Current content of the file being written, if it already exists. Null for an overlay
    /// that doesn't exist yet — the base file always exists, so is never null there.
    /// </param>
    /// <param name="isBaseTarget">
    /// True when writing directly to the base file. For an element-match write this matters: a
    /// base-file write mutates a real array item directly (the base file is a real document, it
    /// never gains <c>$elemMatch</c> syntax — <see cref="JsonLayerMerger"/> never rewrites the
    /// base layer), where an overlay-file write authors an <c>$elemMatch</c> patch instead.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// The first --match isn't "key"/"literal-key"; a genuine nested-path/literal-key collision
    /// or an ambiguous/not-found literal key; an element-match condition or --set field using the
    /// bare/defaulted shorthand (only the first --match, the array's location, may default); the
    /// located path exists but isn't a JSON array; more than one array item matches a patch's
    /// conditions; two patches in one write resolve to the same item; or (overlay-target only) the
    /// key already holds overlay content that isn't an element-match patch list.
    /// </exception>
    public static string Author(
        string precedingJson,
        string? existingTargetJson,
        bool isBaseTarget,
        IReadOnlyList<MatchSpec> matches,
        IReadOnlyList<MatchSpec> setFields)
    {
        var locationMatch = matches[0];
        if (locationMatch.Attribute is not ("key" or "literal-key"))
            throw new InvalidOperationException(
                $"'{locationMatch.Attribute}' is not a valid --match for JSON. Use key=<path> " +
                "(':'-separated, e.g. Logging:LogLevel:Default) or literal-key=<exact key name> " +
                "for a key that itself contains a literal ':'.");

        var preceding = ParseObject(precedingJson, "preceding document");
        var segments = ResolveSegments(preceding, locationMatch);
        var elementConditions = matches.Skip(1).ToList();

        if (elementConditions.Count == 0)
        {
            if (setFields.Count != 1 || setFields[0].Attribute != "value")
                throw new InvalidOperationException(
                    "'set' for JSON writes a single scalar value -- use --set value=<new value>, " +
                    "or the bare form (--set <value>), which defaults to it.");

            var target = existingTargetJson is null ? new JsonObject() : ParseObject(existingTargetJson, "existing overlay");
            SetAtPath(target, segments, setFields[0].Value);
            return target.ToJsonString(Indented);
        }

        foreach (var condition in elementConditions)
            if (condition.WasDefaulted)
                throw new InvalidOperationException(
                    "Element-match conditions need an explicit field=value -- the bare/default " +
                    "shorthand only applies to the first --match (the array's location).");

        if (setFields.Count == 0 || setFields.Any(f => f.WasDefaulted))
            throw new InvalidOperationException(
                "An element-match write needs at least one explicit --set field=value -- the " +
                "bare/default shorthand ('value=') only applies to a plain-field 'set'.");

        if (isBaseTarget)
        {
            var baseDoc = existingTargetJson is null ? new JsonObject() : ParseObject(existingTargetJson, "existing overlay");
            MutateRealArrayItem(baseDoc, segments, elementConditions, setFields);
            return baseDoc.ToJsonString(Indented);
        }

        JsonElemMatchResolver.Probe(preceding, segments, elementConditions);

        var overlay = existingTargetJson is null ? new JsonObject() : ParseObject(existingTargetJson, "existing overlay");
        SetElemMatchAtPath(overlay, segments, elementConditions, setFields);
        return overlay.ToJsonString(Indented);
    }

    /// <summary>Base-target element-match write: walks to the real array and writes into it
    /// directly -- no <c>$elemMatch</c> syntax, since the base file isn't an overlay.</summary>
    private static void MutateRealArrayItem(
        JsonObject baseDoc,
        IReadOnlyList<string> segments,
        IReadOnlyList<MatchSpec> elementConditions,
        IReadOnlyList<MatchSpec> setFields)
    {
        var current = baseDoc;
        for (var i = 0; i < segments.Count - 1; i++)
        {
            if (current[segments[i]] is not JsonObject child)
            {
                child = new JsonObject();
                current[segments[i]] = child;
            }
            current = child;
        }

        var arrayKey = segments[^1];
        if (current[arrayKey] is JsonArray existing)
        {
            MutateArray(existing, elementConditions, setFields, string.Join(":", segments));
            return;
        }

        if (current[arrayKey] is not null)
            throw new InvalidOperationException($"\"{string.Join(":", segments)}\" already exists but is not a JSON array.");

        var array = new JsonArray();
        current[arrayKey] = array;
        MutateArray(array, elementConditions, setFields, string.Join(":", segments));
    }

    private static void MutateArray(
        JsonArray array,
        IReadOnlyList<MatchSpec> elementConditions,
        IReadOnlyList<MatchSpec> setFields,
        string pathDescription)
    {
        var conditions = JsonElemMatchResolver.ToConditions(elementConditions);
        var index = JsonElemMatchResolver.ResolveIndexOrAppend(array, conditions, pathDescription);

        if (index == array.Count)
        {
            var newItem = new JsonObject();
            foreach (var condition in conditions)
                newItem[condition.Field] = condition.Value?.DeepClone();
            array.Add(newItem);
        }

        var item = (JsonObject)array[index]!;
        foreach (var field in setFields)
            item[field.Attribute] = JsonLayerMerger.ToJsonValue(field.Value);
    }

    /// <summary>Overlay-target element-match write: authors/updates one entry in the
    /// <c>$elemMatch</c> patch list at the array's key (docs/FIELD_AUTHORING_DESIGN.md). A patch
    /// with the exact same conditions already present is updated in place (re-run case); a
    /// different condition set appends a second patch, so more than one item in the same array
    /// can be overridden from the same overlay file.</summary>
    private static void SetElemMatchAtPath(
        JsonObject target,
        IReadOnlyList<string> segments,
        IReadOnlyList<MatchSpec> elementConditions,
        IReadOnlyList<MatchSpec> setFields)
    {
        var current = target;
        for (var i = 0; i < segments.Count - 1; i++)
        {
            if (current[segments[i]] is not JsonObject child)
            {
                child = new JsonObject();
                current[segments[i]] = child;
            }
            current = child;
        }

        var arrayKey = segments[^1];
        var pathDescription = string.Join(":", segments);

        JsonArray patchList;
        if (current[arrayKey] is JsonArray existingList && (existingList.Count == 0 || JsonElemMatchResolver.IsPatchList(existingList)))
        {
            patchList = existingList;
        }
        else if (current[arrayKey] is null)
        {
            patchList = new JsonArray();
            current[arrayKey] = patchList;
        }
        else
        {
            throw new InvalidOperationException(
                $"\"{pathDescription}\" in this overlay already has content that isn't an element-match " +
                "patch list -- cannot add an element-match write here.");
        }

        var existingPatch = patchList
            .OfType<JsonObject>()
            .FirstOrDefault(p => p["$elemMatch"] is JsonObject em && SameConditions(em, elementConditions));

        if (existingPatch is not null)
        {
            foreach (var field in setFields)
                existingPatch[field.Attribute] = JsonLayerMerger.ToJsonValue(field.Value);
            return;
        }

        var patch = new JsonObject();
        var elemMatch = new JsonObject();
        foreach (var condition in elementConditions)
            elemMatch[condition.Attribute] = JsonLayerMerger.ToJsonValue(condition.Value);
        patch["$elemMatch"] = elemMatch;
        foreach (var field in setFields)
            patch[field.Attribute] = JsonLayerMerger.ToJsonValue(field.Value);
        patchList.Add(patch);
    }

    private static bool SameConditions(JsonObject elemMatch, IReadOnlyList<MatchSpec> conditions)
    {
        if (elemMatch.Count != conditions.Count)
            return false;
        return conditions.All(c =>
            elemMatch.TryGetPropertyValue(c.Attribute, out var value) &&
            JsonNode.DeepEquals(value, JsonLayerMerger.ToJsonValue(c.Value)));
    }

    private static JsonObject ParseObject(string json, string description) =>
        JsonNode.Parse(json) as JsonObject
            ?? throw new InvalidOperationException($"The {description} did not parse as a JSON object.");

    private static IReadOnlyList<string> ResolveSegments(JsonObject preceding, MatchSpec matchSpec)
    {
        if (matchSpec.Attribute == "literal-key")
        {
            return FindLiteralKeyPath(preceding, matchSpec.Value)
                ?? throw new InvalidOperationException($"No literal key named \"{matchSpec.Value}\" found anywhere in the document.");
        }

        var nestedSegments = matchSpec.Value.Split(':');

        // A single segment (no ':' at all) IS a literal top-level key -- "nested" and "literal"
        // aren't two different interpretations to disambiguate, they're the same one. The
        // collision only exists once ':' introduces a real difference between "walk N levels"
        // and "one literal property name containing ':'".
        if (nestedSegments.Length == 1)
            return nestedSegments;

        var nestedExists = TryNavigate(preceding, nestedSegments);
        var literalPath = FindLiteralKeyPath(preceding, matchSpec.Value);

        if (nestedExists && literalPath is not null)
        {
            throw new InvalidOperationException(
                $"Ambiguous: both a nested path and a literal key named \"{matchSpec.Value}\" exist. Did you mean:\n" +
                $"  --match key={matchSpec.Value}          (nested: {string.Join(" -> ", nestedSegments)})\n" +
                $"  --match literal-key={matchSpec.Value}  (the literal key)");
        }

        // Only the literal reading resolves to something real -- use it, no guess involved
        // (docs/FIELD_AUTHORING_DESIGN.md's "one rule": verify and resolve automatically
        // whenever exactly one interpretation is real).
        if (!nestedExists && literalPath is not null)
            return literalPath;

        // Neither exists (a brand-new key) or the nested path already exists: default to
        // nested -- there's no evidence to check for a brand-new key either way, and nested is
        // the convention every example in docs/GETTING_STARTED.md already uses.
        return nestedSegments;
    }

    private static bool TryNavigate(JsonObject obj, IReadOnlyList<string> segments)
    {
        JsonNode? current = obj;
        foreach (var segment in segments)
        {
            if (current is not JsonObject currentObj || !currentObj.TryGetPropertyValue(segment, out var next))
                return false;
            current = next;
        }
        return true;
    }

    private static List<string>? FindLiteralKeyPath(JsonObject root, string literalKey)
    {
        var matches = FindAllLiteralKeyPaths(root, literalKey, []);
        return matches.Count switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new InvalidOperationException(
                $"Ambiguous: more than one literal key named \"{literalKey}\" found: " +
                string.Join(", ", matches.Select(p => string.Join(":", p))))
        };
    }

    private static List<List<string>> FindAllLiteralKeyPaths(JsonObject obj, string literalKey, List<string> prefix)
    {
        var results = new List<List<string>>();
        foreach (var kvp in obj)
        {
            var path = new List<string>(prefix) { kvp.Key };
            if (kvp.Key == literalKey)
                results.Add(path);
            if (kvp.Value is JsonObject nested)
                results.AddRange(FindAllLiteralKeyPaths(nested, literalKey, path));
        }
        return results;
    }

    private static void SetAtPath(JsonObject target, IReadOnlyList<string> segments, string value)
    {
        var current = target;
        for (var i = 0; i < segments.Count - 1; i++)
        {
            if (current[segments[i]] is not JsonObject child)
            {
                child = new JsonObject();
                current[segments[i]] = child;
            }
            current = child;
        }
        current[segments[^1]] = JsonLayerMerger.ToJsonValue(value);
    }
}
