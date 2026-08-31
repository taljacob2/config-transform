namespace ConfigTransform.Core;

/// <summary>
/// Case-insensitive file lookup. Exists because CI runners are typically Linux
/// (case-sensitive) while local dev is typically Windows (case-insensitive) — a hardcoded
/// exact-case lookup can pass on every developer's machine and still fail in CI. See
/// CONFIG_MANAGEMENT.md §5.4.
/// </summary>
public static class FileResolver
{
    /// <summary>
    /// Returns the resolved path, or null if no file matching <paramref name="fileName"/>
    /// (case-insensitively) exists in <paramref name="directory"/>. Use this for overlay
    /// files, where absence is expected and not an error.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// More than one file in <paramref name="directory"/> matches <paramref name="fileName"/>
    /// case-insensitively. This is always an error, regardless of whether the caller treats a
    /// missing file as required or optional.
    /// </exception>
    public static string? TryResolveCaseInsensitive(string directory, string fileName)
    {
        if (!Directory.Exists(directory))
            return null;

        var matches = Directory.EnumerateFiles(directory)
            .Where(f => string.Equals(Path.GetFileName(f), fileName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count == 0)
            return null;

        if (matches.Count > 1)
            throw new InvalidOperationException(
                $"Ambiguous: multiple files matching '{fileName}' case-insensitively in '{directory}': {string.Join(", ", matches)}");

        return matches[0];
    }

    /// <summary>
    /// Same as <see cref="TryResolveCaseInsensitive"/>, but throws when no match is found. Use
    /// this for the base config file, where absence indicates something is actually broken,
    /// not an intentional gap. See CONFIG_MANAGEMENT.md §5.1.
    /// </summary>
    /// <exception cref="FileNotFoundException">No matching file was found.</exception>
    public static string ResolveCaseInsensitiveRequired(string directory, string fileName)
    {
        return TryResolveCaseInsensitive(directory, fileName)
            ?? throw new FileNotFoundException(
                $"No file matching '{fileName}' (case-insensitive) found in '{directory}'");
    }
}
