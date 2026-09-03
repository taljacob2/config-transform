using System.Globalization;
using System.Text;
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
///
/// A layer containing <c>$elemMatch</c>-shaped array-of-objects overlays
/// (docs/FIELD_AUTHORING_DESIGN.md; <see cref="JsonElemMatchResolver"/>) needs a pre-processing
/// pass -- resolving each patch to a real position by inspecting the actual document -- before
/// its content can be handed to Microsoft.Extensions.Configuration at all, since that library
/// has no concept of matching an array element by a field's value, only by index. That
/// resolution has to be progressive: an Environment-layer patch resolves against the base array,
/// a Client-layer patch against the base+Environment-*merged* array (mirroring how
/// <c>XmlLayerMerger</c> already applies the Client transform to the already-Environment-
/// transformed document, not to the base alone). <see cref="LegacyMerge"/> is the original,
/// untouched implementation, kept verbatim as the fast path for the overwhelming common case (no
/// layer uses <c>$elemMatch</c> at all) -- every existing merge behavior keeps running through
/// the exact code that was already tested, unchanged.
/// </summary>
public static class JsonLayerMerger
{
    public static string Merge(string basePath, string? environmentOverlayPath, string? clientOverlayPath)
    {
        var envTree = ReadIfExists(environmentOverlayPath);
        var clientTree = ReadIfExists(clientOverlayPath);
        var envNeedsRewrite = envTree is not null && JsonElemMatchResolver.ContainsElemMatch(envTree);
        var clientNeedsRewrite = clientTree is not null && JsonElemMatchResolver.ContainsElemMatch(clientTree);

        if (!envNeedsRewrite && !clientNeedsRewrite)
            return LegacyMerge(basePath, environmentOverlayPath, clientOverlayPath);

        var baseTree = JsonNode.Parse(File.ReadAllText(basePath));

        var envLayer = envNeedsRewrite
            ? LayerInput.FromNode(JsonElemMatchResolver.Rewrite(envTree, baseTree))
            : LayerInput.FromPath(environmentOverlayPath);

        // Only worth computing the real base+Environment-merged state when the Client layer
        // actually needs to resolve $elemMatch patches against it -- otherwise the Client layer
        // (a plain path, or absent) merges normally on the final pass below with no pre-pass
        // needed.
        JsonNode? afterEnvTree = null;
        if (clientNeedsRewrite)
            afterEnvTree = JsonNode.Parse(BuildMerge(basePath, envLayer, LayerInput.FromPath(null)));

        var clientLayer = clientNeedsRewrite
            ? LayerInput.FromNode(JsonElemMatchResolver.Rewrite(clientTree, afterEnvTree))
            : LayerInput.FromPath(clientOverlayPath);

        return BuildMerge(basePath, envLayer, clientLayer);
    }

    private static JsonNode? ReadIfExists(string? path) =>
        path is not null && File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) : null;

    private readonly record struct LayerInput(string? Path, JsonNode? RewrittenNode)
    {
        public static LayerInput FromPath(string? path) => new(path, null);
        public static LayerInput FromNode(JsonNode? node) => new(null, node);
    }

    private static string BuildMerge(string basePath, LayerInput env, LayerInput client)
    {
        var builder = new ConfigurationBuilder().AddJsonFile(basePath, optional: false);
        AddLayer(builder, env);
        AddLayer(builder, client);

        var configuration = builder.Build();

        var root = new JsonObject();
        foreach (var child in configuration.GetChildren())
            root[child.Key] = BuildNode(child);

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static void AddLayer(IConfigurationBuilder builder, LayerInput layer)
    {
        if (layer.RewrittenNode is not null)
            builder.AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(layer.RewrittenNode.ToJsonString())));
        else if (layer.Path is not null)
            builder.AddJsonFile(layer.Path, optional: true);
    }

    /// <summary>The original implementation, kept verbatim as the fast path used whenever
    /// neither overlay layer contains <c>$elemMatch</c> anywhere -- see the class remarks.</summary>
    private static string LegacyMerge(string basePath, string? environmentOverlayPath, string? clientOverlayPath)
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

    /// <summary>Internal, not private: reused by <see cref="JsonFieldAuthor"/> so a value <c>set</c> writes gets the exact same bool/integer/float/string type inference a merge would give it.</summary>
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
