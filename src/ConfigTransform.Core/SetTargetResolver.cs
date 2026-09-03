namespace ConfigTransform.Core;

/// <summary>
/// Which file <c>set</c> (docs/FIELD_AUTHORING_DESIGN.md) writes to, given optional --client/
/// --environment: the base file with neither, the Environment overlay with only --environment,
/// or the Client overlay with both. Format-agnostic — same Environments/Clients folder
/// convention <see cref="LayerResolution"/> uses for a resolve, just for one target instead of
/// resolving every layer, and without requiring client/environment the way a resolve does.
/// </summary>
public sealed record SetTarget(
    string TargetPath,
    bool IsBaseTarget,
    /// <summary>
    /// The Environment overlay's resolved path, when the target is a Client overlay — needed
    /// both to compute what "precedes" the client layer and, after writing, to recompute the
    /// effective merged result for the auto-diff, so it's never resolved twice inconsistently.
    /// Null for a base or Environment-layer target, where it isn't needed.
    /// </summary>
    string? EnvironmentOverlayPath);

public static class SetTargetResolver
{
    public static SetTarget Resolve(
        string overlayRoot, string relativeToDirectory, string basePath, string? client, string? environment)
    {
        if (client is null && environment is null)
            return new SetTarget(basePath, IsBaseTarget: true, EnvironmentOverlayPath: null);

        var extension = Path.GetExtension(relativeToDirectory);
        var fileName = $"{environment}{extension}";

        if (client is null)
        {
            var environmentDir = Path.Combine(overlayRoot, "Environments");
            var targetPath = FileResolver.TryResolveCaseInsensitive(environmentDir, fileName)
                ?? Path.Combine(environmentDir, fileName);
            return new SetTarget(targetPath, IsBaseTarget: false, EnvironmentOverlayPath: null);
        }

        var environmentOverlayPath = FileResolver.TryResolveCaseInsensitive(
            Path.Combine(overlayRoot, "Environments"), fileName);

        var clientDir = Path.Combine(overlayRoot, "Clients", client);
        var clientTargetPath = FileResolver.TryResolveCaseInsensitive(clientDir, fileName)
            ?? Path.Combine(clientDir, fileName);
        return new SetTarget(clientTargetPath, IsBaseTarget: false, EnvironmentOverlayPath: environmentOverlayPath);
    }
}
