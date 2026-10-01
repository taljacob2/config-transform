using ConfigTransform.Core.Tests.TestSupport;
using Xunit;

namespace ConfigTransform.Core.Tests;

/// <summary>
/// <see cref="SecretResolver"/>/<see cref="SecretSet"/> against real temp files: precedence across
/// layers, the environment-variable override, locked (git-crypt) files, and every error case.
/// docs/SECRETS_DESIGN.md's "Resolution" section.
/// </summary>
public class SecretResolverTests
{
    private static readonly byte[] GitCryptHeader =
        [0x00, (byte)'G', (byte)'I', (byte)'T', (byte)'C', (byte)'R', (byte)'Y', (byte)'P', (byte)'T', 0x00, 0x42, 0x17];

    private static readonly Func<string, string?> NoEnvironment = _ => null;

    [Fact]
    public void A_name_resolves_to_its_value_and_names_the_file_it_came_from()
    {
        using var dir = new TempDirectory();
        var chain = Chain(dir, ("Environments/Production", [("db.secret.env", "CFSECRET_DB=from-env-layer")]));

        var status = SecretResolver.Build(dir.Path, chain, NoEnvironment).Lookup("CFSECRET_DB");

        Assert.Equal(SecretState.Resolved, status.State);
        Assert.Equal("from-env-layer", status.Value);
        Assert.Equal(".configtransform/Environments/Production/db.secret.env", status.Source);
    }

    [Fact]
    public void A_later_layer_overrides_an_earlier_one()
    {
        using var dir = new TempDirectory();
        var chain = Chain(dir,
            ("Environments/Production", [("db.secret.env", "CFSECRET_DB=env\nCFSECRET_ONLY_ENV=kept")]),
            ("Clients/Acme/Production", [("db.secret.env", "CFSECRET_DB=client")]));

        var secrets = SecretResolver.Build(dir.Path, chain, NoEnvironment);

        Assert.Equal("client", secrets.Lookup("CFSECRET_DB").Value);
        Assert.Equal("kept", secrets.Lookup("CFSECRET_ONLY_ENV").Value);
    }

    [Fact]
    public void A_name_no_file_defines_is_missing()
    {
        using var dir = new TempDirectory();
        var chain = Chain(dir, ("Environments/Production", [("db.secret.env", "CFSECRET_DB=x")]));

        var status = SecretResolver.Build(dir.Path, chain, NoEnvironment).Lookup("CFSECRET_OTHER");

        Assert.Equal(SecretState.Missing, status.State);
        Assert.Null(status.Value);
    }

    [Fact]
    public void An_environment_variable_overrides_every_file_but_an_empty_one_counts_as_unset()
    {
        // A GitHub Actions expression for a secret that doesn't exist evaluates to "" -- that must
        // not deploy an empty value.
        using var dir = new TempDirectory();
        var chain = Chain(dir, ("Environments/Production", [("db.secret.env", "CFSECRET_DB=file\nCFSECRET_API=file")]));
        var environment = new Dictionary<string, string> { ["CFSECRET_DB"] = "from-env-var", ["CFSECRET_API"] = "" };

        var secrets = SecretResolver.Build(dir.Path, chain, name => environment.GetValueOrDefault(name));

        Assert.Equal("from-env-var", secrets.Lookup("CFSECRET_DB").Value);
        Assert.Equal("environment variable", secrets.Lookup("CFSECRET_DB").Source);
        Assert.Equal("file", secrets.Lookup("CFSECRET_API").Value);
    }

    [Fact]
    public void A_locked_file_makes_every_name_unknown_unless_a_later_readable_layer_settles_it()
    {
        using var dir = new TempDirectory();
        var chain = Chain(dir,
            ("Environments/Production", [("db.secret.env", "CFSECRET_DB=env")]),
            ("Clients/Acme/Production", [("db.secret.env", null)]), // locked
            ("Clients/Acme/Production/Hosts/H1", [("db.secret.env", "CFSECRET_HOST_ONLY=h1")]));

        var secrets = SecretResolver.Build(dir.Path, chain, NoEnvironment);

        // The locked Client layer could override CFSECRET_DB, so the Environment value isn't certain.
        Assert.Equal(SecretState.Unknown, secrets.Lookup("CFSECRET_DB").State);
        Assert.Contains("Clients/Acme/Production/db.secret.env is locked", secrets.Lookup("CFSECRET_DB").Source);
        Assert.Equal(SecretState.Unknown, secrets.Lookup("CFSECRET_NEVER_DEFINED").State);
        // ...but a later, readable layer that defines a name settles it again.
        Assert.Equal(SecretState.Resolved, secrets.Lookup("CFSECRET_HOST_ONLY").State);
    }

