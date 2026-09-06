using System.Text;
using System.Xml;
using ConfigTransform.Core;

namespace ConfigTransform.Xml;

/// <summary>
/// Implements the "update an existing element" half of the <c>set</c> command
/// (docs/FIELD_AUTHORING_DESIGN.md): decides <c>xdt:Transform="SetAttributes"</c> mechanically
/// by checking whether the matched element already exists in the real, resolved document, and
/// writes the resulting overlay (or base-file) content. <c>Insert</c> — a genuinely brand-new
/// element with no existing evidence of its parent location — is deliberately not implemented
/// here yet; see the design doc's "Open items" and <see cref="Author"/>'s remarks.
/// </summary>
public static class XmlFieldAuthor
{
    private const string XdtNamespace = "http://schemas.microsoft.com/XML-Document-Transform";

    /// <summary>
    /// Reserved <c>--match</c> coordinate naming the element's own tag, not a real attribute —
    /// for a singleton element with no identifying attribute at all (<c>customErrors</c>,
    /// <c>compilation</c>, <c>httpRuntime</c>...), where real XDT itself matches by tag name alone
    /// and omits <c>xdt:Locator</c> entirely (docs/FIELD_AUTHORING_DESIGN.md's "What --match and
    /// --set mean, per format" → XML). Parallel to JSON's own reserved <c>key</c>/<c>literal-key</c>
    /// coordinates -- the same small, deliberate exception to XML's otherwise-open attribute
    /// vocabulary, accepted for the same reason: a real schema having an attribute literally named
    /// <c>tag</c> is a theoretical collision, not a practical one.
    /// </summary>
    private const string TagCoordinate = "tag";

    /// <param name="precedingXml">
    /// The document that exists immediately before this write's own layer would apply: the base
    /// file alone for an Environment-layer write, base+Environment merged for a Client-layer
    /// write, or the base file itself for a base-layer write (there is no "preceding" layer
    /// before the base).
    /// </param>
    /// <param name="existingTargetXml">
    /// Current content of the file being written, if it already exists — an Environment/Client
    /// overlay that a previous <c>set</c> (or hand-editing) already created, or the base file
    /// (which always exists). Null for an overlay file that doesn't exist yet.
    /// </param>
    /// <param name="isBaseTarget">
    /// True when writing directly to the base file: a plain document edit, no
    /// <c>xdt:Transform</c>/<c>xdt:Locator</c> involved at all, since the base file isn't an XDT
    /// overlay.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// No element matches (Insert is not yet supported — see class remarks), or more than one
    /// element matches (ambiguous — add more --match specs to narrow it down).
    /// </exception>
    public static string Author(
        string precedingXml,
        string? existingTargetXml,
        bool isBaseTarget,
        IReadOnlyList<MatchSpec> matches,
        IReadOnlyList<MatchSpec> setFields)
    {
        var preceding = new XmlDocument { PreserveWhitespace = true };
        preceding.Load(new StringReader(precedingXml));

        var candidates = FindMatchingElements(preceding.DocumentElement!, matches).ToList();

        if (candidates.Count == 0)
            throw new InvalidOperationException(NotFoundMessage(preceding, matches));

        if (candidates.Count > 1)
            throw new InvalidOperationException(AmbiguousMessage(candidates, matches));

        var matched = candidates[0];

        if (isBaseTarget)
        {
            // `preceding` for a base-layer write IS the base file's own current content -- edit
            // the matched element directly, no xdt: anything (the base file isn't an overlay).
            foreach (var field in setFields)
                matched.SetAttribute(field.Attribute, field.Value);

            return Serialize(preceding);
        }

        var ancestorPath = AncestorTagPath(matched);
        var target = existingTargetXml is null
            ? NewOverlayDocument(preceding.DocumentElement!.Name)
            : LoadExisting(existingTargetXml);

        EnsureXdtNamespaceDeclared(target);

        var container = FindOrCreateAncestorPath(target, ancestorPath);
        // The tag coordinate identifies the element to find, but it isn't a real attribute -- it
        // must never be written to the overlay or appear in the Locator string itself (the tag
        // name is already the overlay element's own name). When it's the *only* coordinate given,
        // the overlay carries no xdt:Locator at all, matching real XDT's own default-match
        // behavior for a singleton element with nothing else to identify it by.
        var attributeMatches = matches.Where(m => m.Attribute != TagCoordinate).ToList();
        var locatorAttrs = string.Join(",", attributeMatches.Select(m => m.Attribute));

        var existingOverlayElement = FindExistingOverlayElement(container, matched.Name, attributeMatches);
        var overlayElement = existingOverlayElement ?? target.CreateElement(matched.Name);

        foreach (var m in attributeMatches)
            overlayElement.SetAttribute(m.Attribute, m.Value);
        foreach (var field in setFields)
            overlayElement.SetAttribute(field.Attribute, field.Value);

        overlayElement.SetAttribute("Transform", XdtNamespace, "SetAttributes");
        if (attributeMatches.Count > 0)
            overlayElement.SetAttribute("Locator", XdtNamespace, $"Match({locatorAttrs})");

        if (existingOverlayElement is null)
            container.AppendChild(overlayElement);

        return Serialize(target);
    }

