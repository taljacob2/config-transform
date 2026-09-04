namespace ConfigTransform.Core;

/// <summary>
/// Recursively finds candidate resources for `configtransform init` to suggest -- every file
/// whose extension a registered <see cref="FormatEngine"/> handles, under a directory tree.
/// Directory-level excludes only, never a content/filename heuristic (docs/INIT_COMMAND_DESIGN.md
/// "Scanning: directory filters, not content filters" -- CLAUDE.md's core-concepts rule against
/// special-casing by filename or schema applies to scanning too, not just merging). The numbered
/// checklist a caller builds from the result is where human judgment about "is this really a
/// config file" belongs, not this scan.
/// </summary>
public static class InitScanner
{
    private static readonly string[] ExcludedDirectoryNames = [".git", ".configtransform", "bin", "obj", "node_modules"];

    /// <returns>Repo-root-relative paths, sorted, of every candidate resource found under <paramref name="scanRoot"/>.</returns>
    public static IReadOnlyList<string> Scan(string root, string scanRoot, FormatEngineRegistry engines)
    {
        if (!Directory.Exists(scanRoot))
            return [];

        var results = new List<string>();
        ScanDirectory(scanRoot, root, engines, results);
        results.Sort(StringComparer.OrdinalIgnoreCase);
        return results;
    }

    private static void ScanDirectory(string dir, string root, FormatEngineRegistry engines, List<string> results)
    {
        foreach (var file in Directory.EnumerateFiles(dir))
        {
            if (engines.Find(file) is not null)
                results.Add(LayerChain.ToRepoRelative(root, file));
        }

        foreach (var subDir in Directory.EnumerateDirectories(dir))
        {
            if (ExcludedDirectoryNames.Contains(Path.GetFileName(subDir), StringComparer.OrdinalIgnoreCase))
                continue;

            ScanDirectory(subDir, root, engines, results);
        }
    }
}
