using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.WorkInbox;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// W1-A (AI-D31, AI-D32): the Work inbox source over Finance's proposed bank credit limits, on SYNTHETIC rows. Only
/// Proposed limits are listed; the proposer is the maker; the rule carries the stored proposed amount.
/// </summary>
public sealed class FinanceBankCreditLimitWorkInboxSourceV1Tests : FinanceWorkInboxSourceContractTests
{
    private static readonly Guid Try = Guid.Parse("88888888-8888-4888-8888-888888888888");

    private protected override string ExpectedKind => "finance.bank-credit-limit.v1";

    private protected override string ExpectedPermission => ErpPermissions.FinanceCreditsApprove;

    private protected override IWorkInboxSourceV1 Create(FinanceDbContext db, FinanceWorkInboxFence fence) =>
        new FinanceBankCreditLimitWorkInboxSourceV1(db, fence);

    private protected override Guid AddPending(FinanceDbContext db, Guid company, DateTimeOffset at, string maker)
    {
        var row = Limit(company, at, maker, FinanceCreditAccountService.Proposed);
        db.CreditLimits.Add(row);
        return row.Id;
    }

    private protected override void AddNotPending(FinanceDbContext db, Guid company, DateTimeOffset at) =>
        db.CreditLimits.AddRange(
            Limit(company, at, Maker, FinanceCreditAccountService.Approved),
            Limit(company, at, Maker, FinanceCreditAccountService.Rejected));

    private protected override void AddInvalid(FinanceDbContext db, Guid company, DateTimeOffset at)
    {
        var row = Limit(company, at, Maker, FinanceCreditAccountService.Proposed);
        row.CreditAccountId = Guid.Empty;
        db.CreditLimits.Add(row);
    }

    [Fact]
    public async Task The_rule_carries_the_stored_proposed_amount_and_the_credit_records_are_references()
    {
        var limit = Limit(Company, Start, Maker, FinanceCreditAccountService.Proposed);

        var result = await ListAsync(db => db.CreditLimits.Add(limit));

        var item = Assert.Single(result.Items!);
        Assert.Equal(limit.Id.ToString("D")[..8], item.DocumentLabel);
        Assert.Equal("/finans/krediler", item.Link);
        Assert.Equal(
            [new WorkApprovalRuleResultV1(FinanceBankCreditLimitWorkInboxSourceV1.ApprovalRequiredRule, null, "250000.5", null, null)],
            item.Rules);
        Assert.Contains(item.References, reference => reference.Role == "credit-account" && reference.RecordId == limit.CreditAccountId);
        Assert.Contains(item.References, reference => reference.Role == "lender-party" && reference.RecordId == limit.LenderPartyId);
        Assert.Contains(item.References, reference => reference.Role == "company-account"
            && reference.RecordId == limit.LenderCompanyPartyAccountId);
        Assert.Contains(item.References, reference => reference.Role == "currency" && reference.RecordId == Try);
    }

    private static FinanceCreditLimitRow Limit(Guid company, DateTimeOffset at, string proposedBy, int status) =>
        new()
        {
            Id = Guid.NewGuid(),
            CompanyId = company,
            CreditAccountId = Guid.NewGuid(),
            LenderPartyId = Guid.NewGuid(),
            LenderCompanyPartyAccountId = Guid.NewGuid(),
            CreditGroup = FinanceCreditAccountService.CashGroup,
            CurrencyId = Try,
            Version = 1,
            Revision = 1,
            Status = status,
            AmountText = "250000.50",
            SourceJson = "{}",
            ProposedBy = proposedBy,
            ProposedAtUtc = at,
            DecidedBy = status == FinanceCreditAccountService.Proposed ? null : Caller,
            DecidedAtUtc = status == FinanceCreditAccountService.Proposed ? null : at,
        };
}
