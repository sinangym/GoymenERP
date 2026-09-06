using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.CariAcceptance006;
using GoymenERP.Modules.Counterparty.Infrastructure.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.CariAcceptance006.Tests;

public sealed class ProfileTests
{
    private static ServiceCollection Canonical()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IShellCapabilityProvider, CounterpartyShellCapabilityProvider>();
        return services;
    }

    [Fact]
    public void ExactRun_OpensExactlyFourCapabilities_PreservesPermissionsAndNormalProvider()
    {
        var services = Canonical();
        AcceptanceProfile.Configure(services, "Testing", new RunSettings());
        using var container = services.BuildServiceProvider();
        var provider = container.GetRequiredService<IShellCapabilityProvider>();
        var catalog = container.GetRequiredService<ShellCapabilityAuthorityCatalog>();
        var registry = new ShellCapabilityRegistry([provider], catalog);
        Assert.Equal(AcceptanceProfile.Revision, registry.Revision);
        var original = new CounterpartyShellCapabilityProvider().GetDeclarations();
        Assert.Equal(4, provider.GetDeclarations().Count);
        foreach (var declaration in provider.GetDeclarations())
        {
            var source = Assert.Single(original, x => x.CapabilityId == declaration.CapabilityId);
            Assert.True(declaration.ImplementationAvailable);
            Assert.Equal(ShellCapabilityContextRequirement.SingleCompany, declaration.ContextRequirement);
            Assert.Equal(source.RequiredPermissions, declaration.RequiredPermissions);
            Assert.Equal(source.ScopeRequirement, declaration.ScopeRequirement);
        }
        Assert.All(original, x => Assert.False(x.ImplementationAvailable));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    [InlineData("Production")]
    [InlineData("")]
    public void OutsideTesting_RejectsBeforeReplacingProvider(string environment)
    {
        var services = Canonical();
        Assert.Throws<InvalidOperationException>(() => AcceptanceProfile.Configure(services, environment, new RunSettings()));
        Assert.Equal(typeof(CounterpartyShellCapabilityProvider), Assert.Single(services).ImplementationType);
    }

    public static TheoryData<RunSettings> WrongIdentities => new()
    {
        new RunSettings { RunId = "other" }, new RunSettings { InstanceId = "other" },
        new RunSettings { BackendHead = new string('a', 40) }, new RunSettings { ComposeProject = "goymen-erp" },
        new RunSettings { DatabaseName = "GoymenErp" }, new RunSettings { SqlEndpoint = "remote.invalid,1433" },
        new RunSettings { ApiBaseUrl = "http://0.0.0.0:15080" }, new RunSettings { ApiPort = 5080 },
        new RunSettings { FrontendDiffSha256 = "wrong" }, new RunSettings { EnvironmentName = "Production" },
    };

    [Theory]
    [MemberData(nameof(WrongIdentities))]
    public void WrongRunOrResource_RejectsBeforeReplacingProvider(RunSettings identity)
    {
        var services = Canonical();
        Assert.Throws<InvalidOperationException>(() => AcceptanceProfile.Configure(services, "Testing", identity));
        Assert.Equal(typeof(CounterpartyShellCapabilityProvider), Assert.Single(services).ImplementationType);
    }

    [Fact]
    public void UnexpectedProviderSet_IsNeverAccepted()
    {
        var services = Canonical();
        services.AddSingleton<IShellCapabilityProvider, CariAcceptanceProvider>();
        Assert.Throws<InvalidOperationException>(() => AcceptanceProfile.Configure(services, "Testing", new RunSettings()));
    }
}
