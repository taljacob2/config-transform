namespace ConfigTransform.Core;

/// <summary>One resolved layer in an extends chain: the loaded manifest plus the absolute path it was loaded from.</summary>
public sealed record ResolvedLayer(string Path, LayerManifest Manifest);

/// <summary>
/// One resource's resolution against a chain: its real base file plus every layer's own patch
/// for it, in extends order (outermost/base-most first) — the configtransform.json-based
/// replacement for the old fixed-slot <c>LayerResolutionResult</c>. <see cref="Report"/> mirrors
/// that type's found/not-found reporting (CONFIG_MANAGEMENT.md §5.1), now per layer instead of
/// per fixed base/Environment/Client slot. <see cref="Steps"/> is the same per-layer facts in a
/// structured form for display (docs on the "clearer chain output" change) — collapses "not
/// listed" and "listed with no patch" into one `PatchPath: null`, since the CLI only ever shows
/// "not patched in" for either; the fine-grained distinction still lives in <see cref="Report"/>.
/// </summary>
public sealed record ResolvedResource(
    string BasePath, IReadOnlyList<string> PatchPathsInOrder, IReadOnlyList<string> Report,
    IReadOnlyList<ChainStep> Steps);

/// <summary>One layer's display row in a resolved chain — see <see cref="ResolvedResource.Steps"/>.</summary>
public sealed record ChainStep(string Label, string? PatchPath);

/// <summary>One layer's entry for a resource, found by scanning the whole tree — see <see cref="LayerChain.ReverseLookup"/>.</summary>
public sealed record ReverseLookupEntry(string LayerPath, string? Patch, string? Extends);

/// <summary>
/// Walks a layer's `extends` chain (docs/SELF_DESCRIBING_OVERLAYS_DESIGN.md) and resolves
/// resources against it — the configtransform.json-based replacement for the old
/// <c>LayerResolution</c>'s fixed base→Environments→Clients rule, generalized to an arbitrary
/// number of layers. A missing configtransform.json anywhere in the chain (including the very
/// first one asked for) is never fatal — it just means "nothing configured from here on", the
/// same tolerance a missing overlay always had (CONFIG_MANAGEMENT.md §5.1); only a cycle in
/// `extends` is an error, since it can never resolve. A resource simply not being listed in a
/// layer's `resources`, or listed with no `patch`, is likewise not an error — the same
/// tolerance. A `patch` that *is* declared but whose file doesn't exist on disk is different: an
/// explicit, broken reference, not an omission — that's always an error, the same way a missing
/// base file is (unlike the old convention-based `Environments/`/`Clients/` overlay lookup,
/// where "does a file exist here" and "should one exist here" were the same question).
/// </summary>
public static class LayerChain
{
    /// <summary>
    /// Builds the ordered chain for <paramref name="targetLayerPath"/> by following `extends`,
    /// outermost (no further `extends`) first, the target itself last. Empty when
    /// <paramref name="targetLayerPath"/> is null (base target — no layer at all) or doesn't
    /// exist on disk, or as soon as an `extends` target doesn't exist ("nothing to inherit",
    /// per "Settled decisions" #6 — not an error).
    /// </summary>
    /// <exception cref="InvalidOperationException">The `extends` chain cycles back on itself.</exception>
    public static IReadOnlyList<ResolvedLayer> Build(string root, string? targetLayerPath)
    {
        if (targetLayerPath is null)
            return [];

        var chain = new List<ResolvedLayer>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = targetLayerPath;

        while (current is not null)
        {
            var fullPath = Path.GetFullPath(current, root);

            if (!visited.Add(fullPath))
                throw new InvalidOperationException(
                    $"Cycle detected in 'extends': '{fullPath}' extends back to itself.");

            if (!File.Exists(fullPath))
                break;

            var manifest = LayerManifestLoader.Load(fullPath);
            chain.Add(new ResolvedLayer(fullPath, manifest));
            current = manifest.Extends;
        }

        chain.Reverse();
        return chain;
    }

