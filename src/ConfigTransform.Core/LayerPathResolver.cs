namespace ConfigTransform.Core;

/// <summary>
/// Resolves --client/--environment/--host to a configtransform.json path under .configtransform/
/// (docs/SELF_DESCRIBING_OVERLAYS_DESIGN.md "Settled decisions" #6/#7; docs/HOST_LAYER_DESIGN.md
/// for --host, a third optional axis one level deeper) — replaces <c>ManifestDiscovery</c>'s old
/// multi-candidate disambiguation, which no longer applies: with <c>manifest.json</c> gone there
/// is nothing left to be ambiguous between. Given client/environment/host (or none, for a base
/// target) the path is fully determined, so this is pure path arithmetic, not filesystem
/// discovery — the returned path need not exist yet (a caller reading an existing tree treats a
/// missing file as "nothing configured at this layer", same as a missing overlay always was;
/// <c>set</c> creates it).
/// </summary>
public static class LayerPathResolver
{
    private const string RootFolderName = ".configtransform";
    private const string LayerManifestFileName = "configtransform.json";

    /// <returns>
    /// <c>.configtransform/Clients/&lt;client&gt;/&lt;environment&gt;/Hosts/&lt;host&gt;/configtransform.json</c>
    /// when all three are given, <c>.configtransform/Clients/&lt;client&gt;/&lt;environment&gt;/configtransform.json</c>
    /// with only client+environment, <c>.configtransform/Environments/&lt;environment&gt;/configtransform.json</c>
    /// with only <paramref name="environment"/>, or null with none (a base-target: there is no
    /// layer at all, only the raw base file).
    /// </returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="client"/> is set without <paramref name="environment"/> — there is no
    /// client-only layer. <paramref name="host"/> is set without both <paramref name="client"/>
    /// and <paramref name="environment"/> — there is no host-only or host-without-client layer.
    /// </exception>
    public static string? Resolve(string root, string? client, string? environment, string? host = null)
    {
        if (host is not null && (client is null || environment is null))
            throw new ArgumentException("--host requires --client and --environment.");

        if (client is null && environment is null)
            return null;

        if (environment is null)
            throw new ArgumentException("--environment is required when --client is set.");

        if (client is null)
            return Path.Combine(root, RootFolderName, "Environments", environment, LayerManifestFileName);

        return host is null
            ? Path.Combine(root, RootFolderName, "Clients", client, environment, LayerManifestFileName)
            : Path.Combine(root, RootFolderName, "Clients", client, environment, "Hosts", host, LayerManifestFileName);
    }
}
