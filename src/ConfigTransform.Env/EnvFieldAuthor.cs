using ConfigTransform.Core;

namespace ConfigTransform.Env;

/// <summary>
/// Implements the `set` command (docs/FIELD_AUTHORING_DESIGN.md) for `.env`: the simplest of the
/// three formats' field authors, since a `.env` file is always flat -- there's no nested-path-vs-
/// literal-key collision the way JSON has (only one possible interpretation of a bare key name)
/// and no update-vs-insert distinction the way XML has (a `.env` overlay is just the subset of
/// keys it overrides, same shape whether the key already exists or not). <paramref
/// name="isBaseTarget"/> and <paramref name="precedingContent"/> genuinely don't change this
/// method's behavior at all -- a base write and an overlay write both just parse-or-start-empty,
/// set the key, and reserialize -- kept as parameters only to match the shared <c>FieldAuthor</c>
/// delegate shape in ConfigTransform.Core/FormatEngine.cs.
/// </summary>
public static class EnvFieldAuthor
{
    public static string Author(
        string precedingContent,
        string? existingTargetContent,
        bool isBaseTarget,
        IReadOnlyList<MatchSpec> matches,
        IReadOnlyList<MatchSpec> setFields)
    {
        if (matches.Count != 1 || matches[0].Attribute != "key")
            throw new InvalidOperationException(
                "'set' for .env needs exactly one --match key=<NAME> (bare shorthand, e.g. " +
                "--match FOO, defaults to key=FOO).");

        if (setFields.Count != 1 || setFields[0].Attribute != "value")
            throw new InvalidOperationException(
                "'set' for .env writes a single value -- use --set value=<new value>, or the " +
                "bare form (--set <value>), which defaults to it.");

        var key = matches[0].Value;
        EnvFile.ValidateKey(key);

        var pairs = existingTargetContent is null
            ? []
            : EnvFile.Parse(existingTargetContent);

        var order = pairs.Select(p => p.Key).ToList();
        var values = pairs.ToDictionary(p => p.Key, p => p.Value);

        if (!values.ContainsKey(key))
            order.Add(key);
        values[key] = setFields[0].Value;

        return EnvFile.Serialize(order.Select(k => new KeyValuePair<string, string>(k, values[k])));
    }
}
