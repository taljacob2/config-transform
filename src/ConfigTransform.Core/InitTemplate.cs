namespace ConfigTransform.Core;

/// <summary>
/// The canned `init --template` starter trees (docs/INIT_COMMAND_DESIGN.md "Template mode"):
/// two Environments (Production, Test), two Clients (Client-A, Client-B), and one JSON resource,
/// <see cref="ResourcePath"/>, whose "message" field is overridden with a distinct value at every
/// layer -- naming that layer -- so the tree is immediately runnable as a live demo of the
/// override chain, not just proof the tree was created. `--template` is a value-taking flag with
/// three variants: a bare `--template` (or `--template default`) builds the plain two-axis tree via
/// <see cref="BuildPlan"/>, unchanged byte-for-byte from before any other variant existed;
/// `--template hosts` (docs/HOST_LAYER_DESIGN.md decision log #7) adds one illustrative
/// <see cref="HostName"/> layer under Client-A/Production via <see cref="BuildHostsPlan"/>; and
/// `--template secrets` (docs/INIT_COMMAND_DESIGN.md "The `secrets` variant") adds a runnable
/// secrets example via <see cref="BuildSecretsPlan"/>.
/// </summary>
public static class InitTemplate
{
    public const string ResourcePath = "configtransform-template.json";
    public const string HostName = "Host-1";

    /// <summary>The `secrets` variant's whole-file-secret resource: committed as <see cref="CredentialsBaseContent"/>, replaced at Client-A/Production.</summary>
    public const string CredentialsResourcePath = "configtransform-template-credentials.json";
    public const string CredentialsBaseContent = "{}\n";

    public const string SecretName = "CFSECRET_DEMO_API_KEY";

    /// <summary>Printed after the `secrets` variant writes (or would write) its files -- they're plaintext until git-crypt covers them.</summary>
    public const string SecretsNotice =
        "Note: the *.secret.* files this template wrote are plaintext. Before putting a real secret in\n" +
        "one, set up git-crypt for them (docs/CONFIG_MANAGEMENT.md §7 in config-transform):\n" +
        "  git-crypt init\n" +
        "  echo \".configtransform/**/*.secret.* filter=git-crypt diff=git-crypt\" >> .gitattributes";

    private const string PatchExtension = "json";
    private const string DemoClient = "Client-A";
    private const string DemoEnvironment = "Production";
    private const string SecretsFileName = "demo.secret.env";
    private const string CredentialsSecretFileName = "credentials.secret.json";

    public static readonly IReadOnlyList<string> Environments = ["Production", "Test"];
    public static readonly IReadOnlyList<string> Clients = ["Client-A", "Client-B"];

    public static string BaseContent => Message("base config");

    public static IReadOnlyList<InitFile> BuildPlan(string root) => Build(root, secrets: false);

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

        var clientLayerFullPath = LayerPathResolver.Resolve(root, DemoClient, DemoEnvironment)!;
        var clientLayerRelative = LayerChain.ToRepoRelative(root, clientLayerFullPath);

        var hostLayerFullPath = LayerPathResolver.Resolve(root, DemoClient, DemoEnvironment, HostName)!;
        var hostPatchFullPath = PatchPath(hostLayerFullPath);
        var hostPatchRelative = LayerChain.ToRepoRelative(root, hostPatchFullPath);

        files.Add(new InitFile(hostPatchFullPath, hostPatchRelative, Message($"{DemoClient} {DemoEnvironment} {HostName} config")));

        var hostManifest = new LayerManifest(clientLayerRelative, [new ResourceEntry(ResourcePath, hostPatchRelative)]);
        files.Add(new InitFile(hostLayerFullPath, LayerChain.ToRepoRelative(root, hostLayerFullPath), LayerManifestSerializer.Serialize(hostManifest)));

