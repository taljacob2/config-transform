namespace ConfigTransform.Json.Tests.TestSupport;

/// <summary>
/// Builds a realistic .configtransform-shaped temp workspace (base project + Environment +
/// Client configtransform.json layers, docs/SELF_DESCRIBING_OVERLAYS_DESIGN.md) for exercising
/// JsonCliRunner end-to-end, independent of the test process's current working directory.
/// <see cref="RootPath"/> is the synthetic repo root every path in the generated
/// configtransform.json files is relative to, and doubles as the working directory a test passes
/// to <c>JsonCliRunner.Run</c>. <see cref="ResourcePath"/> ("Project/appsettings.json") is what a
/// test passes as --resource.
/// </summary>
internal sealed class TempCliWorkspace : IDisposable
{
    public string RootPath { get; } = Directory.CreateTempSubdirectory("configtransform-cli-tests-").FullName;
    public string ResourcePath => "Project/appsettings.json";
    public string ProjectFilePath { get; }
    public string EnvironmentLayerPath { get; }
    public string ClientLayerPath { get; }

    public TempCliWorkspace()
    {
        var projectDir = Path.Combine(RootPath, "Project");
        var environmentDir = Path.Combine(RootPath, ".configtransform", "Environments", "Production");
        var clientDir = Path.Combine(RootPath, ".configtransform", "Clients", "ClientA", "Production");
        Directory.CreateDirectory(projectDir);
        Directory.CreateDirectory(environmentDir);
        Directory.CreateDirectory(clientDir);

        ProjectFilePath = Path.Combine(projectDir, "appsettings.json");
        File.WriteAllText(ProjectFilePath, """
            { "ApiUrl": "https://dev.example.com" }
            """);

        var environmentPatchPath = Path.Combine(environmentDir, "patch-Project-appsettings.json.json");
        File.WriteAllText(environmentPatchPath, """
            { "ApiUrl": "https://prod.example.com" }
            """);

        EnvironmentLayerPath = Path.Combine(environmentDir, "configtransform.json");
        File.WriteAllText(EnvironmentLayerPath, """
            { "resources": [ { "path": "Project/appsettings.json", "patch": ".configtransform/Environments/Production/patch-Project-appsettings.json.json" } ] }
            """);

        var clientPatchPath = Path.Combine(clientDir, "patch-Project-appsettings.json.json");
        File.WriteAllText(clientPatchPath, """
            { "ApiUrl": "https://clienta.example.com" }
            """);

        ClientLayerPath = Path.Combine(clientDir, "configtransform.json");
        File.WriteAllText(ClientLayerPath, """
            {
              "extends": ".configtransform/Environments/Production/configtransform.json",
              "resources": [ { "path": "Project/appsettings.json", "patch": ".configtransform/Clients/ClientA/Production/patch-Project-appsettings.json.json" } ]
            }
            """);
    }

    public void Dispose()
    {
        if (Directory.Exists(RootPath))
            Directory.Delete(RootPath, recursive: true);
    }
}
