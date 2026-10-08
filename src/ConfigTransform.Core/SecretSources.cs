namespace ConfigTransform.Core;

internal enum AnswerKind
{
    NotDefined,

    /// <summary><see cref="SourceAnswer.Text"/> names exactly where the value is (a file, or <c>keyvault://vault/secret</c>).</summary>
    Defines,

    /// <summary>Not defined, and <see cref="SourceAnswer.Text"/> says why a source holding the name doesn't count.</summary>
    Note,

    /// <summary>The source can't be read; <see cref="SourceAnswer.Text"/> says which and why.</summary>
    Unreadable,
}

internal readonly record struct SourceAnswer(AnswerKind Kind, string? Text)
{
    public static readonly SourceAnswer NotDefined = new(AnswerKind.NotDefined, null);
}

/// <summary>One entry of a layer's <c>secrets</c>, as <see cref="SecretSet"/> reads it. Never prints, logs or puts in an exception message a value.</summary>
internal abstract class SecretSource
{
    /// <summary>Whether <see cref="Load"/> does anything — only vault sources, which are read lazily.</summary>
    public virtual bool NeedsLoading => false;

    /// <summary>Reads what <see cref="Answer"/> needs. An unreadable source is recorded, not thrown; only a broken reference throws.</summary>
    public virtual void Load()
    {
    }

    public abstract SourceAnswer Answer(string name);

    /// <summary>The value; only called after <see cref="Answer"/> said <see cref="AnswerKind.Defines"/>.</summary>
    public abstract string ValueOf(string name);
}

/// <summary>A <c>*.secret.env</c> file, read when the chain is built. <c>null</c> values: still git-crypt encrypted.</summary>
internal sealed class FileSecretSource(string display, IReadOnlyDictionary<string, string>? values) : SecretSource
{
    public override SourceAnswer Answer(string name) =>
        values is null ? new SourceAnswer(AnswerKind.Unreadable, $"{display} is locked (run git-crypt unlock)")
        : values.ContainsKey(name) ? new SourceAnswer(AnswerKind.Defines, display)
        : SourceAnswer.NotDefined;

    public override string ValueOf(string name) => values![name];
}

/// <summary>What the vault sources share: reading a value, and turning a vault's answers into report text.</summary>
internal abstract class VaultSecretSource(IKeyVault vault, KeyVaultReference reference) : SecretSource
{
    protected IKeyVault Vault => vault;
    protected KeyVaultReference Reference => reference;

    /// <summary>Why the vault can't be read; null when it can (or hasn't been asked yet).</summary>
    protected string? Problem { get; set; }

    public override bool NeedsLoading => true;

    protected SourceAnswer Unreadable() => new(AnswerKind.Unreadable, $"{Reference} {Problem}");

    /// <summary>A secret that exists but doesn't count, and why — so "why isn't my secret used?" has an answer in the tree.</summary>
    protected static string? StateNote(KeyVaultSecretState state, string secretReference) => state switch
    {
        KeyVaultSecretState.Enabled => null,
        KeyVaultSecretState.Disabled => $"{secretReference} is disabled",
        KeyVaultSecretState.Expired => $"{secretReference} is expired",
        _ => $"{secretReference} is not yet valid",
    };

    /// <summary>One secret's value, for a real run or --reveal-secrets. Its metadata was readable, so a failure here is usually a role that can list but not read.</summary>
    protected string ReadValue(string secret)
    {
        var secretReference = new KeyVaultReference(Reference.Vault, secret);
        try
        {
            return Vault.GetSecretValue(Reference.Vault, secret).Value;
        }
        catch (KeyVaultUnavailableException ex)
        {
            throw new InvalidOperationException(
                $"The value of {secretReference} {ex.Message}. Its name was readable, so this is usually a role that can " +
                "list secrets but not read their values (Key Vault Reader).\n" +
                "Try: ask for Key Vault Secrets User on the vault, or on this secret.");
        }
    }

    protected static InvalidOperationException NoSuchSecret(string layer, KeyVaultReference secretReference) =>
        new($"{layer} lists '{secretReference}', but that vault has no secret named '{secretReference.Secret}'.\n" +
            $"Try: check the name, or create it: az keyvault secret set --vault-name {secretReference.Vault} --name {secretReference.Secret} --value <value>");
}

/// <summary><c>keyvault://kv</c>: every <c>CFSECRET-…</c> secret in the vault, one value each. Listing reads names only.</summary>
internal sealed class WholeVaultSource(IKeyVault vault, KeyVaultReference reference) : VaultSecretSource(vault, reference)
{
    private Dictionary<string, KeyVaultSecretInfo>? _secrets;
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    public override void Load()
    {
        try
        {
            // The CFSECRET- prefix is the opt-in -- vaults often hold other applications' secrets too,
            // and an unrelated DB-PASSWORD must never override a lower layer's value. It holds by
            // construction: a placeholder's name starts with CFSECRET_, so Find only ever looks up a
            // CFSECRET- name, and no other secret can match.
            _secrets = Vault.ListSecrets(Reference.Vault).ToDictionary(secret => secret.Name, StringComparer.OrdinalIgnoreCase);
        }
        catch (KeyVaultUnavailableException ex)
        {
            Problem = ex.Message;
        }
    }

