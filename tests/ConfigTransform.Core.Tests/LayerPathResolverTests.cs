using Xunit;

namespace ConfigTransform.Core.Tests;

public class LayerPathResolverTests
{
    [Fact]
    public void Neither_client_nor_environment_resolves_to_null()
    {
        var resolved = LayerPathResolver.Resolve("/repo", client: null, environment: null);

        Assert.Null(resolved);
    }

    [Fact]
    public void Environment_only_resolves_under_Environments()
    {
        var resolved = LayerPathResolver.Resolve("/repo", client: null, environment: "Production");

        Assert.Equal(
            Path.Combine("/repo", ".configtransform", "Environments", "Production", "configtransform.json"),
            resolved);
    }

    [Fact]
    public void Client_and_environment_resolve_under_Clients()
    {
        var resolved = LayerPathResolver.Resolve("/repo", client: "Acme", environment: "Production");

        Assert.Equal(
            Path.Combine("/repo", ".configtransform", "Clients", "Acme", "Production", "configtransform.json"),
            resolved);
    }

    [Fact]
    public void Client_without_environment_throws()
    {
        Assert.Throws<ArgumentException>(() => LayerPathResolver.Resolve("/repo", client: "Acme", environment: null));
    }
}
