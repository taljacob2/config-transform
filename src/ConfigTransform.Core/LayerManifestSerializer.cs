using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConfigTransform.Core;

/// <summary>
/// The one JSON-writing convention for `configtransform.json`, shared by every creator of one --
/// <see cref="SetTargetResolver"/> (`set`) and <see cref="InitPlanner"/>/<see cref="InitTemplate"/>
/// (`init`) -- so they can never drift apart on indentation, null-handling, or escaping. Writes
/// non-ASCII and `&lt; &gt; &amp; ' +` literally rather than as `\uXXXX` escapes, so a client or
/// environment name like `Acme & Co` or a non-Latin path stays readable in `extends`/`path`/`patch`
/// (same reasoning as ConfigTransform.Json's own JsonWriteOptions).
/// </summary>
internal static class LayerManifestSerializer
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <param name="layout">
    /// The layout of the file being rewritten, so it keeps its own line endings and final newline;
    /// <see cref="TextLayout.Default"/> (LF, final newline) for a brand-new file.
    /// </param>
    public static string Serialize(LayerManifest manifest, TextLayout? layout = null) =>
        (layout ?? TextLayout.Default).Apply(JsonSerializer.Serialize(manifest, WriteOptions));
}
