using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.WorkInbox;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// W1-A (AI-D31, AI-D32): the Work inbox source over Finance's proposed bank accounts (API-30, Ö4), on SYNTHETIC rows.
/// Only Proposed cards are listed; the proposer is the maker; the rule has no numbers and the IBAN never leaves Finance.
/// </summary>
public sealed class FinanceBankAccountWorkInboxSourceV1Tests : FinanceWorkInboxSourceContractTests
{
    internal static readonly Guid Try = Guid.Parse("88888888-8888-4888-8888-888888888888");

    private protected override string ExpectedKind => "finance.bank-account.v1";

    private protected override string ExpectedPermission => ErpPermissions.FinanceBankAccountApprove;

    private protected override IWorkInboxSourceV1 Create(FinanceDbContext db, FinanceWorkInboxFence fence) =>
        new FinanceBankAccountWorkInboxSourceV1(db, fence);

    private protected override Guid AddPending(FinanceDbContext db, Guid company, DateTimeOffset at, string maker)
    {
        var row = Account(company, at, maker, FinanceBankAccountService.Proposed);
        db.BankAccounts.Add(row);
        return row.Id;
    }

    private protected override void AddNotPending(FinanceDbContext db, Guid company, DateTimeOffset at) =>
        db.BankAccounts.Add(Account(company, at, Maker, FinanceBankAccountService.Approved));

    private protected override void AddInvalid(FinanceDbContext db, Guid company, DateTimeOffset at)
    {
        var row = Account(company, at, Maker, FinanceBankAccountService.Proposed);
        row.CurrencyId = Guid.Empty;
        db.BankAccounts.Add(row);
    }

    [Fact]
    public async Task The_rule_has_no_numbers_the_account_is_a_reference_and_the_iban_is_not_carried()
    {
        var account = Account(Company, Start, Maker, FinanceBankAccountService.Proposed);

        var result = await ListAsync(db => db.BankAccounts.Add(account));

        var item = Assert.Single(result.Items!);
        Assert.Equal(account.Id.ToString("D")[..8], item.DocumentLabel);
        Assert.Equal("/finans/banka-hesaplari", item.Link);
        Assert.Equal([new WorkApprovalRuleResultV1(FinanceBankAccountWorkInboxSourceV1.ApprovalRequiredRule, null, null, null, null)],
            item.Rules);
        Assert.Contains(item.References, reference => reference.Role == "bank-account" && reference.RecordId == account.Id
            && reference.Label == account.DisplayName);
        Assert.Contains(item.References, reference => reference.Role == "currency" && reference.RecordId == Try);
        Assert.DoesNotContain(item.References, reference => reference.Label.Contains(account.Iban!, StringComparison.Ordinal));
    }

    internal static BankAccountRow Account(Guid company, DateTimeOffset at, string proposedBy, int status) =>
        new()
        {
            Id = Guid.NewGuid(),
            CompanyId = company,
            CurrencyId = Try,
            DisplayName = "SENTETIK Banka " + Guid.NewGuid().ToString("N")[..4],
            BankName = "SENTETIK Banka",
            AccountType = "SENTETIK-vadesiz",
            Iban = "TR000000000000000000000001",
            MoneyMovementEligible = true,
            Status = status,
            Version = 1,
            ProposedBy = proposedBy,
            ProposedAtUtc = at,
            ApprovedBy = status == FinanceBankAccountService.Approved ? Caller : null,
            ApprovedAtUtc = status == FinanceBankAccountService.Approved ? at : null,
            UpdatedAtUtc = at,
        };
}
