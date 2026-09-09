using System.Text;
using System.Xml;
using ConfigTransform.Core;

namespace ConfigTransform.Xml;

/// <summary>
/// Implements the "update an existing element" half of the <c>set</c> command
/// (docs/FIELD_AUTHORING_DESIGN.md): decides <c>xdt:Transform="SetAttributes"</c> mechanically
/// by checking whether the matched element already exists in the real, resolved document, and
/// writes the resulting overlay (or base-file) content. It also implements <c>Insert</c> — a
/// genuinely brand-new element — via the reserved <c>parent=&lt;ancestor/tag/path&gt;</c>
/// <c>--match</c> coordinate (alongside <c>tag=&lt;NewElementName&gt;</c>), which supplies the
/// one piece of information an update never needs to be told explicitly: where the new element
/// belongs. See <see cref="Author"/>'s remarks and docs/FIELD_AUTHORING_DESIGN.md's decision log
/// for why this shape was chosen over a dedicated CLI flag.
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
    /// <c>tag</c> is a theoretical collision, not a practical one. Also names the new element's own
    /// tag for an Insert (below), alongside <see cref="ParentCoordinate"/>.
    /// </summary>
    private const string TagCoordinate = "tag";

    /// <summary>
    /// Reserved <c>--match</c> coordinate naming, as a <c>/</c>-separated ancestor tag path
    /// relative to the document root (never including the root tag itself — that's always
    /// implicit, the same convention <see cref="AncestorTagPath"/> already uses internally), where
    /// a genuinely new element belongs. Only consulted when no real element matches the rest of
    /// the given <c>--match</c> specs at all -- a real match always wins (Insert is never guessed
    /// at when an update is possible). Requires <see cref="TagCoordinate"/> alongside it, to name
    /// the new element itself. See the class remarks and docs/FIELD_AUTHORING_DESIGN.md.
    /// </summary>
    private const string ParentCoordinate = "parent";

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
    /// No element matches and no <c>parent=</c>/<c>tag=</c> pair was given to Insert one (see
    /// class remarks), or more than one element matches (ambiguous — add more --match specs to
    /// narrow it down).
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

        var parentMatch = matches.FirstOrDefault(m => m.Attribute == ParentCoordinate);
        // `parent` is pure location metadata for a not-yet-existing element -- never a real
        // attribute or the tag itself, so it's excluded from every real-element lookup below the
        // same way TagCoordinate already is.
        var elementMatches = matches.Where(m => m.Attribute != ParentCoordinate).ToList();
        var tagMatch = elementMatches.FirstOrDefault(m => m.Attribute == TagCoordinate);

        // Checked before FindMatchingElements runs at all, but only for the genuinely vacuous
        // case (--match parent=... with nothing else at all): an empty elementMatches list would
        // otherwise make FindMatchingElements match against *every* element in the document --
        // a false "ambiguous", not the real problem (there's no tag to Insert as). When
        // elementMatches is non-empty (e.g. a real attribute match with no `tag`), let
        // FindMatchingElements run normally -- a real match must still win over an Insert hint
        // even when it wasn't matched via `tag`.
        if (parentMatch is not null && elementMatches.Count == 0)
            throw new InvalidOperationException(
                "Insert needs --match tag=<NewElementName> to name the new element, " +
                $"alongside --match parent={parentMatch.Value}.");

        var candidates = FindMatchingElements(preceding.DocumentElement!, elementMatches).ToList();

        if (candidates.Count == 0)
        {
            if (parentMatch is null)
                throw new InvalidOperationException(NotFoundMessage(preceding, elementMatches));

            if (tagMatch is null)
                throw new InvalidOperationException(
                    "Insert needs --match tag=<NewElementName> to name the new element, " +
                    $"alongside --match parent={parentMatch.Value}.");

            return AuthorInsert(preceding, existingTargetXml, isBaseTarget, parentMatch.Value, tagMatch.Value, elementMatches, setFields);
        }

        if (candidates.Count > 1)
            throw new InvalidOperationException(AmbiguousMessage(candidates, elementMatches));

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

        var (container, _) = FindOrCreateOverlayPath(target, preceding.DocumentElement!, ancestorPath);
        // The tag coordinate identifies the element to find, but it isn't a real attribute -- it
        // must never be written to the overlay or appear in the Locator string itself (the tag
        // name is already the overlay element's own name). When it's the *only* coordinate given,
        // the overlay carries no xdt:Locator at all, matching real XDT's own default-match
        // behavior for a singleton element with nothing else to identify it by.
        var attributeMatches = elementMatches.Where(m => m.Attribute != TagCoordinate).ToList();
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

    /// <summary>
    /// Creates a genuinely new element (real XDT's own <c>Insert</c>) under <paramref
    /// name="parentPath"/> — a <c>/</c>-separated ancestor tag path relative to the document
    /// root. Verified empirically against real <c>Microsoft.Web.Xdt</c> before writing this:
    /// <c>Insert</c> takes no <c>Locator</c> at all, and marking the *shallowest* ancestor that
    /// doesn't already exist in the resolved document is enough -- XDT copies that element's
    /// whole subtree (attributes and descendants) as a single unit, so every deeper container and
    /// the new leaf element itself are just plain nested XML underneath it, no further
    /// <c>xdt:Transform</c> attributes needed anywhere below that one marked point. Re-running the
    /// same Insert call updates the previously-inserted element in place (via the same
    /// <see cref="FindExistingOverlayElement"/> re-run machinery the update path already uses),
    /// rather than appending a duplicate -- but with no real attribute match beyond
    /// <paramref name="newTag"/> to key off, a *second, distinct* new element sharing that same
    /// tag under the same parent needs at least one real identifying <c>--match</c> attribute to
    /// disambiguate; a genuinely unqualified repeat call just updates the one already inserted.
    /// This is a deliberate, named scope limit, not an oversight -- see
    /// docs/FIELD_AUTHORING_DESIGN.md.
    /// </summary>
    private static string AuthorInsert(
        XmlDocument preceding,
        string? existingTargetXml,
        bool isBaseTarget,
        string parentPath,
        string newTag,
        IReadOnlyList<MatchSpec> elementMatches,
        IReadOnlyList<MatchSpec> setFields)
    {
        var fullPath = new List<string> { preceding.DocumentElement!.Name };
        fullPath.AddRange(parentPath.Split('/', StringSplitOptions.RemoveEmptyEntries));
        var attributeMatches = elementMatches.Where(m => m.Attribute != TagCoordinate).ToList();

        if (isBaseTarget)
        {
            // Editing the real document directly -- no xdt: anything, the base file isn't an
            // overlay. Uses the plain (non-preceding-aware) ancestor-path helper since target and
            // "preceding" are the same document here; there's nothing to compare against.
            var baseContainer = FindOrCreateAncestorPath(preceding, fullPath);
            var existingBaseElement = FindExistingOverlayElement(baseContainer, newTag, attributeMatches);
            var baseElement = existingBaseElement ?? preceding.CreateElement(newTag);

            foreach (var m in attributeMatches)
                baseElement.SetAttribute(m.Attribute, m.Value);
            foreach (var field in setFields)
                baseElement.SetAttribute(field.Attribute, field.Value);

            if (existingBaseElement is null)
                baseContainer.AppendChild(baseElement);

            return Serialize(preceding);
        }

        var target = existingTargetXml is null
            ? NewOverlayDocument(preceding.DocumentElement!.Name)
            : LoadExisting(existingTargetXml);

        EnsureXdtNamespaceDeclared(target);

        var (container, ancestorInsertOccurred) = FindOrCreateOverlayPath(target, preceding.DocumentElement!, fullPath);

        var existingOverlayElement = FindExistingOverlayElement(container, newTag, attributeMatches);
        var overlayElement = existingOverlayElement ?? target.CreateElement(newTag);

        foreach (var m in attributeMatches)
            overlayElement.SetAttribute(m.Attribute, m.Value);
        foreach (var field in setFields)
            overlayElement.SetAttribute(field.Attribute, field.Value);

        // Only the shallowest new point in the tree needs xdt:Transform="Insert" -- if an
        // ancestor container was itself freshly created above (ancestorInsertOccurred), it
        // already carries the marker and this leaf is just part of its copied subtree.
        if (existingOverlayElement is null && !ancestorInsertOccurred)
            overlayElement.SetAttribute("Transform", XdtNamespace, "Insert");

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
    /// Same reuse-by-tag-name/create-if-missing walk as <see cref="FindOrCreateAncestorPath"/>,
    /// generalized to also walk <paramref name="precedingRoot"/> (the real, resolved document) in
    /// lockstep and detect the shallowest ancestor that doesn't already exist there. When it
    /// doesn't -- <paramref name="path"/> was derived from a real matched element's own ancestry
    /// (<see cref="AncestorTagPath"/>), so by construction the whole chain already exists and no
    /// element gets marked -- this is exactly <see cref="FindOrCreateAncestorPath"/>'s old
    /// behavior, now expressed as this function's always-false case. When it doesn't (an Insert's
    /// explicit <c>parent=</c> path can run past what's real), that one ancestor element is marked
    /// <c>xdt:Transform="Insert"</c> -- real XDT copies its whole subtree as a unit, so nothing
    /// deeper needs its own marker (verified empirically, see <see cref="AuthorInsert"/>).
    /// </summary>
    /// <returns>The resolved container element, and whether an ancestor along the way was freshly
    /// created and marked for Insert (false for every pre-existing call site).</returns>
    private static (XmlElement container, bool ancestorInsertOccurred) FindOrCreateOverlayPath(
        XmlDocument target, XmlElement precedingRoot, IReadOnlyList<string> path)
    {
        var current = target.DocumentElement!;
        var precedingCurrent = (XmlElement?)precedingRoot;
        var insertMarked = false;

        // path[0] is the document root tag, already represented by both `current` and
        // `precedingRoot` themselves.
        foreach (var tag in path.Skip(1))
        {
            var child = current.ChildNodes.OfType<XmlElement>().FirstOrDefault(e => e.Name == tag);
            var isNewOverlayElement = child is null;
            if (child is null)
            {
                child = target.CreateElement(tag);
                current.AppendChild(child);
            }

            var precedingChild = precedingCurrent?.ChildNodes.OfType<XmlElement>().FirstOrDefault(e => e.Name == tag);

            if (precedingChild is null && isNewOverlayElement && !insertMarked)
            {
                child.SetAttribute("Transform", XdtNamespace, "Insert");
                insertMarked = true;
            }

            precedingCurrent = precedingChild;
            current = child;
        }

        return (current, insertMarked);
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
        var baseMessage = $"No element found matching {description}. To insert a brand-new element, " +
            "add --match parent=<ancestor/tag/path> alongside --match tag=<NewElementName> to say where " +
            "it goes (see docs/FIELD_AUTHORING_DESIGN.md's Insert section).";

        var suggestion = SuggestAlternateAttribute(preceding, matches);
        return suggestion is null
            ? baseMessage
            : $"{baseMessage} Found an element with {suggestion.Value.attribute}=\"{suggestion.Value.value}\" " +
              $"instead -- did you mean:\n  --match {suggestion.Value.attribute}={suggestion.Value.value}";
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
        return $"More than one element matches {description} -- add another --match to narrow it down:\n{listing}";
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
