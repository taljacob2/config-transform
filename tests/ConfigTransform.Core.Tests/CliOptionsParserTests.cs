using Xunit;

namespace ConfigTransform.Core.Tests;

public class CliOptionsParserTests
{
    [Fact]
    public void Parses_a_full_real_run_invocation()
    {
        var options = CliOptionsParser.Parse(new[]
        {
            "--resource", "Project/App.config",
            "--client", "ClientA",
            "--environment", "Production",
            "--output", "out.config"
        });

        Assert.Equal("Project/App.config", options.Resource);
        Assert.Equal("ClientA", options.Client);
        Assert.Equal("Production", options.Environment);
        Assert.Equal("out.config", options.Output);
        Assert.False(options.DryRun);
        Assert.False(options.Diff);
    }

    [Fact]
    public void Resource_is_optional_for_a_real_run_meaning_every_resource_the_layer_touches()
    {
        var options = CliOptionsParser.Parse(new[]
        {
            "--client", "ClientA", "--environment", "Production", "--output", "out/"
        });

        Assert.Null(options.Resource);
    }

    [Fact]
    public void Short_flags_parse_the_same_as_their_long_forms()
    {
        var options = CliOptionsParser.Parse(new[]
        {
            "-r", "Project/App.config",
            "-c", "ClientA",
            "-e", "Production",
            "-o", "out.config"
        });

        Assert.Equal("Project/App.config", options.Resource);
        Assert.Equal("ClientA", options.Client);
        Assert.Equal("Production", options.Environment);
        Assert.Equal("out.config", options.Output);
    }

    [Fact]
    public void Short_flag_missing_its_value_throws_naming_the_short_flag()
    {
        var ex = Assert.Throws<ArgumentException>(() => CliOptionsParser.Parse(new[] { "-r" }));
        Assert.Contains("-r", ex.Message);
        Assert.Contains("Try: -r <value>", ex.Message);
    }

    [Fact]
    public void A_real_run_accepts_environment_alone_targeting_that_environment_layer()
    {
        var options = CliOptionsParser.Parse(new[]
        {
            "--environment", "Production", "--output", "out.config"
        });

        Assert.Null(options.Client);
        Assert.Equal("Production", options.Environment);
    }

    [Fact]
    public void A_real_run_accepts_neither_client_nor_environment_targeting_the_base_file_directly()
    {
        var options = CliOptionsParser.Parse(new[]
        {
            "--resource", "Project/App.config", "--output", "out.config"
        });

        Assert.Null(options.Client);
        Assert.Null(options.Environment);
    }

    [Fact]
    public void A_real_run_rejects_client_without_environment()
    {
        var ex = Assert.Throws<ArgumentException>(() => CliOptionsParser.Parse(new[]
        {
            "--client", "ClientA", "--output", "out.config"
        }));
        Assert.Contains("--client requires --environment", ex.Message);
        Assert.Contains("Try: add --environment", ex.Message);
    }

    [Fact]
    public void A_real_run_accepts_host_alongside_client_and_environment()
    {
        var options = CliOptionsParser.Parse(new[]
        {
            "--client", "Acme", "--environment", "Production", "--host", "192.168.10.10", "--dry-run"
        });

        Assert.Equal("192.168.10.10", options.Host);
    }

    [Fact]
    public void Short_flag_dash_H_parses_the_same_as_long_form_host()
    {
        var options = CliOptionsParser.Parse(new[]
        {
            "-c", "Acme", "-e", "Production", "-H", "192.168.10.10", "--dry-run"
        });

        Assert.Equal("192.168.10.10", options.Host);
    }

    [Fact]
    public void A_real_run_rejects_host_without_client_and_environment()
    {
        var ex = Assert.Throws<ArgumentException>(() => CliOptionsParser.Parse(new[]
        {
            "--host", "192.168.10.10", "--dry-run"
        }));
        Assert.Contains("--host requires --client and --environment", ex.Message);
    }

