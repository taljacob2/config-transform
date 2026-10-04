using ConfigTransform.Cli.Tests.TestSupport;
using ConfigTransform.Core;
using Xunit;

namespace ConfigTransform.Cli.Tests;

/// <summary>
/// Whole-file secrets end to end (docs/SECRETS_DESIGN.md, "File secrets (replace)"): a layer's
/// <c>replace</c> swaps a resource for an encrypted file byte for byte — any format, binary
/// included, no format engine needed — previews never show the content unless asked, and the
/// layering rules around patches hold.
/// </summary>
public class ReplaceCliTests
{
    private const string FirebaseSecret = """{ "private_key": "-----BEGIN PRIVATE KEY-----abc", "client_email": "x@y" }""";
    private static readonly byte[] SigningSecret = [0x30, 0x82, 0x00, 0x01, 0xFF, 0x00, 0x7F];
    private static readonly byte[] GitCryptHeader =
        [0x00, (byte)'G', (byte)'I', (byte)'T', (byte)'C', (byte)'R', (byte)'Y', (byte)'P', (byte)'T', 0x00, 0x42];

    /// <summary>
    /// app/firebase.json ({}) is patched at the Environment layer and replaced at Acme/Production;
    /// app/signing.p12 (empty -- no engine handles .p12) is replaced at Acme/Production;
    /// app/settings.json is an ordinary patched resource alongside them.
    /// </summary>
    private sealed class Workspace : IDisposable
    {
        private readonly TempDirectory _dir = new();
        public string Root => _dir.Path;
        private const string Env = ".configtransform/Environments/Production";
        private const string Client = ".configtransform/Clients/Acme/Production";

        public Workspace(string clientExtraResources = "")
        {
            Write("app/firebase.json", "{}\n");
            Write("app/signing.p12", "");
            Write("app/settings.json", "{ \"Mode\": \"dev\" }\n");
            Write($"{Env}/patch-app-firebase.json", "{ \"project_id\": \"from-env-patch\" }\n");
            Write($"{Env}/patch-app-settings.json", "{ \"Mode\": \"prod\" }\n");
            Write($"{Env}/configtransform.json", $$"""
                { "resources": [
                    { "path": "app/firebase.json", "patch": "{{Env}}/patch-app-firebase.json" },
                    { "path": "app/settings.json", "patch": "{{Env}}/patch-app-settings.json" }
                ] }
                """);
            Write($"{Client}/firebase.secret.json", FirebaseSecret);
            File.WriteAllBytes(Path.Combine(Root, Client, "signing.secret.p12"), SigningSecret);
            Write($"{Client}/configtransform.json", $$"""
                { "extends": "{{Env}}/configtransform.json", "resources": [
                    { "path": "app/firebase.json", "replace": "{{Client}}/firebase.secret.json" },
                    { "path": "app/signing.p12", "replace": "{{Client}}/signing.secret.p12" }{{clientExtraResources}}
                ] }
                """);
        }

        public void Write(string relativePath, string content)
        {
            var full = Path.Combine(Root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }

        public string Out(string relativePath) => Path.Combine(Root, "out", relativePath);

        public (int ExitCode, string Stdout, string Stderr) Run(params string[] args)
        {
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var exitCode = CliRunner.Run(args, stdout, stderr, FormatEngines.All, Root, environmentVariables: _ => null);
            return (exitCode, stdout.ToString(), stderr.ToString());
        }

        public void Dispose() => _dir.Dispose();
    }

    [Fact]
    public void A_dry_run_shows_the_replacement_and_superseded_patch_in_the_chain_but_not_the_content()
    {
        using var workspace = new Workspace();

        var (exitCode, stdout, _) = workspace.Run("-r", "app/firebase.json", "-c", "Acme", "-e", "Production", "--dry-run");

        Assert.Equal(0, exitCode);
        Assert.Contains("patched in: .configtransform/Environments/Production/patch-app-firebase.json (superseded by a later replace)", stdout);
        Assert.Contains("replaced by: .configtransform/Clients/Acme/Production/firebase.secret.json", stdout);
        Assert.Contains($"(replaced by .configtransform/Clients/Acme/Production/firebase.secret.json, {FirebaseSecret.Length} bytes, not shown", stdout);
        Assert.DoesNotContain("PRIVATE KEY", stdout);
    }

