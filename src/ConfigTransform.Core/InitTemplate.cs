namespace ConfigTransform.Core;

/// <summary>
/// The canned `init --template` starter trees (docs/INIT_COMMAND_DESIGN.md "Template mode"):
/// two Environments (Production, Test), two Clients (Client-A, Client-B), and one JSON resource,
/// <see cref="ResourcePath"/>, whose "message" field is overridden with a distinct value at every
/// layer -- naming that layer -- so the tree is immediately runnable as a live demo of the
/// override chain, not just proof the tree was created. `--template` is a value-taking flag with
/// two variants (docs/HOST_LAYER_DESIGN.md decision log #7): a bare `--template` (or
/// `--template default`) builds the plain two-axis tree via <see cref="BuildPlan"/>, unchanged
/// byte-for-byte from before the `hosts` variant existed; `--template hosts` additionally scaffolds
/// one illustrative <see cref="HostName"/> layer under Client-A/Production via
/// <see cref="BuildHostsPlan"/>, reusing every file the default variant already produces rather
/// than duplicating the tree.
/// </summary>
public static class InitTemplate
{
    public const string ResourcePath = "configtransform-template.json";
    public const string HostName = "Host-1";
    private const string PatchExtension = "json";
    private const string HostClient = "Client-A";
    private const string HostEnvironment = "Production";

    public static readonly IReadOnlyList<string> Environments = ["Production", "Test"];
    public static readonly IReadOnlyList<string> Clients = ["Client-A", "Client-B"];

    public static string BaseContent => Message("base config");

    public static IReadOnlyList<InitFile> BuildPlan(string root)
    {
        var files = new List<InitFile>
        {
            new(Path.Combine(root, ResourcePath), ResourcePath, BaseContent),
        };

        foreach (var environment in Environments)
        {
            var envLayerFullPath = LayerPathResolver.Resolve(root, client: null, environment)!;
            var envPatchFullPath = PatchPath(envLayerFullPath);
            var envPatchRelative = LayerChain.ToRepoRelative(root, envPatchFullPath);

            files.Add(new InitFile(envPatchFullPath, envPatchRelative, Message($"{environment} config")));

            var envManifest = new LayerManifest(null, [new ResourceEntry(ResourcePath, envPatchRelative)]);
            files.Add(new InitFile(envLayerFullPath, LayerChain.ToRepoRelative(root, envLayerFullPath), LayerManifestSerializer.Serialize(envManifest)));

            var envLayerRelative = LayerChain.ToRepoRelative(root, envLayerFullPath);

            foreach (var client in Clients)
            {
                var clientLayerFullPath = LayerPathResolver.Resolve(root, client, environment)!;
                var clientPatchFullPath = PatchPath(clientLayerFullPath);
                var clientPatchRelative = LayerChain.ToRepoRelative(root, clientPatchFullPath);

                files.Add(new InitFile(clientPatchFullPath, clientPatchRelative, Message($"{client} {environment} config")));

                var clientManifest = new LayerManifest(envLayerRelative, [new ResourceEntry(ResourcePath, clientPatchRelative)]);
                files.Add(new InitFile(clientLayerFullPath, LayerChain.ToRepoRelative(root, clientLayerFullPath), LayerManifestSerializer.Serialize(clientManifest)));
            }
        }

        return files;
    }

    /// <summary>
    /// The `hosts` variant: everything <see cref="BuildPlan"/> already produces, plus one worked
    /// <c>Hosts/&lt;<see cref="HostName"/>&gt;/configtransform.json</c> example under
    /// Client-A/Production -- the one client/environment pair a `Hosts/` layer can realistically
    /// exist under in this fixed, illustrative tree. `extends` defaults to that Client/Environment
    /// layer's own relative path, the same convention <see cref="BuildPlan"/>'s Client layers
    /// already follow for their Environment layer, one level deeper.
    /// </summary>
    public static IReadOnlyList<InitFile> BuildHostsPlan(string root)
    {
        var files = BuildPlan(root).ToList();

        var clientLayerFullPath = LayerPathResolver.Resolve(root, HostClient, HostEnvironment)!;
        var clientLayerRelative = LayerChain.ToRepoRelative(root, clientLayerFullPath);

        var hostLayerFullPath = LayerPathResolver.Resolve(root, HostClient, HostEnvironment, HostName)!;
        var hostPatchFullPath = PatchPath(hostLayerFullPath);
        var hostPatchRelative = LayerChain.ToRepoRelative(root, hostPatchFullPath);

        files.Add(new InitFile(hostPatchFullPath, hostPatchRelative, Message($"{HostClient} {HostEnvironment} {HostName} config")));

        var hostManifest = new LayerManifest(clientLayerRelative, [new ResourceEntry(ResourcePath, hostPatchRelative)]);
        files.Add(new InitFile(hostLayerFullPath, LayerChain.ToRepoRelative(root, hostLayerFullPath), LayerManifestSerializer.Serialize(hostManifest)));

        return files;
    }

    private static string PatchPath(string layerFullPath) =>
        Path.Combine(Path.GetDirectoryName(layerFullPath)!, PatchFileNaming.BuildFileName(ResourcePath, PatchExtension));

    private static string Message(string suffix) => $$"""{ "message": "Hello, world! (from {{suffix}})" }""";
}
