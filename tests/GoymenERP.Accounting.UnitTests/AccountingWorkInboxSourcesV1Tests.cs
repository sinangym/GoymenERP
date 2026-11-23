using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using GoymenERP.Modules.Accounting.Infrastructure.WorkInbox;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Kit = GoymenERP.Accounting.UnitTests.AccountingWorkInboxTestKit;

namespace GoymenERP.Accounting.UnitTests;

/// <summary>
/// W1-A (AI-D31): what every Accounting Work inbox source shares — the module registers each one, each names the exact
/// permission its owner command checks, none accepts a scoped approver (every Accounting decision command requires a
/// company-wide grant), and each refuses without items when the caller lacks that permission company-wide or is not the
/// context's user. SYNTHETIC data only.
/// </summary>
public sealed class AccountingWorkInboxSourcesV1Tests
{
    // ROL-D12: reopening is approved under its own permission (not yet in the Accounting manifest).

    public static TheoryData<string, string> Kinds { get; } = new()
    {
        { AccountingPeriodOpeningWorkInboxSourceV1.KindName, AccountingPermissionManifestV1.PeriodsApprove },
        { AccountingPeriodReopeningWorkInboxSourceV1.KindName, AccountingPermissionManifestV1.PeriodsReopen },
        { AccountingNormalJournalDraftWorkInboxSourceV1.KindName, NormalJournalPermissionNamesV1.DraftApprove },
        { AccountingJournalReversalWorkInboxSourceV1.KindName, AccountingJournalReversalPermissionNamesV1.Approve },
        { AccountingManufacturingCostDifferenceWorkInboxSourceV1.KindName, AccountingJournalReversalPermissionNamesV1.Approve },
        { AccountingInvoiceLineCogsCorrectionWorkInboxSourceV1.KindName, AccountingJournalReversalPermissionNamesV1.Approve },
        { AccountingInvoicedShipmentCostWorkInboxSourceV1.KindName, NormalJournalPermissionNamesV1.DraftApprove },
        { AccountingPurchaseCostDifferenceWorkInboxSourceV1.KindName, AccountingJournalReversalPermissionNamesV1.Approve },
        { AccountingInventoryCostCancellationWorkInboxSourceV1.KindName, AccountingJournalReversalPermissionNamesV1.Approve },
        { AccountingSourceVoucherWorkInboxSourceV1.KindName, NormalJournalPermissionNamesV1.DraftApprove },
    };

    public static TheoryData<string> KindNames()
    {
        var data = new TheoryData<string>();
        foreach (var kind in Kinds.Select(row => (string)row[0]))
        {
            data.Add(kind);
        }

        return data;
    }

    public static TheoryData<string, bool, bool, bool> Refusals()
    {
        var data = new TheoryData<string, bool, bool, bool>();
        foreach (var kind in Kinds.Select(row => (string)row[0]))
        {
            data.Add(kind, false, true, false);
            data.Add(kind, true, false, false);
            data.Add(kind, true, true, true);
        }

        return data;
    }

