using Xunit;

namespace ConfigTransform.Json.Tests;

/// <summary>
/// JsonSecretSubstitution.Substitute (docs/SECRETS_DESIGN.md) against Fixtures/Secrets: placeholders in every kind
/// of position this format has, a secret value full of characters the format treats specially, and
/// a name nothing resolves. The expected file is compared byte for byte.
/// </summary>
public class JsonSecretSubstitutionTests
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
        var result = JsonSecretSubstitution.Substitute(File.ReadAllText(Fixture("input.json")), Resolve);

        Assert.Equal(File.ReadAllText(Fixture("expected.json")), result);
    }

    [Fact]
    public void Content_without_placeholders_comes_back_unchanged()
    {
        var content = File.ReadAllText(Fixture("expected.json")).Replace("{{CFSECRET_NOT_PROVIDED}}", "x").Replace("{{CFSECRET_API_KEY}}", "k");

        Assert.Same(content, JsonSecretSubstitution.Substitute(content, Resolve));
    }
}
