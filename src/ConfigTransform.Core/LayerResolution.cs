namespace ConfigTransform.Core;

/// <summary>
/// Result of resolving the base + Environments + Clients layers for one config file, one
/// client, one environment. <see cref="Report"/> explicitly records what was found or not
/// found at each layer — the tool never tries to guess whether an absence was a typo; it just
/// reports honestly whether a match existed. See CONFIG_MANAGEMENT.md §5.1.
/// </summary>
public sealed record LayerResolutionResult(
    string BasePath,
    string? EnvironmentOverlayPath,
    string? ClientOverlayPath,
    IReadOnlyList<string> Report);

/// <summary>
/// Locates the base file and its optional Environments/Clients overlays for one
/// (project, config file, client, environment) combination, per the fixed merge order in
/// CONFIG_MANAGEMENT.md §5.1: base -&gt; Environments/&lt;Environment&gt;.&lt;ext&gt; (optional)
/// -&gt; Clients/&lt;Client&gt;/&lt;Environment&gt;.&lt;ext&gt; (optional).
/// </summary>
public static class LayerResolution
{
    /// <param name="projectDirectory">The project's own directory (containing the base file).</param>
    /// <param name="relativeToProject">Base file name/path, relative to <paramref name="projectDirectory"/> — from the manifest.</param>
    /// <param name="overlayRoot">The file's own overlay folder, e.g. .configtransform/&lt;Project&gt;/App.config/.</param>
    /// <param name="client">Client name — selects Clients/&lt;client&gt;/.</param>
    /// <param name="environment">Environment name — selects the Environments/&lt;environment&gt;.&lt;ext&gt; and Clients/&lt;client&gt;/&lt;environment&gt;.&lt;ext&gt; file names.</param>
    /// <exception cref="FileNotFoundException">The base file does not exist.</exception>
    public static LayerResolutionResult Resolve(
        string projectDirectory,
        string relativeToProject,
        string overlayRoot,
        string client,
        string environment)
    {
        var report = new List<string>();

        var basePath = FileResolver.ResolveCaseInsensitiveRequired(projectDirectory, relativeToProject);
        report.Add($"base: '{relativeToProject}' found at '{basePath}'");

        var extension = Path.GetExtension(relativeToProject);
        var overlayFileName = $"{environment}{extension}";

        var environmentOverlayPath = TryResolveOverlay(
            Path.Combine(overlayRoot, "Environments"), overlayFileName,
            $"Environments/{overlayFileName}", report);

        var clientOverlayPath = TryResolveOverlay(
            Path.Combine(overlayRoot, "Clients", client), overlayFileName,
            $"Clients/{client}/{overlayFileName}", report);

        return new LayerResolutionResult(basePath, environmentOverlayPath, clientOverlayPath, report);
    }

    private static string? TryResolveOverlay(string directory, string fileName, string label, List<string> report)
    {
        var resolved = FileResolver.TryResolveCaseInsensitive(directory, fileName);

        report.Add(resolved is null
            ? $"{label}: not found, skipping (no override at this layer)"
            : $"{label}: found, applying ('{resolved}')");

        return resolved;
    }
}