    [Fact]
    public void A_real_run_rejects_host_with_environment_but_no_client()
    {
        Assert.Throws<ArgumentException>(() => CliOptionsParser.Parse(new[]
        {
            "--environment", "Production", "--host", "192.168.10.10", "--dry-run"
        }));
    }

    [Fact]
    public void Missing_output_throws_for_a_real_run()
    {
        var ex = Assert.Throws<ArgumentException>(() => CliOptionsParser.Parse(new[]
        {
            "--client", "ClientA", "--environment", "Production"
        }));
        Assert.Contains("--output is required", ex.Message);
        Assert.Contains("Try: add --output", ex.Message);
    }

    [Fact]
    public void Missing_output_is_allowed_with_dry_run()
    {
        var options = CliOptionsParser.Parse(new[]
        {
            "--client", "ClientA", "--environment", "Production", "--dry-run"
        });

        Assert.True(options.DryRun);
        Assert.Null(options.Output);
    }

    [Fact]
    public void Missing_output_is_allowed_with_diff()
    {
        var options = CliOptionsParser.Parse(new[]
        {
            "--client", "ClientA", "--environment", "Production", "--diff"
        });

        Assert.True(options.Diff);
    }

    [Fact]
    public void Missing_output_is_allowed_with_diff_layers()
    {
        var options = CliOptionsParser.Parse(new[]
        {
            "--client", "ClientA", "--environment", "Production", "--diff-layers"
        });

        Assert.True(options.DiffLayers);
        Assert.False(options.Diff);
    }

    [Fact]
    public void Diff_and_diff_layers_together_is_an_error()
    {
        var ex = Assert.Throws<ArgumentException>(() => CliOptionsParser.Parse(new[]
        {
            "--client", "ClientA", "--environment", "Production", "--diff", "--diff-layers"
        }));

        Assert.Contains("--diff and --diff-layers are mutually exclusive", ex.Message);
    }

    [Fact]
    public void Diff_layers_is_not_valid_with_init()
    {
        var ex = Assert.Throws<ArgumentException>(() => CliOptionsParser.Parse(new[]
        {
            "init", "--diff-layers"
        }));

        Assert.Contains("--diff-layers is not valid with 'init'", ex.Message);
    }

    [Fact]
    public void List_accepts_environment_alone()
    {
        var options = CliOptionsParser.Parse(new[] { "--list", "--environment", "Production" });

        Assert.True(options.List);
        Assert.Null(options.Client);
        Assert.Equal("Production", options.Environment);
        Assert.Null(options.Output);
    }

    [Fact]
    public void List_accepts_client_and_environment()
    {
        var options = CliOptionsParser.Parse(new[]
        {
            "--list", "--client", "Acme", "--environment", "Production"
        });

        Assert.True(options.List);
        Assert.Equal("Acme", options.Client);
        Assert.Equal("Production", options.Environment);
    }

    [Fact]
    public void List_accepts_resource_alone_as_a_reverse_lookup()
    {
        var options = CliOptionsParser.Parse(new[] { "--list", "--resource", "Project/App.config" });

        Assert.True(options.List);
        Assert.Equal("Project/App.config", options.Resource);
        Assert.Null(options.Client);
        Assert.Null(options.Environment);
    }

    [Fact]
    public void List_with_neither_resource_nor_environment_throws()
    {
        Assert.Throws<ArgumentException>(() => CliOptionsParser.Parse(new[] { "--list" }));
    }

    [Fact]
    public void List_rejects_combining_resource_with_client_or_environment()
    {
        Assert.Throws<ArgumentException>(() => CliOptionsParser.Parse(new[]
        {
            "--list", "--resource", "Project/App.config", "--environment", "Production"
        }));
    }

    [Fact]
    public void List_rejects_client_without_environment()
    {
        Assert.Throws<ArgumentException>(() => CliOptionsParser.Parse(new[]
        {
            "--list", "--client", "Acme"
        }));
    }