    public override SourceAnswer Answer(string name)
    {
        if (Problem is not null)
            return Unreadable();
        if (Find(name) is not { } secret)
            return SourceAnswer.NotDefined;

        var secretReference = new KeyVaultReference(Reference.Vault, secret.Name).ToString();
        return StateNote(secret.State, secretReference) is { } note
            ? new SourceAnswer(AnswerKind.Note, note)
            : new SourceAnswer(AnswerKind.Defines, secretReference);
    }

    public override string ValueOf(string name)
    {
        var secret = Find(name)!.Name;
        lock (_values)
        {
            if (!_values.TryGetValue(secret, out var value))
                _values[secret] = value = ReadValue(secret);
            return value;
        }
    }

    // Key Vault names are case-insensitive, and the dictionary is too.
    private KeyVaultSecretInfo? Find(string name) =>
        _secrets!.GetValueOrDefault(name.Replace('_', '-'));
}

/// <summary><c>keyvault://kv/CFSECRET-X</c>, or any one secret with <c>as</c>: that secret, one value. Reads its metadata only, until a value is needed.</summary>
internal sealed class SingleSecretSource(IKeyVault vault, KeyVaultReference reference, string? asName, string layer)
    : VaultSecretSource(vault, reference)
{
    private KeyVaultSecretInfo? _info;
    private string? _value;

    public override void Load()
    {
        try
        {
            _info = Vault.GetSecretInfo(Reference.Vault, Reference.Secret!) ?? throw NoSuchSecret(layer, Reference);
        }
        catch (KeyVaultUnavailableException ex)
        {
            Problem = ex.Message;
        }
    }

    public override SourceAnswer Answer(string name)
    {
        // Only the one name this secret can hold: an unreadable single secret makes no other name uncertain.
        var holds = asName is not null
            ? string.Equals(name, asName, StringComparison.Ordinal)
            : KeyVaultReference.NamesMatch(name, Reference.Secret!);
        if (!holds)
            return SourceAnswer.NotDefined;
        if (Problem is not null)
            return Unreadable();

        return StateNote(_info!.State, Reference.ToString()) is { } note
            ? new SourceAnswer(AnswerKind.Note, note)
            : new SourceAnswer(AnswerKind.Defines, Reference.ToString());
    }

    public override string ValueOf(string name)
    {
        lock (this)
            return _value ??= ReadValue(Reference.Secret!);
    }
}

/// <summary>
/// <c>keyvault://kv/any-other-name</c>: one secret whose text is a <c>.env</c> file of
/// <c>CFSECRET_…=value</c> lines. Its names are inside its value, so it's read even for previews —
/// and never printed.
/// </summary>
internal sealed class EnvTextSecretSource(IKeyVault vault, KeyVaultReference reference, string layer)
    : VaultSecretSource(vault, reference)
{
    private Dictionary<string, string>? _values;
    private string? _stateNote;

    public override void Load()
    {
        try
        {
            var info = Vault.GetSecretInfo(Reference.Vault, Reference.Secret!) ?? throw NoSuchSecret(layer, Reference);
            _stateNote = StateNote(info.State, Reference.ToString());
            if (_stateNote is null)
                _values = Parse(Vault.GetSecretValue(Reference.Vault, Reference.Secret!).Value);
        }
        catch (KeyVaultUnavailableException ex)
        {
            Problem = ex.Message;
        }
    }

    public override SourceAnswer Answer(string name) =>
        Problem is not null ? Unreadable()
        : _stateNote is not null ? new SourceAnswer(AnswerKind.Note, _stateNote)
        : _values!.ContainsKey(name) ? new SourceAnswer(AnswerKind.Defines, Reference.ToString())
        : SourceAnswer.NotDefined;

    public override string ValueOf(string name) => _values![name];

    /// <summary>
    /// The same rules as a <c>*.secret.env</c> file, but no error quotes a line or a key: a secret
    /// named without the <c>CFSECRET-</c> prefix is read as <c>.env</c> text, so a single value given
    /// the wrong name lands here — and then any "line" or "key" could be part of that value.
    /// </summary>
    private Dictionary<string, string> Parse(string text)
    {
        List<KeyValuePair<string, string>> pairs;
        try
        {
            pairs = EnvFile.ParseAssignments(text);
        }
        catch (InvalidOperationException)
        {
            throw Invalid("isn't valid .env syntax");
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in pairs)
        {
            if (!key.StartsWith(SecretPlaceholders.Prefix, StringComparison.Ordinal))
                throw Invalid($"has a key that doesn't start with '{SecretPlaceholders.Prefix}'");
            if (!SecretPlaceholders.IsName(key))
                throw Invalid("has a key that isn't upper snake case (capital letters, digits and '_')");
            if (!values.TryAdd(key, value))
                throw Invalid("defines one name more than once");
        }

        return values;
    }

    private InvalidOperationException Invalid(string problem) =>
        new($"{layer} lists '{Reference}', which is read as .env text (its name has no '{KeyVaultReference.SecretPrefix}' prefix), " +
            $"but its value {problem}. Neither the line nor the key is shown, since if this secret holds a single value, " +
            "either could be part of it.\n" +
            $"Try: if it holds one value, name the secret {KeyVaultReference.SecretPrefix}... or give the entry an \"as\"; " +
            $"otherwise make every line {SecretPlaceholders.Prefix}NAME=value.");
}
