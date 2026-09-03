using System.Text.Json;
using System.Text.Json.Nodes;
using ConfigTransform.Core;

namespace ConfigTransform.Json;

/// <summary>
/// Implements the plain-field half of the <c>set</c> command (docs/FIELD_AUTHORING_DESIGN.md)
/// for JSON: writes a nested key's value directly, with no XDT-style Transform/Locator concept
/// (JSON overlays are just plain JSON — any layer can introduce or override a key). Unlike XML,
/// there is no "update vs. insert" distinction to make here — both are the same write.
/// Matching an item inside an array of objects is deliberately not implemented: see
/// <see cref="Author"/>'s remarks.
/// </summary>
public static class JsonFieldAuthor
{
    /// <param name="precedingJson">
    /// The document that exists immediately before this write's own layer would apply — same
    /// meaning as in <c>XmlFieldAuthor.Author</c> — used only to verify a bare/defaulted --match
    /// against the real document (docs/FIELD_AUTHORING_DESIGN.md's "Defaults") and to resolve
    /// the nested-path-vs-literal-key collision case.
    /// </param>
    /// <param name="existingTargetJson">
    /// Current content of the file being written, if it already exists. Null for an overlay
    /// that doesn't exist yet — the base file always exists, so is never null there.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// More than one --match, or a --match attribute other than "key"/"literal-key" — matching
    /// an item inside an array of objects (docs/FIELD_AUTHORING_DESIGN.md's array-of-objects
    /// section) is not implemented: <c>Microsoft.Extensions.Configuration</c>'s JSON provider
    /// merges arrays by index, not by matching a field's value the way XDT's Locator does for
    /// XML, so "which array item" can't be resolved the same way — see
    /// docs/FIELD_AUTHORING_DESIGN.md's "Open items". Also thrown for a genuine nested-path/
    /// literal-key collision, or an ambiguous/not-found literal key.
    /// </exception>
    public static string Author(
        string precedingJson,
        string? existingTargetJson,
        IReadOnlyList<MatchSpec> matches,
        IReadOnlyList<MatchSpec> setFields)
    {
        if (matches.Count != 1)
            throw new InvalidOperationException(
                "'set' for JSON supports exactly one --match today: a single key path. Matching " +
                "an item inside an array of objects is not yet implemented -- " +
                "docs/FIELD_AUTHORING_DESIGN.md's 'Open items' explains why (Microsoft.Extensions." +
                "Configuration merges JSON arrays by index, not by matching a field the way XDT " +
                "does for XML).");

        var matchSpec = matches[0];
        if (matchSpec.Attribute is not ("key" or "literal-key"))
            throw new InvalidOperationException(
                $"'{matchSpec.Attribute}' is not a valid --match for JSON. Use key=<path> " +
                "(':'-separated, e.g. Logging:LogLevel:Default) or literal-key=<exact key name> " +
                "for a key that itself contains a literal ':'.");

        if (setFields.Count != 1 || setFields[0].Attribute != "value")
            throw new InvalidOperationException(
                "'set' for JSON writes a single scalar value -- use --set value=<new value>, " +
                "or the bare form (--set <value>), which defaults to it.");

        var preceding = ParseObject(precedingJson, "preceding document");
        var segments = ResolveSegments(preceding, matchSpec);

        var target = existingTargetJson is null ? new JsonObject() : ParseObject(existingTargetJson, "existing overlay");
        SetAtPath(target, segments, setFields[0].Value);

        return target.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
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