    [Fact]
    public void List_accepts_client_environment_and_host()
    {
        var options = CliOptionsParser.Parse(new[]
        {
            "--list", "--client", "Acme", "--environment", "Production", "--host", "192.168.10.10"
        });

        Assert.Equal("192.168.10.10", options.Host);
    }

    [Fact]
    public void List_rejects_host_without_client_and_environment()
    {
        var ex = Assert.Throws<ArgumentException>(() => CliOptionsParser.Parse(new[]
        {
            "--list", "--environment", "Production", "--host", "192.168.10.10"
        }));
        Assert.Contains("--host requires --client and --environment", ex.Message);
    }

    [Fact]
    public void List_rejects_combining_resource_with_host()
    {
        Assert.Throws<ArgumentException>(() => CliOptionsParser.Parse(new[]
        {
            "--list", "--resource", "Project/App.config", "--host", "192.168.10.10"
        }));
    }

    [Fact]
    public void Unrecognized_argument_throws()
    {
        Assert.Throws<ArgumentException>(() => CliOptionsParser.Parse(new[] { "--bogus" }));
    }

    [Theory]
    [InlineData("--otuput", "--output")]
    [InlineData("--lsit", "--list")]
    [InlineData("--dif", "--diff")]
    [InlineData("--clint", "--client")]
    public void Unrecognized_argument_close_to_a_known_flag_suggests_it(string typo, string expectedSuggestion)
    {
        var ex = Assert.Throws<ArgumentException>(() => CliOptionsParser.Parse(new[] { typo }));
        Assert.Contains($"did you mean {expectedSuggestion}?", ex.Message);
    }

    [Fact]
    public void Unrecognized_argument_with_no_close_match_falls_back_to_the_generic_hint()
    {
        var ex = Assert.Throws<ArgumentException>(() => CliOptionsParser.Parse(new[] { "--totally-bogus-xyz" }));
        Assert.DoesNotContain("did you mean", ex.Message);
        Assert.Contains("Try: configtransform --help", ex.Message);
    }

    [Fact]
    public void Flag_missing_its_value_throws()
    {
        Assert.Throws<ArgumentException>(() => CliOptionsParser.Parse(new[] { "--resource" }));
    }

    [Fact]
    public void Set_verb_needs_neither_client_nor_environment()
    {
        var options = CliOptionsParser.Parse(new[]
        {
            "set", "--resource", "Project/App.config", "--match", "key=ApiUrl", "--set", "value=X"
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
            "set", "--resource", "Project/App.config",
            "--match", "name=Prod", "--match", "env=Production",
            "--set", "connectionString=X", "--set", "providerName=Y"
        });

        Assert.Equal(new[] { "name=Prod", "env=Production" }, options.Match);
        Assert.Equal(new[] { "connectionString=X", "providerName=Y" }, options.SetFields);
    }

    [Fact]
    public void Set_verb_requires_resource()
    {
        Assert.Throws<ArgumentException>(() => CliOptionsParser.Parse(new[]
        {
            "set", "--match", "key=ApiUrl", "--set", "value=X"
        }));
    }

    [Fact]
    public void Set_verb_requires_at_least_one_match()
    {
        Assert.Throws<ArgumentException>(() => CliOptionsParser.Parse(new[]
        {
            "set", "--resource", "Project/App.config", "--set", "value=X"
        }));
    }

    [Fact]
    public void Set_verb_requires_at_least_one_set_field()
    {
        Assert.Throws<ArgumentException>(() => CliOptionsParser.Parse(new[]
        {
            "set", "--resource", "Project/App.config", "--match", "key=ApiUrl"
        }));
    }

