using System.Xml.Linq;
using ConfigTransform.Core;
using Xunit;

namespace ConfigTransform.Xml.Tests;

/// <summary>
/// Merge-time (not set-time) proof that an <c>xdt:Transform="Insert"</c> overlay authored by
/// <see cref="XmlFieldAuthor.Author"/> actually creates the new element correctly through
/// <see cref="XmlLayerMerger.Merge"/> and real <c>Microsoft.Web.Xdt</c> -- the merge-time
/// counterpart to XmlFieldAuthorTests' set-authoring-time Insert coverage, mirroring
/// XmlLayerMergerArrayMatchTests' role for compound-Locator matching.
/// </summary>
public class XmlLayerMergerInsertTests
{
    [Fact]
    public void Insert_into_an_existing_container_adds_a_second_sibling_without_disturbing_the_first()
    {
        var tmp = Directory.CreateTempSubdirectory("insert-merge-existing-");
        try
        {
            var basePath = Path.Combine(tmp.FullName, "base.config");
            File.WriteAllText(basePath, """
                <configuration>
                  <appSettings>
                    <add key="ApiUrl" value="https://dev.example.com" />
                  </appSettings>
                </configuration>
                """);

            var overlay = XmlFieldAuthor.Author(
                precedingXml: File.ReadAllText(basePath),
                existingTargetXml: null,
                isBaseTarget: false,
                matches:
                [
                    new MatchSpec("parent", "appSettings", WasDefaulted: false),
                    new MatchSpec("tag", "add", WasDefaulted: false),
                    new MatchSpec("key", "FeatureFlag", WasDefaulted: false)
                ],
                setFields: [new MatchSpec("value", "on", WasDefaulted: false)]);

            var patchPath = Path.Combine(tmp.FullName, "patch.config.xml");
            File.WriteAllText(patchPath, overlay);

            var merged = XmlLayerMerger.Merge(basePath, [patchPath]);
            var adds = XDocument.Parse(merged).Root!.Element("appSettings")!.Elements("add").ToList();

            Assert.Equal(2, adds.Count);
            Assert.Contains(adds, e => (string)e.Attribute("key")! == "ApiUrl" && (string)e.Attribute("value")! == "https://dev.example.com");
            Assert.Contains(adds, e => (string)e.Attribute("key")! == "FeatureFlag" && (string)e.Attribute("value")! == "on");
        }
        finally
        {
            Directory.Delete(tmp.FullName, recursive: true);
        }
    }

    [Fact]
    public void Insert_lines_up_with_an_existing_sibling_and_leaves_the_closing_tag_on_its_own_line()
    {
        // Reproduces a real user report: Microsoft.Web.Xdt's own Insert transform appends the new
        // element as the parent's last child with no whitespace of its own, so it lands glued onto
        // the closing tag -- `<deny users="?" />` then `<allow users="acme-admin" /></authorization>`
        // all on one line, with `allow` at `authorization`'s own (shallower) indent instead of lining
        // up under `deny`. Still well-formed XML (verified independently against XmlDocument.Load),
        // but reads as broken to a human scanning a diff -- InsertWhitespaceFormatter fixes exactly
        // this, without touching any whitespace the patch didn't add.
        var tmp = Directory.CreateTempSubdirectory("insert-merge-whitespace-");
        try
        {
            var basePath = Path.Combine(tmp.FullName, "base.config");
            File.WriteAllText(basePath, """
                <configuration>
                  <location path="Admin">
                    <system.web>
                      <authorization>
                        <deny users="?" />
                      </authorization>
                    </system.web>
                  </location>
                </configuration>
                """);

            var patchPath = Path.Combine(tmp.FullName, "patch.config.xml");
            File.WriteAllText(patchPath, """
                <configuration xmlns:xdt="http://schemas.microsoft.com/XML-Document-Transform">
                  <location path="Admin">
                    <system.web>
                      <authorization>
                        <allow xdt:Transform="Insert" users="acme-admin" />
                      </authorization>
                    </system.web>
                  </location>
                </configuration>
                """);

            var merged = XmlLayerMerger.Merge(basePath, [patchPath]);

            Assert.Contains(
                "      <authorization>\n" +
                "        <deny users=\"?\" />\n" +
                "        <allow users=\"acme-admin\" />\n" +
                "      </authorization>",
                merged);
        }
        finally
        {
            Directory.Delete(tmp.FullName, recursive: true);
        }
    }

    [Fact]
    public void Insert_into_a_container_that_does_not_exist_yet_creates_the_whole_nested_path()
    {
        var tmp = Directory.CreateTempSubdirectory("insert-merge-fresh-");
        try
        {
            var basePath = Path.Combine(tmp.FullName, "base.config");
            File.WriteAllText(basePath, "<configuration></configuration>");

            var overlay = XmlFieldAuthor.Author(
                precedingXml: File.ReadAllText(basePath),
                existingTargetXml: null,
                isBaseTarget: false,
                matches:
                [
                    new MatchSpec("parent", "system.webServer/rewrite/rules", WasDefaulted: false),
                    new MatchSpec("tag", "rule", WasDefaulted: false)
                ],
                setFields:
                [
                    new MatchSpec("name", "WWW-Redirect", WasDefaulted: false),
                    new MatchSpec("enabled", "true", WasDefaulted: false)
                ]);

            var patchPath = Path.Combine(tmp.FullName, "patch.config.xml");
            File.WriteAllText(patchPath, overlay);

            var merged = XmlLayerMerger.Merge(basePath, [patchPath]);
            var rule = XDocument.Parse(merged).Root!
                .Element("system.webServer")!.Element("rewrite")!.Element("rules")!
                .Element("rule")!;

            Assert.Equal("WWW-Redirect", (string)rule.Attribute("name")!);
            Assert.Equal("true", (string)rule.Attribute("enabled")!);

            // Microsoft.Web.Xdt gives a freshly-inserted multi-level subtree none of the patch
            // file's own whitespace at all (confirmed empirically, not assumed -- a hand-authored,
            // nicely-indented Insert still collapses to one line internally); InsertWhitespaceFormatter
            // reformats every level of the new subtree by nesting depth, not just its attachment point.
            Assert.Contains(
                "<system.webServer>\n" +
                "    <rewrite>\n" +
                "      <rules>\n" +
                "        <rule name=\"WWW-Redirect\" enabled=\"true\" />\n" +
                "      </rules>\n" +
                "    </rewrite>\n" +
                "  </system.webServer>",
                merged);
        }
        finally
        {
            Directory.Delete(tmp.FullName, recursive: true);
        }
    }
}
