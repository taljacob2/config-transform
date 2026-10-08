using ConfigTransform.Core;

namespace ConfigTransform.Cli.Tests.TestSupport;

/// <summary>
/// An in-memory <see cref="IKeyVault"/> for tests: vaults of secrets with a state and content type,
/// vaults that can't be read, and values that can't be read. Counts every call, so tests can prove
/// what the tool did NOT do — contact a vault, or read a value during a preview.
/// </summary>
internal sealed class FakeKeyVault : IKeyVault
{
    private sealed record Secret(string Name, string Value, KeyVaultSecretState State, string? ContentType);

    private readonly Dictionary<string, Dictionary<string, Secret>> _vaults = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _unreadableVaults = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _unreadableValues = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _valuesRead = [];
    private int _calls;

    /// <summary>Every call to any method, whatever it returned.</summary>
    public int Calls => _calls;

    public int ListCalls { get; private set; }

    /// <summary>"vault/secret" for every value read, in order.</summary>
    public IReadOnlyList<string> ValuesRead
    {
        get
        {
            lock (_valuesRead)
                return _valuesRead.ToList();
        }
    }

    public FakeKeyVault Add(string vault, string name, string value,
        KeyVaultSecretState state = KeyVaultSecretState.Enabled, string? contentType = null)
    {
        if (!_vaults.TryGetValue(vault, out var secrets))
            _vaults[vault] = secrets = new Dictionary<string, Secret>(StringComparer.OrdinalIgnoreCase);
        secrets[name] = new Secret(name, value, state, contentType);
        return this;
    }

    public FakeKeyVault Unreadable(string vault, string problem)
    {
        _unreadableVaults[vault] = problem;
        return this;
    }

    public FakeKeyVault ValueUnreadable(string vault, string secret, string problem)
    {
        _unreadableValues[$"{vault}/{secret}"] = problem;
        return this;
    }

    public IReadOnlyList<KeyVaultSecretInfo> ListSecrets(string vault)
    {
        Interlocked.Increment(ref _calls);
        lock (this)
            ListCalls++;
        return Vault(vault).Values.Select(s => new KeyVaultSecretInfo(s.Name, s.State)).ToList();
    }

    public KeyVaultSecretInfo? GetSecretInfo(string vault, string secret)
    {
        Interlocked.Increment(ref _calls);
        return Vault(vault).TryGetValue(secret, out var s) ? new KeyVaultSecretInfo(s.Name, s.State) : null;
    }

    public KeyVaultSecretValue GetSecretValue(string vault, string secret)
    {
        Interlocked.Increment(ref _calls);
        lock (_valuesRead)
            _valuesRead.Add($"{vault}/{secret}");

        var secrets = Vault(vault);
        if (_unreadableValues.TryGetValue($"{vault}/{secret}", out var problem))
            throw new KeyVaultUnavailableException(problem);
        if (!secrets.TryGetValue(secret, out var s))
            throw new KeyVaultUnavailableException("can't be read (404 SecretNotFound)");
        return new KeyVaultSecretValue(s.Value, s.ContentType);
    }

    private Dictionary<string, Secret> Vault(string vault)
    {
        if (_unreadableVaults.TryGetValue(vault, out var problem))
            throw new KeyVaultUnavailableException(problem);
        return _vaults.TryGetValue(vault, out var secrets)
            ? secrets
            : throw new KeyVaultUnavailableException("can't be reached (no such vault, or no network)");
    }
}
