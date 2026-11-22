using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.WorkInbox;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// W1-A (AI-D31, AI-D32): the Work inbox source over Finance's proposed opening balances (FIN-S9, FNS-D23), on SYNTHETIC
/// rows. Only Proposed openings are listed; the proposer is the maker; the rule carries the stored amount and the target
/// reference follows the target kind.
/// </summary>
public sealed class FinanceOpeningBalanceWorkInboxSourceV1Tests : FinanceWorkInboxSourceContractTests
{
    internal static readonly Guid Try = Guid.Parse("88888888-8888-4888-8888-888888888888");

    private protected override string ExpectedKind => "finance.opening-balance.v1";

    private protected override string ExpectedPermission => ErpPermissions.FinanceOpeningBalancesApprove;

    private protected override IWorkInboxSourceV1 Create(FinanceDbContext db, FinanceWorkInboxFence fence) =>
        new FinanceOpeningBalanceWorkInboxSourceV1(db, fence);

    private protected override Guid AddPending(FinanceDbContext db, Guid company, DateTimeOffset at, string maker)
    {
        var row = Opening(company, at, maker, FinanceOpeningBalanceService.Proposed);
        db.OpeningBalances.Add(row);
        return row.Id;
    }

    private protected override void AddNotPending(FinanceDbContext db, Guid company, DateTimeOffset at) =>
        db.OpeningBalances.AddRange(
            Opening(company, at, Maker, FinanceOpeningBalanceService.Approved),
            Opening(company, at, Maker, FinanceOpeningBalanceService.Rejected),
            Opening(company, at, Maker, FinanceOpeningBalanceService.Reversed));

    private protected override void AddInvalid(FinanceDbContext db, Guid company, DateTimeOffset at)
    {
        var row = Opening(company, at, Maker, FinanceOpeningBalanceService.Proposed);
        row.TargetKind = 9;
        db.OpeningBalances.Add(row);
    }

    [Fact]
    public async Task The_rule_carries_the_stored_amount_and_the_target_reference_follows_the_target_kind()
    {
        var cash = Opening(Company, Start, Maker, FinanceOpeningBalanceService.Proposed, "1000.000");
        var party = Opening(Company, Start.AddMinutes(1), Maker, FinanceOpeningBalanceService.Proposed, "5",
            FinanceOpeningBalanceService.PartyAccountKind);

        var result = await ListAsync(db => db.OpeningBalances.AddRange(cash, party));

        var items = result.Items!.ToDictionary(item => item.ItemId);
        Assert.Equal(
            [new WorkApprovalRuleResultV1(FinanceOpeningBalanceWorkInboxSourceV1.ApprovalRequiredRule, null, "1000", null, null)],
            items[cash.Id].Rules);
        Assert.Equal("/finans/acilis-bakiyeleri", items[cash.Id].Link);
        Assert.Contains(items[cash.Id].References, reference => reference.Role == "cash-account" && reference.RecordId == cash.TargetId);
        Assert.Contains(items[cash.Id].References, reference => reference.Role == "currency" && reference.RecordId == Try);
        Assert.Contains(items[party.Id].References, reference => reference.Role == "company-account" && reference.RecordId == party.TargetId);
        Assert.Contains(items[party.Id].References, reference => reference.Role == "party" && reference.RecordId == party.PartyId);
    }

    internal static OpeningBalanceRow Opening(Guid company, DateTimeOffset at, string proposedBy, int status,
        string amount = "100", int targetKind = FinanceOpeningBalanceService.CashAccountKind) =>
        new()
        {
            Id = Guid.NewGuid(),
            CompanyId = company,
            TargetKind = targetKind,
            TargetId = Guid.NewGuid(),
            PartyId = targetKind == FinanceOpeningBalanceService.PartyAccountKind ? Guid.NewGuid() : null,
            CurrencyId = Try,
            Direction = targetKind == FinanceOpeningBalanceService.PartyAccountKind
                ? FinanceOpeningBalanceService.DebitDirection
                : null,
            AmountText = amount,
            AsOfDate = new DateOnly(2026, 1, 1),
            Status = status,
            OpeningPeriodVersion = "1",
            ProposedBy = proposedBy,
            ProposedAtUtc = at,
            DecidedBy = status == FinanceOpeningBalanceService.Proposed ? null : Caller,
            DecidedAtUtc = status == FinanceOpeningBalanceService.Proposed ? null : at,
            Version = 1,
            UpdatedAtUtc = at,
        };
}