        return files;
    }

    /// <summary>
    /// The `secrets` variant (docs/INIT_COMMAND_DESIGN.md "The `secrets` variant"): the default
    /// tree, with a <c>{{CFSECRET_DEMO_API_KEY}}</c> placeholder in each Environment patch (never
    /// the base file, so the base alone resolves without a secret), a <c>demo.secret.env</c> at each
    /// Environment layer and a client-level override at Client-A/Production, plus one whole-file
    /// secret: <see cref="CredentialsResourcePath"/>, replaced at Client-A/Production. Every value
    /// is obviously fake and names its own layer; nothing touches <c>.gitattributes</c>.
    /// </summary>
    public static IReadOnlyList<InitFile> BuildSecretsPlan(string root) => Build(root, secrets: true);

    private static List<InitFile> Build(string root, bool secrets)
    {
        var files = new List<InitFile>
        {
            new(Path.Combine(root, ResourcePath), ResourcePath, BaseContent),
        };
        if (secrets)
            files.Add(new InitFile(Path.Combine(root, CredentialsResourcePath), CredentialsResourcePath, CredentialsBaseContent));

        foreach (var environment in Environments)
        {
            var envLayerFullPath = LayerPathResolver.Resolve(root, client: null, environment)!;
            var envPatchFullPath = PatchPath(envLayerFullPath);
            var envPatchRelative = LayerChain.ToRepoRelative(root, envPatchFullPath);

            var envPatch = secrets
                ? MessageWithSecret($"{environment} config")
                : Message($"{environment} config");
            files.Add(new InitFile(envPatchFullPath, envPatchRelative, envPatch));

            var envSecrets = secrets
                ? AddSecretsFile(root, files, envLayerFullPath, $"not-a-real-secret-{environment.ToLowerInvariant()}")
                : null;

            var envManifest = new LayerManifest(null, [new ResourceEntry(ResourcePath, envPatchRelative)], envSecrets);
            files.Add(new InitFile(envLayerFullPath, LayerChain.ToRepoRelative(root, envLayerFullPath), LayerManifestSerializer.Serialize(envManifest)));

            var envLayerRelative = LayerChain.ToRepoRelative(root, envLayerFullPath);

            foreach (var client in Clients)
            {
                var clientLayerFullPath = LayerPathResolver.Resolve(root, client, environment)!;
                var clientPatchFullPath = PatchPath(clientLayerFullPath);
                var clientPatchRelative = LayerChain.ToRepoRelative(root, clientPatchFullPath);

                files.Add(new InitFile(clientPatchFullPath, clientPatchRelative, Message($"{client} {environment} config")));

                var resources = new List<ResourceEntry> { new(ResourcePath, clientPatchRelative) };
                IReadOnlyList<string>? clientSecrets = null;

                if (secrets && client == DemoClient && environment == DemoEnvironment)
                {
                    clientSecrets = AddSecretsFile(root, files, clientLayerFullPath, "not-a-real-secret-client-a-production");

                    var credentialsFullPath = Path.Combine(Path.GetDirectoryName(clientLayerFullPath)!, CredentialsSecretFileName);
                    var credentialsRelative = LayerChain.ToRepoRelative(root, credentialsFullPath);
                    files.Add(new InitFile(credentialsFullPath, credentialsRelative,
                        "{ \"note\": \"not a real credential -- an example whole-file secret\" }\n"));
                    resources.Add(new ResourceEntry(CredentialsResourcePath, Replace: credentialsRelative));
                }

                var clientManifest = new LayerManifest(envLayerRelative, resources, clientSecrets);
                files.Add(new InitFile(clientLayerFullPath, LayerChain.ToRepoRelative(root, clientLayerFullPath), LayerManifestSerializer.Serialize(clientManifest)));
            }
        }

        return files;
    }

    /// <summary>Adds this layer's <c>demo.secret.env</c> to <paramref name="files"/> and returns the layer's <c>secrets</c> list.</summary>
    private static IReadOnlyList<string> AddSecretsFile(string root, List<InitFile> files, string layerFullPath, string value)
    {
        var secretsFullPath = Path.Combine(Path.GetDirectoryName(layerFullPath)!, SecretsFileName);
        var secretsRelative = LayerChain.ToRepoRelative(root, secretsFullPath);
        files.Add(new InitFile(secretsFullPath, secretsRelative, $"{SecretName}={value}\n"));
        return [secretsRelative];
    }

    private static string PatchPath(string layerFullPath) =>
        Path.Combine(Path.GetDirectoryName(layerFullPath)!, PatchFileNaming.BuildFileName(ResourcePath, PatchExtension));

    private static string Message(string suffix) => $$"""{ "message": "Hello, world! (from {{suffix}})" }""";

    private static string MessageWithSecret(string suffix) =>
        $$$"""{ "message": "Hello, world! (from {{{suffix}}})", "apiKey": "{{{{{SecretName}}}}}" }""";
}
