using System.Text;

namespace ConfigTransform.Cli;

/// <summary>
/// Makes stdout/stderr UTF-8 regardless of the console's own code page. Without this, .NET on
/// Windows encodes output in the console's code page (437 by default in cmd.exe/Git Bash), so a
/// pipe or file redirect silently loses every character outside it: the chain report's `↓` comes
/// out as the control byte 0x19, and a non-ASCII config value printed by --dry-run (Hebrew, say)
/// becomes `????` — real data loss, not just cosmetics. --output was never affected, since
/// File.WriteAllText always writes UTF-8. See docs/CONFIG_MANAGEMENT.md §6.
///
/// Two cases, deliberately handled differently:
/// - Redirected (pipe/file): write UTF-8 bytes straight to the standard stream — no BOM, so a
///   `--dry-run &gt; out.json` file is byte-identical to what --output writes. Touches no global
///   console state at all.
/// - A real console: switch the console's output code page to UTF-8 for this run, restored on
///   exit — otherwise the user's own shell would stay on UTF-8 after the tool exits. Best effort:
///   if the code page can't be changed (no console attached), output falls back to the default.
/// </summary>
public static class Utf8Console
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static int Run(Func<TextWriter, TextWriter, int> run)
    {
        var originalEncoding = TrySwitchConsoleToUtf8();

        // Read Console.Out/Console.Error only after the switch above: .NET rebuilds both writers
        // with the new encoding when it changes.
        var stdout = Console.IsOutputRedirected ? OpenUtf8(Console.OpenStandardOutput()) : Console.Out;
        var stderr = Console.IsErrorRedirected ? OpenUtf8(Console.OpenStandardError()) : Console.Error;
        try
        {
            return run(stdout, stderr);
        }
        finally
        {
            stdout.Flush();
            stderr.Flush();
            if (originalEncoding is not null)
                TrySetConsoleEncoding(originalEncoding);
        }
    }

    private static TextWriter OpenUtf8(Stream stream) => new StreamWriter(stream, Utf8NoBom) { AutoFlush = true };

    /// <returns>The encoding to restore on exit, or null when nothing was changed.</returns>
    private static Encoding? TrySwitchConsoleToUtf8()
    {
        if (Console.IsOutputRedirected && Console.IsErrorRedirected)
            return null;

        var original = Console.OutputEncoding;
        if (original.CodePage == Utf8NoBom.CodePage)
            return null;

        return TrySetConsoleEncoding(Utf8NoBom) ? original : null;
    }

    private static bool TrySetConsoleEncoding(Encoding encoding)
    {
        try
        {
            Console.OutputEncoding = encoding;
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