    [Fact]
    public void Set_verb_rejects_client_without_environment()
    {
        var ex = Assert.Throws<ArgumentException>(() => CliOptionsParser.Parse(new[]
        {
            "set", "--resource", "Project/App.config", "--client", "ClientA",
            "--match", "key=ApiUrl", "--set", "value=X"
        }));
        Assert.Contains("--client requires --environment", ex.Message);
    }

    [Fact]
    public void Set_verb_accepts_host_alongside_client_and_environment()
    {
        var options = CliOptionsParser.Parse(new[]
        {
            "set", "--resource", "Project/App.config", "--client", "Acme", "--environment", "Production",
            "--host", "192.168.10.10", "--match", "key=ApiUrl", "--set", "value=X"
        });

        Assert.Equal("192.168.10.10", options.Host);
    }

    [Fact]
    public void Set_verb_rejects_host_without_client_and_environment()
    {
        var ex = Assert.Throws<ArgumentException>(() => CliOptionsParser.Parse(new[]
        {
            "set", "--resource", "Project/App.config", "--host", "192.168.10.10",
            "--match", "key=ApiUrl", "--set", "value=X"
        }));
        Assert.Contains("--host requires --client and --environment", ex.Message);
    }

    [Fact]
    public void Set_verb_rejects_output()
    {
        Assert.Throws<ArgumentException>(() => CliOptionsParser.Parse(new[]
        {
            "set", "--resource", "Project/App.config", "--output", "out.config",
            "--match", "key=ApiUrl", "--set", "value=X"
        }));
    }

    [Fact]
    public void Set_verb_and_list_together_is_an_error()
    {
        Assert.Throws<ArgumentException>(() => CliOptionsParser.Parse(new[]
        {
            "set", "--resource", "Project/App.config", "--list",
            "--match", "key=ApiUrl", "--set", "value=X"
        }));
    }

    [Fact]
    public void No_arguments_at_all_means_help()
    {
        var options = CliOptionsParser.Parse(Array.Empty<string>());
        Assert.True(options.Help);
    }

    [Fact]
    public void Bare_help_verb_means_help()
    {
        var options = CliOptionsParser.Parse(new[] { "help" });
        Assert.True(options.Help);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("help")]
    public void Help_flag_wins_regardless_of_position_or_other_flags(string helpFlag)
    {
        var options = CliOptionsParser.Parse(new[]
        {
            "--client", "ClientA", "--environment", "Production", helpFlag
        });

        Assert.True(options.Help);
    }

    [Fact]
    public void Bare_help_verb_wins_trailing_after_resource_and_environment_flags()
    {
        // Regression: bare "help" used to only short-circuit as args[0], so e.g.
        // `configtransform -e production -r App.config help` fell through to the switch's
        // default case and threw "Unrecognized argument: 'help'." instead of printing help.
        var options = CliOptionsParser.Parse(new[]
        {
            "-e", "production", "-r", "App.config", "help"
        });

        Assert.True(options.Help);
    }

    [Fact]
    public void A_flag_value_that_happens_to_equal_help_is_not_mistaken_for_the_help_verb()
    {
        var options = CliOptionsParser.Parse(new[]
        {
            "set", "--resource", "App.config", "--match", "value=help", "--set", "x=y"
        });

        Assert.False(options.Help);
        Assert.Equal(new[] { "value=help" }, options.Match);
    }

    [Fact]
    public void Help_flag_wins_even_over_what_would_otherwise_be_a_validation_error()
    {
        // No --environment at all would normally throw ("--environment is required") -- --help
        // short-circuits before that validation ever runs.
        var options = CliOptionsParser.Parse(new[] { "--client", "ClientA", "--help" });
        Assert.True(options.Help);
    }

    [Fact]
    public void A_flag_value_that_happens_to_equal_dash_h_is_not_mistaken_for_the_help_flag()
    {
        // Token-position-aware: "-h" consumed by --set as a value never reaches the switch as a
        // flag, so it must not trigger help.
        var options = CliOptionsParser.Parse(new[]
        {
            "set", "--resource", "Project/App.config", "--match", "key=ApiUrl", "--set", "value=-h"
        });

        Assert.False(options.Help);
        Assert.Equal(new[] { "value=-h" }, options.SetFields);
    }

