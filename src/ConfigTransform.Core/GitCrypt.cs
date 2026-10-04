namespace ConfigTransform.Core;

/// <summary>
/// Recognizes a file that is still git-crypt ciphertext on disk -- one under a git-crypt rule in a
/// checkout that hasn't been <c>git-crypt unlock</c>ed. Shared by <see cref="LayerManifestLoader"/>
/// (a locked configtransform.json) and <see cref="SecretResolver"/> (a locked <c>*.secret.env</c>,
/// docs/SECRETS_DESIGN.md), so both detect it the same way.
/// </summary>
public static class GitCrypt
{
    /// <summary>
    /// git-crypt's own magic header for an encrypted file: 10 bytes, NUL + "GITCRYPT" + NUL. Without
    /// special-casing it, ciphertext fails parsing with a confusing "'0x00' is an invalid start of a
    /// value" error, or worse, gets treated as content.
    /// </summary>
    private static readonly byte[] Header =
        [0x00, (byte)'G', (byte)'I', (byte)'T', (byte)'C', (byte)'R', (byte)'Y', (byte)'P', (byte)'T', 0x00];

    public static bool IsLocked(string path)
    {
        using var stream = File.OpenRead(path);
        var buffer = new byte[Header.Length];
        var totalRead = 0;
        int read;
        while (totalRead < buffer.Length && (read = stream.Read(buffer, totalRead, buffer.Length - totalRead)) > 0)
            totalRead += read;

        return totalRead == buffer.Length && buffer.AsSpan().SequenceEqual(Header);
    }
}
