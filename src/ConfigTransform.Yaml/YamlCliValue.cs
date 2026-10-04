using System.Globalization;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace ConfigTransform.Yaml;

/// <summary>
/// The YAML scalar for a value typed on the command line, which arrives as plain text — used by
/// <see cref="YamlFieldAuthor"/> (`set`). Ported from, not shared with, <c>ConfigTransform.Json</c>'s
/// <c>JsonCliValue</c> (CLAUDE.md's "Repo structure").
///
/// A value becomes a number or boolean only when writing it that way reads back <i>exactly</i> as
/// typed: <c>5432</c>, <c>-12</c>, <c>1.5</c>, <c>true</c>, <c>false</c> — written plain. Every
/// other value is a string and is <b>always written double-quoted</b>. YAML 1.1 parsers (PyYAML,
/// among others) read a long tail of unquoted text as something else — <c>NO</c> as a boolean
/// ("the Norway problem"), <c>yes</c>/<c>on</c>/<c>y</c>, <c>~</c>/<c>null</c> as null,
/// <c>0123</c> as octal, <c>1:20</c> as the base-60 number 80 — and quoting every string the tool
/// writes is correct for every reader with no list of risky forms to keep complete. Before this,
/// <c>--set NO</c> wrote an unquoted <c>NO</c>, <c>--set null</c> wrote a real null, and
/// <c>--set 02134</c> wrote <c>2134</c> (docs/FIELD_AUTHORING_DESIGN.md, "Value typing").
/// </summary>
internal static class YamlCliValue
{
    public static YamlScalarNode From(string value)
    {
        var isCanonicalScalar =
            value is "true" or "false" ||
            (long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var longValue) &&
             longValue.ToString(CultureInfo.InvariantCulture) == value) ||
            (double.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var doubleValue) &&
             double.IsFinite(doubleValue) &&
             doubleValue.ToString("R", CultureInfo.InvariantCulture) == value);

        return new YamlScalarNode(value) { Style = isCanonicalScalar ? ScalarStyle.Plain : ScalarStyle.DoubleQuoted };
    }
}
