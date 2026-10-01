using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConfigTransform.Core;

/// <summary>
/// Loads a single configtransform.json (docs/SELF_DESCRIBING_OVERLAYS_DESIGN.md) — the
/// per-layer-directory replacement for <c>manifest.json</c>. Recognizes a configtransform.json that
/// is still git-crypt ciphertext (<see cref="GitCrypt"/>), since the same encrypted tree this file
/// can live under (<c>.configtransform/**</c>, CONFIG_MANAGEMENT.md §7.1) can just as easily leave
/// it locked on disk.
///
/// Strict about unknown fields: a field this version doesn't know is an error, not silently
/// skipped. Before this, a layer using a newer field (e.g. <c>secrets</c>, docs/SECRETS_DESIGN.md)
/// read by an older tool had that field ignored — deploying unresolved placeholders without a word.
/// Rejecting unknown fields means any future addition fails loudly on a too-old tool instead.
/// </summary>
public static class LayerManifestLoader
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>The suffix every <c>secrets</c> entry must have, so the <c>*.secret.*</c> git-crypt rule always covers it.</summary>
    public const string SecretFileSuffix = ".secret.env";

    public static LayerManifest Load(string layerManifestPath)
    {
        if (!File.Exists(layerManifestPath))
            throw new FileNotFoundException($"configtransform.json not found: '{layerManifestPath}'");

        if (GitCrypt.IsLocked(layerManifestPath))
        {
            throw new InvalidOperationException(
                $"'{layerManifestPath}' is still git-crypt encrypted (this is git-crypt's own " +
                "ciphertext, not JSON). Run 'git-crypt unlock' with this repo's git-crypt key before " +
                "running this tool -- see this repo's SECRETS.md, or config-transform's " +
                "SECRETS_AND_LOCAL_SETUP.md §2, for how to get the key.");
        }

        var json = File.ReadAllText(layerManifestPath);

        LayerManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<LayerManifest>(json, ReadOptions);
        }
        catch (JsonException ex) when (ex.Message.Contains("could not be mapped", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"'{layerManifestPath}' has a field this version of configtransform doesn't recognize: {ex.Message}\n" +
                "Try: check the field name for a typo -- or, if this layer was written for a newer version, " +
                "update configtransform.cli's version in .config/dotnet-tools.json.", ex);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Failed to parse '{layerManifestPath}': {ex.Message}", ex);
        }

        manifest = manifest ?? throw new InvalidOperationException($"'{layerManifestPath}' deserialized to null.");

        foreach (var secretsFile in manifest.Secrets ?? [])
        {
            if (!secretsFile.EndsWith(SecretFileSuffix, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"'{layerManifestPath}' lists secrets file '{secretsFile}', which doesn't end in '{SecretFileSuffix}'. " +
                    "Every secrets file must, so the '.configtransform/**/*.secret.*' git-crypt rule always covers it " +
                    $"(docs/SECRETS_DESIGN.md).\nTry: rename it to end in '{SecretFileSuffix}'.");
            RequireInsideConfigTransform(layerManifestPath, "secrets file", secretsFile);
        }

        return manifest;
    }

    /// <summary>
    /// The '.configtransform/**/*.secret.*' git-crypt rule only covers files under .configtransform/
    /// -- a correctly named secret file anywhere else would be committed in plaintext. Paths here are
    /// repo-root-relative, so "inside" means the first segment is .configtransform and no segment
    /// climbs back out with "..".
    /// </summary>
    private static void RequireInsideConfigTransform(string layerManifestPath, string what, string path)
    {
        var segments = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2 || segments[0] != ".configtransform" || segments.Contains(".."))
            throw new InvalidOperationException(
                $"'{layerManifestPath}' lists {what} '{path}', which isn't inside .configtransform/ -- the " +
                "'.configtransform/**/*.secret.*' git-crypt rule wouldn't cover it, so it would be committed in plaintext " +
                "(docs/SECRETS_DESIGN.md).\nTry: move it next to this configtransform.json and list it by its repo-root-relative path.");
    }
}