    [Fact]
    public void The_set_verb_is_only_recognized_as_the_very_first_argument()
    {
        // A literal "set" elsewhere (e.g. as a --resource value) is just a normal string, not
        // the verb -- only args[0] is checked.
        var options = CliOptionsParser.Parse(new[]
        {
            "--resource", "set", "--client", "ClientA", "--environment", "Production", "--output", "out.config"
        });
        Assert.False(options.Set);
        Assert.Equal("set", options.Resource);
    }

    [Fact]
    public void Init_verb_collects_repeated_environment_client_host_and_resource_flags_into_their_own_lists()
    {
        var options = CliOptionsParser.Parse(new[]
        {
            "init", "--environment", "Production", "--environment", "Test",
            "--client", "Acme", "--host", "192.168.10.10", "--resource", "Project/App.config"
        });

        Assert.True(options.Init);
        Assert.Equal(new[] { "Production", "Test" }, options.InitEnvironments);
        Assert.Equal(new[] { "Acme" }, options.InitClients);
        Assert.Equal(new[] { "192.168.10.10" }, options.InitHosts);
        Assert.Equal(new[] { "Project/App.config" }, options.InitResources);
        // The singular fields every other mode uses stay untouched -- init never sets them.
        Assert.Null(options.Client);
        Assert.Null(options.Environment);
        Assert.Null(options.Host);
        Assert.Null(options.Resource);
    }

    [Fact]
    public void Init_verb_needs_no_flags_at_all()
    {
        var options = CliOptionsParser.Parse(new[] { "init" });

        Assert.True(options.Init);
        Assert.Empty(options.InitEnvironments);
        Assert.Empty(options.InitClients);
        Assert.Empty(options.InitHosts);
        Assert.Empty(options.InitResources);
    }

    [Fact]
    public void Init_verb_rejects_host_without_client_and_environment()
    {
        var ex = Assert.Throws<ArgumentException>(() => CliOptionsParser.Parse(new[]
        {
            "init", "--environment", "Production", "--host", "192.168.10.10", "--resource", "x"
        }));
        Assert.Contains("--host requires --client and --environment", ex.Message);
    }

    [Fact]
    public void Init_verb_rejects_host_with_client_but_no_environment()
    {
        // The existing "--client requires --environment" check fires first (client-without-
        // environment is already invalid on its own, regardless of --host) -- still correctly
        // rejected, just via that message rather than --host's own.
        var ex = Assert.Throws<ArgumentException>(() => CliOptionsParser.Parse(new[]
        {
            "init", "--client", "Acme", "--host", "192.168.10.10", "--resource", "x"
        }));
        Assert.Contains("--client requires --environment", ex.Message);
    }

    [Fact]
    public void Init_verb_accepts_host_alongside_client_and_environment()
    {
        var options = CliOptionsParser.Parse(new[]
        {
            "init", "--environment", "Production", "--client", "Acme", "--host", "192.168.10.10"
        });

        Assert.Equal(new[] { "192.168.10.10" }, options.InitHosts);
    }

    [Fact]
    public void Init_verb_parses_scan_root_yes_no_scan_and_template()
    {
        var options = CliOptionsParser.Parse(new[] { "init", "--scan-root", "src", "--yes", "--no-scan", "--resource", "x" });

        Assert.Equal("src", options.ScanRoot);
        Assert.True(options.Yes);
        Assert.True(options.NoScan);
    }

    [Fact]
    public void Init_verb_rejects_diff()
    {
        var ex = Assert.Throws<ArgumentException>(() => CliOptionsParser.Parse(new[] { "init", "--diff" }));
        Assert.Contains("--diff", ex.Message);
    }