    [Fact]
    public void The_same_name_in_two_files_of_one_layer_is_an_error()
    {
        using var dir = new TempDirectory();
        var chain = Chain(dir, ("Environments/Production",
            [("a.secret.env", "CFSECRET_DB=1"), ("b.secret.env", "CFSECRET_DB=2")]));

        var ex = Assert.Throws<InvalidOperationException>(() => SecretResolver.Build(dir.Path, chain, NoEnvironment));

        Assert.Contains("a.secret.env", ex.Message);
        Assert.Contains("b.secret.env", ex.Message);
        Assert.DoesNotContain("=1", ex.Message);
    }

    [Fact]
    public void A_key_without_the_CFSECRET_prefix_is_an_error_naming_the_fix_but_not_the_value()
    {
        using var dir = new TempDirectory();
        var chain = Chain(dir, ("Environments/Production", [("db.secret.env", "DB_PASSWORD=hunter2")]));

        var ex = Assert.Throws<InvalidOperationException>(() => SecretResolver.Build(dir.Path, chain, NoEnvironment));

        Assert.Contains("CFSECRET_DB_PASSWORD", ex.Message);
        Assert.DoesNotContain("hunter2", ex.Message);
    }

    [Fact]
    public void A_malformed_secrets_file_is_an_error_that_never_quotes_its_content()
    {
        // EnvFile's own parse error quotes the offending line -- here, likely the secret itself.
        using var dir = new TempDirectory();
        var chain = Chain(dir, ("Environments/Production", [("db.secret.env", "CFSECRET_OK=fine\nhunter2-pasted-without-a-name")]));

        var ex = Assert.Throws<InvalidOperationException>(() => SecretResolver.Build(dir.Path, chain, NoEnvironment));

        Assert.Contains("db.secret.env", ex.Message);
        Assert.DoesNotContain("hunter2", ex.Message);
    }

    [Fact]
    public void A_listed_secrets_file_that_does_not_exist_is_an_error()
    {
        using var dir = new TempDirectory();
        var layerPath = WriteLayer(dir, "Environments/Production", [".configtransform/Environments/Production/gone.secret.env"]);
        var chain = new[] { new ResolvedLayer(layerPath, LayerManifestLoader.Load(layerPath)) };

        Assert.Throws<FileNotFoundException>(() => SecretResolver.Build(dir.Path, chain, NoEnvironment));
    }

    /// <summary>Writes one layer per (dir, files) pair -- a null file content means "git-crypt locked" -- and returns them as a chain, outermost first.</summary>
    private static IReadOnlyList<ResolvedLayer> Chain(TempDirectory dir, params (string LayerDir, (string Name, string? Content)[] Files)[] layers)
    {
        var chain = new List<ResolvedLayer>();
        foreach (var (layerDir, files) in layers)
        {
            var entries = new List<string>();
            foreach (var (name, content) in files)
            {
                var relative = $".configtransform/{layerDir}/{name}";
                var full = Path.Combine(dir.Path, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                if (content is null)
                    File.WriteAllBytes(full, GitCryptHeader);
                else
                    File.WriteAllText(full, content);
                entries.Add(relative);
            }

            var layerPath = WriteLayer(dir, layerDir, entries);
            chain.Add(new ResolvedLayer(layerPath, LayerManifestLoader.Load(layerPath)));
        }
        return chain;
    }

    private static string WriteLayer(TempDirectory dir, string layerDir, IReadOnlyList<string> secrets)
    {
        var layerPath = Path.Combine(dir.Path, ".configtransform", layerDir, "configtransform.json");
        Directory.CreateDirectory(Path.GetDirectoryName(layerPath)!);
        var list = string.Join(", ", secrets.Select(s => $"\"{s}\""));
        File.WriteAllText(layerPath, $$"""{ "secrets": [ {{list}} ], "resources": [] }""");
        return layerPath;
    }
}
