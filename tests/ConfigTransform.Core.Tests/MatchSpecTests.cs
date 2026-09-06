using Xunit;

namespace ConfigTransform.Core.Tests;

public class MatchSpecTests
{
    [Fact]
    public void Bare_value_defaults_to_the_given_attribute()
    {
        var spec = MatchSpec.Parse("ApiUrl", defaultAttribute: "key");

        Assert.Equal("key", spec.Attribute);
        Assert.Equal("ApiUrl", spec.Value);
        Assert.True(spec.WasDefaulted);
    }

    [Fact]
    public void Explicit_attribute_value_pair_is_not_defaulted()
    {
        var spec = MatchSpec.Parse("name=Prod", defaultAttribute: "key");

        Assert.Equal("name", spec.Attribute);
        Assert.Equal("Prod", spec.Value);
        Assert.False(spec.WasDefaulted);
    }

    [Fact]
    public void Only_the_first_equals_sign_splits_the_attribute_from_the_value()
    {
        var spec = MatchSpec.Parse("connectionString=Data Source=prod;User=admin", defaultAttribute: "value");

        Assert.Equal("connectionString", spec.Attribute);
        Assert.Equal("Data Source=prod;User=admin", spec.Value);
    }

    [Fact]
    public void A_literal_equals_sign_in_a_bare_value_forces_the_explicit_form()
    {
        // docs/FIELD_AUTHORING_DESIGN.md: there is no separate escaping syntax for this -- the
        // explicit form already parses correctly since only the first "=" ever splits.
        var spec = MatchSpec.Parse("key=FOO=BAR", defaultAttribute: "key");

        Assert.Equal("key", spec.Attribute);
        Assert.Equal("FOO=BAR", spec.Value);
    }
}
