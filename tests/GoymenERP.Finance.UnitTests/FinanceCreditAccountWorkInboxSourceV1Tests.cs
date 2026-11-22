using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.WorkInbox;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// W1-A (AI-D31, AI-D32): the Work inbox source over Finance's proposed credit accounts (FIN-SC47), on SYNTHETIC rows.
/// Only Proposed accounts are listed; the proposer is the maker; the rule carries the stored principal and the label is
/// the contract reference.
/// </summary>
public sealed class FinanceCreditAccountWorkInboxSourceV1Tests : FinanceWorkInboxSourceContractTests
{
    private static readonly Guid Try = Guid.Parse("88888888-8888-4888-8888-888888888888");

    private protected override string ExpectedKind => "finance.credit-account.v1";

    private protected override string ExpectedPermission => ErpPermissions.FinanceCreditsApprove;

    private protected override IWorkInboxSourceV1 Create(FinanceDbContext db, FinanceWorkInboxFence fence) =>
        new FinanceCreditAccountWorkInboxSourceV1(db, fence);

    private protected override Guid AddPending(FinanceDbContext db, Guid company, DateTimeOffset at, string maker)
    {
        var row = Account(company, at, maker, FinanceCreditAccountService.Proposed);
        db.CreditAccounts.Add(row);
        return row.Id;
    }

    private protected override void AddNotPending(FinanceDbContext db, Guid company, DateTimeOffset at) =>
        db.CreditAccounts.AddRange(
            Account(company, at, Maker, FinanceCreditAccountService.Approved),
            Account(company, at, Maker, FinanceCreditAccountService.Rejected));

    private protected override void AddInvalid(FinanceDbContext db, Guid company, DateTimeOffset at)
    {
        var row = Account(company, at, Maker, FinanceCreditAccountService.Proposed);
        row.PrincipalAmountText = string.Empty;
        db.CreditAccounts.Add(row);
    }

    [Fact]
    public async Task The_rule_carries_the_stored_principal_and_the_label_is_the_contract_reference()
    {
        var account = Account(Company, Start, Maker, FinanceCreditAccountService.Proposed);

        var result = await ListAsync(db => db.CreditAccounts.Add(account));

        var item = Assert.Single(result.Items!);
        Assert.Equal(account.ContractReference, item.DocumentLabel);
        Assert.Equal("/finans/krediler", item.Link);
        Assert.Equal(
            [new WorkApprovalRuleResultV1(FinanceCreditAccountWorkInboxSourceV1.ApprovalRequiredRule, null, "500000", null, null)],
            item.Rules);
        Assert.Contains(item.References, reference => reference.Role == "lender" && reference.Label == account.LenderName
            && reference.RecordId == account.LenderPartyId);
        Assert.Contains(item.References, reference => reference.Role == "company-account"
            && reference.RecordId == account.LenderCompanyPartyAccountId);
        Assert.Contains(item.References, reference => reference.Role == "currency" && reference.RecordId == Try);
    }

    private static CreditAccountRow Account(Guid company, DateTimeOffset at, string proposedBy, int status) =>
        new()
        {
            Id = Guid.NewGuid(),
            CompanyId = company,
            CreditGroup = FinanceCreditAccountService.CashGroup,
            LenderName = "SENTETIK Banka",
            ContractReference = "SENTETIK-KRD-" + Guid.NewGuid().ToString("N")[..6],
            CurrencyId = Try,
            PrincipalAmountText = "500000.00",
            StartDate = new DateOnly(2026, 11, 1),
            LenderPartyId = Guid.NewGuid(),
            LenderCompanyPartyAccountId = Guid.NewGuid(),
            Status = status,
            ProposedBy = proposedBy,
            ProposedAtUtc = at,
            DecidedBy = status == FinanceCreditAccountService.Proposed ? null : Caller,
            DecidedAtUtc = status == FinanceCreditAccountService.Proposed ? null : at,
            RejectionReason = status == FinanceCreditAccountService.Rejected ? "SENTETIK-reason" : null,
            Version = 1,
            UpdatedAtUtc = at,
        };
}