    [Fact]
    public void Reveal_secrets_shows_the_content_in_a_dry_run_and_a_diff_against_the_base()
    {
        using var workspace = new Workspace();

        var (_, dryRun, _) = workspace.Run("-r", "app/firebase.json", "-c", "Acme", "-e", "Production", "--dry-run", "--reveal-secrets");
        var (_, diff, _) = workspace.Run("-r", "app/firebase.json", "-c", "Acme", "-e", "Production", "--diff", "--reveal-secrets");
        var (_, binary, _) = workspace.Run("-r", "app/signing.p12", "-c", "Acme", "-e", "Production", "--dry-run", "--reveal-secrets");

        Assert.Contains(FirebaseSecret, dryRun);
        Assert.Contains("-{}", diff);
        Assert.Contains("PRIVATE KEY", diff);
        Assert.Contains($"binary content, {SigningSecret.Length} bytes", binary);
    }

    [Fact]
    public void A_real_run_copies_the_replace_files_exact_bytes_even_for_a_format_no_engine_handles()
    {
        using var workspace = new Workspace();

        var (exitCode, _, stderr) = workspace.Run("-r", "app/signing.p12", "-c", "Acme", "-e", "Production", "-o", workspace.Out("signing.p12"));

        Assert.Equal(0, exitCode);
        Assert.Empty(stderr);
        Assert.Equal(SigningSecret, File.ReadAllBytes(workspace.Out("signing.p12")));
    }

    [Fact]
    public void Every_resource_mode_includes_replaced_resources_and_writes_them_byte_for_byte()
    {
        using var workspace = new Workspace();

        var (exitCode, _, stderr) = workspace.Run("-c", "Acme", "-e", "Production", "-o", workspace.Out(""));

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("Skipped", stderr); // signing.p12 has no engine, but it's replaced, not skipped
        Assert.Equal(FirebaseSecret, File.ReadAllText(workspace.Out("app/firebase.json")));
        Assert.Equal(SigningSecret, File.ReadAllBytes(workspace.Out("app/signing.p12")));
        Assert.Contains("\"Mode\": \"prod\"", File.ReadAllText(workspace.Out("app/settings.json")));
    }

    [Fact]
    public void A_locked_replace_file_is_noted_in_a_preview_and_stops_a_real_run_before_anything_is_written()
    {
        using var workspace = new Workspace();
        File.WriteAllBytes(Path.Combine(workspace.Root, ".configtransform/Clients/Acme/Production/firebase.secret.json"), GitCryptHeader);

        var (_, preview, _) = workspace.Run("-r", "app/firebase.json", "-c", "Acme", "-e", "Production", "--dry-run");
        var (exitCode, _, stderr) = workspace.Run("-c", "Acme", "-e", "Production", "-o", workspace.Out(""));

        Assert.Contains("still git-crypt encrypted", preview);
        Assert.Equal(1, exitCode);
        Assert.Contains("git-crypt unlock", stderr);
        Assert.False(Directory.Exists(workspace.Out(""))); // settings.json wasn't written either
    }

    [Fact]
    public void A_patch_in_a_layer_after_the_replace_is_an_error()
    {
        using var workspace = new Workspace();
        workspace.Write(".configtransform/Clients/Acme/Production/Hosts/H1/patch.json", "{ \"x\": 1 }");
        workspace.Write(".configtransform/Clients/Acme/Production/Hosts/H1/configtransform.json", """
            { "extends": ".configtransform/Clients/Acme/Production/configtransform.json", "resources": [
                { "path": "app/firebase.json", "patch": ".configtransform/Clients/Acme/Production/Hosts/H1/patch.json" }
            ] }
            """);

        var (exitCode, _, stderr) = workspace.Run("-r", "app/firebase.json", "-c", "Acme", "-e", "Production", "-H", "H1", "--dry-run");

        Assert.Equal(1, exitCode);
        Assert.Contains("can't merge onto a replaced file", stderr);
    }

    [Fact]
    public void Set_refuses_to_write_a_patch_for_a_replaced_resource()
    {
        using var workspace = new Workspace();

        var (exitCode, _, stderr) = workspace.Run(
            "set", "-r", "app/firebase.json", "-c", "Acme", "-e", "Production", "--match", "key=project_id", "--set", "x");

        Assert.Equal(1, exitCode);
        Assert.Contains("is replaced by the whole-file secret", stderr);
        Assert.False(File.Exists(Path.Combine(workspace.Root, ".configtransform/Clients/Acme/Production/patch-app-firebase.json")));
    }

    [Fact]
    public void List_reverse_lookup_shows_which_layer_replaces_a_resource()
    {
        using var workspace = new Workspace();

        var (exitCode, stdout, _) = workspace.Run("--list", "-r", "app/firebase.json");

        Assert.Equal(0, exitCode);
        Assert.Contains("replaced by .configtransform/Clients/Acme/Production/firebase.secret.json", stdout);
    }
}
