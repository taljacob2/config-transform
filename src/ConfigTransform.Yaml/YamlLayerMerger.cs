using System.Globalization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

namespace ConfigTransform.Yaml;

/// <summary>
/// Applies an arbitrary-length chain of YAML overlays, in order, to a YAML config file by merging
/// each one into the base document's own node tree (docs/TREE_MERGE_DESIGN.md), via YamlDotNet's
/// representation model. Same merge rules as <c>ConfigTransform.Json</c>'s <c>JsonLayerMerger</c>
/// (same data model: maps/sequences/scalars), but not shared code with it -- each format engine
/// in this repo is an independent library (see CLAUDE.md's "Repo structure"). Format-generic by
/// design: no appsettings.yaml-specific logic here (see CLAUDE.md).
///
/// Merge rules -- the ones Microsoft.Extensions.Configuration's own layering applies, which this
/// engine used to delegate to, minus the side effects of flattening everything to strings, and
/// with case-sensitive key matching:
/// - Maps merge key by key, recursively. Keys match exactly -- YAML is case-sensitive. A matched
///   key keeps its position; a new key is appended after the existing ones, in the patch's order.
///   A patch key that matches an existing key <i>only by case</i> is an error, not a new key (same
///   reasoning as <c>JsonLayerMerger</c>; docs/TREE_MERGE_DESIGN.md).
/// - Sequences merge by index: an overlay sequence only overrides the indices it specifies, and
///   any trailing base items beyond that survive untouched. A map whose keys are all sequence
///   indices (<c>"1": ...</c>) addresses individual items of an existing sequence.
/// - Anything else -- a scalar, a null, or a different kind of node than the one it lands on --
///   replaces what was there, exactly as written in the patch.
///
/// Scalars are never interpreted, only carried over: <c>"007"</c> keeps its quotes, <c>007</c>
/// stays plain, <c>'*/15 * * * *'</c> stays single-quoted, a <c>|</c> block stays a block, and
/// <c>{}</c>/<c>[]</c>/<c>null</c>/<c>~</c> survive. The old IConfiguration round trip sorted keys
/// alphabetically, re-quoted values, and dropped empty containers. What still isn't preserved:
/// comments (YamlDotNet's representation model doesn't keep them), anchors/aliases (expanded into
/// copies, as before), and per-level indentation widths (one width is detected from the base file
/// and used throughout -- see <see cref="DetectLayout"/>). One YAML document per file only.
/// </summary>
public static class YamlLayerMerger
{
    public static string Merge(string basePath, IReadOnlyList<string> patchPathsInOrder)
    {
        var parsedBase = Load(basePath);
        var layout = DetectLayout(parsedBase);
        // Merged into in place, so work on a private copy -- see Clone.
        var document = parsedBase is null ? null : Clone(parsedBase);

        foreach (var patchPath in patchPathsInOrder)
        {
            // A missing patch file is skipped, not an error -- the same tolerance AddYamlFile's
            // optional: true gave it (a *declared* patch that's missing is already caught earlier,
            // by LayerChain.ResolveResource).
            if (!File.Exists(patchPath))
                continue;

            var patch = Load(patchPath);
            if (patch is not null)
                document = MergeNode(document, patch, patchPath, "");
        }

        return Save(document, layout);
    }

