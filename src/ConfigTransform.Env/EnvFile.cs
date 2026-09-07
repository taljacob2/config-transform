using System.Text;
using System.Text.RegularExpressions;

namespace ConfigTransform.Env;

/// <summary>
/// The `.env` grammar this tool parses and writes, shared by <see cref="EnvLayerMerger"/> and
/// <see cref="EnvFieldAuthor"/> (mirrors why <c>JsonLayerMerger.ToJsonValue</c> is <c>internal</c>
/// rather than private -- one place decides the format's rules, not two). There is no formal
/// `.env` spec; real tooling disagrees on edge cases, so these rules are picked deliberately --
/// see docs/CONFIG_MANAGEMENT.md's `.env` section and docs/FIELD_AUTHORING_DESIGN.md's decision
/// log for the reasoning behind each one:
///
/// - Blank lines and whole-line `#` comments are dropped on parse and never reappear on
///   serialize -- this matches JSON's own existing behavior (`Microsoft.Extensions.
///   Configuration`'s JSON provider already drops comments/formatting on rebuild), not a new gap.
/// - An optional leading `export ` is stripped before parsing the key (Bash-sourceable files are
///   a common real `.env` convention, e.g. `direnv`/Docker `env_file`).
/// - A key must match the real POSIX env-var-name grammar (`[A-Za-z_][A-Za-z0-9_]*`) -- a `.env`
///   file with an invalid key can never actually be `source`d, so this tool refuses to write one
///   rather than silently producing something unusable.
/// - A value wrapped in matching `"`/`'` has the quotes stripped, with no escape-sequence
///   processing and no `${VAR}` expansion -- real `.env` tooling disagrees wildly here, so a
///   value is treated as opaque text, the one unambiguous choice.
/// - No inline (same-line trailing) comment stripping -- only a whole-line `#` is a comment, to
///   avoid the real ambiguity of a value like `PASSWORD=abc#123`.
/// - On serialize, a value is quoted only when it contains whitespace or `#`, or is empty, so the
///   common case stays readable as plain `KEY=value`.
/// </summary>
public static class EnvFile
{
    private static readonly Regex KeyPattern = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    public static void ValidateKey(string key)
    {
        if (!KeyPattern.IsMatch(key))
            throw new InvalidOperationException(
                $"\"{key}\" is not a valid .env key -- keys must match [A-Za-z_][A-Za-z0-9_]* " +
                "(the same grammar a real shell requires to 'source' the file).");
    }

    /// <returns>Key/value pairs in first-seen order (later duplicate keys within the same
    /// content overwrite the earlier value in place, matching how a real shell sourcing the same
    /// file would leave only the last assignment in effect).</returns>
    public static List<KeyValuePair<string, string>> Parse(string content)
    {
        var order = new List<string>();
        var values = new Dictionary<string, string>();

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            var withoutExport = line.StartsWith("export ", StringComparison.Ordinal)
                ? line["export ".Length..].TrimStart()
                : line;

            var eq = withoutExport.IndexOf('=');
            if (eq < 0)
                throw new InvalidOperationException($"Malformed .env line (no '='): \"{line}\"");

            var key = withoutExport[..eq].Trim();
            ValidateKey(key);

            if (!values.ContainsKey(key))
                order.Add(key);
            values[key] = Unquote(withoutExport[(eq + 1)..].Trim());
        }

        return order.Select(key => new KeyValuePair<string, string>(key, values[key])).ToList();
    }

    public static string Serialize(IEnumerable<KeyValuePair<string, string>> pairs)
    {
        var sb = new StringBuilder();
        foreach (var pair in pairs)
            sb.Append(pair.Key).Append('=').Append(QuoteIfNeeded(pair.Value)).Append('\n');
        return sb.ToString();
    }

    private static string Unquote(string raw) =>
        raw.Length >= 2 && ((raw[0] == '"' && raw[^1] == '"') || (raw[0] == '\'' && raw[^1] == '\''))
            ? raw[1..^1]
            : raw;

    private static string QuoteIfNeeded(string value) =>
        value.Length == 0 || value.Any(c => char.IsWhiteSpace(c) || c == '#')
            ? $"\"{value}\""
            : value;
}
