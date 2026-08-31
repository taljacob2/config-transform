using ConfigTransform.Core.Tests.TestSupport;
using Xunit;

namespace ConfigTransform.Core.Tests;

public class LayerResolutionTests
{
    [Fact]
    public void Resolves_all_three_layers_when_all_present()
    {
        using var root = new TempDirectory();
        var (projectDir, overlayRoot) = CreateLayout(root.Path);
        Directory.CreateDirectory(Path.Combine(overlayRoot, "Environments"));
        Directory.CreateDirectory(Path.Combine(overlayRoot, "Clients", "ClientA"));

        File.WriteAllText(Path.Combine(projectDir, "App.config"), "base");
        File.WriteAllText(Path.Combine(overlayRoot, "Environments", "Production.config"), "env");
        File.WriteAllText(Path.Combine(overlayRoot, "Clients", "ClientA", "Production.config"), "client");

        var result = LayerResolution.Resolve(projectDir, "App.config", overlayRoot, "ClientA", "Production");

        Assert.Equal(Path.Combine(projectDir, "App.config"), result.BasePath);
        Assert.Equal(Path.Combine(overlayRoot, "Environments", "Production.config"), result.EnvironmentOverlayPath);
        Assert.Equal(Path.Combine(overlayRoot, "Clients", "ClientA", "Production.config"), result.ClientOverlayPath);
        Assert.All(result.Report, line => Assert.DoesNotContain("not found", line));
    }

    [Fact]
    public void Missing_overlays_are_reported_but_not_fatal()
    {
        using var root = new TempDirectory();
        var (projectDir, overlayRoot) = CreateLayout(root.Path);
        File.WriteAllText(Path.Combine(projectDir, "App.config"), "base");
        // Deliberately: no Environments/ or Clients/ directories created under overlayRoot.

        var result = LayerResolution.Resolve(projectDir, "App.config", overlayRoot, "ClientA", "Production");

        Assert.Equal(Path.Combine(projectDir, "App.config"), result.BasePath);
        Assert.Null(result.EnvironmentOverlayPath);
        Assert.Null(result.ClientOverlayPath);
        Assert.Contains(result.Report, line => line.Contains("Environments/Production.config") && line.Contains("not found"));
        Assert.Contains(result.Report, line => line.Contains("Clients/ClientA/Production.config") && line.Contains("not found"));
    }

    [Fact]
    public void Missing_base_file_throws_and_does_not_report_overlays()
    {
        using var root = new TempDirectory();
        var (projectDir, overlayRoot) = CreateLayout(root.Path);
        // Deliberately: App.config never created.

        Assert.Throws<FileNotFoundException>(() =>
            LayerResolution.Resolve(projectDir, "App.config", overlayRoot, "ClientA", "Production"));
    }

    [Fact]
    public void Overlay_file_name_uses_the_base_files_extension()
    {
        using var root = new TempDirectory();
        var (projectDir, overlayRoot) = CreateLayout(root.Path);
        Directory.CreateDirectory(Path.Combine(overlayRoot, "Environments"));

        File.WriteAllText(Path.Combine(projectDir, "appsettings.json"), "{}");
        File.WriteAllText(Path.Combine(overlayRoot, "Environments", "Production.json"), "{}");

        var result = LayerResolution.Resolve(projectDir, "appsettings.json", overlayRoot, "ClientA", "Production");

        Assert.Equal(Path.Combine(overlayRoot, "Environments", "Production.json"), result.EnvironmentOverlayPath);
    }

    private static (string projectDir, string overlayRoot) CreateLayout(string root)
    {
        var projectDir = Path.Combine(root, "Project");
        var overlayRoot = Path.Combine(root, "Overlay");
        Directory.CreateDirectory(projectDir);
        return (projectDir, overlayRoot);
    }
}
