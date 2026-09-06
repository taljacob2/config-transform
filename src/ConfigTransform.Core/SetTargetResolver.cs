namespace ConfigTransform.Core;

/// <summary>
/// Which configtransform.json (if any) and which patch file <c>set</c>
/// (docs/FIELD_AUTHORING_DESIGN.md, docs/SELF_DESCRIBING_OVERLAYS_DESIGN.md "Settled decisions"
/// #6) writes to, given a resource and optional --client/--environment: the base file itself
/// with neither, the Environment layer with only --environment, or the Client layer with both.
/// </summary>
public sealed record SetTarget(
    /// <summary>Repo-root-relative, canonicalized from the resolved base file's actual on-disk path/casing.</summary>
    string ResourcePath,
    string ResourceBasePath,
    bool IsBaseTarget,
    /// <summary>Null only when <see cref="IsBaseTarget"/> — there is no layer file to write.</summary>
    string? TargetLayerPath,
    /// <summary>What to set/keep the target layer's own `extends` field to. Null when <see cref="IsBaseTarget"/>, or for an Environment-layer target.</summary>
    string? Extends,
    /// <summary>The patch file to write to — existing (idempotent update) or newly computed. Null only when <see cref="IsBaseTarget"/>.</summary>
    string? PatchPath,
    /// <summary>
    /// Every prior layer's own patch for this resource, in extends order — the state this edit
    /// builds on top of. Always empty for a base target (there's nothing to precede it).
    /// </summary>
    IReadOnlyList<string> PrecedingPatchPathsInOrder);

public static class SetTargetResolver
{
    /// <param name="newPatchFileExtension">
    /// The extension (no leading dot) to use for a brand-new patch file's own name, when this
    /// resource isn't listed yet or is listed with no patch — e.g. "xml" for XDT transforms
    /// regardless of the base resource's own extension, "json" for JSON. See the worked example
    /// in docs/SELF_DESCRIBING_OVERLAYS_DESIGN.md "Proposed shape".
    /// </param>
    public static SetTarget Resolve(
        string root, string resourcePath, string? client, string? environment, string newPatchFileExtension)
    {
        var basePath = LayerChain.ResolveResourceBasePath(root, resourcePath);
        var canonicalResourcePath = LayerChain.ToRepoRelative(root, basePath);

        var targetLayerPath = LayerPathResolver.Resolve(root, client, environment);
        if (targetLayerPath is null)
            return new SetTarget(canonicalResourcePath, basePath, IsBaseTarget: true, null, null, null, []);

        var targetLayerFullPath = Path.GetFullPath(targetLayerPath, root);

        // A Client-layer target defaults `extends` to the matching Environment layer's path, even
        // if that file doesn't exist on disk yet -- a missing `extends` target is "nothing to
        // inherit," not an error (Settled decisions #6). An Environment-layer target has no
        // `extends` at all. Trust an already-existing file's own `extends` over this default, in
        // case it was hand-edited to something else.
        var defaultExtends = client is null
            ? null
            : LayerChain.ToRepoRelative(root, LayerPathResolver.Resolve(root, client: null, environment)!);
        var extends = File.Exists(targetLayerFullPath)
            ? LayerManifestLoader.Load(targetLayerFullPath).Extends
            : defaultExtends;

        var precedingChain = extends is null ? [] : LayerChain.Build(root, extends);
        var precedingPatches = LayerChain.ResolveResource(root, precedingChain, canonicalResourcePath).PatchPathsInOrder;

        var existingEntry = File.Exists(targetLayerFullPath)
            ? LayerManifestLoader.Load(targetLayerFullPath).Resources
                .FirstOrDefault(r => LayerChain.PathsEqual(root, r.Path, canonicalResourcePath))
            : null;

        var patchPath = existingEntry?.Patch is not null
            ? Path.GetFullPath(existingEntry.Patch, root)
            : Path.Combine(
                Path.GetDirectoryName(targetLayerFullPath)!,
                PatchFileNaming.BuildFileName(canonicalResourcePath, newPatchFileExtension));

        return new SetTarget(canonicalResourcePath, basePath, IsBaseTarget: false, targetLayerFullPath, extends, patchPath, precedingPatches);
    }

    /// <summary>
    /// Creates or updates the target's configtransform.json so <see cref="SetTarget.ResourcePath"/>
    /// is listed with <see cref="SetTarget.PatchPath"/> — the layer-declaration side effect
    /// `set` needs beyond just authoring the patch file's own content (Settled decisions #6). A
    /// no-op for a base target: there's no layer file to declare anything in.
    /// </summary>
    public static void EnsureResourceListed(string root, SetTarget target)
    {
        if (target.IsBaseTarget)
            return;

        var targetLayerFullPath = target.TargetLayerPath!;
        var manifest = File.Exists(targetLayerFullPath)
            ? LayerManifestLoader.Load(targetLayerFullPath)
            : new LayerManifest(target.Extends, []);

        var patchRelative = LayerChain.ToRepoRelative(root, target.PatchPath!);
        var resources = manifest.Resources.ToList();
        var existingIndex = resources.FindIndex(r => LayerChain.PathsEqual(root, r.Path, target.ResourcePath));

        if (existingIndex >= 0)
            resources[existingIndex] = resources[existingIndex] with { Patch = patchRelative };
        else
            resources.Add(new ResourceEntry(target.ResourcePath, patchRelative));

        var updated = manifest with { Extends = target.Extends, Resources = resources };

        var dir = Path.GetDirectoryName(targetLayerFullPath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(targetLayerFullPath, LayerManifestSerializer.Serialize(updated));
    }
}
