using Xunit;

namespace ConfigTransform.Yaml.Tests;

/// <summary>
/// YamlSecretSubstitution.Substitute (docs/SECRETS_DESIGN.md) against Fixtures/Secrets: placeholders in every kind
/// of position this format has, a secret value full of characters the format treats specially, and
/// a name nothing resolves. The expected file is compared byte for byte.
/// </summary>
public class YamlSecretSubstitutionTests
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
        var result = YamlSecretSubstitution.Substitute(File.ReadAllText(Fixture("input.yaml")), Resolve);

        Assert.Equal(File.ReadAllText(Fixture("expected.yaml")), result);
    }

    [Fact]
    public void Content_without_placeholders_comes_back_unchanged()
    {
        var content = File.ReadAllText(Fixture("expected.yaml")).Replace("{{CFSECRET_NOT_PROVIDED}}", "x").Replace("{{CFSECRET_API_KEY}}", "k");

        Assert.Same(content, YamlSecretSubstitution.Substitute(content, Resolve));
    }

    [Fact]
    public void A_plain_scalar_a_substitution_changed_is_quoted_so_it_cannot_become_a_boolean()
    {
        // Unquoted, `Code: NO` is a boolean to YAML 1.1 readers -- the Norway problem.
        var result = YamlSecretSubstitution.Substitute("Code: N{{CFSECRET_SUFFIX}}\nOther: plain\n", _ => "O");

        Assert.Equal("Code: \"NO\"\nOther: plain\n", result);
    }
}
