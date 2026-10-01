namespace ConfigTransform.Core;

/// <summary>
/// A text file's line-ending style and whether it ends with a newline -- read from an existing file
/// so that what the tool writes back keeps that file's own conventions. Without this, JSON output
/// had no final newline and both JSON and YAML used the platform's newline (CRLF on Windows), so a
/// `set` against a base file stripped its final newline and could flip every line ending -- churn
/// in a committed file -- and a CI log's `cat` of merged JSON glued the next line onto its closing
/// brace. XML already gets this for free (`XmlLayerMerger` preserves the base's whitespace).
/// Format-agnostic, so shared from Core; used by the JSON/YAML engines and by
/// <see cref="LayerManifestSerializer"/>.
/// </summary>
public readonly record struct TextLayout(string NewLine, bool EndsWithNewLine)
{
    /// <summary>For a brand-new file, with nothing to mirror: LF, ending with a newline.</summary>
    public static readonly TextLayout Default = new("\n", EndsWithNewLine: true);

    /// <summary>CRLF if <paramref name="text"/> uses it anywhere, LF otherwise; empty text gets <see cref="Default"/>.</summary>
    public static TextLayout Of(string text) =>
        text.Length == 0
            ? Default
            : new(text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n", text.EndsWith('\n'));

    /// <summary>
    /// Rewrites <paramref name="output"/>'s line endings to this layout's and adds or removes the
    /// final newline. Only one final newline is ever removed, so content that legitimately ends in
    /// blank lines (a YAML <c>|+</c> block at the end of a document) keeps the rest.
    /// </summary>
    public string Apply(string output)
    {
        var body = output.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (body.EndsWith('\n'))
            body = body[..^1];
        if (NewLine != "\n")
            body = body.Replace("\n", NewLine, StringComparison.Ordinal);
        return EndsWithNewLine ? body + NewLine : body;
    }
}
