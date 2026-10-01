using System.Text;
using System.Xml;
using ConfigTransform.Core;

namespace ConfigTransform.Xml;

/// <summary>
/// Secret substitution for XML (docs/SECRETS_DESIGN.md): <c>{{CFSECRET_…}}</c> placeholders are
/// replaced inside attribute values and text/CDATA content, and the document is written back the
/// same way <see cref="XmlLayerMerger"/> writes it — so a value containing <c>&lt;</c>,
/// <c>&amp;</c> or a quote is escaped by the XML writer instead of breaking the file. Comments and
/// processing instructions are left alone; a placeholder left there is caught by the caller's
/// leftover-placeholder check on a real run.
/// </summary>
public static class XmlSecretSubstitution
{
    public static string Substitute(string content, Func<string, string?> resolve)
    {
        if (!SecretPlaceholders.ContainsMarker(content))
            return content;

        var document = new XmlDocument { PreserveWhitespace = true };
        document.LoadXml(content);
        Visit(document, resolve);

        using var writer = new Utf8StringWriter();
        document.Save(writer);
        return writer.ToString();
    }

    private static void Visit(XmlNode node, Func<string, string?> resolve)
    {
        if (node.Attributes is { } attributes)
            foreach (XmlAttribute attribute in attributes)
                attribute.Value = SecretPlaceholders.Replace(attribute.Value, resolve);

        foreach (XmlNode child in node.ChildNodes)
        {
            if (child is XmlText or XmlCDataSection)
                child.Value = SecretPlaceholders.Replace(child.Value ?? "", resolve);
            else if (child is XmlElement)
                Visit(child, resolve);
        }
    }

    /// <summary>Same reason as <see cref="XmlLayerMerger"/>'s: the declaration must say UTF-8, which is what lands on disk.</summary>
    private sealed class Utf8StringWriter : StringWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
    }
}
