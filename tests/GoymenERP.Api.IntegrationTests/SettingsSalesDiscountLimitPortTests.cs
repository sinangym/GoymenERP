using GoymenERP.Api.Endpoints;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Settings.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// SAT-D61…D64: the precedence of the two discount limit sources behind <see cref="ISettingsSalesPolicyProviderV1"/> and the
/// fail-closed edges of the binding read the Sales adapter uses (synthetic, in-memory Settings store; the real-SQL behaviour
/// is <see cref="SettingsSalesDiscountLimitSqlServerRelationalAcceptanceTests"/>).
/// </summary>
public sealed class SettingsSalesDiscountLimitPortTests
{
    private static readonly Guid Company = TestCompanyIds.CompanyA;
    private static readonly Guid Group = Guid.Parse("9b3c3f8e-1a47-4d0a-8a62-000000000001");
    private static readonly DateOnly Day = new(2026, 10, 27);

    [Fact]
    public async Task A_group_the_store_does_not_define_is_answered_by_the_approved_catalog_unchanged()
    {
        await using var database = Database();
        var catalog = new SyntheticCatalog { Limit = 12m };
        var provider = new SettingsSalesPolicyProviderV1(database, Context(), Actor("actor"), catalog);

        var answer = await provider.GetSalesDiscountLimitAsync(Group, Day);

        Assert.Equal(12m, answer.MaximumDiscountPercent);
        catalog.Limit = null;
        Assert.Equal(SettingsPrerequisiteCodesV1.SalesPolicyNotFound,
            (await Assert.ThrowsAsync<SettingsPrerequisiteException>(() => provider.GetSalesDiscountLimitAsync(Group, Day))).Code);
        catalog.Fault = "settings_provider_unavailable";
        Assert.Equal("settings_provider_unavailable",
            (await Assert.ThrowsAsync<SettingsPrerequisiteException>(() => provider.GetSalesDiscountLimitAsync(Group, Day))).Code);
    }

    [Fact]
    public async Task A_store_group_the_catalog_also_defines_is_a_contradiction_that_fails_closed()
    {
        await using var database = Database();
        await SeedAsync(database, Group, 30m);
        var provider = new SettingsSalesPolicyProviderV1(database, Context(), Actor("actor"), new SyntheticCatalog { Limit = 12m });

        Assert.Equal("settings_profile_invalid",
            (await Assert.ThrowsAsync<SettingsPrerequisiteException>(() => provider.GetSalesDiscountLimitAsync(Group, Day))).Code);

        // Without the contradiction the store answers, with its own record.
        var store = new SettingsSalesPolicyProviderV1(database, Context(), Actor("actor"), new SyntheticCatalog());
        var answer = await store.GetSalesDiscountLimitAsync(Group, Day);
        Assert.Equal((30m, 1L, Day), (answer.MaximumDiscountPercent, answer.Profile.Version, answer.EffectiveFrom));
    }

    [Fact]
    public async Task Access_and_an_empty_group_are_refused_before_anything_is_read()
    {
        await using var database = Database();
        var catalog = new SyntheticCatalog { Limit = 12m };

        Assert.Equal("settings_access_denied", (await Assert.ThrowsAsync<SettingsPrerequisiteException>(
            () => new SettingsSalesPolicyProviderV1(database, Context(), Actor("actor", Guid.NewGuid()), catalog)
                .GetSalesDiscountLimitAsync(Group, Day))).Code);
        Assert.Equal(SettingsPrerequisiteCodesV1.SalesPolicyRequestInvalid, (await Assert.ThrowsAsync<SettingsPrerequisiteException>(
            () => new SettingsSalesPolicyProviderV1(database, Context(), Actor("actor"), catalog)
                .GetSalesDiscountLimitAsync(Guid.Empty, Day))).Code);
        Assert.Equal(0, catalog.Calls);
    }

