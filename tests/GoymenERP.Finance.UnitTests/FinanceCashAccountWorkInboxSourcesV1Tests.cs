using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.WorkInbox;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// WP5 (YOL-D41): the Work inbox source over Finance's proposed cash accounts, on SYNTHETIC rows. The approval has no
/// owner screen, so the link opens the inbox's own approval detail; the proposer is the maker; the rule has no numbers.
/// </summary>
public sealed class FinanceCashAccountWorkInboxSourceV1Tests : FinanceWorkInboxSourceContractTests
{
    private protected override string ExpectedKind => "finance.cash-account.v1";

    private protected override string ExpectedPermission => ErpPermissions.FinanceCashAccountApprove;

    private protected override IWorkInboxSourceV1 Create(FinanceDbContext db, FinanceWorkInboxFence fence) =>
        new FinanceCashAccountWorkInboxSourceV1(db, fence);

    private protected override Guid AddPending(FinanceDbContext db, Guid company, DateTimeOffset at, string maker)
    {
        var row = FinanceCashAccountRows.Account(company, at, maker, FinanceCashAccountService.Proposed);
        db.CashAccounts.Add(row);
        return row.Id;
    }

    private protected override void AddNotPending(FinanceDbContext db, Guid company, DateTimeOffset at) =>
        db.CashAccounts.Add(FinanceCashAccountRows.Account(company, at, Maker, FinanceCashAccountService.Approved));

    private protected override void AddInvalid(FinanceDbContext db, Guid company, DateTimeOffset at)
    {
        var row = FinanceCashAccountRows.Account(company, at, Maker, FinanceCashAccountService.Proposed);
        row.CurrencyId = Guid.Empty;
        db.CashAccounts.Add(row);
    }

    [Fact]
    public async Task The_link_opens_the_inbox_detail_and_the_rule_has_no_numbers()
    {
        var account = FinanceCashAccountRows.Account(Company, Start, Maker, FinanceCashAccountService.Proposed);

        var result = await ListAsync(db => db.CashAccounts.Add(account));

        var item = Assert.Single(result.Items!);
        Assert.Equal("/onaylarim/finance.cash-account.v1/" + account.Id.ToString("D"), item.Link);
        Assert.True(NativeApprovalQueueRulesV1.IsValidLink(item.Link));
        Assert.Equal(Maker, item.PreparedBy);
        Assert.Null(item.ExcludedDeciders);
        Assert.Equal([new WorkApprovalRuleResultV1(FinanceCashAccountWorkInboxSourceV1.ApprovalRequiredRule, null, null, null, null)],
            item.Rules);
        Assert.Contains(item.References, reference => reference.Role == "cash-account" && reference.RecordId == account.Id
            && reference.Label == account.DisplayName);
    }
}

/// <summary>WP5 (YOL-D41): a pending passivation of an active cash account; the change proposer is the maker.</summary>
public sealed class FinanceCashAccountPassivationWorkInboxSourceV1Tests : FinanceWorkInboxSourceContractTests
{
    private protected override string ExpectedKind => "finance.cash-account-passivation.v1";

    private protected override string ExpectedPermission => ErpPermissions.FinanceCashAccountPassivationApprove;

    private protected override IWorkInboxSourceV1 Create(FinanceDbContext db, FinanceWorkInboxFence fence) =>
        new FinanceCashAccountPassivationWorkInboxSourceV1(db, fence);

    private protected override Guid AddPending(FinanceDbContext db, Guid company, DateTimeOffset at, string maker)
    {
        var row = FinanceCashAccountRows.Change(company, at, maker, FinanceCashAccountService.Approved,
            FinanceCashAccountService.PassivationKind);
        db.CashAccounts.Add(row);
        return row.Id;
    }

    private protected override void AddNotPending(FinanceDbContext db, Guid company, DateTimeOffset at)
    {
        db.CashAccounts.Add(FinanceCashAccountRows.Account(company, at, Maker, FinanceCashAccountService.Approved));
        // A pending reactivation is another kind, never a passivation.
        db.CashAccounts.Add(FinanceCashAccountRows.Change(company, at, Maker, FinanceCashAccountService.Passive,
            FinanceCashAccountService.ReactivationKind));
    }

