namespace ConfigTransform.Core;

/// <summary>
/// The one canned `init --template` starter tree (docs/INIT_COMMAND_DESIGN.md "Template mode"):
/// two Environments (Production, Test), two Clients (Client-A, Client-B), and one JSON resource,
/// <see cref="ResourcePath"/>, whose "message" field is overridden with a distinct value at every
/// layer -- naming that layer -- so the tree is immediately runnable as a live demo of the
/// override chain, not just proof the tree was created. `--template` is a bare switch: there is
/// exactly one template, meant to stay the basic/default starter either way (see the design doc's
/// decision log for why a named flag was rejected).
/// </summary>
public static class InitTemplate
{
    public const string ResourcePath = "configtransform-template.json";
    private const string PatchExtension = "json";

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

    private static string PatchPath(string layerFullPath) =>
        Path.Combine(Path.GetDirectoryName(layerFullPath)!, PatchFileNaming.BuildFileName(ResourcePath, PatchExtension));

    private static string Message(string suffix) => $$"""{ "message": "Hello, world! (from {{suffix}})" }""";
}