    [Fact]
    public void Init_verb_rejects_list()
    {
        var ex = Assert.Throws<ArgumentException>(() => CliOptionsParser.Parse(new[] { "init", "--list" }));
        Assert.Contains("--list", ex.Message);
    }

    [Fact]
    public void Init_verb_rejects_output()
    {
        var ex = Assert.Throws<ArgumentException>(() => CliOptionsParser.Parse(new[] { "init", "--output", "out/" }));
        Assert.Contains("--output", ex.Message);
    }

    [Fact]
    public void Init_verb_rejects_match_and_set()
    {
        Assert.Throws<ArgumentException>(() => CliOptionsParser.Parse(new[] { "init", "--match", "x" }));
        Assert.Throws<ArgumentException>(() => CliOptionsParser.Parse(new[] { "init", "--set", "x" }));
    }

    [Fact]
    public void Init_verb_rejects_client_without_environment()
    {
        var ex = Assert.Throws<ArgumentException>(() => CliOptionsParser.Parse(new[] { "init", "--client", "Acme" }));
        Assert.Contains("--client requires --environment", ex.Message);
    }

    [Fact]
    public void Init_verb_rejects_no_scan_with_no_resource()
    {
        var ex = Assert.Throws<ArgumentException>(() => CliOptionsParser.Parse(new[] { "init", "--no-scan" }));
        Assert.Contains("--no-scan requires at least one --resource", ex.Message);
    }

    [Fact]
    public void Init_verb_parses_a_bare_template_switch_as_the_default_variant()
    {
        var options = CliOptionsParser.Parse(new[] { "init", "--template" });

        Assert.Equal("default", options.Template);
    }

    [Fact]
    public void Init_verb_parses_an_explicit_template_default_variant()
    {
        var options = CliOptionsParser.Parse(new[] { "init", "--template", "default" });

        Assert.Equal("default", options.Template);
    }

    [Fact]
    public void Init_verb_parses_the_template_hosts_variant()
    {
        var options = CliOptionsParser.Parse(new[] { "init", "--template", "hosts" });

        Assert.Equal("hosts", options.Template);
    }

    [Fact]
    public void Init_verb_rejects_an_unrecognized_template_variant()
    {
        var ex = Assert.Throws<ArgumentException>(() => CliOptionsParser.Parse(new[] { "init", "--template", "nonsense" }));
        Assert.Contains("Unrecognized --template variant", ex.Message);
    }

    [Fact]
    public void Init_verb_treats_a_trailing_template_followed_by_help_as_help_not_a_variant()
    {
        var options = CliOptionsParser.Parse(new[] { "init", "--template", "help" });

        Assert.True(options.Help);
    }

    [Theory]
    [InlineData("--scan-root", "src")]
    [InlineData("--environment", "Production")]
    [InlineData("--client", "Acme")]
    [InlineData("--host", "192.168.10.10")]
    [InlineData("--resource", "x")]
    public void Init_verb_rejects_template_combined_with_any_other_init_flag(string flag, string value)
    {
        var ex = Assert.Throws<ArgumentException>(() => CliOptionsParser.Parse(new[] { "init", "--template", flag, value }));
        Assert.Contains("--template", ex.Message);
    }

    [Theory]
    [InlineData("--yes")]
    [InlineData("--no-scan")]
    public void Init_verb_rejects_template_combined_with_a_bare_switch_init_flag(string flag)
    {
        var ex = Assert.Throws<ArgumentException>(() => CliOptionsParser.Parse(new[] { "init", "--template", flag }));
        Assert.Contains("--template", ex.Message);
    }

    [Fact]
    public void The_init_verb_is_only_recognized_as_the_very_first_argument()
    {
        var options = CliOptionsParser.Parse(new[]
        {
            "--resource", "init", "--client", "ClientA", "--environment", "Production", "--output", "out.config"
        });

        Assert.False(options.Init);
        Assert.Equal("init", options.Resource);
    }
}
