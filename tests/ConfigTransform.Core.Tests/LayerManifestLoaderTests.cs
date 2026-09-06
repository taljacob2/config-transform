using ConfigTransform.Core.Tests.TestSupport;
using Xunit;

namespace ConfigTransform.Core.Tests;

public class LayerManifestLoaderTests
{
    [Fact]
    public void Loads_a_valid_layer_manifest()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "configtransform.json");
        File.WriteAllText(path, """
            { "extends": ".configtransform/Environments/Production/configtransform.json", "resources": [ { "path": "Project/App.config", "patch": ".configtransform/Clients/Acme/Production/patch.xml" } ] }
            """);

        var manifest = LayerManifestLoader.Load(path);

        Assert.Equal(".configtransform/Environments/Production/configtransform.json", manifest.Extends);
        Assert.Single(manifest.Resources);
        Assert.Equal("Project/App.config", manifest.Resources[0].Path);
        Assert.Equal(".configtransform/Clients/Acme/Production/patch.xml", manifest.Resources[0].Patch);
    }

    [Fact]
    public void Extends_and_patch_are_optional()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "configtransform.json");
        File.WriteAllText(path, """{ "resources": [ { "path": "Project/App.config" } ] }""");

        var manifest = LayerManifestLoader.Load(path);

        Assert.Null(manifest.Extends);
        Assert.Null(manifest.Resources[0].Patch);
    }

    [Fact]
    public void Missing_file_throws_FileNotFoundException()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "configtransform.json");

        Assert.Throws<FileNotFoundException>(() => LayerManifestLoader.Load(path));
    }

    [Fact]
    public void Malformed_json_throws_InvalidOperationException()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "configtransform.json");
        File.WriteAllText(path, "{ this is not valid json");

        Assert.Throws<InvalidOperationException>(() => LayerManifestLoader.Load(path));
    }

    [Fact]
    public void Git_crypt_locked_layer_manifest_gives_an_actionable_message()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "configtransform.json");
        byte[] header = [0x00, (byte)'G', (byte)'I', (byte)'T', (byte)'C', (byte)'R', (byte)'Y', (byte)'P', (byte)'T', 0x00];
        byte[] fakeCiphertext = [0x9f, 0x02, 0x7c, 0x11, 0xab, 0x44];
        File.WriteAllBytes(path, [.. header, .. fakeCiphertext]);

        var ex = Assert.Throws<InvalidOperationException>(() => LayerManifestLoader.Load(path));

        Assert.Contains("git-crypt", ex.Message);
        Assert.Contains("git-crypt unlock", ex.Message);
    }
}
