using System.Collections;
using ConfigTransform.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;
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
/// It also matches or creates an item inside an array of objects (`--match key=Rules --match
/// role=Admin --set enabled=true`) via the same `$elemMatch` overlay shape as JSON, resolved by
/// <see cref="YamlElemMatchResolver"/> (docs/FIELD_AUTHORING_DESIGN.md, "YAML array-of-objects
/// matching"). Conditions compare by text, not type -- the one deliberate difference from JSON.
///
/// Reading the <i>preceding</i> document (to resolve and verify the key path) uses the plain
/// `IDictionary`/`IList` object graph YamlDotNet's default (untyped) deserialization produces --
/// verified empirically that a nested untyped YAML mapping deserializes as
/// `Dictionary&lt;object, object&gt;`, not `Dictionary&lt;string, object&gt;`, so navigation goes
/// through the non-generic interfaces both shapes satisfy.
///
/// Writing the <i>target</i> file edits its node tree directly (YamlDotNet's representation model,
/// the same one <see cref="YamlLayerMerger"/> uses): only the one value being set changes, and
/// every other line keeps its own quoting, order and layout. Re-serializing the whole file — what
/// this did before — re-quoted values it never touched. The new value is typed by
/// <see cref="YamlCliValue"/>: strings are always double-quoted (the "Norway problem").
/// </summary>
public static class YamlFieldAuthor
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder().Build();

    /// <exception cref="InvalidOperationException">
    /// The first --match isn't "key"/"literal-key"; a genuine nested-path/literal-key collision or
    /// an ambiguous/not-found literal key; the bare/defaulted shorthand used for --set on a
    /// plain-field write, or for an element-match condition or field; the located path exists but
    /// isn't a sequence; more than one item matches; or the overlay already holds non-patch-list
    /// content at the array's key.
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

        var elementConditions = matches.Skip(1).ToList();
        if (elementConditions.Count == 0 && (setFields.Count != 1 || setFields[0].Attribute != "value"))
            throw new InvalidOperationException(
                "'set' for YAML writes a single scalar value -- use --set value=<new value>, " +
                "or the bare form (--set <value>), which defaults to it.");

        if (elementConditions.Any(c => c.WasDefaulted))
            throw new InvalidOperationException(
                "Element-match conditions need an explicit field=value -- the bare/default " +
                "shorthand only applies to the first --match (the array's location).");

        if (elementConditions.Count > 0 && (setFields.Count == 0 || setFields.Any(f => f.WasDefaulted)))
            throw new InvalidOperationException(
                "An element-match write needs at least one explicit --set field=value -- the " +
                "bare/default shorthand ('value=') only applies to a plain-field 'set'.");

        var preceding = ParseMap(precedingYaml);
        var segments = ResolveSegments(preceding, locationMatch);
        RejectCaseOnlyMismatch(preceding, segments);

        var existingRoot = existingTargetYaml is null ? null : YamlLayerMerger.Load(existingTargetYaml, "the file being written");
        if (existingRoot is not null and not YamlMappingNode)
            throw new InvalidOperationException("The file being written isn't a YAML map at its top level, so there's no key path to set.");

        var target = existingRoot as YamlMappingNode ?? new YamlMappingNode();
        var layout = existingRoot is null ? (Indent: 2, IndentSequences: false) : YamlLayerMerger.DetectLayout(existingRoot);

        if (elementConditions.Count == 0)
        {
            SetAtPath(target, segments, setFields[0].Value);
        }
        else if (isBaseTarget)
        {
            MutateRealSequenceItem(target, segments, elementConditions, setFields);
        }
        else
        {
            YamlElemMatchResolver.Probe(YamlLayerMerger.Load(precedingYaml, "the preceding document"), segments, elementConditions);
            SetElemMatchAtPath(target, segments, elementConditions, setFields);
        }

        // The file being rewritten keeps its own line endings and final newline (see TextLayout).
        var textLayout = existingTargetYaml is null ? TextLayout.Default : TextLayout.Of(existingTargetYaml);
        return textLayout.Apply(YamlLayerMerger.Save(target, layout));
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

    /// <summary>
    /// Refuses a key path that exists in the document only with different casing -- keys are
    /// case-sensitive, so writing it would add a second key that the merge then rejects
    /// (<c>YamlLayerMerger</c>'s own case-only-mismatch check). Same as JSON's, ported not shared.
    /// </summary>
    private static void RejectCaseOnlyMismatch(IDictionary preceding, IReadOnlyList<string> segments)
    {
        IDictionary? current = preceding;
        for (var i = 0; i < segments.Count && current is not null; i++)
        {
            if (current.Contains(segments[i]))
            {
                current = current[segments[i]] as IDictionary;
                continue;
            }

            var caseVariant = current.Keys.Cast<object>().Select(k => k.ToString())
                .FirstOrDefault(existing => string.Equals(existing, segments[i], StringComparison.OrdinalIgnoreCase));
            if (caseVariant is null)
                return; // a genuinely new key from here on

            var suggested = string.Join(":", segments.Take(i).Append(caseVariant).Concat(segments.Skip(i + 1)));
            throw new InvalidOperationException(
                $"No key \"{segments[i]}\" exists at that level, but \"{caseVariant}\" does -- keys are case-sensitive.\n" +
                $"Try: --match key={suggested}");
        }
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

    /// <summary>
    /// Walks (creating as needed) the maps along <paramref name="segments"/> and sets the last key's
    /// value — in place, so an existing key keeps its position and every other node its style.
    /// </summary>
    private static void SetAtPath(YamlMappingNode target, IReadOnlyList<string> segments, string value)
    {
        var current = target;
        for (var i = 0; i < segments.Count - 1; i++)
        {
            var key = FindKey(current, segments[i]);
            if (key is not null && current.Children[key] is YamlMappingNode child)
            {
                current = child;
                continue;
            }

            var created = new YamlMappingNode();
            if (key is not null)
                current.Children[key] = created;
            else
                current.Children.Add(new YamlScalarNode(segments[i]), created);
            current = created;
        }

        // A map that was written as an empty flow map ({}) gets its keys in block style instead.
        if (current.Children.Count == 0)
            current.Style = MappingStyle.Block;

        var leafKey = FindKey(current, segments[^1]);
        var leafValue = YamlCliValue.From(value);
        if (leafKey is not null)
            current.Children[leafKey] = leafValue;
        else
            current.Children.Add(new YamlScalarNode(segments[^1]), leafValue);
    }

    /// <summary>The map at <paramref name="segments"/> minus the last one, created as needed -- the parent of the array an element-match write targets.</summary>
    private static YamlMappingNode WalkToParent(YamlMappingNode target, IReadOnlyList<string> segments)
    {
        var current = target;
        for (var i = 0; i < segments.Count - 1; i++)
        {
            if (YamlElemMatchResolver.Get(current, segments[i]) is YamlMappingNode child)
            {
                current = child;
                continue;
            }

            var created = new YamlMappingNode();
            YamlElemMatchResolver.UseBlockStyleIfEmpty(current);
            YamlElemMatchResolver.Set(current, segments[i], created);
            current = created;
        }
        YamlElemMatchResolver.UseBlockStyleIfEmpty(current);
        return current;
    }

    /// <summary>Base-target element-match write: edits the real sequence directly -- no <c>$elemMatch</c> syntax, since the base file isn't an overlay.</summary>
    private static void MutateRealSequenceItem(
        YamlMappingNode target, IReadOnlyList<string> segments, IReadOnlyList<MatchSpec> elementConditions, IReadOnlyList<MatchSpec> setFields)
    {
        var parent = WalkToParent(target, segments);
        var pathDescription = string.Join(":", segments);

        var sequence = YamlElemMatchResolver.Get(parent, segments[^1]) switch
        {
            YamlSequenceNode existing => existing,
            null => CreateSequence(parent, segments[^1]),
            _ => throw new InvalidOperationException($"\"{pathDescription}\" already exists but is not a YAML sequence."),
        };

        var conditions = YamlElemMatchResolver.ToConditions(elementConditions);
        var index = YamlElemMatchResolver.ResolveIndexOrAppend(sequence, conditions, pathDescription);
        if (index == sequence.Children.Count)
        {
            var newItem = new YamlMappingNode();
            foreach (var condition in conditions)
                YamlElemMatchResolver.Set(newItem, condition.Field, condition.Value);
            sequence.Children.Add(newItem);
        }

        var item = (YamlMappingNode)sequence.Children[index];
        foreach (var field in setFields)
            YamlElemMatchResolver.Set(item, field.Attribute, YamlCliValue.From(field.Value));
    }

    private static YamlSequenceNode CreateSequence(YamlMappingNode parent, string key)
    {
        var sequence = new YamlSequenceNode();
        YamlElemMatchResolver.Set(parent, key, sequence);
        return sequence;
    }

    /// <summary>
    /// Overlay-target element-match write: authors or updates one patch in the <c>$elemMatch</c>
    /// patch list at the array's key. A patch with the same conditions is updated in place (the
    /// re-run case); different conditions append a second patch.
    /// </summary>
    private static void SetElemMatchAtPath(
        YamlMappingNode target, IReadOnlyList<string> segments, IReadOnlyList<MatchSpec> elementConditions, IReadOnlyList<MatchSpec> setFields)
    {
        var parent = WalkToParent(target, segments);
        var pathDescription = string.Join(":", segments);

        var patchList = YamlElemMatchResolver.Get(parent, segments[^1]) switch
        {
            YamlSequenceNode existing when existing.Children.Count == 0 || YamlElemMatchResolver.IsPatchList(existing) => existing,
            null => CreateSequence(parent, segments[^1]),
            _ => throw new InvalidOperationException(
                $"\"{pathDescription}\" in this overlay already has content that isn't an element-match " +
                "patch list -- cannot add an element-match write here."),
        };

        var conditions = YamlElemMatchResolver.ToConditions(elementConditions);
        var existingPatch = patchList.Children
            .OfType<YamlMappingNode>()
            .FirstOrDefault(p => YamlElemMatchResolver.Get(p, "$elemMatch") is YamlMappingNode em && SameConditions(em, conditions));

        if (existingPatch is not null)
        {
            foreach (var field in setFields)
                YamlElemMatchResolver.Set(existingPatch, field.Attribute, YamlCliValue.From(field.Value));
            return;
        }

        var elemMatch = new YamlMappingNode();
        foreach (var condition in conditions)
            YamlElemMatchResolver.Set(elemMatch, condition.Field, condition.Value);

        var patch = new YamlMappingNode();
        patch.Children.Add(new YamlScalarNode("$elemMatch"), elemMatch);
        foreach (var field in setFields)
            YamlElemMatchResolver.Set(patch, field.Attribute, YamlCliValue.From(field.Value));
        patchList.Children.Add(patch);
    }

    /// <summary>Same condition fields with text-equal values, in any order -- the same text rule matching uses.</summary>
    private static bool SameConditions(YamlMappingNode elemMatch, IReadOnlyList<YamlElemMatchResolver.Condition> conditions) =>
        elemMatch.Children.Count == conditions.Count &&
        conditions.All(c => YamlElemMatchResolver.Get(elemMatch, c.Field) is { } value && YamlElemMatchResolver.NodesEqual(value, c.Value));

    /// <summary>The existing key spelled exactly <paramref name="name"/>, or null — keys are case-sensitive (docs/TREE_MERGE_DESIGN.md).</summary>
    private static YamlNode? FindKey(YamlMappingNode map, string name) =>
        map.Children.Keys.FirstOrDefault(k => k is YamlScalarNode { Value: { } text } && text == name);
}
