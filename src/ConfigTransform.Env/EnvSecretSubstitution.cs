using ConfigTransform.Core;

namespace ConfigTransform.Env;

/// <summary>
/// Secret substitution for `.env` resources (docs/SECRETS_DESIGN.md): <c>{{CFSECRET_…}}</c>
/// placeholders are replaced inside values, and the file is written back through
/// <see cref="EnvFile"/>, the same as <see cref="EnvLayerMerger"/> writes it. The `.env` grammar has
/// no escape sequences, so a value that wouldn't read back unchanged — one containing a line break,
/// or one that starts and ends with the same quote character — is refused rather than written
/// corrupted. The error names the key, never the value.
/// </summary>
public static class EnvSecretSubstitution
{
    public static string Substitute(string content, Func<string, string?> resolve)
    {
        if (!SecretPlaceholders.ContainsMarker(content))
            return content;

        var pairs = EnvFile.Parse(content).Select(pair =>
        {
            var substituted = SecretPlaceholders.Replace(pair.Value, resolve);
            if (substituted != pair.Value)
                EnsureRoundTrips(pair.Key, substituted);
            return new KeyValuePair<string, string>(pair.Key, substituted);
        }).ToList();

        // Same as every other engine's substitution: content comes back in its own line-ending layout.
        return TextLayout.Of(content).Apply(EnvFile.Serialize(pairs));
    }

    private static void EnsureRoundTrips(string key, string value)
    {
        // Line breaks are checked up front, and EnvFile's own parse errors are never passed on:
        // they quote the offending line, which here would be part of the secret.
        bool roundTrips;
        try
        {
            var readBack = value.Contains('\n') || value.Contains('\r')
                ? []
                : EnvFile.Parse(EnvFile.Serialize([new(key, value)]));
            roundTrips = readBack.Count == 1 && readBack[0].Value == value;
        }
        catch (InvalidOperationException)
        {
            roundTrips = false;
        }

        if (!roundTrips)
            throw new InvalidOperationException(
                $"The secret substituted into \"{key}\" can't be written to a .env file unchanged -- the .env grammar " +
                "has no escape sequences, so a value containing a line break, or one that starts and ends with the " +
                "same quote character, wouldn't read back the same.");
    }
}
