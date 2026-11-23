using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.WorkInbox;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// W1-A (AI-D31, AI-D32): the Work inbox source over Finance's expense payments waiting for approval (FNS-D25), on
/// SYNTHETIC rows. Only Draft payments are listed; the preparer is the maker; the rule carries the stored amount and the
/// treasury reference follows the payment's source kind.
/// </summary>
public sealed class FinanceExpensePaymentWorkInboxSourceV1Tests : FinanceWorkInboxSourceContractTests
{
    private static readonly Guid Try = Guid.Parse("88888888-8888-4888-8888-888888888888");

    private protected override string ExpectedKind => "finance.expense-payment.v1";

    private protected override string ExpectedPermission => FinancePermissionNames.ExpensePaymentApprove;

    private protected override IWorkInboxSourceV1 Create(FinanceDbContext db, FinanceWorkInboxFence fence) =>
        new FinanceExpensePaymentWorkInboxSourceV1(db, fence);

    private protected override Guid AddPending(FinanceDbContext db, Guid company, DateTimeOffset at, string maker)
    {
        var row = Payment(company, at, maker, ExpensePaymentStatuses.Draft);
        db.ExpensePayments.Add(row);
        return row.Id;
    }

    private protected override void AddNotPending(FinanceDbContext db, Guid company, DateTimeOffset at) =>
        db.ExpensePayments.AddRange(
            Payment(company, at, Maker, ExpensePaymentStatuses.Approved),
            Payment(company, at, Maker, ExpensePaymentStatuses.Recorded));

    private protected override void AddInvalid(FinanceDbContext db, Guid company, DateTimeOffset at) =>
        db.ExpensePayments.Add(Payment(company, at, Maker, ExpensePaymentStatuses.Draft, sourceKind: 7));

    [Fact]
    public async Task The_rule_carries_the_stored_amount_and_the_treasury_reference_follows_the_source_kind()
    {
        var bank = Payment(Company, Start, Maker, ExpensePaymentStatuses.Draft, "300.250");
        var cash = Payment(Company, Start.AddMinutes(1), Maker, ExpensePaymentStatuses.Draft, "40",
            ExpensePaymentSourceKinds.Cash);
        var cashAccount = new CashAccountRow
        {
            Id = cash.TreasuryAccountId,
            CompanyId = Company,
            CurrencyId = Try,
            DisplayName = "SENTETIK Merkez Kasa",
            Status = 2,
            Version = 1,
            ProposedBy = "SENTETIK-proposer",
            ProposedAtUtc = Start,
            UpdatedAtUtc = Start,
        };

        var result = await ListAsync(db =>
        {
            db.ExpensePayments.AddRange(bank, cash);
            db.CashAccounts.Add(cashAccount);
        });

        var items = result.Items!.ToDictionary(item => item.ItemId);
        Assert.Equal(
            [new WorkApprovalRuleResultV1(FinanceExpensePaymentWorkInboxSourceV1.ApprovalRequiredRule, null, "300.25", null, null)],
            items[bank.Id].Rules);
        Assert.Equal("/finans/masraf-odemeleri", items[bank.Id].Link);
        Assert.Equal(bank.Id.ToString("D")[..8], items[bank.Id].DocumentLabel);
        Assert.Contains(items[bank.Id].References, reference => reference.Role == "bank-account" && reference.RecordId == bank.TreasuryAccountId);
        Assert.Contains(items[bank.Id].References, reference => reference.Role == "expense-item" && reference.RecordId == bank.ExpenseItemId);
        Assert.Contains(items[bank.Id].References, reference => reference.Role == "currency" && reference.RecordId == Try);
        Assert.Contains(items[cash.Id].References, reference => reference.Role == "cash-account"
            && reference.RecordId == cash.TreasuryAccountId && reference.Label == "SENTETIK Merkez Kasa");
    }

    [Fact]
    public async Task A_later_reviser_of_the_draft_is_an_excluded_decider_and_a_failed_save_is_not()
    {
        // FNS-D118: the owner refuses everyone whose draft save succeeded; a refused save makes no one a maker.
        var payment = Payment(Company, Start, Maker, ExpensePaymentStatuses.Draft);

        var result = await ListAsync(db =>
        {
            db.ExpensePayments.Add(payment);
            db.ExpensePaymentCommands.AddRange(
                SaveCommand(payment.Id, Maker, CommandOutcome.Succeeded),
                SaveCommand(payment.Id, "SENTETIK-reviser", CommandOutcome.Succeeded),
                SaveCommand(payment.Id, "SENTETIK-reviser", CommandOutcome.Succeeded),
                SaveCommand(payment.Id, "SENTETIK-refused", CommandOutcome.ValidationFailed));
        });

        var item = Assert.Single(result.Items!);
        Assert.Equal(Maker, item.PreparedBy);
        Assert.Equal(["SENTETIK-reviser"], item.ExcludedDeciders);
        Assert.True(item.Excludes("SENTETIK-reviser"));
        Assert.False(item.Excludes("SENTETIK-refused"));
        Assert.False(item.Excludes(Caller));
    }

    [Fact]
    public async Task An_unreadable_save_result_makes_the_source_unavailable_rather_than_a_partial_exclusion()
    {
        var payment = Payment(Company, Start, Maker, ExpensePaymentStatuses.Draft);
        var broken = SaveCommand(payment.Id, "SENTETIK-reviser", CommandOutcome.Succeeded);
        broken.ResultJson = "{\"unexpected\":true}";

        var result = await ListAnyAsync(db =>
        {
            db.ExpensePayments.Add(payment);
            db.ExpensePaymentCommands.Add(broken);
        });

        Assert.Equal(NativeApprovalQueueOutcomeV1.Unavailable, result.Outcome);
        Assert.Null(result.Items);
    }

    private static ExpensePaymentCommandRow SaveCommand(Guid paymentId, string actor, CommandOutcome outcome) =>
        new()
        {
            Id = Guid.NewGuid(),
            CompanyId = Company,
            CommandName = GoymenERP.Modules.Finance.Infrastructure.ExpensePaymentService.DraftCommand,
            IdempotencyKeyHash = Guid.NewGuid().ToString("N"),
            ActorId = actor,
            PayloadHash = "SENTETIK",
            PaymentId = paymentId,
            ResultJson = System.Text.Json.JsonSerializer.Serialize(
                new ExpensePaymentCommandResult(outcome, null, false, "SENTETIK-correlation", []), FinanceJson.CreateOptions()),
            CorrelationId = "SENTETIK-correlation",
            RecordedAtUtc = Start,
        };

    private static ExpensePaymentRow Payment(Guid company, DateTimeOffset at, string preparedBy, int status,
        string amount = "100", int sourceKind = ExpensePaymentSourceKinds.Bank) =>
        new()
        {
            Id = Guid.NewGuid(),
            CompanyId = company,
            Version = 1,
            SourceKind = sourceKind,
            TreasuryAccountId = Guid.NewGuid(),
            ExpenseItemId = Guid.NewGuid(),
            CurrencyId = Try,
            AmountText = amount,
            ProfileId = "SENTETIK-profile",
            ProfileVersion = "1",
            OccurredOn = new DateOnly(2026, 11, 20),
            Description = "SENTETIK masraf",
            Status = status,
            PreparedBy = preparedBy,
            PreparedAtUtc = at,
            ApprovedBy = status == ExpensePaymentStatuses.Draft ? null : Caller,
            ApprovedAtUtc = status == ExpensePaymentStatuses.Draft ? null : at,
        };
}
