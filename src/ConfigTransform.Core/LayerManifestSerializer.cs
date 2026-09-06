using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConfigTransform.Core;

/// <summary>
/// The one JSON-writing convention for `configtransform.json`, shared by every creator of one --
/// <see cref="SetTargetResolver"/> (`set`) and <see cref="InitPlanner"/>/<see cref="InitTemplate"/>
/// (`init`) -- so they can never drift apart on indentation or null-handling.
/// </summary>
internal static class LayerManifestSerializer
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(LayerManifest manifest) => JsonSerializer.Serialize(manifest, WriteOptions);
}
