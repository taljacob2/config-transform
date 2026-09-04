using System.Xml.Linq;
using ConfigTransform.Core;
using Xunit;

namespace ConfigTransform.Xml.Tests;

public class XmlLayerMergerTests
{
    private static string FixturesRoot => Path.Combine(AppContext.BaseDirectory, "Fixtures", "DotNetFramework");
    private const string ResourcePath = "Project/App.config";

    [Fact]
    public void Merges_base_environment_and_client_layers_end_to_end()
    {
        var resolved = Resolve("ClientA", "Production");
        var merged = XmlLayerMerger.Merge(resolved.BasePath, resolved.PatchPathsInOrder);

        var doc = XDocument.Parse(merged);
        var appSettings = doc.Root!.Element("appSettings")!;

        Assert.Equal("https://clienta.example.com", GetAppSetting(appSettings, "ApiUrl"));
        Assert.Equal("60", GetAppSetting(appSettings, "Timeout"));

        var connectionString = doc.Root!.Element("connectionStrings")!
            .Elements("add").Single(e => (string)e.Attribute("name")! == "Main")
            .Attribute("connectionString")!.Value;
        Assert.Equal("Server=clienta-prod-db;Database=App;", connectionString);
    }

    [Fact]
    public void Applies_only_base_when_no_overlays_match()
    {
        // Neither Environments/Staging nor Clients/ClientB/Staging exist.
        var resolved = Resolve("ClientB", "Staging");
        Assert.Empty(resolved.PatchPathsInOrder);

        var merged = XmlLayerMerger.Merge(resolved.BasePath, resolved.PatchPathsInOrder);
        var doc = XDocument.Parse(merged);
        var appSettings = doc.Root!.Element("appSettings")!;

        Assert.Equal("https://dev.example.com", GetAppSetting(appSettings, "ApiUrl"));
        Assert.Equal("30", GetAppSetting(appSettings, "Timeout"));
    }

    [Fact]
    public void Applies_environment_layer_only_when_client_has_no_override()
    {
        // ClientB's own configtransform.json declares only `extends` (no resources of its own)
        // -- the "accepted cost" workaround the design doc names explicitly: unlike the old
        // fixed base->Environments->Clients rule, a Client layer must still exist on disk (even
        // resource-less) for the Environment layer's content to flow through to it at all.
        var resolved = Resolve("ClientB", "Production");
        Assert.Single(resolved.PatchPathsInOrder);

        var merged = XmlLayerMerger.Merge(resolved.BasePath, resolved.PatchPathsInOrder);
        var doc = XDocument.Parse(merged);
        var appSettings = doc.Root!.Element("appSettings")!;

        // ApiUrl is untouched (only ClientA's overlay changes it); Timeout picks up the
        // environment-wide value shared across every client in Production.
        Assert.Equal("https://dev.example.com", GetAppSetting(appSettings, "ApiUrl"));
        Assert.Equal("60", GetAppSetting(appSettings, "Timeout"));
    }

    [Fact]
    public void Merged_result_survives_a_real_disk_round_trip_through_a_strict_parser()
    {
        // XDocument.Parse(string), used by every other test here, parses an already-decoded
        // .NET string and ignores whatever encoding the XML declaration claims — so it cannot
        // catch a declared-vs-actual encoding mismatch. CliRunner's real (--output) path
        // instead writes the merged string to disk via File.WriteAllText (UTF-8), then a
        // consumer reads those bytes back. Reproduce that here: write to a real file and load
        // it with XDocument.Load(path), which does honor the declared encoding, the same way
        // any standards-compliant XML parser reading the file from disk would.
        var resolved = Resolve("ClientA", "Production");
        var merged = XmlLayerMerger.Merge(resolved.BasePath, resolved.PatchPathsInOrder);

        var tempPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempPath, merged);
            var doc = XDocument.Load(tempPath);
            Assert.Equal("https://clienta.example.com",
                GetAppSetting(doc.Root!.Element("appSettings")!, "ApiUrl"));
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    private static string GetAppSetting(XElement appSettings, string key) =>
        appSettings.Elements("add").Single(e => (string)e.Attribute("key")! == key).Attribute("value")!.Value;

    private static ResolvedResource Resolve(string client, string environment)
    {
        var chain = LayerChain.Build(FixturesRoot, LayerPathResolver.Resolve(FixturesRoot, client, environment));
        return LayerChain.ResolveResource(FixturesRoot, chain, ResourcePath);
    }
}
