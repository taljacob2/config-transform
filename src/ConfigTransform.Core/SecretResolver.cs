using System.Runtime.ExceptionServices;

namespace ConfigTransform.Core;

public enum SecretState
{
    /// <summary>A value exists; <see cref="SecretStatus.Source"/> says where it came from.</summary>
    Resolved,

    /// <summary>Every source in the chain is readable, and none defines this name.</summary>
    Missing,

    /// <summary>A source that could define (or override) this name can't be read — a git-crypt-locked file, or a vault the caller can't reach.</summary>
    Unknown,
}

/// <summary>One placeholder name's resolution — never its value; that's <see cref="SecretSet.ValueOf"/>, asked for only when substituting.</summary>
public sealed record SecretStatus(string Name, SecretState State, string? Source);

/// <summary>
/// One layer's say on one secret name, for the report's per-secret tree: the source that sets it
/// (at most one — two in one layer is an error), every unreadable source that might, and notes on
/// why a source holding the name doesn't count (a disabled vault secret). Repo-relative paths and
/// <c>keyvault://</c> references only.
/// </summary>
public sealed record SecretLayerStep(
    string Layer, IReadOnlyList<string> PatchedIn, IReadOnlyList<string> Unknown, IReadOnlyList<string> Notes);

public enum SecretVariableState
{
    Unset,

    /// <summary>Set to an empty string, which counts as unset (see <see cref="SecretSet.Lookup"/>).</summary>
    Empty,

    Set,
}

/// <summary>Everywhere one secret name's value could come from, in precedence order: every layer of the chain, outermost first, then the environment variable.</summary>
public sealed record SecretTrace(IReadOnlyList<SecretLayerStep> Layers, SecretVariableState Variable);

/// <summary>
/// The secret values one layer chain resolves to (docs/SECRETS_DESIGN.md): every source every layer
/// lists under <c>secrets</c> — <c>*.secret.env</c> files and Azure Key Vault sources
/// (docs/KEYVAULT_SECRETS_DESIGN.md) — outermost layer first, later layers overriding earlier ones
/// name by name, the same precedence patches have, and an environment variable of the same name
/// overriding every source.
///
/// Files are read when the set is built. Vault sources are read on the first lookup, all in
/// parallel — so a run whose resources use no placeholder never contacts Azure.
/// </summary>
public sealed class SecretSet
{
    private readonly IReadOnlyList<LayerSources> _layers;
    private readonly Func<string, string?> _environment;
    private readonly object _loadLock = new();
    private bool _loaded;
    private ExceptionDispatchInfo? _loadError;

    internal SecretSet(IReadOnlyList<LayerSources> layers, Func<string, string?> environment)
    {
        _layers = layers;
        _environment = environment;
    }

    internal sealed record LayerSources(string Layer, IReadOnlyList<SecretSource> Sources);

    public SecretStatus Lookup(string name)
    {
        // An empty variable counts as unset: a GitHub Actions expression looking up a secret that
        // doesn't exist evaluates to "", and a typo there must not deploy an empty password.
        if (!string.IsNullOrEmpty(_environment(name)))
            return new SecretStatus(name, SecretState.Resolved, "environment variable");

        var (state, source, _) = Resolve(name);
        return new SecretStatus(name, state, source);
    }

    /// <summary>The value <paramref name="name"/> resolves to, or null when it doesn't resolve. Reads a vault value only here — for a real run or <c>--reveal-secrets</c>.</summary>
    public string? ValueOf(string name)
    {
        var fromEnvironment = _environment(name);
        if (!string.IsNullOrEmpty(fromEnvironment))
            return fromEnvironment;

        var (state, _, winner) = Resolve(name);
        return state == SecretState.Resolved ? winner!.ValueOf(name) : null;
    }

    /// <summary>Where <paramref name="name"/>'s value could come from, step by step -- never the value itself.</summary>
    public SecretTrace Trace(string name)
    {
        EnsureLoaded();

        var layers = _layers
            .Select(layer =>
            {
                var answers = Answers(layer, name);
                return new SecretLayerStep(
                    layer.Layer,
                    TextsOf(answers, AnswerKind.Defines),
                    TextsOf(answers, AnswerKind.Unreadable),
                    TextsOf(answers, AnswerKind.Note));
            })
            .ToList();

        var variable = _environment(name) switch
        {
            null => SecretVariableState.Unset,
            "" => SecretVariableState.Empty,
            _ => SecretVariableState.Set,
        };

        return new SecretTrace(layers, variable);
    }

    private (SecretState State, string? Source, SecretSource? Winner) Resolve(string name)
    {
        EnsureLoaded();

        var state = SecretState.Missing;
        string? source = null;
        SecretSource? winner = null;

        foreach (var layer in _layers)
        {
            var answers = Answers(layer, name);
            var unreadable = TextsOf(answers, AnswerKind.Unreadable);
            var defining = answers.FirstOrDefault(a => a.Answer.Kind == AnswerKind.Defines);

            // An unreadable source in a layer could define this name -- or redefine it -- so whatever
            // an earlier layer said is no longer certain. Only a later, readable layer settles it again.
            if (unreadable.Count > 0)
                (state, source, winner) = (SecretState.Unknown, string.Join(", ", unreadable), null);
            else if (defining.Source is not null)
                (state, source, winner) = (SecretState.Resolved, defining.Answer.Text, defining.Source);
        }

        return (state, source, winner);
    }

