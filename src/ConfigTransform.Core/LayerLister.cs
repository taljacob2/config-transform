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

            var resolved = LayerChain.ResolveResource(root, chain, resourcePath);
            LayerChain.PrintChain(stdout, resourcePath, resolved);
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
