using ConfigTransform.Core.Tests.TestSupport;
using Xunit;

namespace ConfigTransform.Core.Tests;

public class ManifestLoaderTests
{
    [Fact]
    public void Loads_a_valid_manifest()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "manifest.json");
        File.WriteAllText(path, """
            { "directory": "Project", "files": [ { "relativeToDirectory": "App.config", "type": "xml" } ] }
            """);

        var manifest = ManifestLoader.Load(path);

        Assert.Equal("Project", manifest.Directory);
        Assert.Single(manifest.Files);
    }

    [Fact]
    public void Missing_file_throws_FileNotFoundException()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "manifest.json");

        Assert.Throws<FileNotFoundException>(() => ManifestLoader.Load(path));
    }

    [Fact]
    public void Malformed_json_throws_InvalidOperationException()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "manifest.json");
        File.WriteAllText(path, "{ this is not valid json");

        Assert.Throws<InvalidOperationException>(() => ManifestLoader.Load(path));
    }

    [Fact]
    public void Git_crypt_locked_manifest_gives_an_actionable_message()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "manifest.json");
        // git-crypt's real encrypted-file header: NUL + "GITCRYPT" + NUL, followed by ciphertext
        // bytes -- a locked manifest looks exactly like this on disk, not just a NUL byte.
        byte[] header = [0x00, (byte)'G', (byte)'I', (byte)'T', (byte)'C', (byte)'R', (byte)'Y', (byte)'P', (byte)'T', 0x00];
        byte[] fakeCiphertext = [0x9f, 0x02, 0x7c, 0x11, 0xab, 0x44];
        File.WriteAllBytes(path, [.. header, .. fakeCiphertext]);

        var ex = Assert.Throws<InvalidOperationException>(() => ManifestLoader.Load(path));

        Assert.Contains("git-crypt", ex.Message);
        Assert.Contains("git-crypt unlock", ex.Message);
    }
}
