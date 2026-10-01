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

    [Fact]
    public void Loads_the_secrets_list()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "configtransform.json");
        File.WriteAllText(path, """
            { "secrets": [ ".configtransform/Environments/Production/db.secret.env" ], "resources": [] }
            """);

        var manifest = LayerManifestLoader.Load(path);

        Assert.Equal([".configtransform/Environments/Production/db.secret.env"], manifest.Secrets);
    }

    [Theory]
    [InlineData(".configtransform/Environments/Production/db.env")]
    [InlineData(".configtransform/Environments/Production/db.secrets.env")]
    [InlineData(".configtransform/Environments/Production/db.SECRET.env")]
    public void A_secrets_file_not_ending_in_dot_secret_dot_env_is_rejected(string entry)
    {
        // The suffix is the only thing tying the file to the *.secret.* git-crypt rule.
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "configtransform.json");
        File.WriteAllText(path, $$"""{ "secrets": [ "{{entry}}" ], "resources": [] }""");

        var ex = Assert.Throws<InvalidOperationException>(() => LayerManifestLoader.Load(path));

        Assert.Contains(".secret.env", ex.Message);
    }

    [Theory]
    [InlineData("app/db.secret.env")]
    [InlineData(".configtransform/../app/db.secret.env")]
    [InlineData("db.secret.env")]
    public void A_secrets_file_outside_dot_configtransform_is_rejected(string entry)
    {
        // The git-crypt rule only covers .configtransform/ -- anywhere else, the file is plaintext.
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "configtransform.json");
        File.WriteAllText(path, $$"""{ "secrets": [ "{{entry}}" ], "resources": [] }""");

        var ex = Assert.Throws<InvalidOperationException>(() => LayerManifestLoader.Load(path));

        Assert.Contains("isn't inside .configtransform/", ex.Message);
    }

    [Fact]
    public void Loads_a_replace_entry()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "configtransform.json");
        File.WriteAllText(path, """
            { "resources": [ { "path": "app/firebase.json", "replace": ".configtransform/Clients/Acme/Production/firebase.secret.json" } ] }
            """);

        var manifest = LayerManifestLoader.Load(path);

        Assert.Equal(".configtransform/Clients/Acme/Production/firebase.secret.json", manifest.Resources[0].Replace);
        Assert.Null(manifest.Resources[0].Patch);
    }

    [Theory]
    [InlineData("""{ "path": "app/f.json", "patch": ".configtransform/E/p.json", "replace": ".configtransform/E/f.secret.json" }""", "both a patch and a replace")]
    [InlineData("""{ "path": "app/f.json", "replace": ".configtransform/E/firebase.json" }""", "doesn't contain '.secret.'")]
    [InlineData("""{ "path": "app/f.json", "replace": "app/firebase.secret.json" }""", "isn't inside .configtransform/")]
    public void An_invalid_replace_entry_is_rejected(string resource, string expectedMessage)
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "configtransform.json");
        File.WriteAllText(path, $$"""{ "resources": [ {{resource}} ] }""");

        var ex = Assert.Throws<InvalidOperationException>(() => LayerManifestLoader.Load(path));

        Assert.Contains(expectedMessage, ex.Message);
    }

    [Fact]
    public void An_unknown_field_is_rejected_instead_of_silently_ignored()
    {
        // An older tool used to skip fields it didn't know -- deploying unresolved placeholders
        // when a layer used a newer field like "secrets".
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "configtransform.json");
        File.WriteAllText(path, """{ "resources": [], "secretz": [] }""");

        var ex = Assert.Throws<InvalidOperationException>(() => LayerManifestLoader.Load(path));

        Assert.Contains("secretz", ex.Message);
        Assert.Contains("dotnet-tools.json", ex.Message);
    }
}
