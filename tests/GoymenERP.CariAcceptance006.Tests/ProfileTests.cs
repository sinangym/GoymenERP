using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Infrastructure.Seeding;
using Microsoft.Extensions.Options;
using GoymenERP.CariAcceptance006;
using GoymenERP.Modules.Counterparty.Infrastructure.Shell;
using GoymenERP.Modules.Organization.Infrastructure.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.CariAcceptance006.Tests;

public sealed class ProfileTests
{
    [Fact]
    public void L04PreservesTheBrowserFixtureRecipeAndUsesANewPrivateRunDirectory()
    {
        Assert.Equal(new[]
        {
            "tools/GoymenERP.CariAcceptance006/AcceptanceFactory.cs",
            "tools/GoymenERP.CariAcceptance006/AcceptanceProfile.cs",
            "tools/cari-acceptance-006/run.json",
        }, SourceBinding.CariFixturePaths);
        Assert.Equal(RunSettings.PrivatePasswordPath, Path.Combine(RunSettings.ApprovedRoot, "secrets", "cari-acceptance-006",
            RunSettings.ExpectedRun, "password.dpapi"));
        Assert.NotEqual(RunSettings.PrivatePasswordPath,
            Path.Combine(RunSettings.ApprovedRoot, "secrets", "cari-acceptance-006", "password.dpapi"));
    }

    private static ServiceCollection Canonical()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IShellCapabilityProvider, CounterpartyShellCapabilityProvider>();
        services.AddSingleton<IShellCapabilityProvider, OrganizationShellCapabilityProvider>();
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
        Assert.Equal(6, provider.GetDeclarations().Count);
        var status = Assert.Single(provider.GetDeclarations(), x => x.CapabilityId == "cari.accounts.status.update");
        Assert.Equal(CounterpartyShellCapabilityProvider.DetailCapabilityId, status.ParentCapabilityId);
        Assert.Contains(ErpPermissions.CounterpartyAccountsStatusUpdate, status.RequiredPermissions);
        var edit = Assert.Single(provider.GetDeclarations(), x => x.CapabilityId == "cari.accounts.contact.update");
        Assert.Equal("cari.contact-edit", edit.RouteId);
        Assert.Equal("/cari/:partyId/bilgiler", edit.RoutePath);
        Assert.Equal("C-06", edit.ScreenId);
        Assert.True(edit.ImplementationAvailable);
        Assert.Equal(ShellCapabilityContextRequirement.SingleCompany, edit.ContextRequirement);
        Assert.Equal(ShellCapabilityScopeRequirement.CompanyWide, edit.ScopeRequirement);
        Assert.Equal(new[] { ErpPermissions.CounterpartyPartiesRead, ErpPermissions.CounterpartyPartiesUpdateBasic }, edit.RequiredPermissions);
        foreach (var declaration in provider.GetDeclarations().Where(x => x.CapabilityId != edit.CapabilityId && x.CapabilityId != status.CapabilityId))
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
        options.Roles.Add(new IdentityRoleSeed
        {
            Id = Guid.NewGuid(), Code = "ORGANIZATION_OPERATOR", Name = "Existing test operator",
            Permissions = [ErpPermissions.IdentityProfileRead],
        });
        var configure = Assert.Single(container.GetServices<IPostConfigureOptions<IdentitySeedOptions>>());
        configure.PostConfigure(Options.DefaultName, options);
        configure.PostConfigure(Options.DefaultName, options);
        Assert.Single(options.Roles[0].Permissions, permission => permission == ErpPermissions.CounterpartyPartiesUpdateBasic);
        Assert.Single(options.Roles[0].Permissions, permission => permission == ErpPermissions.CounterpartyAccountsStatusUpdate);
        Assert.DoesNotContain(ErpPermissions.CounterpartyAccountsStatusUpdate, options.Roles[1].Permissions);
        Assert.Equal("company-a-user", AcceptanceProfile.ReadOnlyActor);
        Assert.Single(options.Roles[1].Permissions, permission => permission == ErpPermissions.CounterpartyPartiesRead);
        Assert.DoesNotContain(ErpPermissions.CounterpartyPartiesUpdateBasic, options.Roles[1].Permissions);
        Assert.DoesNotContain(ErpPermissions.CounterpartyPartiesCreate, options.Roles[1].Permissions);
        Assert.DoesNotContain(ErpPermissions.CounterpartyAccountsCreate, options.Roles[1].Permissions);
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
        Assert.Equal(2, services.Count);
        Assert.Contains(services, x => x.ImplementationType == typeof(CounterpartyShellCapabilityProvider));
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
        Assert.Equal(2, services.Count);
        Assert.Contains(services, x => x.ImplementationType == typeof(CounterpartyShellCapabilityProvider));
    }

    [Fact]
    public void CurrentCandidate_BindsFreshRunAndFrontend_WithoutReplacingApiDependencyWithSquashMain()
    {
        var identity = new RunSettings();
        identity.Validate("Testing");
        Assert.Equal("c006-c79a094df633", identity.RunId);
        Assert.Equal("a226ca3a-08b3-4efd-92ec-b22333778557", identity.InstanceId);
        Assert.Equal("0d20850562d271ea934c04ff8c002679030dfaf2", SourceBinding.FrontendHead);
        Assert.Equal("311edcb5a7a45ffcda20fc04adbbf16e126347dfe185cfff879824ea4fbc6faa", identity.FrontendDiffSha256);
        Assert.Equal(SourceBinding.FrontendDiff, identity.FrontendDiffSha256);
        Assert.Equal("9df7f40e3aefc5b02ef42a378c0d1aa800a2fe29", identity.BackendHead);
        var services = Canonical();
        Assert.Throws<InvalidOperationException>(() => AcceptanceProfile.Configure(services, "Testing",
            identity with { BackendHead = "78494ed9bc60692fac6c186aa52b0049bbf4c306" }));
        Assert.Equal(2, services.Count);
        Assert.Contains(services, x => x.ImplementationType == typeof(CounterpartyShellCapabilityProvider));
    }

    public static TheoryData<RunSettings> ConsumedRunIdentities => new()
    {
        new RunSettings { RunId = "c006-fc721a1b9752" },
        new RunSettings { InstanceId = "7ec92416-5a5d-42ad-9823-3574f27592a8" },
        new RunSettings { DatabaseName = "GoymenCariC006_fc721a1b9752" },
        new RunSettings { ComposeProject = "goymen-cari-c006-fc721a1b9752" },
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
        Assert.Equal(2, services.Count);
        Assert.Contains(services, x => x.ImplementationType == typeof(CounterpartyShellCapabilityProvider));
    }

    [Fact]
    public void UnexpectedProviderSet_IsNeverAccepted()
    {
        var services = Canonical();
        services.AddSingleton<IShellCapabilityProvider, CariAcceptanceProvider>();
        Assert.Throws<InvalidOperationException>(() => AcceptanceProfile.Configure(services, "Testing", new RunSettings()));
    }
}
