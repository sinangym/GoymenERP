using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Settings.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Infrastructure.WorkInbox;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Settings.UnitTests;

/// <summary>
/// WP7 (AI-D31/AI-D32, No 133, URN-D3/URN-D6): the Ayarlar policy Work inbox sources list every version of the company that
/// waits for publication (Draft; a Manufacturing profile also when left Approved), oldest preparation first; the caller must hold
/// every permission the family's publish command measures; no maker is named because the manager publishes alone.
/// SENTETIK / NOT_ENV.
/// </summary>
public sealed class SettingsPolicyWorkInboxSourceV1Tests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 22, 7, 0, 0, TimeSpan.Zero);
    private static readonly Guid CompanyA = Guid.Parse("a1111111-1111-4111-8111-111111111111");
    private static readonly Guid CompanyB = Guid.Parse("b2222222-2222-4222-8222-222222222222");
    private const string Manager = "SENTETIK-manager";

    private readonly string databaseName = "settings-work-inbox-" + Guid.NewGuid().ToString("N");
    private readonly HashSet<string> withheld = new(StringComparer.Ordinal);
    private readonly List<string> asked = [];

    [Fact]
    public async Task Draft_finance_approval_policies_of_the_caller_company_are_listed_oldest_first_without_a_maker()
    {
        var older = Finance<FinanceApprovalPolicyRow>(CompanyA, 2, "Draft", Start);
        var newer = Finance<FinanceApprovalPolicyRow>(CompanyA, 3, "Draft", Start.AddHours(1));
        var published = Finance<FinanceApprovalPolicyRow>(CompanyA, 1, "Draft", Start.AddDays(-1));
        await SeedAsync(CompanyA, db => db.FinanceApprovalPolicies.AddRange(newer, older, published));
        await SeedAsync(CompanyA, db => Publish(db.FinanceApprovalPolicies.Single(row => row.PolicyId == published.PolicyId)));
        await SeedAsync(CompanyB, db => db.FinanceApprovalPolicies.Add(Finance<FinanceApprovalPolicyRow>(CompanyB, 1, "Draft", Start.AddDays(-2))));

        var source = new SettingsFinanceApprovalPolicyWorkInboxSourceV1(Context(CompanyA), Fence(CompanyA));
        var result = await source.ListPendingAsync(Caller(), 10, CancellationToken.None);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Found, result.Outcome);
        Assert.False(result.HasMore);
        Assert.Equal([older.PolicyId, newer.PolicyId], result.Items!.Select(item => item.ItemId));
        var first = result.Items![0];
        AssertShape(first);
        Assert.Equal("settings.finance-approval-policy.v1", first.Kind);
        Assert.Equal("v2", first.DocumentLabel);
        Assert.Equal("/ayarlar/finans-onay-politikalari", first.Link);
        Assert.Equal(Start, first.WaitingSinceUtc);
        Assert.Null(first.PreparedBy);
        Assert.False(first.Excludes(Manager));
        Assert.Equal([SettingsPolicyWorkInboxSourceV1.DraftAwaitingPublicationRule, SettingsPolicyWorkInboxSourceV1.NextVersionRule],
            first.Rules.Select(rule => rule.RuleCode));
        Assert.Equal(SettingsFinancePolicyPermissionsV1.PublishCritical, source.ApproverPermission);
        Assert.Equal(
            [SettingsFinancePolicyPermissionsV1.Prepare, SettingsFinancePolicyPermissionsV1.Publish, SettingsFinancePolicyPermissionsV1.PublishCritical],
            asked.Take(3));
    }

    [Fact]
    public async Task The_finance_matrix_is_denied_without_publish_critical_and_the_other_families_need_prepare_and_publish()
    {
        await SeedAsync(CompanyA, db =>
        {
            db.FinanceApprovalPolicies.Add(Finance<FinanceApprovalPolicyRow>(CompanyA, 1, "Draft", Start));
            var cash = Finance<CashCountDifferencePolicyRow>(CompanyA, 1, "Draft", Start);
            cash.CashAccountId = Guid.NewGuid();
            cash.CashAccountVersion = "1";
            cash.CurrencyId = Guid.NewGuid();
            cash.AmountThreshold = "10";
            db.CashCountDifferencePolicies.Add(cash);
        });
        withheld.Add(SettingsFinancePolicyPermissionsV1.PublishCritical);

        var matrix = await new SettingsFinanceApprovalPolicyWorkInboxSourceV1(Context(CompanyA), Fence(CompanyA))
            .ListPendingAsync(Caller(), 5, CancellationToken.None);
        var cashResult = await new SettingsCashCountDifferencePolicyWorkInboxSourceV1(Context(CompanyA), Fence(CompanyA))
            .ListPendingAsync(Caller(), 5, CancellationToken.None);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Denied, matrix.Outcome);
        Assert.Null(matrix.Items);
        var cashItem = Assert.Single(cashResult.Items!);
        AssertShape(cashItem);
        Assert.Equal("cash-account", Assert.Single(cashItem.References).Role);
        Assert.Equal([SettingsPolicyWorkInboxSourceV1.DraftAwaitingPublicationRule], cashItem.Rules.Select(rule => rule.RuleCode));

        withheld.Add(SettingsFinancePolicyPermissionsV1.Prepare);
        var withoutPrepare = await new SettingsCashCountDifferencePolicyWorkInboxSourceV1(Context(CompanyA), Fence(CompanyA))
            .ListPendingAsync(Caller(), 5, CancellationToken.None);
        Assert.Equal(NativeApprovalQueueOutcomeV1.Denied, withoutPrepare.Outcome);
    }

    [Fact]
    public async Task Bank_institution_and_collateral_rate_drafts_are_listed_on_their_own_screens()
    {
        await SeedAsync(CompanyA, db =>
        {
            db.BankInstitutionPolicies.Add(Finance<BankInstitutionPolicyRow>(CompanyA, 1, "Draft", Start));
            var rate = Finance<CollateralRatePolicyRow>(CompanyA, 4, "Draft", Start);
            rate.ScopeKey = "SENTETIK-scope";
            rate.Scope = "Type";
            rate.RatePercent = "50";
            var earlier = Finance<CollateralRatePolicyRow>(CompanyA, 3, "Draft", Start.AddDays(-1));
            earlier.ScopeKey = "SENTETIK-scope";
            earlier.Scope = "Type";
            earlier.RatePercent = "40";
            db.CollateralRatePolicies.AddRange(rate, earlier);
        });
        await SeedAsync(CompanyA, db => Publish(db.CollateralRatePolicies.Single(row => row.Version == 3)));

        var bank = Assert.Single((await new SettingsBankInstitutionPolicyWorkInboxSourceV1(Context(CompanyA), Fence(CompanyA))
            .ListPendingAsync(Caller(), 5, CancellationToken.None)).Items!);
        var collateral = Assert.Single((await new SettingsCollateralRatePolicyWorkInboxSourceV1(Context(CompanyA), Fence(CompanyA))
            .ListPendingAsync(Caller(), 5, CancellationToken.None)).Items!);

        AssertShape(bank);
        AssertShape(collateral);
        Assert.Equal(("settings.bank-institution-policy.v1", "/ayarlar/banka-kurumlari"), (bank.Kind, bank.Link));
        Assert.Equal(("settings.collateral-rate-policy.v1", "/ayarlar/teminat-oran-politikalari", "v4"),
            (collateral.Kind, collateral.Link, collateral.DocumentLabel));
    }

    [Fact]
    public async Task Manufacturing_profiles_in_draft_or_left_approved_are_listed_and_published_ones_are_not()
    {
        var workCenter = Guid.NewGuid();
        var draft = Parameter(CompanyA, 2, ManufacturingProfileStatusesV1.Draft, Start.AddMinutes(30), workCenter);
        var approved = Parameter(CompanyA, 1, ManufacturingProfileStatusesV1.Approved, Start, null);
        await SeedAsync(CompanyA, db =>
        {
            db.ManufacturingParameterProfiles.AddRange(draft, approved,
                Parameter(CompanyA, 1, ManufacturingProfileStatusesV1.Published, Start.AddDays(-1), null));
            db.ManufacturingToleranceProfileVersions.Add(Tolerance(CompanyA, 1, ManufacturingProfileStatusesV1.Draft, Start));
        });

        var parameters = (await new SettingsManufacturingParameterProfileWorkInboxSourceV1(Context(CompanyA), Fence(CompanyA))
            .ListPendingAsync(Caller(), 5, CancellationToken.None)).Items!;
        var tolerance = Assert.Single((await new SettingsManufacturingToleranceProfileWorkInboxSourceV1(Context(CompanyA), Fence(CompanyA))
            .ListPendingAsync(Caller(), 5, CancellationToken.None)).Items!);

        Assert.Equal([approved.ProfileId, draft.ProfileId], parameters.Select(item => item.ItemId));
        Assert.All(parameters, AssertShape);
        Assert.Equal([SettingsPolicyWorkInboxSourceV1.ApprovedAwaitingPublicationRule], parameters[0].Rules.Select(rule => rule.RuleCode));
        Assert.Equal("Slitting v2", parameters[1].DocumentLabel);
        Assert.Equal(("work-center", workCenter), (parameters[1].References[0].Role, parameters[1].References[0].RecordId));
        Assert.Equal("/ayarlar/uretim-parametre-profilleri", parameters[1].Link);
        AssertShape(tolerance);
        Assert.Equal(("settings.manufacturing-tolerance-profile.v1", "/ayarlar/uretim-tolerans-profilleri"), (tolerance.Kind, tolerance.Link));
        Assert.Contains(ManufacturingParameterProfileActionsV1.Publish, asked);
        Assert.Contains(ManufacturingToleranceProfileActionsV1.Publish, asked);
    }

    [Fact]
    public async Task More_waiting_versions_than_requested_set_has_more_and_another_user_is_denied()
    {
        var rows = Enumerable.Range(1, 3).Select(index => Finance<BankInstitutionPolicyRow>(CompanyA, index, "Draft", Start.AddMinutes(index))).ToArray();
        await SeedAsync(CompanyA, db => db.BankInstitutionPolicies.AddRange(rows));

        var result = await new SettingsBankInstitutionPolicyWorkInboxSourceV1(Context(CompanyA), Fence(CompanyA))
            .ListPendingAsync(Caller(), 2, CancellationToken.None);
        Assert.True(result.HasMore);
        Assert.Equal([rows[0].PolicyId, rows[1].PolicyId], result.Items!.Select(item => item.ItemId));

        var other = await new SettingsBankInstitutionPolicyWorkInboxSourceV1(Context(CompanyA), Fence(CompanyA))
            .ListPendingAsync(new WorkInboxSourceContextV1(CompanyA, "SENTETIK-other", "SENTETIK-correlation", null), 2,
                CancellationToken.None);
        Assert.Equal(NativeApprovalQueueOutcomeV1.Denied, other.Outcome);
    }

    private static void AssertShape(WorkInboxItemV1 item)
    {
        Assert.Equal("settings", item.Module);
        Assert.True(NativeApprovalQueueRulesV1.IsValidKind(item.Module, item.Kind));
        Assert.True(NativeApprovalQueueRulesV1.IsValidLink(item.Link), item.Link);
        Assert.Equal(TimeSpan.Zero, item.WaitingSinceUtc.Offset);
        Assert.InRange(item.Rules.Count, 1, WorkInboxRulesV1.MaxRulesPerItem);
        Assert.All(item.Rules, rule => Assert.True(WorkInboxRulesV1.IsValidRule("settings", rule), rule.RuleCode));
        Assert.All(item.References, reference => Assert.True(WorkInboxRulesV1.IsValidReference(reference), reference.Role));
    }

    private static WorkInboxSourceContextV1 Caller() => new(CompanyA, Manager, "SENTETIK-correlation", null);

    private SettingsDbContext Context(Guid company)
    {
        var context = new CompanyContext();
        context.Set(company);
        return new SettingsDbContext(new DbContextOptionsBuilder<SettingsDbContext>().UseInMemoryDatabase(databaseName).Options, context);
    }

    private SettingsWorkInboxFence Fence(Guid company)
    {
        var context = new CompanyContext();
        context.Set(company);
        return new SettingsWorkInboxFence(context, new User(), new Authority(this));
    }

    private async Task SeedAsync(Guid company, Action<SettingsDbContext> seed)
    {
        await using var context = Context(company);
        seed(context);
        await context.SaveChangesAsync();
    }

    private static TRow Finance<TRow>(Guid company, long version, string state, DateTimeOffset preparedAt)
        where TRow : class, IFinancePolicyVersionRow, new()
    {
        var row = new TRow();
        IFinancePolicyVersionRow value = row;
        value.CompanyId = company;
        value.PolicyId = Guid.NewGuid();
        value.Version = version;
        value.StateVersion = 1;
        value.State = state;
        value.ValidFrom = new DateOnly(2026, 12, 1);
        value.PreparedBy = Manager;
        value.PreparedAtUtc = preparedAt;
        value.Reason = "SENTETIK";
        value.LastReason = "SENTETIK";
        return row;
    }

    /// <summary>The owner's Draft → Published transition (the write guard allows only that change).</summary>
    private static void Publish(IFinancePolicyVersionRow row)
    {
        row.State = "Published";
        row.StateVersion += 1;
        row.PublishedBy = Manager;
        row.PublishedAtUtc = row.PreparedAtUtc.AddMinutes(1);
    }

    private static ManufacturingParameterProfileRow Parameter(Guid company, long version, string status, DateTimeOffset preparedAt,
        Guid? workCenter) => new()
    {
        CompanyId = company, ProfileId = Guid.NewGuid(), Process = "Slitting", WorkCenterId = workCenter, ProfileVersion = version,
        StateVersion = 1, Status = status, EffectiveFrom = new DateOnly(2026, 12, 1), PreparedBy = Manager, PreparedAtUtc = preparedAt,
        PreparationReason = "SENTETIK",
        ApprovedBy = status == ManufacturingProfileStatusesV1.Draft ? null : "SENTETIK-old-approver",
        ApprovedAtUtc = status == ManufacturingProfileStatusesV1.Draft ? null : preparedAt.AddMinutes(1),
        ApprovalReason = status == ManufacturingProfileStatusesV1.Draft ? null : "SENTETIK",
        PublishedBy = status == ManufacturingProfileStatusesV1.Published ? Manager : null,
        PublishedAtUtc = status == ManufacturingProfileStatusesV1.Published ? preparedAt.AddMinutes(2) : null,
        PublicationReason = status == ManufacturingProfileStatusesV1.Published ? "SENTETIK" : null,
    };

    private static ManufacturingToleranceProfileVersionRow Tolerance(Guid company, long version, string status, DateTimeOffset preparedAt) => new()
    {
        CompanyId = company, ProfileId = Guid.NewGuid(), Process = "Slitting", ProfileVersion = version, StateVersion = 1, Status = status,
        MaximumFirePercent = 2m, MassBalanceDifferenceTolerancePercent = 1m, EffectiveFrom = new DateOnly(2026, 12, 1),
        PreparedBy = Manager, PreparedAtUtc = preparedAt, PreparationReason = "SENTETIK",
    };

    private sealed class User : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => Manager;
        public IReadOnlySet<Guid> AllowedCompanyIds => new HashSet<Guid> { CompanyA, CompanyB };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }

    private sealed class Authority(SettingsPolicyWorkInboxSourceV1Tests test) : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default)
        {
            test.asked.Add(permission);
            return Task.FromResult(new PermissionScope(!test.withheld.Contains(permission), true,
                new HashSet<string>(StringComparer.Ordinal), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
        }
    }
}
