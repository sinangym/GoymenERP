using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.WorkInbox;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// W1-A (AI-D31, AI-D32): the Work inbox source over the proposed reversals of Finance's approved opening balances
/// (FNS-D85), on SYNTHETIC rows. Only Proposed reversals of an approved opening are listed; the reversal's proposer is the
/// maker (the opening's own proposer is not refused); the rule carries the opening's stored amount.
/// </summary>
public sealed class FinanceOpeningBalanceReversalWorkInboxSourceV1Tests : FinanceWorkInboxSourceContractTests
{
    private protected override string ExpectedKind => "finance.opening-balance-reversal.v1";

    private protected override string ExpectedPermission => ErpPermissions.FinanceOpeningBalancesApprove;

    private protected override IWorkInboxSourceV1 Create(FinanceDbContext db, FinanceWorkInboxFence fence) =>
        new FinanceOpeningBalanceReversalWorkInboxSourceV1(db, fence);

    private protected override Guid AddPending(FinanceDbContext db, Guid company, DateTimeOffset at, string maker) =>
        Add(db, company, at, maker, FinanceOpeningBalanceService.Approved, FinanceOpeningBalanceService.Proposed).Reversal.Id;

    private protected override void AddNotPending(FinanceDbContext db, Guid company, DateTimeOffset at)
    {
        Add(db, company, at, Maker, FinanceOpeningBalanceService.Reversed, FinanceOpeningBalanceService.Approved);
        Add(db, company, at, Maker, FinanceOpeningBalanceService.Approved, FinanceOpeningBalanceService.Rejected);
    }

    private protected override void AddInvalid(FinanceDbContext db, Guid company, DateTimeOffset at) =>
        // A proposed reversal whose opening is not approved breaks the owner's invariant.
        Add(db, company, at, Maker, FinanceOpeningBalanceService.Proposed, FinanceOpeningBalanceService.Proposed);

    [Fact]
    public async Task The_rule_carries_the_openings_stored_amount_and_the_opening_is_a_reference()
    {
        var opening = FinanceOpeningBalanceWorkInboxSourceV1Tests.Opening(Company, Start.AddDays(-5), "SENTETIK-opening-proposer",
            FinanceOpeningBalanceService.Approved, "420.50");
        var reversal = Reversal(Company, opening.Id, Start, Maker, FinanceOpeningBalanceService.Proposed);

        var result = await ListAsync(db =>
        {
            db.OpeningBalances.Add(opening);
            db.OpeningBalanceReversals.Add(reversal);
        });

        var item = Assert.Single(result.Items!);
        Assert.Equal(reversal.Id, item.ItemId);
        Assert.Equal(Start.ToUniversalTime(), item.WaitingSinceUtc);
        Assert.False(item.Excludes("SENTETIK-opening-proposer"));
        Assert.Equal(opening.Id.ToString("D")[..8], item.DocumentLabel);
        Assert.Equal("/finans/acilis-bakiyeleri", item.Link);
        Assert.Equal(
            [new WorkApprovalRuleResultV1(FinanceOpeningBalanceReversalWorkInboxSourceV1.ApprovalRequiredRule, null, "420.5", null, null)],
            item.Rules);
        Assert.Contains(item.References, reference => reference.Role == "opening-balance" && reference.RecordId == opening.Id);
        Assert.Contains(item.References, reference => reference.Role == "cash-account" && reference.RecordId == opening.TargetId);
    }

    private static (OpeningBalanceRow Opening, OpeningBalanceReversalRow Reversal) Add(FinanceDbContext db, Guid company,
        DateTimeOffset at, string proposedBy, int openingStatus, int reversalStatus)
    {
        var opening = FinanceOpeningBalanceWorkInboxSourceV1Tests.Opening(company, at.AddDays(-5), "SENTETIK-opening-proposer",
            openingStatus);
        var reversal = Reversal(company, opening.Id, at, proposedBy, reversalStatus);
        db.OpeningBalances.Add(opening);
        db.OpeningBalanceReversals.Add(reversal);
        return (opening, reversal);
    }

    private static OpeningBalanceReversalRow Reversal(Guid company, Guid openingId, DateTimeOffset at, string proposedBy,
        int status) =>
        new()
        {
            Id = Guid.NewGuid(),
            CompanyId = company,
            OpeningBalanceId = openingId,
            Status = status,
            Reason = "SENTETIK-reason",
            OpeningPeriodVersion = "1",
            ProposedBy = proposedBy,
            ProposedAtUtc = at,
            DecidedBy = status == FinanceOpeningBalanceService.Proposed ? null : Caller,
            DecidedAtUtc = status == FinanceOpeningBalanceService.Proposed ? null : at,
            Version = 1,
            UpdatedAtUtc = at,
        };
}
