using Xunit;

namespace ConfigTransform.Core.Tests;

public class CliOptionsParserTests
{
    [Fact]
    public void Parses_a_full_real_run_invocation()
    {
        var options = CliOptionsParser.Parse(new[]
        {
            "--manifest", "manifest.json",
            "--file", "App.config",
            "--client", "ClientA",
            "--environment", "Production",
            "--output", "out.config"
        });

        Assert.Equal("manifest.json", options.ManifestPath);
        Assert.Equal("App.config", options.File);
        Assert.Equal("ClientA", options.Client);
        Assert.Equal("Production", options.Environment);
        Assert.Equal("out.config", options.Output);
        Assert.False(options.DryRun);
        Assert.False(options.Diff);
    }

    [Fact]
    public void File_is_optional()
    {
        var options = CliOptionsParser.Parse(new[]
        {
            "--manifest", "manifest.json",
            "--client", "ClientA",
            "--environment", "Production",
            "--output", "out.config"
        });

        Assert.Null(options.File);
    }

    [Fact]
    public void Missing_manifest_throws()
    {
        Assert.Throws<ArgumentException>(() => CliOptionsParser.Parse(new[]
        {
            "--client", "ClientA", "--environment", "Production", "--output", "out.config"
        }));
    }

    [Fact]
    public void Missing_client_throws()
    {
        Assert.Throws<ArgumentException>(() => CliOptionsParser.Parse(new[]
        {
            "--manifest", "manifest.json", "--environment", "Production", "--output", "out.config"
        }));
    }

    [Fact]
    public void Missing_environment_throws()
    {
        Assert.Throws<ArgumentException>(() => CliOptionsParser.Parse(new[]
        {
            "--manifest", "manifest.json", "--client", "ClientA", "--output", "out.config"
        }));
    }

    [Fact]
    public void Missing_output_throws_for_a_real_run()
    {
        Assert.Throws<ArgumentException>(() => CliOptionsParser.Parse(new[]
        {
            "--manifest", "manifest.json", "--client", "ClientA", "--environment", "Production"
        }));
    }

    [Fact]
    public void Missing_output_is_allowed_with_dry_run()
    {
        var options = CliOptionsParser.Parse(new[]
        {
            "--manifest", "manifest.json", "--client", "ClientA", "--environment", "Production", "--dry-run"
        });

        Assert.True(options.DryRun);
        Assert.Null(options.Output);
    }

    [Fact]
    public void Missing_output_is_allowed_with_diff()
    {
        var options = CliOptionsParser.Parse(new[]
        {
            "--manifest", "manifest.json", "--client", "ClientA", "--environment", "Production", "--diff"
        });

        Assert.True(options.Diff);
    }

    [Fact]
    public void Unrecognized_argument_throws()
    {
        Assert.Throws<ArgumentException>(() => CliOptionsParser.Parse(new[] { "--bogus" }));
    }

    [Fact]
    public void Flag_missing_its_value_throws()
    {
        Assert.Throws<ArgumentException>(() => CliOptionsParser.Parse(new[] { "--manifest" }));
    }
}
