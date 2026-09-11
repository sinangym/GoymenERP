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
        new RunSettings { ApiBaseUrl = "http://0.0.0.0:25080" }, new RunSettings { ApiPort = 5080 },
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
    public void CurrentCandidate_BindsFreshRunAndFrontend_WithoutReplacingApiDependencyWithSquashMain()
    {
        var identity = new RunSettings();
        identity.Validate("Testing");
        Assert.Equal("c006-ac5fc7069528", identity.RunId);
        Assert.Equal("292e8f25-7e06-4d8a-a96c-06586a316cb1", identity.InstanceId);
        Assert.Equal("a5851a148b55f66ca6555cdcc810107f949fb6a4", SourceBinding.FrontendHead);
        Assert.Equal("6af32c6103aff854feffcd4c451ad9fd42b3d6608c6aa68825aceac96a18f743", identity.FrontendDiffSha256);
        Assert.Equal(SourceBinding.FrontendDiff, identity.FrontendDiffSha256);
        Assert.Equal("880cae838e5d2f01a988f1e4a89afbe67858f48c", identity.BackendHead);
        var services = Canonical();
        Assert.Throws<InvalidOperationException>(() => AcceptanceProfile.Configure(services, "Testing",
            identity with { BackendHead = "78494ed9bc60692fac6c186aa52b0049bbf4c306" }));
        Assert.Equal(typeof(CounterpartyShellCapabilityProvider), Assert.Single(services).ImplementationType);
    }

    public static TheoryData<RunSettings> ConsumedRunIdentities => new()
    {
        new RunSettings { RunId = "c006-303760997c6f" },
        new RunSettings { InstanceId = "3a6f9f42-2d52-4874-95d0-7cff415aaf15" },
        new RunSettings { DatabaseName = "GoymenCariC006_303760997c6f" },
        new RunSettings { ComposeProject = "goymen-cari-c006-303760997c6f" },
        new RunSettings { ContainerName = "goymen-cari-c006-303760997c6f-sql" },
        new RunSettings { VolumeName = "goymen-cari-c006-303760997c6f-data" },
        new RunSettings { SmokePrefix = "C006-SMOKE-303760997c6f" },
        new RunSettings { BrowserPrefix = "CARI-JOURNEY-303760997c6f" },
        new RunSettings { FrontendDiffSha256 = "b9e9354e971da26bbd5a0743dcefc78c2f3b4a7e4cbb6d59d74793c7c3b25c11" },
    };

    [Theory]
    [MemberData(nameof(ConsumedRunIdentities))]
    public void ConsumedRunComponent_RejectsBeforeReplacingProvider(RunSettings identity)
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
