namespace ConfigTransform.Core;

/// <summary>
/// The patch-file naming convention shared by every creator of a brand-new patch file --
/// <see cref="SetTargetResolver"/> (`set`) and <see cref="InitTemplate"/> (`init --template`),
/// docs/INIT_COMMAND_DESIGN.md "Related fix to SetTargetResolver". `patch-{path-with-'/'-as-'-'}`,
/// plus `.{patchExtension}` -- but only when the sanitized path doesn't already end with it, since
/// a resource whose own extension already matches the patch extension (every plain `.json`
/// resource, the common case) would otherwise stutter (`patch-appsettings.json.json`).
/// </summary>
public static class PatchFileNaming
{
    public static string BuildFileName(string canonicalResourcePath, string patchExtension)
    {
        var sanitized = $"patch-{canonicalResourcePath.Replace('/', '-')}";
        var suffix = $".{patchExtension}";
        return sanitized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? sanitized : sanitized + suffix;
    }
}
