namespace ConfigTransform.Cli.Tests.TestSupport;

/// <summary>
/// Builds a realistic .configtransform-shaped temp workspace (base project + Environment +
/// Client configtransform.json layers, docs/SELF_DESCRIBING_OVERLAYS_DESIGN.md) for exercising
/// the unified <see cref="CliRunner"/> dispatcher end-to-end, independent of the test process's
/// current working directory. <see cref="RootPath"/> is the synthetic repo root every path in the
/// generated configtransform.json files is relative to, and doubles as the working directory a
/// test passes to <c>CliRunner.Run</c>. Unlike the pre-unification per-format workspaces this
/// replaces, both an XML resource (<see cref="XmlResourcePath"/>) and a JSON resource
/// (<see cref="JsonResourcePath"/>) are registered in the *same* Environment/Client layers, so a
/// single omitted-`--resource` call can exercise both formats at once.
/// </summary>
internal sealed class TempCliWorkspace : IDisposable
{
    public string RootPath { get; } = Directory.CreateTempSubdirectory("configtransform-cli-tests-").FullName;
    public string XmlResourcePath => "Project/App.config";
    public string JsonResourcePath => "Project/appsettings.json";
    public string XmlProjectFilePath { get; }
    public string JsonProjectFilePath { get; }
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

        XmlProjectFilePath = Path.Combine(projectDir, "App.config");
        File.WriteAllText(XmlProjectFilePath, """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <appSettings>
                <add key="ApiUrl" value="https://dev.example.com" />
              </appSettings>
            </configuration>
            """);

        JsonProjectFilePath = Path.Combine(projectDir, "appsettings.json");
        File.WriteAllText(JsonProjectFilePath, """
            { "ApiUrl": "https://dev.example.com" }
            """);

        var xmlEnvironmentPatchPath = Path.Combine(environmentDir, "patch-Project-App.config.xml");
        File.WriteAllText(xmlEnvironmentPatchPath, """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration xmlns:xdt="http://schemas.microsoft.com/XML-Document-Transform">
              <appSettings>
                <add key="ApiUrl" value="https://prod.example.com" xdt:Transform="SetAttributes" xdt:Locator="Match(key)" />
              </appSettings>
            </configuration>
            """);

        var jsonEnvironmentPatchPath = Path.Combine(environmentDir, "patch-Project-appsettings.json.json");
        File.WriteAllText(jsonEnvironmentPatchPath, """
            { "ApiUrl": "https://prod.example.com" }
            """);

        EnvironmentLayerPath = Path.Combine(environmentDir, "configtransform.json");
        File.WriteAllText(EnvironmentLayerPath, """
            { "resources": [
                { "path": "Project/App.config", "patch": ".configtransform/Environments/Production/patch-Project-App.config.xml" },
                { "path": "Project/appsettings.json", "patch": ".configtransform/Environments/Production/patch-Project-appsettings.json.json" }
            ] }
            """);

        var xmlClientPatchPath = Path.Combine(clientDir, "patch-Project-App.config.xml");
        File.WriteAllText(xmlClientPatchPath, """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration xmlns:xdt="http://schemas.microsoft.com/XML-Document-Transform">
              <appSettings>
                <add key="ApiUrl" value="https://clienta.example.com" xdt:Transform="SetAttributes" xdt:Locator="Match(key)" />
              </appSettings>
            </configuration>
            """);

        var jsonClientPatchPath = Path.Combine(clientDir, "patch-Project-appsettings.json.json");
        File.WriteAllText(jsonClientPatchPath, """
            { "ApiUrl": "https://clienta.example.com" }
            """);

        ClientLayerPath = Path.Combine(clientDir, "configtransform.json");
        File.WriteAllText(ClientLayerPath, """
            {
              "extends": ".configtransform/Environments/Production/configtransform.json",
              "resources": [
                { "path": "Project/App.config", "patch": ".configtransform/Clients/ClientA/Production/patch-Project-App.config.xml" },
                { "path": "Project/appsettings.json", "patch": ".configtransform/Clients/ClientA/Production/patch-Project-appsettings.json.json" }
              ]
            }
            """);
    }

    public void Dispose()
    {
        if (Directory.Exists(RootPath))
            Directory.Delete(RootPath, recursive: true);
    }
}
