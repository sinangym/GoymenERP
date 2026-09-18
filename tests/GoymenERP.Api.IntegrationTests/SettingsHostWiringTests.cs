using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// The host's Settings provider is the approved one (T02-a on main, #279), resolved through the
/// real host composition — only the actor and the company are fixed for the test. GDC kg
/// resolves to its approved profile; everything without an approved source stays fail-closed,
/// exactly as before the swap.
/// </summary>
public sealed class SettingsHostWiringTests
{
    private static readonly Guid Gdc = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Gsp = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid Gin = Guid.Parse("10000000-0000-0000-0000-000000000003");
    private static readonly DateOnly Approval = new(2026, 9, 18);

    [Fact]
    public void The_host_registers_the_approved_provider_as_the_one_Settings_port()
    {
        using var factory = Host(Gdc);
        using var scope = factory.Services.CreateScope();

        var provider = scope.ServiceProvider.GetRequiredService<ISettingsPrerequisiteProvider>();

        Assert.IsType<ApprovedSettingsPrerequisiteProvider>(provider);
        // One registration, not a second one shadowing the first.
        Assert.Single(scope.ServiceProvider.GetServices<ISettingsPrerequisiteProvider>());
    }

    [Fact]
    public async Task GDC_kilogram_resolves_to_the_approved_profile_through_the_host()
    {
        using var factory = Host(Gdc);
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<ISettingsPrerequisiteProvider>();

        var profile = await provider.GetUnitProfileAsync(SettingsUnitIdentitiesV1.Kilogram, Approval);

        Assert.Equal(Gdc, profile.CompanyId);
        Assert.Equal(SettingsUnitIdentitiesV1.Kilogram, profile.UnitId);
        Assert.True(profile.IsKilogram);
        Assert.Equal(3, profile.Scale);
        Assert.Equal(ApprovedUnitProfileCatalog.GdcKilogramV1.ProfileId, profile.Profile.ProfileId);
        Assert.Equal(1, profile.Profile.Version);
    }

    [Theory]
    [InlineData("gsp")]
    [InlineData("gin")]
    public async Task A_company_without_an_approved_source_stays_unavailable_through_the_host(string company)
    {
        using var factory = Host(company == "gsp" ? Gsp : Gin);
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<ISettingsPrerequisiteProvider>();

        Assert.Equal("settings_provider_unavailable", (await Assert.ThrowsAsync<SettingsPrerequisiteException>(
            () => provider.GetUnitProfileAsync(SettingsUnitIdentitiesV1.Kilogram, Approval))).Code);
    }

    [Fact]
    public async Task An_unknown_unit_in_GDC_is_not_found_through_the_host()
    {
        using var factory = Host(Gdc);
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<ISettingsPrerequisiteProvider>();

        Assert.Equal(ApprovedSettingsPrerequisiteProvider.UnitProfileNotFoundCode,
            (await Assert.ThrowsAsync<SettingsPrerequisiteException>(
                () => provider.GetUnitProfileAsync(Guid.Parse("0f3bd0a1-6e2c-4d77-a3a5-0b1c2d3e4f50"), Approval))).Code);
    }

    [Fact]
    public async Task Money_and_period_are_unchanged_by_the_swap()
    {
        using var factory = Host(Gdc);
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<ISettingsPrerequisiteProvider>();

        Assert.Equal("settings_provider_unavailable", (await Assert.ThrowsAsync<SettingsPrerequisiteException>(
            () => provider.GetMoneyProfileAsync(Guid.NewGuid(), "synthetic-operation", Approval))).Code);
        Assert.Equal("settings_provider_unavailable", (await Assert.ThrowsAsync<SettingsPrerequisiteException>(
            () => provider.GetPeriodEligibilityAsync("synthetic-operation", Approval, null))).Code);
    }

    [Fact]
    public async Task An_actor_outside_the_company_is_still_denied_through_the_host()
    {
        using var factory = Host(Gdc, actorAllowedIn: Gsp);
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<ISettingsPrerequisiteProvider>();

        Assert.Equal("settings_access_denied", (await Assert.ThrowsAsync<SettingsPrerequisiteException>(
            () => provider.GetUnitProfileAsync(SettingsUnitIdentitiesV1.Kilogram, Approval))).Code);
    }

    [Fact]
    public async Task The_Sales_reference_read_composes_over_the_product_runtime_which_is_unconfigured_in_this_host()
    {
        using var factory = Host(Gdc);
        using var scope = factory.Services.CreateScope();

        // The Sales handler is built over the swapped provider without error.
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ISalesReferenceReadService>());
        // C3 registers the Product owner's eligibility runtime. This host configures no Product
        // runtime, so the port answers 503 product_runtime_unconfigured, which Sales maps to the
        // same 503 product_provider_unavailable it gave for a missing port; and the Sales product
        // route binds no product permission, so it answers 403 before asking. The swap and C3
        // change no reachable Sales answer here.
        var eligibility = scope.ServiceProvider.GetRequiredService<IProductSalesEligibilityV1>();
        var failure = await Assert.ThrowsAsync<ProductOperationException>(() => eligibility.EvaluateAsync(
            new ProductSalesSelectionV1(Guid.NewGuid(), "1", Guid.NewGuid(), Approval), default));
        Assert.Equal((503, "product_runtime_unconfigured"), (failure.StatusCode, failure.Code));
        Assert.Null(scope.ServiceProvider.GetRequiredService<SalesReferenceReadBindings>().ProductPermission);
    }

    private static WebApplicationFactoryWithFixedContext Host(Guid company, Guid? actorAllowedIn = null) =>
        new(company, actorAllowedIn ?? company);

    private sealed class WebApplicationFactoryWithFixedContext : IDisposable
    {
        private readonly GoymenErpApiFactory _factory = new();
        private readonly Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> _host;

        public WebApplicationFactoryWithFixedContext(Guid company, Guid actorAllowedIn)
        {
            _host = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICurrentUser>();
                services.AddScoped<ICurrentUser>(_ => new FixedActor(actorAllowedIn));
                services.RemoveAll<ICompanyContext>();
                services.AddScoped<ICompanyContext>(_ => new FixedCompany(company));
            }));
        }

        public IServiceProvider Services => _host.Services;

        public void Dispose()
        {
            _host.Dispose();
            _factory.Dispose();
        }
    }

    private sealed class FixedCompany(Guid companyId) : ICompanyContext
    {
        public bool IsResolved => true;
        public Guid CompanyId => companyId;
        public void Set(Guid id) => throw new NotSupportedException();
    }

    private sealed class FixedActor(Guid allowedCompany) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => "20000000-0000-0000-0000-0000000000aa";
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { allowedCompany };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
}
