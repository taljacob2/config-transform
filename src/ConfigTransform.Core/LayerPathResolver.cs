namespace ConfigTransform.Core;

/// <summary>
/// Resolves --client/--environment to a configtransform.json path under .configtransform/
/// (docs/SELF_DESCRIBING_OVERLAYS_DESIGN.md "Settled decisions" #6/#7) — replaces
/// <c>ManifestDiscovery</c>'s old multi-candidate disambiguation, which no longer applies: with
/// <c>manifest.json</c> gone there is nothing left to be ambiguous between. Given
/// client/environment (or neither, for a base target) the path is fully determined, so this is
/// pure path arithmetic, not filesystem discovery — the returned path need not exist yet (a
/// caller reading an existing tree treats a missing file as "nothing configured at this layer",
/// same as a missing overlay always was; <c>set</c> creates it).
/// </summary>
public static class LayerPathResolver
{
    private const string RootFolderName = ".configtransform";
    private const string LayerManifestFileName = "configtransform.json";

    /// <returns>
    /// <c>.configtransform/Clients/&lt;client&gt;/&lt;environment&gt;/configtransform.json</c> when
    /// both are given, <c>.configtransform/Environments/&lt;environment&gt;/configtransform.json</c>
    /// with only <paramref name="environment"/>, or null with neither (a base-target: there is no
    /// layer at all, only the raw base file).
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="client"/> is set without <paramref name="environment"/> — there is no client-only layer.</exception>
    public static string? Resolve(string root, string? client, string? environment)
    {
        if (client is null && environment is null)
            return null;

        if (environment is null)
            throw new ArgumentException("--environment is required when --client is set.");

        return client is null
            ? Path.Combine(root, RootFolderName, "Environments", environment, LayerManifestFileName)
            : Path.Combine(root, RootFolderName, "Clients", client, environment, LayerManifestFileName);
    }
}
