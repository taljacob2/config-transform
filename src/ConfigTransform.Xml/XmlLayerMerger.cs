using Microsoft.Web.XmlTransform;

namespace ConfigTransform.Xml;

/// <summary>
/// Applies the base -&gt; Environments -&gt; Clients layering (CONFIG_MANAGEMENT.md §5.1) to an
/// XML config file using Microsoft.Web.Xdt — the same engine ASP.NET's own Web.config
/// transforms use. Format-generic by design: no App.config- or Web.config-specific logic here
/// (see CLAUDE.md) — every entry point takes plain file paths and XDT semantics.
/// </summary>
public static class XmlLayerMerger
{
    public static string Merge(string basePath, string? environmentOverlayPath, string? clientOverlayPath)
    {
        // XmlTransformableDocument (like the XmlDocument it derives from) does not implement
        // IDisposable — do not wrap it in a `using` statement.
        var document = new XmlTransformableDocument { PreserveWhitespace = true };
        document.Load(basePath);

        if (environmentOverlayPath is not null)
            Apply(document, environmentOverlayPath);

        if (clientOverlayPath is not null)
            Apply(document, clientOverlayPath);

        using var writer = new StringWriter();
        document.Save(writer);
        return writer.ToString();
    }

    private static void Apply(XmlTransformableDocument document, string transformPath)
    {
        using var transformation = new XmlTransformation(transformPath);
        if (!transformation.Apply(document))
            throw new InvalidOperationException($"XDT transform failed to apply: '{transformPath}'.");
    }
}
