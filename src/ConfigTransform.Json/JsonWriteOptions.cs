using System.Text.Encodings.Web;
using System.Text.Json;

namespace ConfigTransform.Json;

/// <summary>
/// The one set of serializer options every JSON this library writes goes through — merged output,
/// <c>set</c>'s overlay/base writes, and error messages that quote JSON back to the user.
/// System.Text.Json's default encoder escapes every non-ASCII character and the HTML-sensitive
/// <c>&lt; &gt; &amp; ' +</c>, so a password <c>a+b</c> became <c>a\u002Bb</c> and a Hebrew value became
/// a run of <c>\u05XX</c> escapes — still valid JSON, but unreadable in a deployed file or a
/// <c>--diff</c>. <see cref="JavaScriptEncoder.UnsafeRelaxedJsonEscaping"/> writes them literally.
/// Its "unsafe" is about embedding the output in HTML/script, which config files never are; it
/// still escapes <c>"</c>, <c>\</c> and control characters. One remaining quirk is
/// System.Text.Json's own: a character outside the Basic Multilingual Plane (an emoji, say) is
/// always written as an escaped surrogate pair, whatever encoder is used.
/// </summary>
internal static class JsonWriteOptions
{
    public static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static readonly JsonSerializerOptions Compact = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
