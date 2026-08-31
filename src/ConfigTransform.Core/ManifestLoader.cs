using System.Text.Json;

namespace ConfigTransform.Core;

public static class ManifestLoader
{
    /// <summary>
    /// git-crypt's own magic header for an encrypted file: 10 bytes, NUL + "GITCRYPT" + NUL.
    /// A manifest under a git-crypt'd `.configtransform/**` tree that hasn't been
    /// `git-crypt unlock`ed still has this ciphertext on disk, which fails JSON parsing with a
    /// confusing "'0x00' is an invalid start of a value" error unless callers special-case it.
    /// </summary>
    private static readonly byte[] GitCryptHeader =
        [0x00, (byte)'G', (byte)'I', (byte)'T', (byte)'C', (byte)'R', (byte)'Y', (byte)'P', (byte)'T', 0x00];

    public static Manifest Load(string manifestPath)
    {
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException($"Manifest not found: '{manifestPath}'");

        if (StartsWithGitCryptHeader(manifestPath))
        {
            throw new InvalidOperationException(
                $"Manifest '{manifestPath}' is still git-crypt encrypted (this is git-crypt's own " +
                "ciphertext, not JSON). Run 'git-crypt unlock' with this repo's git-crypt key before " +
                "running this tool -- see this repo's SECRETS.md, or config-transform's " +
                "SECRETS_AND_LOCAL_SETUP.md §2, for how to get the key.");
        }

        var json = File.ReadAllText(manifestPath);

        Manifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<Manifest>(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Failed to parse manifest '{manifestPath}': {ex.Message}", ex);
        }

        return manifest ?? throw new InvalidOperationException($"Manifest '{manifestPath}' deserialized to null.");
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
