using ConfigTransform.Core;
using Xunit;

namespace ConfigTransform.Cli.Tests;

/// <summary>
/// End-to-end tests of the help page through <see cref="CliRunner.Run(string[],TextWriter,TextWriter,FormatEngineRegistry,string?)"/>
/// against the real production <see cref="FormatEngines.All"/> registry. Content-level assertions
/// (exact wording) intentionally live in <c>HelpPrinterTests</c> (ConfigTransform.Core.Tests) —
/// these confirm the dispatcher actually reaches <see cref="HelpPrinter"/> for every entry point,
/// exits 0, writes nothing to stderr, and never touches disk.
/// </summary>
public class HelpTests
{
    [Fact]
    public void No_arguments_at_all_prints_help_and_exits_zero()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = CliRunner.Run(Array.Empty<string>(), stdout, stderr, FormatEngines.All, Directory.GetCurrentDirectory());

        Assert.Equal(0, exitCode);
        Assert.Contains("configtransform", stdout.ToString());
        Assert.Contains("USAGE", stdout.ToString());
        Assert.Empty(stderr.ToString());
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("help")]
    public void Every_help_entry_point_prints_the_same_page(string arg)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = CliRunner.Run(new[] { arg }, stdout, stderr, FormatEngines.All, Directory.GetCurrentDirectory());

        Assert.Equal(0, exitCode);
        Assert.Contains("COMMON COMMANDS", stdout.ToString());
        Assert.Empty(stderr.ToString());
    }

    [Fact]
    public void Help_lists_every_currently_registered_format_extension()
    {
        var stdout = new StringWriter();

        CliRunner.Run(new[] { "--help" }, stdout, new StringWriter(), FormatEngines.All, Directory.GetCurrentDirectory());

        var output = stdout.ToString();
        foreach (var engine in FormatEngines.All.Engines)
            foreach (var extension in engine.Extensions)
                Assert.Contains(extension, output);
    }

    [Fact]
    public void Help_never_requires_a_working_directory_or_touches_disk()
    {
        // No .configtransform tree exists at this path at all -- help must not try to resolve
        // one, unlike every other command.
        using var empty = new TempEmptyDirectory();

        var stdout = new StringWriter();
        var exitCode = CliRunner.Run(Array.Empty<string>(), stdout, new StringWriter(), FormatEngines.All, empty.Path);

        Assert.Equal(0, exitCode);
        Assert.Empty(Directory.GetFileSystemEntries(empty.Path));
    }

    private sealed class TempEmptyDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("configtransform-help-tests-").FullName;
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