    private protected override void AddInvalid(FinanceDbContext db, Guid company, DateTimeOffset at)
    {
        var row = FinanceCashAccountRows.Change(company, at, Maker, FinanceCashAccountService.Approved,
            FinanceCashAccountService.PassivationKind);
        row.ChangeProposedAtUtc = null;
        db.CashAccounts.Add(row);
    }

    [Fact]
    public async Task The_change_proposer_is_the_maker_and_the_change_time_is_the_waiting_time()
    {
        var account = FinanceCashAccountRows.Change(Company, Start, Maker, FinanceCashAccountService.Approved,
            FinanceCashAccountService.PassivationKind);
        account.ProposedBy = "SENTETIK-original-proposer";
        account.ProposedAtUtc = Start.AddDays(-30);

        var result = await ListAsync(db => db.CashAccounts.Add(account));

        var item = Assert.Single(result.Items!);
        Assert.Equal(Maker, item.PreparedBy);
        Assert.Equal(Start.ToUniversalTime(), item.WaitingSinceUtc);
        Assert.Equal("/onaylarim/finance.cash-account-passivation.v1/" + account.Id.ToString("D"), item.Link);
        Assert.Equal(FinanceCashAccountPassivationWorkInboxSourceV1.ApprovalRequiredRule, Assert.Single(item.Rules).RuleCode);
    }
}

/// <summary>WP5 (YOL-D41): a pending reactivation of a passive cash account; the change proposer is the maker.</summary>
public sealed class FinanceCashAccountReactivationWorkInboxSourceV1Tests : FinanceWorkInboxSourceContractTests
{
    private protected override string ExpectedKind => "finance.cash-account-reactivation.v1";

    private protected override string ExpectedPermission => ErpPermissions.FinanceCashAccountReactivationApprove;

    private protected override IWorkInboxSourceV1 Create(FinanceDbContext db, FinanceWorkInboxFence fence) =>
        new FinanceCashAccountReactivationWorkInboxSourceV1(db, fence);

    private protected override Guid AddPending(FinanceDbContext db, Guid company, DateTimeOffset at, string maker)
    {
        var row = FinanceCashAccountRows.Change(company, at, maker, FinanceCashAccountService.Passive,
            FinanceCashAccountService.ReactivationKind);
        db.CashAccounts.Add(row);
        return row.Id;
    }

    private protected override void AddNotPending(FinanceDbContext db, Guid company, DateTimeOffset at)
    {
        db.CashAccounts.Add(FinanceCashAccountRows.Account(company, at, Maker, FinanceCashAccountService.Passive));
        db.CashAccounts.Add(FinanceCashAccountRows.Change(company, at, Maker, FinanceCashAccountService.Approved,
            FinanceCashAccountService.PassivationKind));
    }

    private protected override void AddInvalid(FinanceDbContext db, Guid company, DateTimeOffset at)
    {
        var row = FinanceCashAccountRows.Change(company, at, Maker, FinanceCashAccountService.Passive,
            FinanceCashAccountService.ReactivationKind);
        row.ChangeProposedBy = " ";
        db.CashAccounts.Add(row);
    }
}

internal static class FinanceCashAccountRows
{
    internal static readonly Guid Try = Guid.Parse("88888888-8888-4888-8888-888888888888");

    internal static CashAccountRow Account(Guid company, DateTimeOffset at, string proposedBy, int status) =>
        new()
        {
            Id = Guid.NewGuid(),
            CompanyId = company,
            CurrencyId = Try,
            DisplayName = "SENTETIK Kasa " + Guid.NewGuid().ToString("N")[..4],
            Status = status,
            Version = 1,
            ProposedBy = proposedBy,
            ProposedAtUtc = at,
            DecidedBy = status == FinanceCashAccountService.Proposed ? null : "SENTETIK-decider",
            DecidedAtUtc = status == FinanceCashAccountService.Proposed ? null : at,
            UpdatedAtUtc = at,
        };

    internal static CashAccountRow Change(Guid company, DateTimeOffset at, string changeProposedBy, int status, int kind)
    {
        var row = Account(company, at.AddDays(-10), "SENTETIK-account-proposer", status);
        row.ChangeKind = kind;
        row.ChangeStatus = FinanceCashAccountService.ChangePending;
        row.ChangeProposedBy = changeProposedBy;
        row.ChangeProposedAtUtc = at;
        row.UpdatedAtUtc = at;
        return row;
    }
}
