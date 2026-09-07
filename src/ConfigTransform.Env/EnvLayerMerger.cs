namespace ConfigTransform.Env;

/// <summary>
/// Applies an arbitrary-length chain of `.env` overlays, in order, to a `.env` base file: a flat
/// `KEY→VALUE` union where each later layer overrides matching keys and appends new ones, in
/// chain order. Simpler than <c>JsonLayerMerger</c> -- no nesting, no arrays, so no
/// `$elemMatch`-equivalent is needed at all. Format-generic by design, same as the other two
/// engines (see CLAUDE.md): no assumption anywhere about which real keys a `.env` file holds.
/// </summary>
public static class EnvLayerMerger
{
    public static string Merge(string basePath, IReadOnlyList<string> patchPathsInOrder)
    {
        var order = new List<string>();
        var values = new Dictionary<string, string>();

        void Apply(IEnumerable<KeyValuePair<string, string>> pairs)
        {
            foreach (var pair in pairs)
            {
                if (!values.ContainsKey(pair.Key))
                    order.Add(pair.Key);
                values[pair.Key] = pair.Value;
            }
        }

        Apply(EnvFile.Parse(File.ReadAllText(basePath)));

        foreach (var patchPath in patchPathsInOrder)
            if (File.Exists(patchPath))
                Apply(EnvFile.Parse(File.ReadAllText(patchPath)));

        return EnvFile.Serialize(order.Select(key => new KeyValuePair<string, string>(key, values[key])));
    }
}
