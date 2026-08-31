using ConfigTransform.Core.Tests.TestSupport;
using Xunit;

namespace ConfigTransform.Core.Tests;

public class ManifestListerTests
{
    [Fact]
    public void Lists_environments_and_clients_that_actually_exist_on_disk()
    {
        using var dir = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(dir.Path, "App.config", "Environments"));
        Directory.CreateDirectory(Path.Combine(dir.Path, "App.config", "Clients", "Acme"));
        Directory.CreateDirectory(Path.Combine(dir.Path, "App.config", "Clients", "Globex"));
        File.WriteAllText(Path.Combine(dir.Path, "App.config", "Environments", "Production.config"), "<x/>");
        File.WriteAllText(Path.Combine(dir.Path, "App.config", "Environments", "Staging.config"), "<x/>");
        File.WriteAllText(Path.Combine(dir.Path, "App.config", "Clients", "Acme", "Production.config"), "<x/>");

        var manifest = new Manifest(dir.Path, new[]
        {
            new ManifestFileEntry("App.config", "xml")
        });

        var stdout = new StringWriter();
        ManifestLister.List(manifest, fileArg: null, dir.Path, stdout);
        var output = stdout.ToString();

        Assert.Contains("App.config (xml)", output);
        Assert.Contains("environments: Production, Staging", output);
        Assert.Contains("Acme: Production", output);
        Assert.Contains("Globex: (none)", output);
    }

    [Fact]
    public void No_overlay_directories_at_all_reports_none_rather_than_throwing()
    {
        using var dir = new TempDirectory();

        var manifest = new Manifest(dir.Path, new[]
        {
            new ManifestFileEntry("App.config", "xml")
        });

        var stdout = new StringWriter();
        ManifestLister.List(manifest, fileArg: null, dir.Path, stdout);
        var output = stdout.ToString();

        Assert.Contains("environments: (none)", output);
        Assert.Contains("clients: (none)", output);
    }

    [Fact]
    public void No_file_arg_lists_every_entry()
    {
        using var dir = new TempDirectory();
        var manifest = new Manifest(dir.Path, new[]
        {
            new ManifestFileEntry("App.config", "xml"),
            new ManifestFileEntry("NLog.config", "xml")
        });

        var stdout = new StringWriter();
        ManifestLister.List(manifest, fileArg: null, dir.Path, stdout);
        var output = stdout.ToString();

        Assert.Contains("App.config (xml)", output);
        Assert.Contains("NLog.config (xml)", output);
    }

    [Fact]
    public void File_arg_filters_to_the_matching_entry_by_relativeToDirectory()
    {
        using var dir = new TempDirectory();
        var manifest = new Manifest(dir.Path, new[]
        {
            new ManifestFileEntry("App.config", "xml"),
            new ManifestFileEntry("NLog.config", "xml")
        });

        var stdout = new StringWriter();
        ManifestLister.List(manifest, fileArg: "NLog.config", dir.Path, stdout);
        var output = stdout.ToString();

        Assert.DoesNotContain("App.config (xml)", output);
        Assert.Contains("NLog.config (xml)", output);
    }

    [Fact]
    public void File_arg_matches_the_overlay_name_case_insensitively()
    {
        using var dir = new TempDirectory();
        var manifest = new Manifest(dir.Path, new[]
        {
            new ManifestFileEntry("sub/config.xml", "xml", "MainConfig")
        });

        var stdout = new StringWriter();
        ManifestLister.List(manifest, fileArg: "mainconfig", dir.Path, stdout);
        var output = stdout.ToString();

        Assert.Contains("sub/config.xml (xml)", output);
    }

    [Fact]
    public void Unmatched_file_arg_throws()
    {
        using var dir = new TempDirectory();
        var manifest = new Manifest(dir.Path, new[]
        {
            new ManifestFileEntry("App.config", "xml")
        });

        Assert.Throws<ArgumentException>(() =>
            ManifestLister.List(manifest, fileArg: "NoSuchFile", dir.Path, new StringWriter()));
    }
}
