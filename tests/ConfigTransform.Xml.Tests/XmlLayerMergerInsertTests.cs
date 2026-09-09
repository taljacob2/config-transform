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
        }
        finally
        {
            Directory.Delete(tmp.FullName, recursive: true);
        }
    }
}