    [Fact]
    public async Task The_sales_adapter_reads_no_group_for_an_unbound_or_non_erp_actor_and_passes_a_denial_on()
    {
        await using var database = Database();
        await SeedAsync(database, Group, 30m);

        var unbound = new SalesDiscountLimitGroupHost(
            new SettingsSalesDiscountLimitGroupMembershipReaderV1(database, Context(), Actor(Guid.NewGuid().ToString("D"))));
        var nonErp = new SalesDiscountLimitGroupHost(
            new SettingsSalesDiscountLimitGroupMembershipReaderV1(database, Context(), Actor("not-an-erp-user-id")));
        var foreign = new SalesDiscountLimitGroupHost(
            new SettingsSalesDiscountLimitGroupMembershipReaderV1(database, Context(), Actor(Guid.NewGuid().ToString("D"), Guid.NewGuid())));

        Assert.Empty(await unbound.GetCurrentActorLimitGroupsAsync());
        Assert.Empty(await nonErp.GetCurrentActorLimitGroupsAsync());
        Assert.True((await Assert.ThrowsAsync<SalesDiscountLimitGroupSourceException>(
            () => foreign.GetCurrentActorLimitGroupsAsync())).IsDenied);
    }

    private static SettingsDbContext Database() => new(new DbContextOptionsBuilder<SettingsDbContext>()
        .UseInMemoryDatabase($"settings-limit-port-{Guid.NewGuid():N}").Options, Context());

    private static CompanyContext Context()
    {
        var context = new CompanyContext();
        context.Set(Company);
        return context;
    }

    private static async Task SeedAsync(SettingsDbContext database, Guid group, decimal percent)
    {
        var now = new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);
        database.SalesDiscountLimitGroups.Add(new SalesDiscountLimitGroupRow
        {
            CompanyId = Company, Id = group, Code = "SENTETIK", Name = "Sentetik", IsActive = true, Version = 2, LimitVersion = 1,
            CreatedAtUtc = now, CreatedBy = "x", UpdatedAtUtc = now, UpdatedBy = "x",
        });
        database.SalesDiscountLimitVersions.Add(new SalesDiscountLimitVersionRow
        {
            CompanyId = Company, LimitGroupId = group, Version = 1, ProfileId = Guid.NewGuid(), Kind = SalesDiscountLimitKindsV1.Fixed,
            Percent = percent, EffectiveFrom = Day, RecordedAtUtc = now, RecordedBy = "x", Reason = "SENTETIK",
        });
        await database.SaveChangesAsync();
    }

    private static FixedActor Actor(string userId, Guid? company = null) => new(userId, company ?? Company);

    private sealed class FixedActor(string userId, Guid companyId) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId { get; } = userId;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { companyId };
        public bool CanAccessCompany(Guid candidate) => AllowedCompanyIds.Contains(candidate);
    }

    /// <summary>A catalog with one optional discount limit for every group, or a fault.</summary>
    private sealed class SyntheticCatalog : ISettingsSalesPolicyProviderV1
    {
        public decimal? Limit { get; set; }
        public string? Fault { get; set; }
        public int Calls { get; private set; }

        public Task<SalesDiscountLimitPolicyV1> GetSalesDiscountLimitAsync(Guid limitGroupId, DateOnly effectiveOn,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Fault is { } fault)
            {
                return Task.FromException<SalesDiscountLimitPolicyV1>(new SettingsPrerequisiteException(fault));
            }

            return Limit is { } limit
                ? Task.FromResult(new SalesDiscountLimitPolicyV1(Company, limitGroupId, new ProfileVersionV1(Guid.NewGuid(), 1), limit,
                    true, effectiveOn, null))
                : Task.FromException<SalesDiscountLimitPolicyV1>(
                    new SettingsPrerequisiteException(SettingsPrerequisiteCodesV1.SalesPolicyNotFound));
        }

        public Task<SalesDeliveryTolerancePolicyV1> GetSalesDeliveryToleranceAsync(DateOnly effectiveOn,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<SalesApprovalAmountThresholdPolicyV1> GetSalesApprovalAmountThresholdAsync(Guid currencyId, DateOnly effectiveOn,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
