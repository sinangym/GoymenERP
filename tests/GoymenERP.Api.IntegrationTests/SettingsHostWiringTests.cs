using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Settings.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// The host's Settings provider is the approved one (T02-a on main, #279), resolved through the
/// real host composition — only the actor and the company are fixed for the test. GDC kg
/// resolves to its approved profile, and since T02-b GDC/TRY to its approved money profile for the
/// Finance and Sales keys; everything without an approved source stays fail-closed.
/// </summary>
public sealed class SettingsHostWiringTests
{
    private static readonly Guid Gdc = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Gsp = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid Gin = Guid.Parse("10000000-0000-0000-0000-000000000003");
    // Not a seed company: GDC, GSP and GIN all have approved unit/money/Sales sources since SAT-D14/D18.
    private static readonly Guid Unserved = Guid.Parse("71000000-0000-4000-8000-0000000000ee");
    private static readonly DateOnly ExtraApproval = new(2026, 10, 26);
    private static readonly DateOnly Approval = new(2026, 9, 18);

    [Fact]
    public void The_host_registers_the_inventory_parent_roll_validation_port_once()
    {
        using var factory = Host(Gdc);
        using var scope = factory.Services.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IInventoryParentRollValidationV1>());
        Assert.Single(scope.ServiceProvider.GetServices<IInventoryParentRollValidationV1>());
    }

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
    public void The_host_registers_the_approved_provider_as_the_one_manufacturing_tolerance_port()
    {
        using var factory = Host(Gdc);
        using var scope = factory.Services.CreateScope();

        var provider = scope.ServiceProvider.GetRequiredService<ISettingsManufacturingToleranceProviderV1>();

        Assert.IsType<SettingsManufacturingToleranceProviderV1>(provider);
        Assert.Single(scope.ServiceProvider.GetServices<ISettingsManufacturingToleranceProviderV1>());
    }

    [Theory]
    [InlineData(SettingsManufacturingProcessesV1.Slitting)]
    [InlineData(SettingsManufacturingProcessesV1.ColdRolling)]
    [InlineData(SettingsManufacturingProcessesV1.TubeProfile)]
    [InlineData(SettingsManufacturingProcessesV1.Paint)]
    [InlineData(SettingsManufacturingProcessesV1.CutToLength)]
    public async Task The_host_keeps_every_manufacturing_tolerance_process_unavailable_without_an_approved_source(
        string process)
    {
        using var factory = Host(Gdc);
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<ISettingsManufacturingToleranceProviderV1>();

        var failure = await Assert.ThrowsAsync<SettingsPrerequisiteException>(() =>
            provider.GetManufacturingToleranceAsync(process, Approval));

        Assert.Equal("settings_provider_unavailable", failure.Code);
    }

    [Fact]
    public async Task The_manufacturing_tolerance_host_port_denies_an_actor_outside_the_company()
    {
        using var factory = Host(Gdc, actorAllowedIn: Gsp);
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<ISettingsManufacturingToleranceProviderV1>();

        var failure = await Assert.ThrowsAsync<SettingsPrerequisiteException>(() =>
            provider.GetManufacturingToleranceAsync(SettingsManufacturingProcessesV1.Slitting, Approval));

        Assert.Equal("settings_access_denied", failure.Code);
    }

    [Fact]
    public void The_host_registers_the_approved_provider_as_the_one_sales_policy_port()
    {
        using var factory = Host(Gdc);
        using var scope = factory.Services.CreateScope();

        var provider = scope.ServiceProvider.GetRequiredService<ISettingsSalesPolicyProviderV1>();

        Assert.IsType<ApprovedSettingsPrerequisiteProvider>(provider);
        Assert.Single(scope.ServiceProvider.GetServices<ISettingsSalesPolicyProviderV1>());
    }

    [Fact]
    public async Task GDC_sales_policies_resolve_to_the_approved_catalog_through_the_host()
    {
        using var factory = Host(Gdc);
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<ISettingsSalesPolicyProviderV1>();
        var salesApproval = new DateOnly(2026, 10, 25);

        var tolerance = await provider.GetSalesDeliveryToleranceAsync(salesApproval);
        var threshold = await provider.GetSalesApprovalAmountThresholdAsync(
            SettingsCurrencyIdentitiesV1.TurkishLira, salesApproval);
        var discount = await Assert.ThrowsAsync<SettingsPrerequisiteException>(
            () => provider.GetSalesDiscountLimitAsync(Guid.NewGuid(), salesApproval));

        Assert.Equal((1m, 2m, 3m), (tolerance.WarnPercent, tolerance.ApprovalPercent, tolerance.BlockPercent));
        Assert.Equal(ApprovedSalesPolicyCatalog.GdcDeliveryToleranceV1.ProfileId, tolerance.Profile.ProfileId);
        Assert.False(threshold.IsEnabled);
        Assert.Null(threshold.ThresholdAmountText);
        Assert.Equal(SettingsPrerequisiteCodesV1.SalesPolicyNotFound, discount.Code);
    }

    [Theory]
    [InlineData("gsp")]
    [InlineData("gin")]
    public async Task GSP_and_GIN_get_GDC_s_sales_policies_through_the_host(string company)
    {
        // SAT-D14 (2026-10-26): GSP and GIN run on GDC's Sales policies.
        var companyId = company == "gsp" ? Gsp : Gin;
        using var factory = Host(companyId);
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<ISettingsSalesPolicyProviderV1>();
        var extraApproval = new DateOnly(2026, 10, 26);

        var tolerance = await provider.GetSalesDeliveryToleranceAsync(extraApproval);
        var threshold = await provider.GetSalesApprovalAmountThresholdAsync(
            SettingsCurrencyIdentitiesV1.TurkishLira, extraApproval);
        var discount = await Assert.ThrowsAsync<SettingsPrerequisiteException>(
            () => provider.GetSalesDiscountLimitAsync(Guid.NewGuid(), extraApproval));

        Assert.Equal(companyId, tolerance.CompanyId);
        Assert.Equal((1m, 2m, 3m), (tolerance.WarnPercent, tolerance.ApprovalPercent, tolerance.BlockPercent));
        Assert.Equal(extraApproval, tolerance.EffectiveFrom);
        Assert.Equal(companyId, threshold.CompanyId);
        Assert.False(threshold.IsEnabled);
        Assert.Null(threshold.ThresholdAmountText);
        Assert.Equal(SettingsPrerequisiteCodesV1.SalesPolicyNotFound, discount.Code);
    }

    [Fact]
    public async Task A_company_without_an_approved_sales_policy_source_stays_unavailable_through_the_host()
    {
        // Not a seed company: GDC, GSP and GIN are all served since SAT-D14.
        using var factory = Host(Unserved);
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<ISettingsSalesPolicyProviderV1>();

        Assert.Equal("settings_provider_unavailable", (await Assert.ThrowsAsync<SettingsPrerequisiteException>(
            () => provider.GetSalesDeliveryToleranceAsync(Approval))).Code);
    }

    [Fact]
    public async Task The_sales_policy_host_port_denies_an_actor_outside_the_company()
    {
        using var factory = Host(Gdc, actorAllowedIn: Gsp);
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<ISettingsSalesPolicyProviderV1>();

        Assert.Equal("settings_access_denied", (await Assert.ThrowsAsync<SettingsPrerequisiteException>(
            () => provider.GetSalesDeliveryToleranceAsync(Approval))).Code);
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

    [Fact]
    public async Task A_company_without_an_approved_source_stays_unavailable_through_the_host()
    {
        using var factory = Host(Unserved);
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<ISettingsPrerequisiteProvider>();

        Assert.Equal("settings_provider_unavailable", (await Assert.ThrowsAsync<SettingsPrerequisiteException>(
            () => provider.GetUnitProfileAsync(SettingsUnitIdentitiesV1.Kilogram, Approval))).Code);
    }

    [Theory]
    [InlineData("gsp")]
    [InlineData("gin")]
    public async Task GSP_and_GIN_kilogram_resolve_to_their_own_approved_profile_through_the_host(string company)
    {
        // SAT-D18: GSP and GIN get GDC's kg values as their own per-company version.
        var companyId = company == "gsp" ? Gsp : Gin;
        using var factory = Host(companyId);
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<ISettingsPrerequisiteProvider>();

        var profile = await provider.GetUnitProfileAsync(SettingsUnitIdentitiesV1.Kilogram, ExtraApproval);

        Assert.Equal(companyId, profile.CompanyId);
        Assert.True(profile.IsKilogram);
        Assert.Equal(3, profile.Scale);
        Assert.Equal(ExtraApproval, profile.EffectiveFrom);
        Assert.NotEqual(ApprovedUnitProfileCatalog.GdcKilogramV1.ProfileId, profile.Profile.ProfileId);
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
    public async Task An_unapproved_money_operation_and_period_stay_unavailable_through_the_host()
    {
        using var factory = Host(Gdc);
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<ISettingsPrerequisiteProvider>();

        Assert.Equal("settings_provider_unavailable", (await Assert.ThrowsAsync<SettingsPrerequisiteException>(
            () => provider.GetMoneyProfileAsync(Guid.NewGuid(), "synthetic-operation", Approval))).Code);
        Assert.Equal("settings_provider_unavailable", (await Assert.ThrowsAsync<SettingsPrerequisiteException>(
            () => provider.GetPeriodEligibilityAsync("synthetic-operation", Approval, null))).Code);
    }

    [Theory]
    [InlineData("finance-receipt")]
    [InlineData("finance-payment")]
    [InlineData("sales")]
    // ACC-V0a: a journal amount is a final TRY amount (Ö2 "nihai tutar 2"), the same approved profile.
    [InlineData("accounting")]
    public async Task GDC_TRY_resolves_to_the_approved_money_profile_for_each_owner_s_key_through_the_host(string owner)
    {
        var operation = owner switch
        {
            "finance-receipt" => FinanceMoneyOperations.ManualBankReceiptV1,
            "finance-payment" => FinanceMoneyOperations.ManualBankPaymentV1,
            "sales" => SalesMoneyOperations.OrderDraftV1,
            _ => AccountingMoneyOperations.JournalAmountV1,
        };
        using var factory = Host(Gdc);
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<ISettingsPrerequisiteProvider>();

        var profile = await provider.GetMoneyProfileAsync(SettingsCurrencyIdentitiesV1.TurkishLira, operation, Approval);

        Assert.Equal(Gdc, profile.CompanyId);
        Assert.Equal("TRY", profile.CurrencyCode);
        Assert.Equal(operation, profile.Operation);
        Assert.Equal(2, profile.Scale);
        Assert.Equal(ApprovedMoneyProfileCatalog.GdcTurkishLiraV1.ProfileId, profile.Profile.ProfileId);
    }

    [Fact]
    public void The_money_catalog_answers_exactly_the_owners_published_operation_keys()
    {
        // Settings references none of the owner modules, so its literals are pinned here, where every owner's
        // constant is visible: an owner renaming its key turns this red instead of silently
        // turning its money read into settings_provider_unavailable.
        Assert.Equal(
            new[]
            {
                FinanceMoneyOperations.ManualBankPaymentV1, FinanceMoneyOperations.ManualBankReceiptV1,
                SalesMoneyOperations.OrderDraftV1,
                AccountingMoneyOperations.JournalAmountV1,
            }.Order(StringComparer.Ordinal),
            ApprovedMoneyProfileCatalog.GdcTurkishLiraV1.Operations.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_company_without_an_approved_money_source_stays_unavailable_through_the_host()
    {
        using var factory = Host(Unserved);
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<ISettingsPrerequisiteProvider>();

        Assert.Equal("settings_provider_unavailable", (await Assert.ThrowsAsync<SettingsPrerequisiteException>(
            () => provider.GetMoneyProfileAsync(
                SettingsCurrencyIdentitiesV1.TurkishLira, SalesMoneyOperations.OrderDraftV1, Approval))).Code);
    }

    [Theory]
    [InlineData("gsp")]
    [InlineData("gin")]
    public async Task GSP_and_GIN_TRY_answers_only_the_Sales_owner_s_key_through_the_host(string company)
    {
        // SAT-D18: the GSP/GIN TRY profile covers sales.order-draft.v1 only; the Accounting and Finance
        // owners' keys need a separate decision and stay unavailable there.
        var companyId = company == "gsp" ? Gsp : Gin;
        using var factory = Host(companyId);
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<ISettingsPrerequisiteProvider>();

        var profile = await provider.GetMoneyProfileAsync(
            SettingsCurrencyIdentitiesV1.TurkishLira, SalesMoneyOperations.OrderDraftV1, ExtraApproval);

        Assert.Equal((companyId, "TRY", SalesMoneyOperations.OrderDraftV1, 2),
            (profile.CompanyId, profile.CurrencyCode, profile.Operation, profile.Scale));
        foreach (var operation in new[]
                 {
                     FinanceMoneyOperations.ManualBankReceiptV1, FinanceMoneyOperations.ManualBankPaymentV1,
                     AccountingMoneyOperations.JournalAmountV1,
                 })
        {
            Assert.Equal("settings_provider_unavailable", (await Assert.ThrowsAsync<SettingsPrerequisiteException>(
                () => provider.GetMoneyProfileAsync(SettingsCurrencyIdentitiesV1.TurkishLira, operation, ExtraApproval))).Code);
        }
    }

    [Fact]
    public void The_GSP_and_GIN_money_profiles_answer_exactly_the_Sales_owner_s_published_key()
    {
        Assert.Equal([SalesMoneyOperations.OrderDraftV1], ApprovedMoneyProfileCatalog.GspTurkishLiraV1.Operations.ToArray());
        Assert.Equal([SalesMoneyOperations.OrderDraftV1], ApprovedMoneyProfileCatalog.GinTurkishLiraV1.Operations.ToArray());
    }

    [Fact]
    public async Task The_host_serves_GDC_s_approved_money_currencies_as_candidates()
    {
        using var factory = Host(Gdc);
        using var scope = factory.Services.CreateScope();

        var reader = scope.ServiceProvider.GetRequiredService<ISettingsMoneyCandidateReader>();
        Assert.IsType<ApprovedSettingsPrerequisiteProvider>(reader);
        Assert.Single(scope.ServiceProvider.GetServices<ISettingsMoneyCandidateReader>());

        var candidate = Assert.Single(await reader.GetMoneyCurrencyCandidatesAsync());
        Assert.Equal(new MoneyCurrencyCandidateV1(SettingsCurrencyIdentitiesV1.TurkishLira, "TRY", "TRY"), candidate);
    }

    [Theory]
    [InlineData("gsp")]
    [InlineData("gin")]
    public async Task GSP_and_GIN_get_their_approved_TRY_as_the_only_candidate_through_the_host(string company)
    {
        using var factory = Host(company == "gsp" ? Gsp : Gin);
        using var scope = factory.Services.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<ISettingsMoneyCandidateReader>();

        Assert.Equal(new MoneyCurrencyCandidateV1(SettingsCurrencyIdentitiesV1.TurkishLira, "TRY", "TRY"),
            Assert.Single(await reader.GetMoneyCurrencyCandidatesAsync()));
    }

    [Fact]
    public async Task A_company_without_an_approved_money_source_gets_no_candidate_list_through_the_host()
    {
        using var factory = Host(Unserved);
        using var scope = factory.Services.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<ISettingsMoneyCandidateReader>();

        // Unavailable, never an empty list or GDC's TRY.
        Assert.Equal("settings_provider_unavailable", (await Assert.ThrowsAsync<SettingsPrerequisiteException>(
            () => reader.GetMoneyCurrencyCandidatesAsync())).Code);
    }

    [Theory]
    [InlineData("TRY", 200, null)]
    [InlineData("other currency", 503, "settings_provider_unavailable")]
    public async Task The_Sales_currency_read_with_the_host_s_own_money_binding_reaches_the_approved_profile(
        string currency, int status, string? code)
    {
        using var factory = Host(Gdc);
        using var scope = factory.Services.CreateScope();
        var hostBindings = scope.ServiceProvider.GetRequiredService<SalesReferenceReadBindings>();
        // The Sales owner's key, bound by the host (Sales decision, T02-b).
        Assert.Equal(SalesMoneyOperations.OrderDraftV1, hostBindings.MoneyOperation);
        // Since SALES-API (#321) the host binds the draft form's permission; no actor holds it (the grant
        // is a human gate), so the route answers 403 for every real actor. Only the permission is
        // synthetic here — the money operation, the Settings provider and the account source are the
        // host's own.
        Assert.Equal(SalesPermissionManifestV1.DraftsCreate, hostBindings.CurrencyPermission);
        var handler = new SalesReferenceReadHandler(
            scope.ServiceProvider.GetRequiredService<ICompanyContext>(),
            scope.ServiceProvider.GetRequiredService<ICurrentUser>(),
            new GrantsOnly(SyntheticCurrencyPermission, Gdc),
            scope.ServiceProvider.GetRequiredService<ISalesAccountSelectionSourceV1>(),
            scope.ServiceProvider.GetRequiredService<ISettingsPrerequisiteProvider>(),
            null,
            hostBindings with { CurrencyPermission = SyntheticCurrencyPermission });
        var currencyId = currency == "TRY"
            ? SettingsCurrencyIdentitiesV1.TurkishLira
            : Guid.Parse("0f3bd0a1-6e2c-4d77-a3a5-0b1c2d3e4f52");

        var result = await handler.GetCurrencyAsync(new SalesCurrencyQueryV1(currencyId, Approval));

        Assert.Equal(status, result.StatusCode);
        Assert.Equal(code, result.Code);
        if (status == 200)
        {
            Assert.Equal(Gdc, result.Value!.CompanyId);
            Assert.Equal("TRY", result.Value.Code);
            Assert.Equal(ApprovedMoneyProfileCatalog.GdcTurkishLiraV1.ProfileId, result.Value.ProfileId);
            Assert.Equal("1", result.Value.ProfileVersion);
        }
        else
        {
            // Today's Sales mapping of settings_money_profile_not_found (Sales' own follow-up maps it
            // to 422): fail-closed, no profile.
            Assert.Null(result.Value);
        }
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
        // route's permission (sales.drafts.create since #321) is held by no actor, so it answers 403
        // before asking. The swap and C3 change no reachable Sales answer here.
        var eligibility = scope.ServiceProvider.GetRequiredService<IProductSalesEligibilityV1>();
        var failure = await Assert.ThrowsAsync<ProductOperationException>(() => eligibility.EvaluateAsync(
            new ProductSalesSelectionV1(Guid.NewGuid(), "1", Guid.NewGuid(), Approval), default));
        Assert.Equal((503, "product_runtime_unconfigured"), (failure.StatusCode, failure.Code));
        Assert.Equal(SalesPermissionManifestV1.DraftsCreate,
            scope.ServiceProvider.GetRequiredService<SalesReferenceReadBindings>().ProductPermission);
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

    private const string SyntheticCurrencyPermission = "synthetic.sales-currency.read";

    /// <summary>Grants one synthetic permission in one company; denies everything else.</summary>
    private sealed class GrantsOnly(string permission, Guid company) : IErpAuthorizationService
    {
        private PermissionScope Scope(string asked, Guid companyId) =>
            asked == permission && companyId == company
                ? new(true, true, new HashSet<string>(StringComparer.Ordinal), new HashSet<Guid>(),
                    new HashSet<Guid>(), new HashSet<Guid>())
                : PermissionScope.Denied;

        public Task<bool> HasPermissionAsync(string asked, Guid companyId,
            CancellationToken cancellationToken = default) => Task.FromResult(Scope(asked, companyId).IsGranted);

        public Task<PermissionScope> GetPermissionScopeAsync(string asked, Guid companyId,
            CancellationToken cancellationToken = default) => Task.FromResult(Scope(asked, companyId));

        public Task<AuthorizationSnapshot> GetAuthorizationSnapshotAsync(Guid companyId,
            IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AuthorizationSnapshot("synthetic-settings-host",
                companyId == company ? new HashSet<Guid> { companyId } : new HashSet<Guid>(),
                permissions.ToDictionary(asked => asked, asked => Scope(asked, companyId))));
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
