namespace ConfigTransform.Json.Tests.TestSupport;

/// <summary>
/// Builds a realistic .configtransform-shaped temp workspace (manifest + project + overlay
/// tree) for exercising JsonCliRunner end-to-end, independent of the test process's current
/// working directory. All paths embedded in the generated manifest.json are absolute, so
/// resolution is identical regardless of where the test runner's CWD happens to be.
/// </summary>
internal sealed class TempCliWorkspace : IDisposable
{
    public string RootPath { get; } = Directory.CreateTempSubdirectory("configtransform-cli-tests-").FullName;
    public string ManifestPath { get; }

    public TempCliWorkspace()
    {
        var projectDir = Path.Combine(RootPath, "Project");
        var overlayRoot = Path.Combine(RootPath, "appsettings.json"); // must match the derived OverlayFolderName
        Directory.CreateDirectory(projectDir);
        Directory.CreateDirectory(Path.Combine(overlayRoot, "Environments"));
        Directory.CreateDirectory(Path.Combine(overlayRoot, "Clients", "ClientA"));

        File.WriteAllText(Path.Combine(projectDir, "appsettings.json"), """
            { "ApiUrl": "https://dev.example.com" }
            """);

        File.WriteAllText(Path.Combine(overlayRoot, "Environments", "Production.json"), """
            { "ApiUrl": "https://prod.example.com" }
            """);

        File.WriteAllText(Path.Combine(overlayRoot, "Clients", "ClientA", "Production.json"), """
            { "ApiUrl": "https://clienta.example.com" }
            """);

        ManifestPath = Path.Combine(RootPath, "manifest.json");
        var projectDirPath = projectDir.Replace('\\', '/');
        File.WriteAllText(ManifestPath,
            $$"""{ "directory": "{{projectDirPath}}", "files": [ { "relativeToDirectory": "appsettings.json", "type": "json" } ] }""");
    }

    public void Dispose()
    {
        if (Directory.Exists(RootPath))
            Directory.Delete(RootPath, recursive: true);
    }
}
