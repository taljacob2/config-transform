namespace ConfigTransform.Core;

/// <summary>
/// Implements --list under the self-describing overlays design
/// (docs/SELF_DESCRIBING_OVERLAYS_DESIGN.md "Settled decisions" #7) — replaces
/// <c>ManifestLister</c>. Two modes: given a target layer (--client/--environment), shows that
/// layer's own resources plus what it inherits via `extends`; given --resource instead, it's a
/// tree-wide reverse lookup — every layer anywhere under .configtransform/ that patches one
/// project.
/// </summary>
public static class LayerLister
{
    public static void ListLayer(string root, string targetLayerPath, TextWriter stdout)
    {
        var fullPath = Path.GetFullPath(targetLayerPath, root);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"No configtransform.json at '{fullPath}' -- nothing to list.");

        var chain = LayerChain.Build(root, targetLayerPath);
        var target = chain[^1];

        stdout.WriteLine(LayerChain.ToRepoRelative(root, fullPath));
        if (target.Manifest.Extends is not null)
            stdout.WriteLine($"  extends: {target.Manifest.Extends}");

        foreach (var resourcePath in LayerChain.ResolveAllResources(chain))
        {
            stdout.WriteLine();
            stdout.WriteLine($"  {resourcePath}");

            var targetEntry = target.Manifest.Resources.FirstOrDefault(r => LayerChain.PathsEqual(root, r.Path, resourcePath));
            var priorLayers = chain.Take(chain.Count - 1).ToList();

            if (targetEntry?.Patch is not null)
            {
                stdout.WriteLine($"    patched here: {targetEntry.Patch}");

                foreach (var layer in priorLayers)
                {
                    var entry = layer.Manifest.Resources.FirstOrDefault(r => LayerChain.PathsEqual(root, r.Path, resourcePath));
                    if (entry?.Patch is not null)
                        stdout.WriteLine($"    also patched in: {LayerChain.ToRepoRelative(root, layer.Path)}");
                }

                continue;
            }

            var inheritedFrom = priorLayers.AsEnumerable().Reverse()
                .FirstOrDefault(layer => layer.Manifest.Resources
                    .Any(r => LayerChain.PathsEqual(root, r.Path, resourcePath) && r.Patch is not null));

            stdout.WriteLine(inheritedFrom is null
                ? "    not patched anywhere — using the base file directly"
                : $"    not patched here — inherited from {LayerChain.ToRepoRelative(root, inheritedFrom.Path)}");
        }
    }

    public static void ListReverseLookup(string root, string resourcePath, TextWriter stdout)
    {
        var found = LayerChain.ReverseLookup(root, resourcePath);

        if (found.Count == 0)
        {
            stdout.WriteLine($"{resourcePath} is not patched anywhere.");
            return;
        }

        stdout.WriteLine($"{resourcePath} is patched in:");
        foreach (var entry in found)
        {
            var extendsNote = entry.Extends is null ? "" : $", extends {entry.Extends}";
            stdout.WriteLine($"  {entry.LayerPath}  ({entry.Patch}{extendsNote})");
        }
    }
}
