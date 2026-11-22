using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.WorkInbox;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// W1-A (AI-D31, AI-D32): the Work inbox source over Finance's proposed credit movements, on SYNTHETIC rows. Only
/// Proposed movements are listed; the proposer is the maker; the rule names the kind and carries only the stored bank
/// amount.
/// </summary>
public sealed class FinanceCreditMovementWorkInboxSourceV1Tests : FinanceWorkInboxSourceContractTests
{
    private static readonly Guid Try = Guid.Parse("88888888-8888-4888-8888-888888888888");

    private protected override string ExpectedKind => "finance.credit-movement.v1";

    private protected override string ExpectedPermission => ErpPermissions.FinanceCreditsApprove;

    private protected override IWorkInboxSourceV1 Create(FinanceDbContext db, FinanceWorkInboxFence fence) =>
        new FinanceCreditMovementWorkInboxSourceV1(db, fence);

    private protected override Guid AddPending(FinanceDbContext db, Guid company, DateTimeOffset at, string maker)
    {
        var row = Movement(company, at, maker, CreditMovementStatusV1.Proposed, CreditMovementKindV1.Drawdown);
        db.CreditMovements.Add(row);
        return row.Id;
    }

    private protected override void AddNotPending(FinanceDbContext db, Guid company, DateTimeOffset at) =>
        db.CreditMovements.AddRange(
            Movement(company, at, Maker, CreditMovementStatusV1.Approved, CreditMovementKindV1.Drawdown),
            Movement(company, at, Maker, CreditMovementStatusV1.Rejected, CreditMovementKindV1.Repayment));

    private protected override void AddInvalid(FinanceDbContext db, Guid company, DateTimeOffset at)
    {
        var row = Movement(company, at, Maker, CreditMovementStatusV1.Proposed, CreditMovementKindV1.Drawdown);
        row.Kind = 99;
        db.CreditMovements.Add(row);
    }

    [Fact]
    public async Task The_rule_names_the_kind_and_carries_only_the_stored_bank_amount()
    {
        var bank = Guid.NewGuid();
        var repayment = Movement(Company, Start, Maker, CreditMovementStatusV1.Proposed, CreditMovementKindV1.Repayment,
            bank, "1200.00");
        var interest = Movement(Company, Start.AddMinutes(1), Maker, CreditMovementStatusV1.Proposed,
            CreditMovementKindV1.InterestAccrual);
        var opening = Movement(Company, Start.AddMinutes(2), Maker, CreditMovementStatusV1.Proposed,
            CreditMovementKindV1.OpeningBalance);

        var result = await ListAsync(db => db.CreditMovements.AddRange(repayment, interest, opening));

        var items = result.Items!.ToDictionary(item => item.ItemId);
        Assert.Equal([new WorkApprovalRuleResultV1(FinanceCreditMovementWorkInboxSourceV1.RepaymentRule, null, "1200", null, null)],
            items[repayment.Id].Rules);
        Assert.Equal([new WorkApprovalRuleResultV1(FinanceCreditMovementWorkInboxSourceV1.InterestAccrualRule, null, null, null, null)],
            items[interest.Id].Rules);
        Assert.Equal([new WorkApprovalRuleResultV1(FinanceCreditMovementWorkInboxSourceV1.OpeningBalanceRule, null, null, null, null)],
            items[opening.Id].Rules);
        Assert.Contains(items[repayment.Id].References, reference => reference.Role == "bank-account" && reference.RecordId == bank);
        Assert.Contains(items[repayment.Id].References, reference => reference.Role == "credit-account"
            && reference.RecordId == repayment.CreditAccountId);
        Assert.Contains(items[repayment.Id].References, reference => reference.Role == "currency" && reference.RecordId == Try);
        Assert.Equal("/finans/krediler", items[repayment.Id].Link);
    }

    private static FinanceCreditMovementRow Movement(Guid company, DateTimeOffset at, string proposedBy,
        CreditMovementStatusV1 status, CreditMovementKindV1 kind, Guid? bank = null, string? bankAmount = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            CompanyId = company,
            CreditAccountId = Guid.NewGuid(),
            CurrencyId = Try,
            Version = 1,
            Status = (int)status,
            Kind = (int)kind,
            SourceJson = "{}",
            SourceIdentityHash = new string('b', 64),
            MoneyProfileJson = "{}",
            BankAccountId = bank,
            BankAmountText = bankAmount,
            ProposedBy = proposedBy,
            ProposedAtUtc = at,
            DecidedBy = status == CreditMovementStatusV1.Proposed ? null : Caller,
            DecidedAtUtc = status == CreditMovementStatusV1.Proposed ? null : at,
        };
}
