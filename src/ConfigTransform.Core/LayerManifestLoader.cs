using System.Text.Json;

namespace ConfigTransform.Core;

/// <summary>
/// Loads a single configtransform.json (docs/SELF_DESCRIBING_OVERLAYS_DESIGN.md) — the
/// per-layer-directory replacement for <c>manifest.json</c>. Mirrors the old
/// <c>ManifestLoader</c>'s git-crypt-locked-file detection exactly: the same encrypted tree
/// this file lives under (<c>.configtransform/**</c>, CONFIG_MANAGEMENT.md §7.1) can just as
/// easily leave a configtransform.json still git-crypt ciphertext on disk.
/// </summary>
public static class LayerManifestLoader
{
    /// <summary>
    /// git-crypt's own magic header for an encrypted file: 10 bytes, NUL + "GITCRYPT" + NUL.
    /// A configtransform.json under a git-crypt'd `.configtransform/**` tree that hasn't been
    /// `git-crypt unlock`ed still has this ciphertext on disk, which fails JSON parsing with a
    /// confusing "'0x00' is an invalid start of a value" error unless callers special-case it.
    /// </summary>
    private static readonly byte[] GitCryptHeader =
        [0x00, (byte)'G', (byte)'I', (byte)'T', (byte)'C', (byte)'R', (byte)'Y', (byte)'P', (byte)'T', 0x00];

    public static LayerManifest Load(string layerManifestPath)
    {
        if (!File.Exists(layerManifestPath))
            throw new FileNotFoundException($"configtransform.json not found: '{layerManifestPath}'");

        if (StartsWithGitCryptHeader(layerManifestPath))
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
            manifest = JsonSerializer.Deserialize<LayerManifest>(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Failed to parse '{layerManifestPath}': {ex.Message}", ex);
        }

        return manifest ?? throw new InvalidOperationException($"'{layerManifestPath}' deserialized to null.");
    }

    private static bool StartsWithGitCryptHeader(string path)
    {
        using var stream = File.OpenRead(path);
        var buffer = new byte[GitCryptHeader.Length];
        var totalRead = 0;
        int read;
        while (totalRead < buffer.Length && (read = stream.Read(buffer, totalRead, buffer.Length - totalRead)) > 0)
            totalRead += read;

        return totalRead == buffer.Length && buffer.AsSpan().SequenceEqual(GitCryptHeader);
    }
}
