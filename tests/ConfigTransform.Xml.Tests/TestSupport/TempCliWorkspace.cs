namespace ConfigTransform.Xml.Tests.TestSupport;

/// <summary>
/// Builds a realistic .configtransform-shaped temp workspace (manifest + project + overlay
/// tree) for exercising XmlCliRunner end-to-end, independent of the test process's current
/// working directory. All paths embedded in the generated manifest.json are absolute, so
/// resolution is identical regardless of where the test runner's CWD happens to be. The manifest
/// itself lives at the real repo convention's path,
/// "&lt;RootPath&gt;/.configtransform/ProjectA/manifest.json" (docs/GETTING_STARTED.md) — which
/// also means RootPath is directly usable as the working directory for manifest auto-discovery
/// (ManifestDiscovery) tests, since it holds exactly one candidate.
/// </summary>
internal sealed class TempCliWorkspace : IDisposable
{
    public string RootPath { get; } = Directory.CreateTempSubdirectory("configtransform-cli-tests-").FullName;
    public string ManifestPath { get; }

    public TempCliWorkspace()
    {
        var projectDir = Path.Combine(RootPath, "Project");
        var configTransformDir = Path.Combine(RootPath, ".configtransform", "ProjectA");
        var overlayRoot = Path.Combine(configTransformDir, "App.config"); // must match the derived OverlayFolderName
        Directory.CreateDirectory(projectDir);
        Directory.CreateDirectory(Path.Combine(overlayRoot, "Environments"));
        Directory.CreateDirectory(Path.Combine(overlayRoot, "Clients", "ClientA"));

        File.WriteAllText(Path.Combine(projectDir, "App.config"), """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <appSettings>
                <add key="ApiUrl" value="https://dev.example.com" />
              </appSettings>
            </configuration>
            """);

        File.WriteAllText(Path.Combine(overlayRoot, "Environments", "Production.config"), """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration xmlns:xdt="http://schemas.microsoft.com/XML-Document-Transform">
              <appSettings>
                <add key="ApiUrl" value="https://prod.example.com" xdt:Transform="SetAttributes" xdt:Locator="Match(key)" />
              </appSettings>
            </configuration>
            """);

        File.WriteAllText(Path.Combine(overlayRoot, "Clients", "ClientA", "Production.config"), """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration xmlns:xdt="http://schemas.microsoft.com/XML-Document-Transform">
              <appSettings>
                <add key="ApiUrl" value="https://clienta.example.com" xdt:Transform="SetAttributes" xdt:Locator="Match(key)" />
              </appSettings>
            </configuration>
            """);

        ManifestPath = Path.Combine(configTransformDir, "manifest.json");
        var projectDirPath = projectDir.Replace('\\', '/');
        File.WriteAllText(ManifestPath,
            $$"""{ "directory": "{{projectDirPath}}", "files": [ { "relativeToDirectory": "App.config", "type": "xml" } ] }""");
    }

    public void Dispose()
    {
        if (Directory.Exists(RootPath))
            Directory.Delete(RootPath, recursive: true);
    }
}
