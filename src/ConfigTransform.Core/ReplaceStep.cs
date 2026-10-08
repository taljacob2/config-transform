using System.Text;

namespace ConfigTransform.Core;

/// <summary>
/// What the resolve flow does with a resource that a layer <c>replace</c>s with a whole-file secret
/// (docs/SECRETS_DESIGN.md, "File secrets"): no format engine, no merge, no parsing — a real run
/// copies the file's bytes, so any format works, binary included. Previews never show the content
/// unless <c>--reveal-secrets</c> is given. Shared by <see cref="CliRunner"/>'s single-resource and
/// every-resource paths.
///
/// The replace can also be a <c>keyvault://vault/secret</c> (docs/KEYVAULT_SECRETS_DESIGN.md,
/// "Whole files"): previews read only its metadata, a real run reads its value, and a certificate's
/// value (content type <see cref="CertificateContentType"/>) is base64-decoded to the real bytes.
/// </summary>
public static class ReplaceStep
{
    /// <summary>The content type Key Vault gives the secret behind an imported certificate, whose value is base64.</summary>
    public const string CertificateContentType = "application/x-pkcs12";

    /// <summary>The preview output for a replaced resource: a one-line note, or with <paramref name="reveal"/> the content (<c>--dry-run</c>) or a diff against the base (<c>--diff</c>/<c>--diff-layers</c>).</summary>
    public static string Preview(string root, ResolvedResource resolved, bool reveal, bool diff, bool color, IKeyVault? keyVault = null)
    {
        var replacePath = resolved.ReplacePath!;
        byte[] bytes;
        string display;

        if (KeyVaultReference.IsKeyVault(replacePath))
        {
            var reference = KeyVaultReference.Parse(replacePath);
            display = reference.ToString();
            var vault = Require(keyVault, reference);

            KeyVaultSecretInfo info;
            try
            {
                info = vault.GetSecretInfo(reference.Vault, reference.Secret!) ?? throw NoSuchSecret(reference);
            }
            catch (KeyVaultUnavailableException ex)
            {
                return $"(replaced by {display}, which {ex.Message} -- it can't be shown or deployed until it can be read)";
            }

            if (StateProblem(info.State) is { } problem)
                return $"(replaced by {display}, which {problem} -- a real run would fail)";
            if (!reveal)
                return $"(replaced by {display}, not shown -- pass --reveal-secrets to see it)";

            bytes = ReadVaultBytes(vault, reference);
        }
        else
        {
            display = LayerChain.ToRepoRelative(root, replacePath);
            if (GitCrypt.IsLocked(replacePath))
                return $"(replaced by {display}, which is still git-crypt encrypted -- run git-crypt unlock to see or deploy it)";
            if (!reveal)
                return $"(replaced by {display}, {new FileInfo(replacePath).Length} bytes, not shown -- pass --reveal-secrets to see it)";

            bytes = File.ReadAllBytes(replacePath);
        }

        if (Array.IndexOf(bytes, (byte)0) >= 0)
            return $"(replaced by {display}: binary content, {bytes.Length} bytes)";

        var text = Encoding.UTF8.GetString(bytes);
        if (!diff)
            return text;

        var rendered = GitDiff.Render(File.ReadAllText(resolved.BasePath), text, color);
        return string.IsNullOrWhiteSpace(rendered) ? "(no changes)" : rendered;
    }

    /// <summary>
    /// For a real run: the replace's exact bytes. Throws, before anything is written, if a file is
    /// still git-crypt encrypted or a vault secret can't be read — deploying ciphertext, or nothing,
    /// would be worse than failing.
    /// </summary>
    public static byte[] ForRealRun(string root, string resourcePath, ResolvedResource resolved, IKeyVault? keyVault = null)
    {
        var replacePath = resolved.ReplacePath!;

        if (KeyVaultReference.IsKeyVault(replacePath))
        {
            var reference = KeyVaultReference.Parse(replacePath);
            var vault = Require(keyVault, reference);
            try
            {
                var info = vault.GetSecretInfo(reference.Vault, reference.Secret!) ?? throw NoSuchSecret(reference);
                if (StateProblem(info.State) is { } problem)
                    throw new InvalidOperationException(
                        $"'{resourcePath}' is replaced by {reference}, which {problem}, so nothing was written.\n" +
                        $"Try: enable it, or set its validity dates: az keyvault secret set-attributes --vault-name {reference.Vault} --name {reference.Secret} --enabled true");
            }
            catch (KeyVaultUnavailableException ex)
            {
                throw new InvalidOperationException($"'{resourcePath}' is replaced by {reference}, which {ex.Message}, so nothing was written.");
            }

            return ReadVaultBytes(vault, reference);
        }

        if (GitCrypt.IsLocked(replacePath))
            throw new InvalidOperationException(
                $"'{resourcePath}' is replaced by '{LayerChain.ToRepoRelative(root, replacePath)}', which is still git-crypt " +
                "encrypted, so nothing was written.\nTry: run git-crypt unlock with this repo's key.");

        return File.ReadAllBytes(replacePath);
    }

    private static IKeyVault Require(IKeyVault? keyVault, KeyVaultReference reference) =>
        keyVault ?? throw new InvalidOperationException(
            $"A resource is replaced by {reference}, but this build of configtransform has no Azure Key Vault support.");

    private static string? StateProblem(KeyVaultSecretState state) => state switch
    {
        KeyVaultSecretState.Enabled => null,
        KeyVaultSecretState.Disabled => "is disabled",
        KeyVaultSecretState.Expired => "is expired",
        _ => "is not yet valid",
    };

    private static InvalidOperationException NoSuchSecret(KeyVaultReference reference) =>
        new($"A resource is replaced by {reference}, but that vault has no secret named '{reference.Secret}'.\n" +
            $"Try: check the name, or upload the file: az keyvault secret set --vault-name {reference.Vault} --name {reference.Secret} --file <file>");

    /// <summary>The value as the file's bytes: UTF-8 text as stored, or a certificate's base64 decoded.</summary>
    private static byte[] ReadVaultBytes(IKeyVault vault, KeyVaultReference reference)
    {
        KeyVaultSecretValue value;
        try
        {
            value = vault.GetSecretValue(reference.Vault, reference.Secret!);
        }
        catch (KeyVaultUnavailableException ex)
        {
            throw new InvalidOperationException(
                $"The value of {reference} {ex.Message}. Its name was readable, so this is usually a role that can list " +
                "secrets but not read their values (Key Vault Reader).\nTry: ask for Key Vault Secrets User on the vault, or on this secret.");
        }

        if (!string.Equals(value.ContentType, CertificateContentType, StringComparison.OrdinalIgnoreCase))
            return Encoding.UTF8.GetBytes(value.Value);

        try
        {
            return Convert.FromBase64String(value.Value);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException(
                $"{reference} has content type '{CertificateContentType}', but its value isn't base64, so it can't be written as a certificate.");
        }
    }
}