    /// <summary>Every source's answer for <paramref name="name"/> in one layer; two sources defining it is an error, since sources in one layer have no order.</summary>
    private static List<(SecretSource Source, SourceAnswer Answer)> Answers(LayerSources layer, string name)
    {
        var answers = layer.Sources.Select(source => (source, source.Answer(name))).ToList();
        var defining = answers.Where(a => a.Item2.Kind == AnswerKind.Defines).ToList();
        if (defining.Count > 1)
            throw new InvalidOperationException(
                $"{layer.Layer} gets \"{name}\" from two sources, '{defining[0].Item2.Text}' and '{defining[1].Item2.Text}' -- " +
                "sources in the same layer have no order, so neither can win.\nTry: keep it in one of them.");
        return answers;
    }

    private static List<string> TextsOf(List<(SecretSource Source, SourceAnswer Answer)> answers, AnswerKind kind) =>
        answers.Where(a => a.Answer.Kind == kind).Select(a => a.Answer.Text!).ToList();

    /// <summary>Reads every vault source once, in parallel. A hard error (a named secret that doesn't exist) is rethrown on every later call too.</summary>
    private void EnsureLoaded()
    {
        lock (_loadLock)
        {
            if (!_loaded)
            {
                var sources = _layers.SelectMany(layer => layer.Sources).Where(source => source.NeedsLoading).ToList();
                var errors = new Exception?[sources.Count];
                Parallel.For(0, sources.Count, i =>
                {
                    try
                    {
                        sources[i].Load();
                    }
                    catch (Exception ex)
                    {
                        errors[i] = ex;
                    }
                });

                // The first error in chain order, whichever thread hit it first -- the same message every run.
                if (errors.FirstOrDefault(error => error is not null) is { } first)
                    _loadError = ExceptionDispatchInfo.Capture(first);
                _loaded = true;
            }

            _loadError?.Throw();
        }
    }
}

public static class SecretResolver
{
    /// <param name="keyVault">Reads <c>keyvault://</c> sources; null when the host has no Key Vault support, which makes a layer that lists one an error.</param>
    /// <exception cref="FileNotFoundException">A layer lists a secrets file that doesn't exist.</exception>
    /// <exception cref="InvalidOperationException">
    /// A secrets file has a key without the <c>CFSECRET_</c> prefix, or two secrets files of the same
    /// layer define the same name (no order between them decides which wins).
    /// </exception>
    public static SecretSet Build(
        string root, IReadOnlyList<ResolvedLayer> chain, Func<string, string?> environment, IKeyVault? keyVault = null)
    {
        var layers = new List<SecretSet.LayerSources>();

        foreach (var layer in chain)
        {
            var label = LayerChain.ToRepoRelative(root, layer.Path);
            var sources = new List<SecretSource>();
            var definedByFiles = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var entry in layer.Manifest.Secrets ?? [])
            {
                if (KeyVaultReference.IsKeyVault(entry.Source))
                {
                    var reference = KeyVaultReference.Parse(entry.Source);
                    var vault = keyVault ?? throw new InvalidOperationException(
                        $"{label} lists '{entry}', but this build of configtransform has no Azure Key Vault support.");
                    sources.Add(reference.KindWith(entry.As) switch
                    {
                        KeyVaultSourceKind.WholeVault => new WholeVaultSource(vault, reference),
                        KeyVaultSourceKind.SingleSecret => new SingleSecretSource(vault, reference, entry.As, label),
                        _ => new EnvTextSecretSource(vault, reference, label),
                    });
                    continue;
                }

                sources.Add(ReadFile(root, label, entry.Source, definedByFiles));
            }

            layers.Add(new SecretSet.LayerSources(label, sources));
        }

        return new SecretSet(layers, environment);
    }

    private static FileSecretSource ReadFile(string root, string label, string entry, Dictionary<string, string> definedByFiles)
    {
        var fullPath = Path.GetFullPath(entry, root);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException(
                $"{label} lists secrets file '{entry}', but no file exists at '{fullPath}'.");

        var display = LayerChain.ToRepoRelative(root, fullPath);
        if (GitCrypt.IsLocked(fullPath))
            return new FileSecretSource(display, values: null);

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in ParseWithoutLeaking(fullPath, display))
        {
            // Shell semantics (last assignment wins) are right for a .env resource, but in a
            // secrets file a repeated name is almost always a copy-paste mistake -- and which
            // value won would only surface when something failed in production.
            if (values.ContainsKey(key))
                throw new InvalidOperationException(
                    $"'{display}' defines \"{key}\" more than once.\nTry: keep one definition.");

            if (!key.StartsWith(SecretPlaceholders.Prefix, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"'{display}' defines \"{key}\", but every secret name must start with '{SecretPlaceholders.Prefix}' " +
                    $"-- it's the same name a placeholder uses ({{{{{SecretPlaceholders.Prefix}...}}}}), so without the prefix " +
                    $"it could never be used.\nTry: rename it to {SecretPlaceholders.Prefix}{key}.");

            if (!SecretPlaceholders.IsName(key))
                throw new InvalidOperationException(
                    $"'{display}' defines \"{key}\", but a secret's name is upper snake case -- capital letters, digits and '_' -- " +
                    "so that it matches the same way in a *.secret.env file, an environment variable on every OS, and Key Vault " +
                    $"(docs/SECRETS_DESIGN.md).\nTry: rename it to {key.ToUpperInvariant()}.");

            if (definedByFiles.TryGetValue(key, out var earlier))
                throw new InvalidOperationException(
                    $"{label} defines \"{key}\" in two secrets files, '{earlier}' and '{display}' -- " +
                    "files in the same layer have no order, so neither can win.\nTry: keep it in one of them.");

            values[key] = value;
            definedByFiles[key] = display;
        }

        return new FileSecretSource(display, values);
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
