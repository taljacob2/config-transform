using System.Globalization;
using System.Text.Json.Nodes;

namespace ConfigTransform.Json;

/// <summary>
/// The JSON type of a value typed on the command line, which arrives as plain text — used by
/// <see cref="JsonFieldAuthor"/> (`set`) and <see cref="JsonElemMatchResolver"/>'s condition parsing,
/// so <c>--set true</c> writes a JSON <c>true</c> and <c>--match enabled=true</c> compares against
/// one. Never applied to values read from a file: those keep their own type.
///
/// A value becomes a number or boolean only when writing it that way reads back <i>exactly</i> as
/// typed: <c>5432</c>, <c>-12</c>, <c>1.5</c>, <c>true</c>, <c>false</c>. Anything else stays a
/// string — <c>02134</c> (a zip code), <c>007</c>, <c>1.10</c> (a version), <c>1e3</c>, <c>True</c>,
/// <c>NO</c>. The earlier rule (anything that <i>parses</i> as a number is one) turned <c>02134</c>
/// into <c>2134</c> and <c>1.10</c> into <c>1.1</c> — the same implicit-typing family as YAML's
/// "Norway problem" (docs/FIELD_AUTHORING_DESIGN.md, "Value typing").
/// </summary>
internal static class JsonCliValue
{
    public static JsonNode? From(string? value)
    {
        if (value is null)
            return null;

        if (value is "true" or "false")
            return JsonValue.Create(value == "true");

        if (long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var longValue) &&
            longValue.ToString(CultureInfo.InvariantCulture) == value)
            return JsonValue.Create(longValue);

        if (double.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var doubleValue) &&
            double.IsFinite(doubleValue) &&
            doubleValue.ToString("R", CultureInfo.InvariantCulture) == value)
            return JsonValue.Create(doubleValue);

        return JsonValue.Create(value);
    }
}
