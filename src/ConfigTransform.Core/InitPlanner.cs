namespace ConfigTransform.Core;

/// <summary>
/// Builds the skeleton tree `configtransform init` scaffolds from real inputs (a scanned/typed
/// resource list, environments, clients, hosts) -- docs/INIT_COMMAND_DESIGN.md "Manifest shape",
/// docs/HOST_LAYER_DESIGN.md for the optional third `hosts` axis. Every Environment layer lists
/// every selected resource, deliberately with no `patch`
/// (<see cref="LayerChain.ResolveAllResources"/>'s no-`--resource` union only sees resources
/// actually listed somewhere in the chain -- leaving a resource unlisted would make a freshly
/// `init`'d tree resolve to nothing until `set` ran once per resource). Every Client layer, and
/// every Host layer, declares `extends` only, an empty `resources[]` -- the documented "layer that
/// only exists to declare `extends`" shape. <paramref name="hosts"/> cross-multiplies with every
/// client x environment pair already being scaffolded, the same way clients already cross-
/// multiply with environments -- not scoped to specific pairs, consistent with how a client
/// already gets a layer under every environment regardless of whether that combination makes real
/// sense; unwanted combinations can simply be deleted after scaffolding. Idempotent: re-running
/// against a tree `init` or `set` already touched merges in what's new (mirrors
/// <see cref="SetTargetResolver.EnsureResourceListed"/>'s convention) -- an existing `patch`
/// reference is never cleared, an existing `resources[]` entry is never duplicated, an already-set
/// `extends` is never overwritten.
/// </summary>
public static class InitPlanner
{
    public static IReadOnlyList<InitFile> BuildPlan(
        string root, IReadOnlyList<string> resources, IReadOnlyList<string> environments,
        IReadOnlyList<string> clients, IReadOnlyList<string> hosts)
    {
        var files = new List<InitFile>();

        foreach (var environment in environments)
        {
            var envLayerFullPath = LayerPathResolver.Resolve(root, client: null, environment)!;
            var envManifest = MergeResources(root, envLayerFullPath, resources);
            files.Add(ToInitFile(root, envLayerFullPath, envManifest));

            var envLayerRelative = LayerChain.ToRepoRelative(root, envLayerFullPath);

            foreach (var client in clients)
            {
                var clientLayerFullPath = LayerPathResolver.Resolve(root, client, environment)!;
                var clientManifest = MergeExtends(clientLayerFullPath, envLayerRelative);
                files.Add(ToInitFile(root, clientLayerFullPath, clientManifest));

                var clientLayerRelative = LayerChain.ToRepoRelative(root, clientLayerFullPath);

                foreach (var host in hosts)
                {
                    var hostLayerFullPath = LayerPathResolver.Resolve(root, client, environment, host)!;
                    var hostManifest = MergeExtends(hostLayerFullPath, clientLayerRelative);
                    files.Add(ToInitFile(root, hostLayerFullPath, hostManifest));
                }
            }
        }

        return files;
    }

    private static LayerManifest MergeResources(string root, string layerFullPath, IReadOnlyList<string> resources)
    {
        var existing = File.Exists(layerFullPath) ? LayerManifestLoader.Load(layerFullPath) : new LayerManifest(null, []);
        var mergedResources = existing.Resources.ToList();

        foreach (var resource in resources)
        {
            if (!mergedResources.Any(r => LayerChain.PathsEqual(root, r.Path, resource)))
                mergedResources.Add(new ResourceEntry(resource));
        }

        return existing with { Resources = mergedResources };
    }

    private static LayerManifest MergeExtends(string layerFullPath, string defaultExtends)
    {
        if (!File.Exists(layerFullPath))
            return new LayerManifest(defaultExtends, []);

        var existing = LayerManifestLoader.Load(layerFullPath);
        return existing with { Extends = existing.Extends ?? defaultExtends };
    }

    private static InitFile ToInitFile(string root, string fullPath, LayerManifest manifest) =>
        new(fullPath, LayerChain.ToRepoRelative(root, fullPath), LayerManifestSerializer.Serialize(manifest));
}
