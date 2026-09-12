using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Infrastructure.Seeding;
using Microsoft.Extensions.Options;
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
    public void ExactRun_OpensFourReadsAndContactEdit_PreservesPermissionsAndNormalProvider()
    {
        var services = Canonical();
        AcceptanceProfile.Configure(services, "Testing", new RunSettings());
        using var container = services.BuildServiceProvider();
        var provider = container.GetRequiredService<IShellCapabilityProvider>();
        var catalog = container.GetRequiredService<ShellCapabilityAuthorityCatalog>();
        var registry = new ShellCapabilityRegistry([provider], catalog);
        Assert.Equal(AcceptanceProfile.Revision, registry.Revision);
        var original = new CounterpartyShellCapabilityProvider().GetDeclarations();
        Assert.Equal(5, provider.GetDeclarations().Count);
        var edit = Assert.Single(provider.GetDeclarations(), x => x.CapabilityId == "cari.accounts.contact.update");
        Assert.Equal("cari.contact-edit", edit.RouteId);
        Assert.Equal("/cari/:partyId/bilgiler", edit.RoutePath);
        Assert.Equal("C-06", edit.ScreenId);
        Assert.True(edit.ImplementationAvailable);
        Assert.Equal(ShellCapabilityContextRequirement.SingleCompany, edit.ContextRequirement);
        Assert.Equal(ShellCapabilityScopeRequirement.CompanyWide, edit.ScopeRequirement);
        Assert.Equal(new[] { ErpPermissions.CounterpartyPartiesRead, ErpPermissions.CounterpartyPartiesUpdateBasic }, edit.RequiredPermissions);
        foreach (var declaration in provider.GetDeclarations().Where(x => x.CapabilityId != edit.CapabilityId))
        {
            var source = Assert.Single(original, x => x.CapabilityId == declaration.CapabilityId);
            Assert.True(declaration.ImplementationAvailable);
            Assert.Equal(ShellCapabilityContextRequirement.SingleCompany, declaration.ContextRequirement);
            Assert.Equal(source.RequiredPermissions, declaration.RequiredPermissions);
            Assert.Equal(source.ScopeRequirement, declaration.ScopeRequirement);
        }
        Assert.All(original, x => Assert.False(x.ImplementationAvailable));
    }

    [Fact]
    public void BasicPermissionGrant_IsTestOnlyExplicitAndIdempotent()
    {
        var services = Canonical();
        AcceptanceProfile.Configure(services, "Testing", new RunSettings());
        using var container = services.BuildServiceProvider();
        var options = new IdentitySeedOptions();
        options.Roles.Add(new IdentityRoleSeed
        {
            Id = Guid.NewGuid(), Code = "SYSTEM_ADMINISTRATOR", Name = "Test only",
            Permissions = [ErpPermissions.CounterpartyPartiesRead],
        });
        var configure = Assert.Single(container.GetServices<IPostConfigureOptions<IdentitySeedOptions>>());
        configure.PostConfigure(null, options);
        configure.PostConfigure(null, options);
        Assert.Single(options.Roles[0].Permissions, permission => permission == ErpPermissions.CounterpartyPartiesUpdateBasic);
        Assert.DoesNotContain(ErpPermissions.CounterpartyPartiesUpdateBasic,
            new IdentityRoleSeed().Permissions);
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
        Assert.Equal("c006-fc721a1b9752", identity.RunId);
        Assert.Equal("7ec92416-5a5d-42ad-9823-3574f27592a8", identity.InstanceId);
        Assert.Equal("c3cb45e1f85a7079c742bc0abbe62ca3aa805dfe", SourceBinding.FrontendHead);
        Assert.Equal("a184b46b34b2208dde74c366abe105b54c9d01515aa1930f0a597cb597fd6e53", identity.FrontendDiffSha256);
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
        new RunSettings { RunId = "c006-ac5fc7069528" },
        new RunSettings { InstanceId = "292e8f25-7e06-4d8a-a96c-06586a316cb1" },
        new RunSettings { DatabaseName = "GoymenCariC006_ac5fc7069528" },
        new RunSettings { ComposeProject = "goymen-cari-c006-ac5fc7069528" },
        new RunSettings { ContainerName = "goymen-cari-c006-ac5fc7069528-sql" },
        new RunSettings { VolumeName = "goymen-cari-c006-ac5fc7069528-data" },
        new RunSettings { SmokePrefix = "C006-SMOKE-ac5fc7069528" },
        new RunSettings { BrowserPrefix = "CARI-JOURNEY-ac5fc7069528" },
        new RunSettings { FrontendDiffSha256 = "6af32c6103aff854feffcd4c451ad9fd42b3d6608c6aa68825aceac96a18f743" },
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