    private static IEnumerable<XmlElement> FindMatchingElements(XmlElement root, IReadOnlyList<MatchSpec> matches)
    {
        var tagMatches = matches.Where(m => m.Attribute == TagCoordinate).ToList();
        var attributeMatches = matches.Where(m => m.Attribute != TagCoordinate).ToList();

        return Descendants(root).Where(el =>
            tagMatches.All(t => el.Name == t.Value) &&
            attributeMatches.All(m => el.HasAttribute(m.Attribute) && el.GetAttribute(m.Attribute) == m.Value));
    }

    private static IEnumerable<XmlElement> Descendants(XmlElement root)
    {
        yield return root;
        foreach (var childNode in root.ChildNodes)
        {
            if (childNode is XmlElement child)
                foreach (var descendant in Descendants(child))
                    yield return descendant;
        }
    }

    private static IReadOnlyList<string> AncestorTagPath(XmlElement element)
    {
        var path = new List<string>();
        var current = element.ParentNode as XmlElement;
        while (current is not null)
        {
            path.Insert(0, current.Name);
            current = current.ParentNode as XmlElement;
        }
        return path;
    }

    private static XmlDocument NewOverlayDocument(string rootTagName)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.AppendChild(doc.CreateXmlDeclaration("1.0", "utf-8", null));
        var root = doc.CreateElement(rootTagName);
        doc.AppendChild(root);
        return doc;
    }

    private static XmlDocument LoadExisting(string existingXml)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.Load(new StringReader(existingXml));
        return doc;
    }

    private static void EnsureXdtNamespaceDeclared(XmlDocument doc)
    {
        var root = doc.DocumentElement!;
        if (root.GetAttribute("xmlns:xdt") == string.Empty)
            root.SetAttribute("xmlns:xdt", XdtNamespace);
    }

    /// <summary>
    /// The overlay's root already corresponds to <paramref name="path"/>'s implicit first
    /// segment (the base document's own root tag) -- so this walks the *rest* of the ancestor
    /// chain under the root, reusing existing elements by tag name when present (so re-running
    /// <c>set</c> against an overlay that already has other content in the same section doesn't
    /// duplicate it) and creating any missing ones.
    /// </summary>
    private static XmlElement FindOrCreateAncestorPath(XmlDocument target, IReadOnlyList<string> path)
    {
        var current = target.DocumentElement!;
        // path[0] is the base document's root tag name, already represented by `current` itself.
        foreach (var tag in path.Skip(1))
        {
            var child = current.ChildNodes.OfType<XmlElement>().FirstOrDefault(e => e.Name == tag);
            if (child is null)
            {
                child = target.CreateElement(tag);
                current.AppendChild(child);
            }
            current = child;
        }
        return current;
    }

    /// <summary>
    /// An element already carrying this exact --match locator (same tag, same matched
    /// attributes/values) -- re-running <c>set</c> for the same field updates it in place
    /// instead of adding a duplicate overlay entry.
    /// </summary>
    private static XmlElement? FindExistingOverlayElement(XmlElement container, string tagName, IReadOnlyList<MatchSpec> matches) =>
        container.ChildNodes.OfType<XmlElement>()
            .FirstOrDefault(e => e.Name == tagName && matches.All(m => e.HasAttribute(m.Attribute) && e.GetAttribute(m.Attribute) == m.Value));

    private static string NotFoundMessage(XmlDocument preceding, IReadOnlyList<MatchSpec> matches)
    {
        var description = string.Join(", ", matches.Select(m => $"{m.Attribute}={m.Value}"));
        var baseMessage = $"No element found matching {description}. This 'set' does not yet support " +
            "creating a brand-new element (no existing element to derive its parent location from — " +
            "docs/FIELD_AUTHORING_DESIGN.md's 'Open items').";

        var suggestion = SuggestAlternateAttribute(preceding, matches);
        return suggestion is null
            ? baseMessage
            : $"{baseMessage} Found an element with {suggestion.Value.attribute}=\"{suggestion.Value.value}\" " +
              $"instead — did you mean:\n  --match {suggestion.Value.attribute}={suggestion.Value.value}";
    }

    /// <summary>
    /// Only offered when exactly one --match was given and it used the bare/default form
    /// (docs/FIELD_AUTHORING_DESIGN.md's "Defaults") — an explicit --match that simply didn't
    /// find anything gets a plain not-found message, not a guessed suggestion.
    /// </summary>
    private static (string attribute, string value)? SuggestAlternateAttribute(XmlDocument preceding, IReadOnlyList<MatchSpec> matches)
    {
        if (matches.Count != 1 || !matches[0].WasDefaulted)
            return null;

        var value = matches[0].Value;
        foreach (var element in Descendants(preceding.DocumentElement!))
        {
            foreach (XmlAttribute attr in element.Attributes)
            {
                if (attr.Value == value && attr.Name != matches[0].Attribute)
                    return (attr.Name, value);
            }
        }

        return null;
    }

    private static string AmbiguousMessage(IReadOnlyList<XmlElement> candidates, IReadOnlyList<MatchSpec> matches)
    {
        var description = string.Join(", ", matches.Select(m => $"{m.Attribute}={m.Value}"));
        var listing = string.Join("\n", candidates.Select(c =>
            "  <" + c.Name + " " + string.Join(" ", c.Attributes.Cast<XmlAttribute>().Select(a => $"{a.Name}=\"{a.Value}\"")) + " />"));
        return $"More than one element matches {description} — add another --match to narrow it down:\n{listing}";
    }

    private static string Serialize(XmlDocument document)
    {
        using var writer = new Utf8StringWriter();
        document.Save(writer);
        return writer.ToString();
    }

    private sealed class Utf8StringWriter : StringWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
    }
}