    [Fact]
    public void The_module_registers_one_scoped_source_per_accounting_kind_and_the_fence()
    {
        var services = new ServiceCollection();
        services.AddAccountingModule(new SyntheticConfiguration());

        var sources = services.Where(descriptor => descriptor.ServiceType == typeof(IWorkInboxSourceV1)).ToArray();

        Assert.All(sources, descriptor => Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime));
        Assert.Equal(
            [
                typeof(AccountingPeriodOpeningWorkInboxSourceV1),
                typeof(AccountingPeriodReopeningWorkInboxSourceV1),
                typeof(AccountingNormalJournalDraftWorkInboxSourceV1),
                typeof(AccountingJournalReversalWorkInboxSourceV1),
                typeof(AccountingManufacturingCostDifferenceWorkInboxSourceV1),
                typeof(AccountingInvoiceLineCogsCorrectionWorkInboxSourceV1),
                typeof(AccountingInvoicedShipmentCostWorkInboxSourceV1),
                typeof(AccountingPurchaseCostDifferenceWorkInboxSourceV1),
                typeof(AccountingInventoryCostCancellationWorkInboxSourceV1),
                typeof(AccountingSourceVoucherWorkInboxSourceV1),
            ],
            sources.Select(descriptor => descriptor.ImplementationType));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(AccountingWorkInboxFence)
            && descriptor.Lifetime == ServiceLifetime.Scoped);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Each_source_names_its_owner_commands_permission_and_accepts_only_company_wide_approvers(
        string kind, string permission)
    {
        await using var kit = new Kit();
        var source = Create(kind, kit);

        Assert.Equal(("accounting", kind), (source.Module, source.Kind));
        Assert.Equal(permission, source.ApproverPermission);
        Assert.True(WorkInboxRulesV1.IsValidPermission(source.ApproverPermission));
        Assert.False(source.AcceptsScopedApprovers);
    }

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task Without_the_company_wide_approve_permission_or_with_another_actor_every_source_is_denied(
        string kind, bool granted, bool companyWide, bool otherActor)
    {
        await using var kit = new Kit();
        var authority = new Kit.FixedAuthority(granted, companyWide);
        var source = Create(kind, kit, authority);

        var result = await source.ListPendingAsync(
            Kit.Context(user: otherActor ? "SENTETIK-someone-else" : null), 10, CancellationToken.None);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Denied, result.Outcome);
        Assert.Null(result.Items);
        Assert.Equal(Guid.Empty, result.CompanyId);
        Assert.All(authority.Asked, asked => Assert.Equal((source.ApproverPermission, Kit.Company), asked));
        Assert.Equal(otherActor ? 0 : 1, authority.Asked.Count);
    }

    [Theory]
    [MemberData(nameof(KindNames))]
    public async Task A_context_company_other_than_the_server_resolved_one_is_denied_before_any_read(string kind)
    {
        await using var kit = new Kit();
        var authority = new Kit.FixedAuthority(true, true);
        var source = Create(kind, kit, authority);

        var result = await source.ListPendingAsync(Kit.Context(Kit.OtherCompany), 10, CancellationToken.None);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Denied, result.Outcome);
        Assert.Empty(authority.Asked);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task An_empty_company_is_found_empty_and_nothing_is_tracked(string kind, string permission)
    {
        await using var kit = new Kit();
        var authority = new Kit.FixedAuthority(true, true);
        var source = Create(kind, kit, authority);

        var result = await source.ListPendingAsync(Kit.Context(), 10, CancellationToken.None);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Found, result.Outcome);
        Assert.Equal(Kit.Company, result.CompanyId);
        Assert.Empty(result.Items!);
        Assert.False(result.HasMore);
        // Fence before and after the read.
        Assert.Equal([(permission, Kit.Company), (permission, Kit.Company)], authority.Asked);
        Assert.False(kit.Db.ChangeTracker.HasChanges());
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task A_failed_read_is_fenced_again_and_answers_unavailable_or_the_fresh_refusal(string kind, string permission)
    {
        await using var kit = new Kit();
        var authority = new Kit.FixedAuthority(true, true);
        var source = Create(kind, kit, authority);
        await kit.Db.DisposeAsync();

        var result = await source.ListPendingAsync(Kit.Context(), 10, CancellationToken.None);

        Assert.Equal(NativeApprovalQueueOutcomeV1.Unavailable, result.Outcome);
        Assert.Null(result.Items);
        Assert.Equal([(permission, Kit.Company), (permission, Kit.Company)], authority.Asked);
    }

    private sealed class SyntheticConfiguration : IConfiguration
    {
        public string? this[string key]
        {
            get => key == "DB_CONNECTION_STRING" ? "Server=SENTETIK;Database=SENTETIK;Integrated Security=true" : null;
            set => throw new NotSupportedException();
        }

        public IEnumerable<IConfigurationSection> GetChildren() => [];

        public IChangeToken GetReloadToken() => throw new NotSupportedException();

        public IConfigurationSection GetSection(string key) => throw new NotSupportedException();
    }

    internal static IWorkInboxSourceV1 Create(string kind, Kit kit, Kit.FixedAuthority? authority = null, Guid? company = null)
    {
        var fence = kit.Fence(company, authority);
        AccountingDbContext db = kit.Db;
        return kind switch
        {
            AccountingPeriodOpeningWorkInboxSourceV1.KindName => new AccountingPeriodOpeningWorkInboxSourceV1(db, fence),
            AccountingPeriodReopeningWorkInboxSourceV1.KindName => new AccountingPeriodReopeningWorkInboxSourceV1(db, fence),
            AccountingNormalJournalDraftWorkInboxSourceV1.KindName => new AccountingNormalJournalDraftWorkInboxSourceV1(db, fence),
            AccountingJournalReversalWorkInboxSourceV1.KindName => new AccountingJournalReversalWorkInboxSourceV1(db, fence),
            AccountingManufacturingCostDifferenceWorkInboxSourceV1.KindName =>
                new AccountingManufacturingCostDifferenceWorkInboxSourceV1(db, fence),
            AccountingInvoiceLineCogsCorrectionWorkInboxSourceV1.KindName =>
                new AccountingInvoiceLineCogsCorrectionWorkInboxSourceV1(db, fence),
            AccountingInvoicedShipmentCostWorkInboxSourceV1.KindName => new AccountingInvoicedShipmentCostWorkInboxSourceV1(db, fence),
            AccountingPurchaseCostDifferenceWorkInboxSourceV1.KindName =>
                new AccountingPurchaseCostDifferenceWorkInboxSourceV1(db, fence),
            AccountingInventoryCostCancellationWorkInboxSourceV1.KindName =>
                new AccountingInventoryCostCancellationWorkInboxSourceV1(db, fence),
            AccountingSourceVoucherWorkInboxSourceV1.KindName => new AccountingSourceVoucherWorkInboxSourceV1(db, fence),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }
}