    /// <summary>
    /// Resolves one resource (by its repo-root-relative path) against an already-built chain.
    /// </summary>
    /// <exception cref="FileNotFoundException">The resource's base file does not exist.</exception>
    public static ResolvedResource ResolveResource(string root, IReadOnlyList<ResolvedLayer> chain, string resourcePath)
    {
        var basePath = ResolveResourceBasePath(root, resourcePath);
        var report = new List<string> { $"base: '{resourcePath}' found at '{basePath}'" };
        var patches = new List<string>();
        var steps = new List<ChainStep>();

        foreach (var layer in chain)
        {
            var label = ToRepoRelative(root, layer.Path);
            var entry = layer.Manifest.Resources.FirstOrDefault(r => PathsEqual(root, r.Path, resourcePath));

            if (entry is null)
            {
                report.Add($"{label}: '{resourcePath}' not listed, skipping (no override at this layer)");
                steps.Add(new ChainStep(label, null));
                continue;
            }

            if (entry.Patch is null)
            {
                report.Add($"{label}: '{resourcePath}' listed with no patch, skipping (no override at this layer)");
                steps.Add(new ChainStep(label, null));
                continue;
            }

            var patchPath = Path.GetFullPath(entry.Patch, root);
            if (!File.Exists(patchPath))
                throw new FileNotFoundException(
                    $"{label} declares patch '{entry.Patch}' for '{resourcePath}', but no file exists at '{patchPath}'.");

            report.Add($"{label}: '{resourcePath}' patched, applying ('{patchPath}')");
            patches.Add(patchPath);
            steps.Add(new ChainStep(label, ToRepoRelative(root, patchPath)));
        }

        return new ResolvedResource(basePath, patches, report, steps);
    }

    /// <summary>The union of every `resources[].path` mentioned anywhere in the chain — for a no-`--resource` invocation.</summary>
    public static IReadOnlyList<string> ResolveAllResources(IReadOnlyList<ResolvedLayer> chain) =>
        chain
            .SelectMany(layer => layer.Manifest.Resources.Select(r => r.Path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// `--list --resource`: every configtransform.json anywhere under .configtransform/ that
    /// patches this one resource, regardless of which chain it belongs to.
    /// </summary>
    public static IReadOnlyList<ReverseLookupEntry> ReverseLookup(string root, string resourcePath)
    {
        var configtransformRoot = Path.Combine(root, ".configtransform");
        if (!Directory.Exists(configtransformRoot))
            return [];

        var results = new List<ReverseLookupEntry>();
        foreach (var layerPath in Directory.EnumerateFiles(configtransformRoot, "configtransform.json", SearchOption.AllDirectories))
        {
            var manifest = LayerManifestLoader.Load(layerPath);
            var entry = manifest.Resources.FirstOrDefault(r => PathsEqual(root, r.Path, resourcePath));
            if (entry is not null)
                results.Add(new ReverseLookupEntry(ToRepoRelative(root, layerPath), entry.Patch, manifest.Extends));
        }

        return results.OrderBy(r => r.LayerPath, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Case-insensitive on the resource's own file name only (mirrors the old
    /// <c>LayerResolution</c>'s case-insensitivity, which likewise only ever covered the base
    /// file's own name within its declared directory — see <see cref="FileResolver"/>).
    /// </summary>
    internal static string ResolveResourceBasePath(string root, string resourcePath)
    {
        var fullPath = Path.GetFullPath(resourcePath, root);
        var parentDir = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException($"Could not determine the directory of '{fullPath}'.");

        return FileResolver.ResolveCaseInsensitiveRequired(parentDir, Path.GetFileName(fullPath));
    }

    internal static bool PathsEqual(string root, string a, string b) =>
        string.Equals(Path.GetFullPath(a, root), Path.GetFullPath(b, root), StringComparison.OrdinalIgnoreCase);

    internal static string ToRepoRelative(string root, string fullPath) =>
        Path.GetRelativePath(root, fullPath).Replace(Path.DirectorySeparatorChar, '/');
}
