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
    public void Missing_manifest_leaves_ManifestPath_null_for_CliRunner_to_auto_discover()
    {
        var options = CliOptionsParser.Parse(new[]
        {
            "--client", "ClientA", "--environment", "Production", "--output", "out.config"
        });

        Assert.Null(options.ManifestPath);
    }

    [Fact]
    public void Short_flags_parse_the_same_as_their_long_forms()
    {
        var options = CliOptionsParser.Parse(new[]
        {
            "-m", "manifest.json",
            "-f", "App.config",
            "-c", "ClientA",
            "-e", "Production",
            "-o", "out.config"
        });

        Assert.Equal("manifest.json", options.ManifestPath);
        Assert.Equal("App.config", options.File);
        Assert.Equal("ClientA", options.Client);
        Assert.Equal("Production", options.Environment);
        Assert.Equal("out.config", options.Output);
    }

    [Fact]
    public void Short_flag_missing_its_value_throws_naming_the_short_flag()
    {
        var ex = Assert.Throws<ArgumentException>(() => CliOptionsParser.Parse(new[] { "-m" }));
        Assert.Contains("-m", ex.Message);
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
    public void List_needs_only_manifest()
    {
        var options = CliOptionsParser.Parse(new[] { "--manifest", "manifest.json", "--list" });

        Assert.True(options.List);
        Assert.Null(options.Client);
        Assert.Null(options.Environment);
        Assert.Null(options.Output);
    }

    [Fact]
    public void List_still_accepts_an_optional_file_filter()
    {
        var options = CliOptionsParser.Parse(new[]
        {
            "--manifest", "manifest.json", "--file", "App.config", "--list"
        });

        Assert.True(options.List);
        Assert.Equal("App.config", options.File);
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

    [Fact]
    public void Set_verb_needs_neither_client_nor_environment()
    {
        var options = CliOptionsParser.Parse(new[]
        {
            "set", "--manifest", "manifest.json", "--match", "key=ApiUrl", "--set", "value=X"
        });

        Assert.True(options.Set);
        Assert.Null(options.Client);
        Assert.Null(options.Environment);
    }

    [Fact]
    public void Set_verb_collects_repeated_match_and_set_flags_in_order()
    {
        var options = CliOptionsParser.Parse(new[]
        {
            "set", "--manifest", "manifest.json",
            "--match", "name=Prod", "--match", "env=Production",
            "--set", "connectionString=X", "--set", "providerName=Y"
        });

        Assert.Equal(new[] { "name=Prod", "env=Production" }, options.Match);
        Assert.Equal(new[] { "connectionString=X", "providerName=Y" }, options.SetFields);
    }

    [Fact]
    public void Set_verb_requires_at_least_one_match()
    {
        Assert.Throws<ArgumentException>(() => CliOptionsParser.Parse(new[]
        {
            "set", "--manifest", "manifest.json", "--set", "value=X"
        }));
    }

    [Fact]
    public void Set_verb_requires_at_least_one_set_field()
    {
        Assert.Throws<ArgumentException>(() => CliOptionsParser.Parse(new[]
        {
            "set", "--manifest", "manifest.json", "--match", "key=ApiUrl"
        }));
    }

    [Fact]
    public void Set_verb_rejects_client_without_environment()
    {
        var ex = Assert.Throws<ArgumentException>(() => CliOptionsParser.Parse(new[]
        {
            "set", "--manifest", "manifest.json", "--client", "ClientA",
            "--match", "key=ApiUrl", "--set", "value=X"
        }));
        Assert.Contains("--client requires --environment", ex.Message);
    }

    [Fact]
    public void Set_verb_rejects_output()
    {
        Assert.Throws<ArgumentException>(() => CliOptionsParser.Parse(new[]
        {
            "set", "--manifest", "manifest.json", "--output", "out.config",
            "--match", "key=ApiUrl", "--set", "value=X"
        }));
    }

    [Fact]
    public void Set_verb_and_list_together_is_an_error()
    {
        Assert.Throws<ArgumentException>(() => CliOptionsParser.Parse(new[]
        {
            "set", "--manifest", "manifest.json", "--list",
            "--match", "key=ApiUrl", "--set", "value=X"
        }));
    }

    [Fact]
    public void The_set_verb_is_only_recognized_as_the_very_first_argument()
    {
        // A literal "set" elsewhere (e.g. as a --manifest value) is just a normal string, not
        // the verb -- only args[0] is checked.
        var options = CliOptionsParser.Parse(new[] { "--manifest", "set", "--list" });
        Assert.False(options.Set);
        Assert.Equal("set", options.ManifestPath);
    }
}