    /// <returns>The file's single document's root, or null for an empty file. Never mutated by the merge (see <see cref="MergeNode"/>).</returns>
    private static YamlNode? Load(string path)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(File.ReadAllText(path)));

        return stream.Documents.Count switch
        {
            0 => null,
            1 => stream.Documents[0].RootNode,
            _ => throw new InvalidOperationException(
                $"'{path}' contains {stream.Documents.Count} YAML documents (separated by '---'); a config file must contain exactly one."),
        };
    }

    private static string Save(YamlNode? document, (int Indent, bool IndentSequences) layout)
    {
        if (document is null)
            return "{}" + Environment.NewLine;

        var writer = new StringWriter();
        var settings = new EmitterSettings(
            bestIndent: layout.Indent, bestWidth: int.MaxValue, isCanonical: false, maxSimpleKeyLength: 1024,
            indentSequences: layout.IndentSequences);
        new YamlStream(new YamlDocument(document)).Save(new Emitter(writer, settings), assignAnchors: false);

        // YamlStream always ends a document with an explicit "..." end marker; a config file doesn't have one.
        var output = writer.ToString();
        var marker = "..." + writer.NewLine;
        return output.EndsWith(marker, StringComparison.Ordinal) ? output[..^marker.Length] : output;
    }

    /// <returns>
    /// The node that now belongs at <paramref name="target"/>'s position: <paramref name="target"/>
    /// itself, merged into in place, or a copy of <paramref name="patch"/> that replaces it.
    /// <paramref name="patch"/> itself is never mutated or inserted -- only copies of its nodes.
    /// </returns>
    private static YamlNode MergeNode(YamlNode? target, YamlNode patch, string patchPath, string path)
    {
        switch (target, patch)
        {
            case (YamlMappingNode targetMap, YamlMappingNode patchMap):
                MergeMap(targetMap, patchMap, patchPath, path);
                return targetMap;
            case (YamlSequenceNode targetSequence, YamlSequenceNode patchSequence):
                for (var i = 0; i < patchSequence.Children.Count; i++)
                    MergeSequenceItem(targetSequence, i, patchSequence.Children[i], patchPath, path);
                return targetSequence;
            case (YamlSequenceNode targetSequence, YamlMappingNode patchMap) when IsIndexMap(patchMap, out var indexed):
                foreach (var (index, value) in indexed)
                {
                    if (index > targetSequence.Children.Count)
                        throw new InvalidOperationException(
                            $"A patch addresses item {index} of a sequence that has only {targetSequence.Children.Count} item(s) -- " +
                            $"an index-keyed patch can update an existing item or append the next one ({targetSequence.Children.Count}), " +
                            "not leave a gap.");
                    MergeSequenceItem(targetSequence, index, value, patchPath, path);
                }
                return targetSequence;
            default:
                return Clone(patch);
        }
    }

    private static void MergeMap(YamlMappingNode target, YamlMappingNode patch, string patchPath, string path)
    {
        foreach (var (patchKey, patchValue) in patch.Children)
        {
            var keyPath = path.Length == 0 ? patchKey.ToString() : $"{path}:{patchKey}";
            var existingKey = target.Children.Keys.FirstOrDefault(k => k.Equals(patchKey));
            if (existingKey is null)
            {
                if (FindCaseVariant(target, patchKey) is { } caseVariant)
                    throw CaseOnlyMismatch(patchPath, keyPath, path.Length == 0 ? caseVariant : $"{path}:{caseVariant}", caseVariant);

                target.Children.Add(Clone(patchKey), Clone(patchValue));
                continue;
            }

            var current = target.Children[existingKey];
            var merged = MergeNode(current, patchValue, patchPath, keyPath);
            if (!ReferenceEquals(merged, current))
                target.Children[existingKey] = merged;
        }
    }

    private static void MergeSequenceItem(YamlSequenceNode target, int index, YamlNode patchValue, string patchPath, string path)
    {
        if (index == target.Children.Count)
        {
            target.Children.Add(Clone(patchValue));
            return;
        }

        var current = target.Children[index];
        var merged = MergeNode(current, patchValue, patchPath, $"{path}:{index}");
        if (!ReferenceEquals(merged, current))
            target.Children[index] = merged;
    }

    /// <summary>An existing scalar key equal to <paramref name="key"/> ignoring case, when there's no exact match -- see <see cref="CaseOnlyMismatch"/>.</summary>
    private static string? FindCaseVariant(YamlMappingNode target, YamlNode key)
    {
        if (key is not YamlScalarNode { Value: { } keyText })
            return null;

        return target.Children.Keys
            .OfType<YamlScalarNode>()
            .Select(k => k.Value)
            .FirstOrDefault(existing => string.Equals(existing, keyText, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A patch key that differs from an existing key only by case: stop rather than add a second
    /// key -- same reasoning as <c>JsonLayerMerger</c>'s own (a .NET consumer reading this through
    /// <c>Microsoft.Extensions.Configuration</c> would refuse to load it), ported not shared.
    /// </summary>
    private static InvalidOperationException CaseOnlyMismatch(string patchPath, string keyPath, string existingPath, string existingKey) =>
        new($"'{patchPath}' sets \"{keyPath}\", but the existing key is \"{existingPath}\" -- they differ only by case. " +
            "Keys are case-sensitive, so this would add a second key instead of overriding the existing one " +
            $"(and .NET's configuration loader rejects keys that differ only by case).\nTry: spell it \"{existingKey}\" in the patch.");

    /// <summary>True when every key is a canonical sequence index ("0", "12" -- not "01" or "-1"); the pairs come back in ascending index order.</summary>
    private static bool IsIndexMap(YamlMappingNode map, out List<(int Index, YamlNode Value)> indexed)
    {
        indexed = [];
        foreach (var (key, value) in map.Children)
        {
            if (key is not YamlScalarNode { Value: { } text } ||
                !int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var index) ||
                index.ToString(CultureInfo.InvariantCulture) != text)
                return false;
            indexed.Add((index, value));
        }

        indexed.Sort((a, b) => a.Index.CompareTo(b.Index));
        return indexed.Count > 0;
    }

    /// <summary>
    /// A deep copy keeping each node's style and tag but not its anchor -- so merging into the
    /// base never mutates a node shared through an alias, or a patch's own tree. Aliases come out
    /// as independent copies, which is how the old IConfiguration-based merge wrote them too.
    /// </summary>
    private static YamlNode Clone(YamlNode node)
    {
        switch (node)
        {
            case YamlScalarNode scalar:
                return new YamlScalarNode(scalar.Value) { Style = scalar.Style, Tag = scalar.Tag };
            case YamlSequenceNode sequence:
            {
                var copy = new YamlSequenceNode { Style = sequence.Style, Tag = sequence.Tag };
                foreach (var child in sequence.Children)
                    copy.Children.Add(Clone(child));
                return copy;
            }
            case YamlMappingNode map:
            {
                var copy = new YamlMappingNode { Style = map.Style, Tag = map.Tag };
                foreach (var (key, value) in map.Children)
                    copy.Children.Add(Clone(key), Clone(value));
                return copy;
            }
            default:
                throw new InvalidOperationException($"Unsupported YAML node type '{node.NodeType}'.");
        }
    }

    /// <summary>
    /// The base file's indentation width and whether its block sequences are indented under their
    /// key (<c>key:\n  - a</c>) or flush with it (<c>key:\n- a</c>), read from the source positions
    /// YamlDotNet records on each node -- so the merged file is laid out like the file it came
    /// from. Defaults to 2 / flush (YamlDotNet's own) when the base has nothing nested to read from.
    /// Needs the base exactly as parsed: <see cref="Clone"/> doesn't carry source positions over.
    /// </summary>
    private static (int Indent, bool IndentSequences) DetectLayout(YamlNode? root)
    {
        int? indent = null;
        bool? indentSequences = null;

        void Visit(YamlNode node)
        {
            if (node is not YamlMappingNode { Style: not MappingStyle.Flow } map)
            {
                if (node is YamlSequenceNode sequence)
                    foreach (var child in sequence.Children)
                        Visit(child);
                return;
            }

            foreach (var (key, value) in map.Children)
            {
                if (indent is not null && indentSequences is not null)
                    return;

                if (value is YamlMappingNode { Style: not MappingStyle.Flow } childMap && childMap.Children.Count > 0)
                {
                    var childKey = childMap.Children.Keys.First();
                    if (childKey.Start.Line > key.Start.Line && childKey.Start.Column > key.Start.Column)
                        indent ??= (int)(childKey.Start.Column - key.Start.Column);
                }
                else if (value is YamlSequenceNode { Style: not SequenceStyle.Flow } childSequence && childSequence.Children.Count > 0)
                {
                    indentSequences ??= childSequence.Start.Column > key.Start.Column;
                }

                Visit(value);
            }
        }

        if (root is not null)
            Visit(root);

        return (indent is > 0 ? indent.Value : 2, indentSequences ?? false);
    }

    /// <summary>
    /// Type inference for a value typed on the command line, which arrives as plain text: used by
    /// <see cref="YamlFieldAuthor"/> (`set`), so <c>--set true</c> writes a YAML boolean rather than
    /// the string "true". Never applied to values read from a file -- those keep exactly what was
    /// written.
    /// </summary>
    internal static object? ToYamlValue(string? value)
    {
        if (value is null)
            return null;

        if (bool.TryParse(value, out var boolValue))
            return boolValue;

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue))
            return longValue;

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
            return doubleValue;

        return value;
    }
}
