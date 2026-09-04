using System.Text;
using Microsoft.Web.XmlTransform;

namespace ConfigTransform.Xml;

/// <summary>
/// Applies an arbitrary-length chain of XDT overlays, in order, to an XML config file using
/// Microsoft.Web.Xdt — the same engine ASP.NET's own Web.config transforms use. Under
/// docs/SELF_DESCRIBING_OVERLAYS_DESIGN.md a chain's length varies with how deep its `extends`
/// nesting goes (no longer a fixed two-slot base→Environment→Client rule). Format-generic by
/// design: no App.config- or Web.config-specific logic here (see CLAUDE.md) — every entry point
/// takes plain file paths and XDT semantics.
/// </summary>
public static class XmlLayerMerger
{
    public static string Merge(string basePath, IReadOnlyList<string> patchPathsInOrder)
    {
        // XmlTransformableDocument (like the XmlDocument it derives from) does not implement
        // IDisposable — do not wrap it in a `using` statement.
        var document = new XmlTransformableDocument { PreserveWhitespace = true };
        document.Load(basePath);

        foreach (var patchPath in patchPathsInOrder)
            Apply(document, patchPath);

        // XmlDocument.Save(TextWriter) writes the XML declaration's encoding attribute from
        // writer.Encoding — a plain StringWriter reports UTF-16 (its in-memory
        // representation), regardless of how the caller later persists the returned string.
        // CliRunner's real-run path (--output) persists it via File.WriteAllText, which
        // defaults to UTF-8 — so the file would end up declaring "utf-16" while actually
        // being UTF-8 bytes. In-memory assertions (XDocument.Parse(string) in this project's
        // own tests) never surface this, because parsing an already-decoded .NET string
        // ignores the declared encoding entirely; only a real disk round-trip through a
        // standards-compliant parser does. Force the declaration to match what actually gets
        // written to disk.
        using var writer = new Utf8StringWriter();
        document.Save(writer);
        return writer.ToString();
    }

    private static void Apply(XmlTransformableDocument document, string transformPath)
    {
        using var transformation = new XmlTransformation(transformPath);
        if (!transformation.Apply(document))
            throw new InvalidOperationException($"XDT transform failed to apply: '{transformPath}'.");
    }

    private sealed class Utf8StringWriter : StringWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
    }
}
