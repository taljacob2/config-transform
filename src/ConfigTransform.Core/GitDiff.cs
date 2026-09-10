using System.Diagnostics;
using System.Text.RegularExpressions;

namespace ConfigTransform.Core;

/// <summary>
/// Renders a unified diff between two pieces of text content using <c>git diff --no-index</c>,
/// via throwaway temp files cleaned up immediately after. Requires <c>git</c> on PATH — a safe
/// assumption for this tool's users, since git-crypt (a prerequisite of the whole consuming
/// architecture, CONFIG_MANAGEMENT.md §7) already requires it.
/// </summary>
public static class GitDiff
{
    // git's own 4-line file-identity header ("diff --git a/... b/...", "index ...", "--- a/...",
    // "+++ b/..."), reported against the published tool: since a/b here are always throwaway OS
    // temp file paths (`Render`'s leftPath/rightPath), these lines are meaningless noise, not
    // useful file identity -- the caller already shows the real resource path (the "Resolving
    // '<path>'" header for one resource, the "=== <path> ===" banner for every resource). Real
    // content lines never collide with this prefix set: every hunk-body line already starts with
    // a '+'/'-'/' ' diff marker (or '\' for "\ No newline at end of file"), so a line beginning
    // with one of these four literal strings is always git's own meta line, never file content.
    private static readonly string[] MetaLinePrefixes = ["diff --git ", "index ", "--- ", "+++ "];

    // Strips ANSI SGR color codes (from --color=always) so a meta line can be recognized under
    // its coloring without altering the line itself -- git wraps each of the four meta lines
    // (and every other line) in \x1b[...m...\x1b[m, prefix and all. Internal (not private) so
    // LayerDiffAttribution can classify a Render'd line's leading marker (' '/'-'/'+'/'@') the
    // same way, without a second copy of the same pattern.
    internal static readonly Regex AnsiEscapeSequence = new(@"\x1b\[[0-9;]*m", RegexOptions.Compiled);

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

            return StripMetaHeaderLines(output);
        }
        finally
        {
            File.Delete(leftPath);
            File.Delete(rightPath);
        }
    }

    private static string StripMetaHeaderLines(string diffOutput)
    {
        if (diffOutput.Length == 0)
            return diffOutput;

        var lines = diffOutput.Split('\n').Where(line =>
        {
            var plain = AnsiEscapeSequence.Replace(line, "");
            return !MetaLinePrefixes.Any(prefix => plain.StartsWith(prefix, StringComparison.Ordinal));
        });

        return string.Join('\n', lines);
    }
}
