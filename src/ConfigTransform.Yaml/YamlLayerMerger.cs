using System.Globalization;
using Microsoft.Extensions.Configuration;
using YamlDotNet.Serialization;

namespace ConfigTransform.Yaml;

/// <summary>
/// Applies an arbitrary-length chain of YAML overlays, in order, to a YAML config file using
/// Microsoft.Extensions.Configuration's own ConfigurationBuilder as the merge engine (base then
/// each patch in turn via AddYamlFile, from NetEscapades.Configuration.Yaml -- there's no YAML
/// support in the BCL), then flattens the resulting configuration back out to a single YAML
/// document. Same architecture as <c>ConfigTransform.Json</c>'s <c>JsonLayerMerger</c> (same data
/// model: maps/lists/scalars) but not shared code with it -- each format engine in this repo is
/// an independent library (see CLAUDE.md's "Repo structure"). Format-generic by design: no
/// appsettings.yaml-specific logic here (see CLAUDE.md).
///
/// Verified empirically (not assumed) against a real NetEscapades.Configuration.Yaml parse before
/// writing this: <c>IConfiguration.GetChildren()</c> flattens a YAML sequence to indexed keys
/// ("Numbers:0", "Numbers:1", ...) and a nested map to ':'-separated keys, identically to the
/// JSON provider -- so every quirk already documented for <c>JsonLayerMerger</c> applies here
/// unchanged, for the same underlying reason (both are just <c>IConfiguration</c> flattening):
/// - Every leaf value is stored as a plain string internally. This class infers bool/integer/
///   float/string from the flattened value (in that priority order) to preserve the original
///   YAML scalar's type in the overwhelming common case -- ported from, not shared with,
///   <c>JsonLayerMerger.ToJsonValue</c>.
/// - An array is not replaced wholesale by an overlay -- each element is a separate flattened
///   key, so an overlay array only overrides the indices it specifies; any trailing base-layer
///   indices beyond that survive untouched.
/// - An originally-empty YAML map (<c>{}</c>) or sequence (<c>[]</c>) produces no flattened keys
///   at all, so it round-trips as absent, not as an empty container.
/// - YAML is case-sensitive but <c>IConfiguration</c> is not: two sibling keys differing only in
///   case throw a duplicate-key exception at parse time (a real NetEscapades limitation, not a
///   bug in this class) -- see CONFIG_MANAGEMENT.md §5.6.
///
/// Output is written via YamlDotNet's high-level <see cref="Serializer"/> against a plain
/// <c>Dictionary&lt;string, object&gt;</c>/<c>List&lt;object&gt;</c> object graph (not YamlDotNet's
/// lower-level node-tree API) -- the serializer already makes sensible quoting/block-vs-flow-style
/// decisions for a plain graph, so there's no tag/emitter-state management needed here.
/// </summary>
public static class YamlLayerMerger
{
    private static readonly ISerializer Serializer = new SerializerBuilder().Build();

    public static string Merge(string basePath, IReadOnlyList<string> patchPathsInOrder)
    {
        var builder = new ConfigurationBuilder().AddYamlFile(basePath, optional: false);

        foreach (var patchPath in patchPathsInOrder)
            builder.AddYamlFile(patchPath, optional: true);

        var configuration = builder.Build();

        var root = new Dictionary<string, object?>();
        foreach (var child in configuration.GetChildren())
            root[child.Key] = BuildNode(child);

        return Serializer.Serialize(root);
    }

    private static object? BuildNode(IConfigurationSection section)
    {
        var children = section.GetChildren().ToList();

        if (children.Count == 0)
            return ToYamlValue(section.Value);

        var isArray = children
            .Select((child, index) => child.Key == index.ToString(CultureInfo.InvariantCulture))
            .All(matches => matches);

        if (isArray)
            return children.Select(BuildNode).ToList();

        var obj = new Dictionary<string, object?>();
        foreach (var child in children)
            obj[child.Key] = BuildNode(child);
        return obj;
    }

    /// <summary>Internal, not private: reused by <see cref="YamlFieldAuthor"/> so a value <c>set</c>
    /// writes gets the exact same bool/integer/float/string type inference a merge would give it.</summary>
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
