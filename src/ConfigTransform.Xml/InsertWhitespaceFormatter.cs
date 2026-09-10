using System.Xml;

namespace ConfigTransform.Xml;

/// <summary>
/// Fixes a real, reported formatting artifact of `Microsoft.Web.Xdt`'s own `Insert` transform.
/// Two distinct symptoms, same root cause -- the library gives a freshly-inserted subtree none of
/// the surrounding whitespace a hand-formatted document would have:
///
/// 1. The new element is appended as its parent's last child with no separator of its own. The
///    parent's *existing* trailing whitespace (previously separating the old last child from the
///    closing tag) ends up repositioned as the new element's leading separator instead -- at the
///    closing tag's shallower indent, not the sibling depth it should line up at -- and nothing is
///    left between the new element and the closing tag. E.g. `&lt;deny .../&gt;` then
///    `&lt;allow .../&gt;&lt;/authorization&gt;`, with `allow` landing at `authorization`'s own
///    indent instead of under `deny`.
/// 2. When the inserted node is itself a whole multi-level subtree (a brand-new nested path, e.g.
///    via `set`'s `--match parent=`), every bit of whitespace *within* that subtree is gone too --
///    confirmed empirically, not assumed: a hand-authored, nicely-indented `Insert` patch still
///    collapses to one line internally. The subtree's own formatting in the patch file is not
///    preserved by the library at all, so it isn't something worth trying to preserve.
///
/// The merged XML is still well-formed either way (verified: `XmlDocument.Load` never throws on
/// it, and every parent's children are correctly nested) -- this is cosmetic, not a correctness
/// bug -- but it reads as broken to a human scanning a diff. `XmlLayerMerger.Apply` calls
/// <see cref="Fix"/> once per patch, right after `XmlTransformation.Apply`, passing the set of
/// element references that existed immediately before that patch ran; reference identity (not
/// name/attribute matching) is what lets this find exactly the nodes one specific `Insert` added,
/// with no name-based heuristics and no risk of reformatting whitespace the patch didn't touch.
/// </summary>
internal static class InsertWhitespaceFormatter
{
    private const string DefaultIndentUnit = "  ";

    public static HashSet<XmlElement> SnapshotElements(XmlDocument document) => [.. AllElements(document)];

    /// <summary>
    /// <paramref name="before"/> is every element reference present right before the transform
    /// that just ran; anything in the document now but not in that set is new. Only a new
    /// element's own root within the existing tree is reattached to its (pre-existing) siblings by
    /// reusing their real indentation; every descendant of that root is reformatted purely by
    /// depth, since none of them have a trustworthy pre-existing sibling to learn from -- the whole
    /// subtree came in with no whitespace of its own (symptom 2 above). The full list of roots is
    /// materialized before any mutation starts -- <see cref="AllElements"/> walks live `ChildNodes`
    /// lists, so touching the tree mid-walk would be unsafe.
    /// </summary>
    public static void Fix(XmlDocument document, HashSet<XmlElement> before)
    {
        var newRoots = AllElements(document)
            .Where(element => !before.Contains(element))
            .Where(element => element.ParentNode is not XmlElement parent || before.Contains(parent))
            .ToList();

        foreach (var root in newRoots)
        {
            SeparateFromSiblings(root);
            NormalizeDescendantIndentation(root, FindOwnIndent(root) ?? "\n");
        }
    }

    private static IEnumerable<XmlElement> AllElements(XmlNode root)
    {
        foreach (XmlNode child in root.ChildNodes)
        {
            if (child is not XmlElement element)
                continue;

            yield return element;
            foreach (var descendant in AllElements(element))
                yield return descendant;
        }
    }

