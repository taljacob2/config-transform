using Xunit;

namespace ConfigTransform.Env.Tests;

/// <summary>
/// EnvSecretSubstitution.Substitute (docs/SECRETS_DESIGN.md) against Fixtures/Secrets: placeholders in every kind
/// of position this format has, a secret value full of characters the format treats specially, and
/// a name nothing resolves. The expected file is compared byte for byte.
/// </summary>
public class EnvSecretSubstitutionTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Secrets", name);

    /// <summary>Resolves two names; CFSECRET_NOT_PROVIDED stays unresolved. The password needs escaping in every format.</summary>
    internal static string? Resolve(string name) => name switch
    {
        "CFSECRET_DB_PASSWORD" => "Pa5\"5+w&r<d>: #x'",
        "CFSECRET_API_KEY" => "abc123",
        _ => null,
    };

    [Fact]
    public void Substitutes_inside_values_only_with_the_formats_own_escaping()
    {
        var result = EnvSecretSubstitution.Substitute(File.ReadAllText(Fixture("input.env")), Resolve);

        Assert.Equal(File.ReadAllText(Fixture("expected.env")), result);
    }

    [Fact]
    public void Content_without_placeholders_comes_back_unchanged()
    {
        var content = File.ReadAllText(Fixture("expected.env")).Replace("{{CFSECRET_NOT_PROVIDED}}", "x");

        Assert.Same(content, EnvSecretSubstitution.Substitute(content, Resolve));
    }

    [Theory]
    [InlineData("line one\nline two")]
    [InlineData("\"quoted\"")]
    public void A_value_the_env_grammar_cannot_represent_is_refused_without_printing_it(string secret)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            EnvSecretSubstitution.Substitute("TOKEN={{CFSECRET_T}}\n", _ => secret));

        Assert.Contains("TOKEN", ex.Message);
        Assert.DoesNotContain(secret, ex.Message);
    }
}
