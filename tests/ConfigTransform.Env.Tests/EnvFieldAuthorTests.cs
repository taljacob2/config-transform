using ConfigTransform.Core;
using Xunit;

namespace ConfigTransform.Env.Tests;

/// <summary>
/// Direct tests of the "set" command's decision logic for .env (docs/FIELD_AUTHORING_DESIGN.md).
/// The simplest of the three formats' field authors -- no nested-path disambiguation (JSON) and
/// no update-vs-insert branch (XML), since a `.env` overlay is always just the flat subset of
/// keys it overrides.
/// </summary>
public class EnvFieldAuthorTests
{
    private const string Preceding = "DATABASE_URL=postgres://dev\nLOG_LEVEL=info\n";

    [Fact]
    public void Creates_a_brand_new_key_in_an_empty_overlay()
    {
        var result = EnvFieldAuthor.Author(
            Preceding, existingTargetContent: null, isBaseTarget: false,
            matches: [new MatchSpec("key", "LOG_LEVEL", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "warn", WasDefaulted: false)]);

        Assert.Equal("LOG_LEVEL=warn\n", result);
    }

    [Fact]
    public void Adds_a_key_to_an_overlay_that_already_has_other_keys()
    {
        var result = EnvFieldAuthor.Author(
            Preceding, existingTargetContent: "API_KEY=abc\n", isBaseTarget: false,
            matches: [new MatchSpec("key", "LOG_LEVEL", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "warn", WasDefaulted: false)]);

        Assert.Equal("API_KEY=abc\nLOG_LEVEL=warn\n", result);
    }

    [Fact]
    public void Re_running_set_for_the_same_key_updates_it_in_place_rather_than_duplicating()
    {
        var first = EnvFieldAuthor.Author(
            Preceding, existingTargetContent: null, isBaseTarget: false,
            matches: [new MatchSpec("key", "LOG_LEVEL", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "warn", WasDefaulted: false)]);

        var second = EnvFieldAuthor.Author(
            Preceding, existingTargetContent: first, isBaseTarget: false,
            matches: [new MatchSpec("key", "LOG_LEVEL", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "error", WasDefaulted: false)]);

        Assert.Equal("LOG_LEVEL=error\n", second);
    }

    [Fact]
    public void IsBaseTarget_does_not_change_the_result()
    {
        var overlayWrite = EnvFieldAuthor.Author(
            Preceding, existingTargetContent: null, isBaseTarget: false,
            matches: [new MatchSpec("key", "PORT", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "8080", WasDefaulted: false)]);

        var baseWrite = EnvFieldAuthor.Author(
            Preceding, existingTargetContent: null, isBaseTarget: true,
            matches: [new MatchSpec("key", "PORT", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "8080", WasDefaulted: false)]);

        Assert.Equal(overlayWrite, baseWrite);
    }

    [Fact]
    public void Bare_match_defaults_to_key_and_bare_set_defaults_to_value()
    {
        var result = EnvFieldAuthor.Author(
            Preceding, existingTargetContent: null, isBaseTarget: false,
            matches: [new MatchSpec("key", "LOG_LEVEL", WasDefaulted: true)],
            setFields: [new MatchSpec("value", "warn", WasDefaulted: true)]);

        Assert.Equal("LOG_LEVEL=warn\n", result);
    }

    [Fact]
    public void An_invalid_key_name_is_rejected()
    {
        Assert.Throws<InvalidOperationException>(() => EnvFieldAuthor.Author(
            Preceding, existingTargetContent: null, isBaseTarget: false,
            matches: [new MatchSpec("key", "not-a-valid-key", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "x", WasDefaulted: false)]));
    }

    [Fact]
    public void A_match_attribute_other_than_key_is_rejected()
    {
        Assert.Throws<InvalidOperationException>(() => EnvFieldAuthor.Author(
            Preceding, existingTargetContent: null, isBaseTarget: false,
            matches: [new MatchSpec("tag", "LOG_LEVEL", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "warn", WasDefaulted: false)]));
    }

    [Fact]
    public void More_than_one_match_is_rejected()
    {
        Assert.Throws<InvalidOperationException>(() => EnvFieldAuthor.Author(
            Preceding, existingTargetContent: null, isBaseTarget: false,
            matches: [new MatchSpec("key", "LOG_LEVEL", WasDefaulted: false), new MatchSpec("key", "PORT", WasDefaulted: false)],
            setFields: [new MatchSpec("value", "warn", WasDefaulted: false)]));
    }

    [Fact]
    public void A_set_attribute_other_than_value_is_rejected()
    {
        Assert.Throws<InvalidOperationException>(() => EnvFieldAuthor.Author(
            Preceding, existingTargetContent: null, isBaseTarget: false,
            matches: [new MatchSpec("key", "LOG_LEVEL", WasDefaulted: false)],
            setFields: [new MatchSpec("other", "warn", WasDefaulted: false)]));
    }
}