    // Attaches `node` to its real, pre-existing parent: reuses another element child's own
    // leading whitespace when one exists (matches the file's real indentation exactly, no
    // guessing), otherwise falls back to one indent step deeper than the parent itself.
    private static void SeparateFromSiblings(XmlElement node)
    {
        if (node.ParentNode is not { } parent)
            return;

        var childIndent = FindSiblingIndent(node) ?? Indent(FindOwnIndent(parent), deeper: true);
        var closeIndent = FindOwnIndent(parent) ?? Indent(FindOwnIndent(parent), deeper: false);

        SetWhitespace(node, leading: true, childIndent);
        SetWhitespace(node, leading: false, closeIndent);
    }

    // Everything inside a freshly-inserted subtree arrives with no whitespace of its own (symptom
    // 2) -- there's no real sibling anywhere in here to learn from, so indentation is computed
    // purely from nesting depth off `parentIndent` (the indent `parent` itself was just given).
    private static void NormalizeDescendantIndentation(XmlElement parent, string parentIndent)
    {
        var elementChildren = parent.ChildNodes.Cast<XmlNode>().OfType<XmlElement>().ToList();
        if (elementChildren.Count == 0)
            return;

        var childIndent = parentIndent + DefaultIndentUnit;

        foreach (var child in elementChildren)
        {
            SetWhitespace(child, leading: true, childIndent);
            NormalizeDescendantIndentation(child, childIndent);
        }

        // Only the last child needs a trailing separator -- every earlier child is already
        // separated from its next sibling by that sibling's own just-set leading whitespace.
        SetWhitespace(elementChildren[^1], leading: false, parentIndent);
    }

    // Always sets the separator to the computed indent, never just "insert if entirely absent" --
    // symptom 1 leaves *some* whitespace node adjacent to the new element (the old separator,
    // repositioned), just at the wrong depth, so a presence-only check would leave it uncorrected.
    private static void SetWhitespace(XmlElement node, bool leading, string indent)
    {
        var document = node.OwnerDocument!;
        var neighbor = leading ? node.PreviousSibling : node.NextSibling;

        if (IsNewlineWhitespace(neighbor))
        {
            if (neighbor!.Value != indent)
                neighbor.Value = indent;
            return;
        }

        var whitespace = document.CreateWhitespace(indent);
        if (leading)
            node.ParentNode!.InsertBefore(whitespace, node);
        else
            node.ParentNode!.InsertAfter(whitespace, node);
    }

    // Reuses another element child's own already-correct leading whitespace, when one exists.
    // Deliberately does not consider `node`'s own current (pre-fix) leading whitespace as a
    // candidate reference -- Insert always appends the new element as the parent's last child, so
    // any whitespace already sitting next to it is the displaced old separator, not a trustworthy
    // sample of the sibling indent.
    private static string? FindSiblingIndent(XmlElement node)
    {
        if (node.ParentNode is not { } parent)
            return null;

        foreach (XmlNode sibling in parent.ChildNodes)
        {
            if (sibling == node || sibling is not XmlElement || !IsNewlineWhitespace(sibling.PreviousSibling))
                continue;

            return sibling.PreviousSibling!.Value;
        }

        return null;
    }

    // The whitespace immediately before `node` itself, in ITS OWN parent -- i.e. `node`'s own
    // indentation level. A closing tag lines up at the same indent as its opening tag, so this is
    // exactly the trailing separator a freshly-appended last child needs before it.
    private static string? FindOwnIndent(XmlNode node) =>
        IsNewlineWhitespace(node.PreviousSibling) ? node.PreviousSibling!.Value : null;

    // Fallback for a container with no other children to learn indentation from at all (a whole
    // new nested path just created for this Insert) -- degrades to a fixed two-space step off
    // whatever indentation IS known, rather than refusing to format at all.
    private static string Indent(string? knownIndent, bool deeper) =>
        (knownIndent ?? "\n") + (deeper ? DefaultIndentUnit : "");

    private static bool IsNewlineWhitespace(XmlNode? node) =>
        node is { NodeType: XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace or XmlNodeType.Text }
        && node.Value is { } value && value.Contains('\n') && string.IsNullOrWhiteSpace(value);
}
