namespace ConfigTransform.Xml.Tests.TestSupport;

/// <summary>
/// Builds a realistic .configtransform-shaped temp workspace (base project + Environment +
/// Client configtransform.json layers, docs/SELF_DESCRIBING_OVERLAYS_DESIGN.md) for exercising
/// XmlCliRunner end-to-end, independent of the test process's current working directory.
/// <see cref="RootPath"/> is the synthetic repo root every path in the generated
/// configtransform.json files is relative to, and doubles as the working directory a test passes
/// to <c>XmlCliRunner.Run</c>. <see cref="ResourcePath"/> ("Project/App.config") is what a test
/// passes as --resource.
/// </summary>
internal sealed class TempCliWorkspace : IDisposable
{
    public string RootPath { get; } = Directory.CreateTempSubdirectory("configtransform-cli-tests-").FullName;
    public string ResourcePath => "Project/App.config";
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

        ProjectFilePath = Path.Combine(projectDir, "App.config");
        File.WriteAllText(ProjectFilePath, """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <appSettings>
                <add key="ApiUrl" value="https://dev.example.com" />
              </appSettings>
            </configuration>
            """);

        var environmentPatchPath = Path.Combine(environmentDir, "patch-Project-App.config.xml");
        File.WriteAllText(environmentPatchPath, """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration xmlns:xdt="http://schemas.microsoft.com/XML-Document-Transform">
              <appSettings>
                <add key="ApiUrl" value="https://prod.example.com" xdt:Transform="SetAttributes" xdt:Locator="Match(key)" />
              </appSettings>
            </configuration>
            """);

        EnvironmentLayerPath = Path.Combine(environmentDir, "configtransform.json");
        File.WriteAllText(EnvironmentLayerPath, """
            { "resources": [ { "path": "Project/App.config", "patch": ".configtransform/Environments/Production/patch-Project-App.config.xml" } ] }
            """);

        var clientPatchPath = Path.Combine(clientDir, "patch-Project-App.config.xml");
        File.WriteAllText(clientPatchPath, """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration xmlns:xdt="http://schemas.microsoft.com/XML-Document-Transform">
              <appSettings>
                <add key="ApiUrl" value="https://clienta.example.com" xdt:Transform="SetAttributes" xdt:Locator="Match(key)" />
              </appSettings>
            </configuration>
            """);

        ClientLayerPath = Path.Combine(clientDir, "configtransform.json");
        File.WriteAllText(ClientLayerPath, """
            {
              "extends": ".configtransform/Environments/Production/configtransform.json",
              "resources": [ { "path": "Project/App.config", "patch": ".configtransform/Clients/ClientA/Production/patch-Project-App.config.xml" } ]
            }
            """);
    }

    public void Dispose()
    {
        if (Directory.Exists(RootPath))
            Directory.Delete(RootPath, recursive: true);
    }
}
