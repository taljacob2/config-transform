using System.Diagnostics;

namespace ConfigTransform.Core;

/// <summary>
/// Renders a unified diff between two pieces of text content using <c>git diff --no-index</c>,
/// via throwaway temp files cleaned up immediately after. Requires <c>git</c> on PATH — a safe
/// assumption for this tool's users, since git-crypt (a prerequisite of the whole consuming
/// architecture, CONFIG_MANAGEMENT.md §7) already requires it.
/// </summary>
public static class GitDiff
{
    /// <returns>The diff output (empty when the two contents are identical).</returns>
    public static string Render(string leftContent, string rightContent)
    {
        var leftPath = Path.GetTempFileName();
        var rightPath = Path.GetTempFileName();

        try
        {
            File.WriteAllText(leftPath, leftContent);
            File.WriteAllText(rightPath, rightContent);

            var startInfo = new ProcessStartInfo("git")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("diff");
            startInfo.ArgumentList.Add("--no-index");
            startInfo.ArgumentList.Add("--color=always");
            startInfo.ArgumentList.Add("--");
            startInfo.ArgumentList.Add(leftPath);
            startInfo.ArgumentList.Add(rightPath);

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start 'git diff'.");

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            // `git diff --no-index` exits 1 when the inputs differ -- the expected, successful
            // case, not an error. Only a higher exit code indicates a real failure.
            if (process.ExitCode > 1)
                throw new InvalidOperationException($"'git diff' failed (exit {process.ExitCode}): {error}");

            return output;
        }
        finally
        {
            File.Delete(leftPath);
            File.Delete(rightPath);
        }
    }
}
