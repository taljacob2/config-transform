using System.Diagnostics;
using System.Text;
using ConfigTransform.Cli.Tests.TestSupport;
using Xunit;

namespace ConfigTransform.Cli.Tests;

/// <summary>
/// Runs the real built CLI as a child process with stdout redirected — the one case
/// <see cref="Utf8Console"/> exists for, and one an in-process <c>CliRunner.Run</c> test with a
/// StringWriter can never reach (it bypasses the console's encoding entirely). Before the fix, on
/// a Windows console code page like 437, the chain report's `↓` came through as 0x19 and a
/// non-ASCII config value as `?`.
/// </summary>
public class Utf8ConsoleTests
{
    [Fact]
    public void Redirected_stdout_is_utf8_without_a_bom()
    {
        using var root = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(root.Path, "Project"));
        File.WriteAllText(Path.Combine(root.Path, "Project", ".env"), "TITLE=\"שלום café\"\n");
        var layerDir = Path.Combine(root.Path, ".configtransform", "Environments", "Production");
        Directory.CreateDirectory(layerDir);
        File.WriteAllText(Path.Combine(layerDir, "configtransform.json"), """
            { "resources": [ { "path": "Project/.env" } ] }
            """);

        var (exitCode, stdoutBytes) = RunCli(root.Path, "--resource", "Project/.env", "--environment", "Production", "--dry-run");

        Assert.Equal(0, exitCode);
        Assert.False(stdoutBytes.AsSpan().StartsWith(Encoding.UTF8.Preamble), "stdout must not start with a UTF-8 BOM");
        var stdout = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(stdoutBytes);
        Assert.Contains("↓", stdout);
        Assert.Contains("TITLE=\"שלום café\"", stdout);
    }

    private static (int ExitCode, byte[] Stdout) RunCli(string workingDirectory, params string[] args)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "ConfigTransform.Cli.dll"));
        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start the CLI.");
        using var stdout = new MemoryStream();
        var stderrTask = process.StandardError.ReadToEndAsync();
        process.StandardOutput.BaseStream.CopyTo(stdout);
        process.WaitForExit();
        stderrTask.Wait();
        return (process.ExitCode, stdout.ToArray());
    }
}
