using System.Text;

namespace ConfigTransform.Core;

/// <summary>
/// What the resolve flow does with a resource that a layer <c>replace</c>s with a whole-file secret
/// (docs/SECRETS_DESIGN.md, "File secrets"): no format engine, no merge, no parsing — a real run
/// copies the file's bytes, so any format works, binary included. Previews never show the content
/// unless <c>--reveal-secrets</c> is given. Shared by <see cref="CliRunner"/>'s single-resource and
/// every-resource paths.
/// </summary>
public static class ReplaceStep
{
    /// <summary>The preview output for a replaced resource: a one-line note, or with <paramref name="reveal"/> the content (<c>--dry-run</c>) or a diff against the base (<c>--diff</c>/<c>--diff-layers</c>).</summary>
    public static string Preview(string root, ResolvedResource resolved, bool reveal, bool diff, bool color)
    {
        var replacePath = resolved.ReplacePath!;
        var display = LayerChain.ToRepoRelative(root, replacePath);

        if (GitCrypt.IsLocked(replacePath))
            return $"(replaced by {display}, which is still git-crypt encrypted -- run git-crypt unlock to see or deploy it)";

        if (!reveal)
            return $"(replaced by {display}, {new FileInfo(replacePath).Length} bytes, not shown -- pass --reveal-secrets to see it)";

        var bytes = File.ReadAllBytes(replacePath);
        if (Array.IndexOf(bytes, (byte)0) >= 0)
            return $"(replaced by {display}: binary content, {bytes.Length} bytes)";

        var text = Encoding.UTF8.GetString(bytes);
        if (!diff)
            return text;

        var rendered = GitDiff.Render(File.ReadAllText(resolved.BasePath), text, color);
        return string.IsNullOrWhiteSpace(rendered) ? "(no changes)" : rendered;
    }

    /// <summary>For a real run: the replace file's exact bytes. Throws, before anything is written, if it's still git-crypt encrypted — deploying ciphertext would be worse than failing.</summary>
    public static byte[] ForRealRun(string root, string resourcePath, ResolvedResource resolved)
    {
        var replacePath = resolved.ReplacePath!;
        if (GitCrypt.IsLocked(replacePath))
            throw new InvalidOperationException(
                $"'{resourcePath}' is replaced by '{LayerChain.ToRepoRelative(root, replacePath)}', which is still git-crypt " +
                "encrypted, so nothing was written.\nTry: run git-crypt unlock with this repo's key.");

        return File.ReadAllBytes(replacePath);
    }
}
