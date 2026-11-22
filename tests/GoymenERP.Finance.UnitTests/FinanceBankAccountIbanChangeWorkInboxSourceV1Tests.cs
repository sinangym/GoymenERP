using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.WorkInbox;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// W1-A (AI-D31, AI-D32): the Work inbox source over the IBAN changes pending on Finance's approved bank accounts (D4,
/// Ö4), on SYNTHETIC rows. Only accounts carrying a pending IBAN are listed; the change's proposer is the maker (the
/// account's own proposer is not refused); neither IBAN is carried into the inbox.
/// </summary>
public sealed class FinanceBankAccountIbanChangeWorkInboxSourceV1Tests : FinanceWorkInboxSourceContractTests
{
    private const string PendingIban = "TR000000000000000000000002";

    private protected override string ExpectedKind => "finance.bank-account-iban-change.v1";

    private protected override string ExpectedPermission => ErpPermissions.FinanceBankAccountApprove;

    private protected override IWorkInboxSourceV1 Create(FinanceDbContext db, FinanceWorkInboxFence fence) =>
        new FinanceBankAccountIbanChangeWorkInboxSourceV1(db, fence);

    private protected override Guid AddPending(FinanceDbContext db, Guid company, DateTimeOffset at, string maker)
    {
        var row = Changing(company, at, maker);
        db.BankAccounts.Add(row);
        return row.Id;
    }

    private protected override void AddNotPending(FinanceDbContext db, Guid company, DateTimeOffset at) =>
        db.BankAccounts.AddRange(
            FinanceBankAccountWorkInboxSourceV1Tests.Account(company, at, Maker, FinanceBankAccountService.Approved),
            FinanceBankAccountWorkInboxSourceV1Tests.Account(company, at, Maker, FinanceBankAccountService.Proposed));

    private protected override void AddInvalid(FinanceDbContext db, Guid company, DateTimeOffset at)
    {
        var row = Changing(company, at, Maker);
        row.PendingIbanReason = null;
        db.BankAccounts.Add(row);
    }

    [Fact]
    public async Task The_change_waits_since_it_was_proposed_its_proposer_is_the_maker_and_no_iban_is_carried()
    {
        var account = Changing(Company, Start, Maker);

        var result = await ListAsync(db => db.BankAccounts.Add(account));

        var item = Assert.Single(result.Items!);
        Assert.Equal(account.Id, item.ItemId);
        Assert.Equal(Start.ToUniversalTime(), item.WaitingSinceUtc);
        Assert.Equal(Maker, item.PreparedBy);
        Assert.False(item.Excludes(account.ProposedBy));
        Assert.Equal("/finans/banka-hesaplari", item.Link);
        Assert.Equal(
            [new WorkApprovalRuleResultV1(FinanceBankAccountIbanChangeWorkInboxSourceV1.ApprovalRequiredRule, null, null, null, null)],
            item.Rules);
        Assert.Contains(item.References, reference => reference.Role == "bank-account" && reference.RecordId == account.Id);
        Assert.DoesNotContain(item.References, reference => reference.Label.Contains(PendingIban, StringComparison.Ordinal)
            || reference.Label.Contains(account.Iban!, StringComparison.Ordinal));
    }

    private static BankAccountRow Changing(Guid company, DateTimeOffset at, string changeProposedBy)
    {
        var row = FinanceBankAccountWorkInboxSourceV1Tests.Account(company, at.AddDays(-10), "SENTETIK-card-proposer",
            FinanceBankAccountService.Approved);
        row.PendingIban = PendingIban;
        row.PendingIbanProposedBy = changeProposedBy;
        row.PendingIbanProposedAtUtc = at;
        row.PendingIbanReason = "SENTETIK-reason";
        row.Version = 2;
        return row;
    }
}
