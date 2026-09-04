using Xunit;

namespace ConfigTransform.Core.Tests;

public class HelpPrinterTests
{
    private static readonly FormatEngineRegistry TwoEngines = new(
    [
        new FormatEngine("XML", [".config", ".xml"], "xml", DummyMerge, DummyAuthor),
        new FormatEngine("JSON", [".json"], "json", DummyMerge, DummyAuthor),
    ]);

    [Fact]
    public void Prints_the_command_name_and_a_usage_section()
    {
        var stdout = new StringWriter();
        HelpPrinter.Print(stdout, TwoEngines);

        var output = stdout.ToString();
        Assert.Contains("configtransform", output);
        Assert.Contains("USAGE", output);
    }

    [Fact]
    public void Lists_every_registered_engines_extensions_dynamically()
    {
        var stdout = new StringWriter();
        HelpPrinter.Print(stdout, TwoEngines);

        var output = stdout.ToString();
        Assert.Contains(".config", output);
        Assert.Contains(".xml", output);
        Assert.Contains(".json", output);
    }

    [Fact]
    public void Includes_a_common_commands_quick_reference()
    {
        var stdout = new StringWriter();
        HelpPrinter.Print(stdout, TwoEngines);

        Assert.Contains("COMMON COMMANDS", stdout.ToString());
    }

    [Theory]
    [InlineData("--dry-run — print")]
    [InlineData("--diff — print")]
    [InlineData("--output, -o — write")]
    [InlineData("--list — show")]
    [InlineData("set — author")]
    public void Every_command_has_both_an_easy_and_a_tldr_example(string sectionHeader)
    {
        var stdout = new StringWriter();
        HelpPrinter.Print(stdout, TwoEngines);

        var output = stdout.ToString();
        var sectionStart = output.IndexOf(sectionHeader, StringComparison.Ordinal);
        Assert.True(sectionStart >= 0, $"Expected a section starting with '{sectionHeader}'.");

        // Scope the easy:/tldr: search to a window starting at this command's own section, so a
        // Theory case doesn't accidentally match a later command's examples instead of its own.
        var section = output[sectionStart..Math.Min(output.Length, sectionStart + 800)];
        Assert.Contains("easy:", section);
        Assert.Contains("tldr:", section);
    }

    [Fact]
    public void Points_to_the_full_reference_doc()
    {
        var stdout = new StringWriter();
        HelpPrinter.Print(stdout, TwoEngines);

        Assert.Contains("docs/USAGE.md", stdout.ToString());
    }

    private static string DummyMerge(string basePath, IReadOnlyList<string> patchPathsInOrder) =>
        throw new NotImplementedException("Not invoked by these tests -- HelpPrinter never calls into an engine's Merge/Author.");

    private static string DummyAuthor(
        string precedingContent, string? existingTargetContent, bool isBaseTarget,
        IReadOnlyList<MatchSpec> matches, IReadOnlyList<MatchSpec> setFields) =>
        throw new NotImplementedException("Not invoked by these tests -- HelpPrinter never calls into an engine's Merge/Author.");
}
