namespace ConfigTransform.Core;

public enum SecretState
{
    /// <summary>A value exists; <see cref="SecretStatus.Source"/> says where it came from.</summary>
    Resolved,

    /// <summary>Every secrets file in the chain is readable, and none defines this name.</summary>
    Missing,

    /// <summary>A secrets file that could define (or override) this name is still git-crypt encrypted.</summary>
    Unknown,
}

/// <summary>One placeholder name's resolution. <see cref="Value"/> is set only when <see cref="State"/> is <see cref="SecretState.Resolved"/>, and is never printed.</summary>
public sealed record SecretStatus(string Name, SecretState State, string? Source, string? Value);

/// <summary>
/// The secret values one layer chain resolves to (docs/SECRETS_DESIGN.md): every <c>*.secret.env</c>
/// file every layer lists under <c>secrets</c>, outermost layer first, later layers overriding
/// earlier ones name by name — the same precedence patches have — and an environment variable of
/// the same name overriding every file.
/// </summary>
public sealed class SecretSet
{
    private readonly IReadOnlyList<LayerSecrets> _layers;
    private readonly Func<string, string?> _environment;

    internal SecretSet(IReadOnlyList<LayerSecrets> layers, Func<string, string?> environment)
    {
        _layers = layers;
        _environment = environment;
    }

    /// <summary>A layer's secrets: the names its readable files define, and its still-encrypted files.</summary>
    internal sealed record LayerSecrets(IReadOnlyDictionary<string, (string Value, string File)> Defined, IReadOnlyList<string> LockedFiles);

    public SecretStatus Lookup(string name)
    {
        // An empty variable counts as unset: a GitHub Actions expression looking up a secret that
        // doesn't exist evaluates to "", and a typo there must not deploy an empty password.
        var fromEnvironment = _environment(name);
        if (!string.IsNullOrEmpty(fromEnvironment))
            return new SecretStatus(name, SecretState.Resolved, "environment variable", fromEnvironment);

        var status = new SecretStatus(name, SecretState.Missing, null, null);
        foreach (var layer in _layers)
        {
            // A locked file in a layer could define this name -- or redefine it -- so whatever an
            // earlier layer said is no longer certain. Only a later, readable layer settles it again.
            if (layer.LockedFiles.Count > 0)
                status = new SecretStatus(name, SecretState.Unknown, string.Join(", ", layer.LockedFiles), null);
            else if (layer.Defined.TryGetValue(name, out var defined))
                status = new SecretStatus(name, SecretState.Resolved, defined.File, defined.Value);
        }

        return status;
    }
}

public static class SecretResolver
{
    /// <exception cref="FileNotFoundException">A layer lists a secrets file that doesn't exist.</exception>
    /// <exception cref="InvalidOperationException">
    /// A secrets file has a key without the <c>CFSECRET_</c> prefix, or two secrets files of the same
    /// layer define the same name (no order between them decides which wins).
    /// </exception>
    public static SecretSet Build(string root, IReadOnlyList<ResolvedLayer> chain, Func<string, string?> environment)
    {
        var layers = new List<SecretSet.LayerSecrets>();

        foreach (var layer in chain)
        {
            var label = LayerChain.ToRepoRelative(root, layer.Path);
            var defined = new Dictionary<string, (string Value, string File)>(StringComparer.Ordinal);
            var locked = new List<string>();

            foreach (var entry in layer.Manifest.Secrets ?? [])
            {
                var fullPath = Path.GetFullPath(entry, root);
                if (!File.Exists(fullPath))
                    throw new FileNotFoundException(
                        $"{label} lists secrets file '{entry}', but no file exists at '{fullPath}'.");

                var display = LayerChain.ToRepoRelative(root, fullPath);
                if (GitCrypt.IsLocked(fullPath))
                {
                    locked.Add($"{display} is locked (run git-crypt unlock)");
                    continue;
                }

                var seenInThisFile = new HashSet<string>(StringComparer.Ordinal);
                foreach (var (key, value) in ParseWithoutLeaking(fullPath, display))
                {
                    // Shell semantics (last assignment wins) are right for a .env resource, but in a
                    // secrets file a repeated name is almost always a copy-paste mistake -- and which
                    // value won would only surface when something failed in production.
                    if (!seenInThisFile.Add(key))
                        throw new InvalidOperationException(
                            $"'{display}' defines \"{key}\" more than once.\nTry: keep one definition.");

                    if (!key.StartsWith(SecretPlaceholders.Prefix, StringComparison.Ordinal))
                        throw new InvalidOperationException(
                            $"'{display}' defines \"{key}\", but every secret name must start with '{SecretPlaceholders.Prefix}' " +
                            $"-- it's the same name a placeholder uses ({{{{{SecretPlaceholders.Prefix}...}}}}), so without the prefix " +
                            $"it could never be used.\nTry: rename it to {SecretPlaceholders.Prefix}{key}.");

                    if (defined.TryGetValue(key, out var earlier))
                        throw new InvalidOperationException(
                            $"{label} defines \"{key}\" in two secrets files, '{earlier.File}' and '{display}' -- " +
                            "files in the same layer have no order, so neither can win.\nTry: keep it in one of them.");

                    defined[key] = (value, display);
                }
            }

            layers.Add(new SecretSet.LayerSecrets(defined, locked));
        }

        return new SecretSet(layers, environment);
    }

    /// <summary>
    /// <see cref="EnvFile.Parse"/>'s own errors quote the offending line -- in a secrets file, that
    /// line may well be a secret. Name the file and the rule instead, never the content.
    /// </summary>
    private static List<KeyValuePair<string, string>> ParseWithoutLeaking(string fullPath, string display)
    {
        try
        {
            return EnvFile.ParseAssignments(File.ReadAllText(fullPath));
        }
        catch (InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"'{display}' isn't valid .env syntax: every line must be blank, a '#' comment, or NAME=value with " +
                "NAME matching [A-Za-z_][A-Za-z0-9_]*. (The offending line isn't shown, since it may contain a secret.)");
        }
    }
}
