using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;

namespace ConfigTransform.Json;

/// <summary>
/// Applies the base -&gt; Environments -&gt; Clients layering (CONFIG_MANAGEMENT.md §5.1) to a
/// JSON config file using Microsoft.Extensions.Configuration's own ConfigurationBuilder as the
/// merge engine (base → env → client via AddJsonFile, in order), then flattens the resulting
/// configuration back out to a single JSON document. Format-generic by design: no
/// appsettings.json-specific logic here (see CLAUDE.md).
///
/// Two things worth knowing, both inherent to IConfiguration's flat string-keyed model, not
/// specific to this tool:
/// - IConfiguration stores every leaf value as a plain string. A naive round-trip would turn
///   `"enabled": false` into `"enabled": "false"`. This class infers bool/integer/float/string
///   from the flattened value (in that priority order) to preserve the original JSON type in
///   the overwhelming common case.
/// - An array is not replaced wholesale by an overlay — each element is a separate flattened
///   key ("Origins:0", "Origins:1", ...), so an overlay array only overrides the indices it
///   specifies; any trailing base-layer indices beyond that survive untouched. See
///   JsonLayerMergerTests for a test that pins this exact behavior.
/// - An originally-empty JSON object ({}) or array ([]) produces no flattened keys at all, so
///   it round-trips as null rather than as an empty object/array.
/// </summary>
public static class JsonLayerMerger
{
    public static string Merge(string basePath, string? environmentOverlayPath, string? clientOverlayPath)
    {
        var builder = new ConfigurationBuilder().AddJsonFile(basePath, optional: false);

        if (environmentOverlayPath is not null)
            builder.AddJsonFile(environmentOverlayPath, optional: true);

        if (clientOverlayPath is not null)
            builder.AddJsonFile(clientOverlayPath, optional: true);

        var configuration = builder.Build();

        var root = new JsonObject();
        foreach (var child in configuration.GetChildren())
            root[child.Key] = BuildNode(child);

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonNode? BuildNode(IConfigurationSection section)
    {
        var children = section.GetChildren().ToList();

        if (children.Count == 0)
            return ToJsonValue(section.Value);

        var isArray = children
            .Select((child, index) => child.Key == index.ToString(CultureInfo.InvariantCulture))
            .All(matches => matches);

        if (isArray)
        {
            var array = new JsonArray();
            foreach (var child in children)
                array.Add(BuildNode(child));
            return array;
        }

        var obj = new JsonObject();
        foreach (var child in children)
            obj[child.Key] = BuildNode(child);
        return obj;
    }

    private static JsonNode? ToJsonValue(string? value)
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
