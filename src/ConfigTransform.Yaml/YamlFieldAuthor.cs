using System.Collections;
using ConfigTransform.Core;
using YamlDotNet.Serialization;

namespace ConfigTransform.Yaml;

/// <summary>
/// Implements the `set` command (docs/FIELD_AUTHORING_DESIGN.md) for YAML: a single key path
/// (nested or top-level, ':'-separated) -- both updating an existing key and creating a
/// brand-new one, since a YAML overlay is just YAML, any layer can introduce or override a key
/// with no XDT-style Transform/Locator concept to make (same reasoning as JSON's plain-field
/// path, ported not shared -- see CLAUDE.md's "Repo structure" for why each format engine here
/// is an independent library). Also ports JSON's nested-path-vs-literal-key disambiguation (a
/// key that itself contains a literal ':' is rare but real for YAML too, same nesting model).
///
/// <b>Matching an item inside a YAML array of objects is not implemented</b> -- a deliberate,
/// named scope cut for this format's first version, not an oversight: JSON's equivalent
/// (`JsonElemMatchResolver`) is ~200 lines tightly coupled to `System.Text.Json.Nodes` types
/// (deep-equality, deep-cloning, a numeric-string-keyed rewrite object to avoid clobbering
/// skipped array indices); porting that to YAML's own object-graph types is real, separable
/// work. See docs/FIELD_AUTHORING_DESIGN.md's "Open items" for the same posture XML's own
/// `Insert`/array-matching gaps already have.
///
/// Operates on the plain `IDictionary`/`IList` object graph YamlDotNet's default (untyped)
/// deserialization produces -- verified empirically that a nested untyped YAML mapping
/// deserializes as `Dictionary&lt;object, object&gt;`, not `Dictionary&lt;string, object&gt;`,
/// even when the top-level target type requests the latter, so all navigation here goes through
/// the non-generic `System.Collections.IDictionary`/`IList` interfaces both shapes satisfy,
/// rather than assuming one concrete generic type throughout.
/// </summary>
public static class YamlFieldAuthor
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder().Build();
    private static readonly ISerializer Serializer = new SerializerBuilder().Build();

    /// <exception cref="InvalidOperationException">
    /// The first --match isn't "key"/"literal-key"; more than one --match was given (the
    /// array-of-objects shape, not yet supported); a genuine nested-path/literal-key collision or
    /// an ambiguous/not-found literal key; or the bare/defaulted shorthand used for --set.
    /// </exception>
    public static string Author(
        string precedingYaml,
        string? existingTargetYaml,
        bool isBaseTarget,
        IReadOnlyList<MatchSpec> matches,
        IReadOnlyList<MatchSpec> setFields)
    {
        var locationMatch = matches[0];
        if (locationMatch.Attribute is not ("key" or "literal-key"))
            throw new InvalidOperationException(
                $"'{locationMatch.Attribute}' is not a valid --match for YAML. Use key=<path> " +
                "(':'-separated, e.g. Logging:LogLevel:Default) or literal-key=<exact key name> " +
                "for a key that itself contains a literal ':'.");

        if (matches.Count > 1)
            throw new InvalidOperationException(
                "Matching an item inside a YAML array of objects is not yet supported -- only a " +
                "single --match (the field's own key path) is accepted. See " +
                "docs/FIELD_AUTHORING_DESIGN.md's \"Open items\" for why this is a real, " +
                "deliberately deferred gap, not an oversight.");

        if (setFields.Count != 1 || setFields[0].Attribute != "value")
            throw new InvalidOperationException(
                "'set' for YAML writes a single scalar value -- use --set value=<new value>, " +
                "or the bare form (--set <value>), which defaults to it.");

        var preceding = ParseMap(precedingYaml);
        var segments = ResolveSegments(preceding, locationMatch);

        var target = existingTargetYaml is null ? new Dictionary<string, object?>() : ParseMap(existingTargetYaml);
        SetAtPath(target, segments, setFields[0].Value);
        return Serializer.Serialize(target);
    }

    private static IDictionary ParseMap(string yaml) =>
        Deserializer.Deserialize<Dictionary<string, object>>(yaml) ?? new Dictionary<string, object>();

    private static IReadOnlyList<string> ResolveSegments(IDictionary preceding, MatchSpec matchSpec)
    {
        if (matchSpec.Attribute == "literal-key")
        {
            return FindLiteralKeyPath(preceding, matchSpec.Value)
                ?? throw new InvalidOperationException($"No literal key named \"{matchSpec.Value}\" found anywhere in the document.");
        }

        var nestedSegments = matchSpec.Value.Split(':');

        // A single segment (no ':' at all) IS a literal top-level key -- same reasoning as
        // JsonFieldAuthor: the collision only exists once ':' introduces a real difference
        // between "walk N levels" and "one literal property name containing ':'".
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

        if (!nestedExists && literalPath is not null)
            return literalPath;

        return nestedSegments;
    }

    private static bool TryNavigate(IDictionary obj, IReadOnlyList<string> segments)
    {
        object? current = obj;
        foreach (var segment in segments)
        {
            if (current is not IDictionary currentObj || !currentObj.Contains(segment))
                return false;
            current = currentObj[segment];
        }
        return true;
    }

    private static List<string>? FindLiteralKeyPath(IDictionary root, string literalKey)
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

    private static List<List<string>> FindAllLiteralKeyPaths(IDictionary obj, string literalKey, List<string> prefix)
    {
        var results = new List<List<string>>();
        foreach (DictionaryEntry entry in obj)
        {
            var key = entry.Key.ToString()!;
            var path = new List<string>(prefix) { key };
            if (key == literalKey)
                results.Add(path);
            if (entry.Value is IDictionary nested)
                results.AddRange(FindAllLiteralKeyPaths(nested, literalKey, path));
        }
        return results;
    }

    private static void SetAtPath(IDictionary target, IReadOnlyList<string> segments, string value)
    {
        var current = target;
        for (var i = 0; i < segments.Count - 1; i++)
        {
            if (current[segments[i]] is not IDictionary child)
            {
                child = new Dictionary<string, object?>();
                current[segments[i]] = child;
            }
            current = child;
        }
        current[segments[^1]] = YamlLayerMerger.ToYamlValue(value);
    }
}
