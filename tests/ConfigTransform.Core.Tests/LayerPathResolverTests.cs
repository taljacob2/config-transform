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

    [Fact]
    public void Client_environment_and_host_resolve_under_a_Hosts_subdirectory()
    {
        var resolved = LayerPathResolver.Resolve("/repo", client: "Acme", environment: "Production", host: "192.168.10.10");

        Assert.Equal(
            Path.Combine("/repo", ".configtransform", "Clients", "Acme", "Production", "Hosts", "192.168.10.10", "configtransform.json"),
            resolved);
    }

    [Fact]
    public void Host_without_client_and_environment_throws()
    {
        Assert.Throws<ArgumentException>(() =>
            LayerPathResolver.Resolve("/repo", client: null, environment: null, host: "192.168.10.10"));
    }

    [Fact]
    public void Host_without_client_throws()
    {
        Assert.Throws<ArgumentException>(() =>
            LayerPathResolver.Resolve("/repo", client: null, environment: "Production", host: "192.168.10.10"));
    }

    [Fact]
    public void Host_with_client_but_no_environment_throws()
    {
        Assert.Throws<ArgumentException>(() =>
            LayerPathResolver.Resolve("/repo", client: "Acme", environment: null, host: "192.168.10.10"));
    }
}
