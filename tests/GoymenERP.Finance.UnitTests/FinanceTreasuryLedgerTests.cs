using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Finance.UnitTests;

/// <summary>SENTETIK / NOT_ENV ledger golden cases. InMemory proves no SQL lock or transaction property.</summary>
public sealed class FinanceTreasuryLedgerTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Currency = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Bank = Guid.Parse("33333333-3333-4333-8333-333333333333");

    [Fact]
    public async Task Internal_bank_ledger_counts_each_recorded_fact_once_uses_POS_net_and_ignores_drafts_and_dates()
    {
        var company = Resolved(Company);
        await using var db = Context(company);
        db.BankAccounts.Add(new BankAccountRow { Id = Bank, CompanyId = Company, CurrencyId = Currency,
            DisplayName = "SENTETIK banka", Status = 2, Version = 2 });
        var historicalOpening = Guid.NewGuid();
        db.OpeningBalanceMovements.AddRange(Opening(1, "1000"), Opening(1, "100", historicalOpening), Opening(2, "100", historicalOpening));
        db.MoneyEvents.AddRange(Receipt(1, "200", "190"), Receipt(1, "50", "45"), Receipt(2, "50", "45"), Receipt(3, "30"));
        db.OutgoingMoneyEvents.AddRange(Outgoing("100"), Outgoing("40"));
        db.OutgoingMoneyEventReversals.Add(new OutgoingMoneyEventReversalRow { Id = Guid.NewGuid(), CompanyId = Company,
            TreasuryAccountId = Bank, CurrencyId = Currency, AmountText = "40" });
        db.ExpensePayments.AddRange(Expense(2, "50"), Expense(0, "999999"), Expense(1, "999999"));
        db.CashTransfers.AddRange(Transfer(outgoing: true, "70"), Transfer(outgoing: false, "25"));
        await db.SaveChangesAsync();

        var actual = await new FinanceTreasuryLedger(db, company)
            .ReadAsync(Company, ExpensePaymentSourceKind.Bank, Bank, Currency, CancellationToken.None);

        Assert.NotNull(actual);
        Assert.True(actual.Covers(Parse("1025.0000000000000000000000000000000000000")));
        Assert.False(actual.Covers(Parse("1025.0000000000000000000000000000000000001")));
        Assert.False(actual.IsZero);
    }

    [Fact]
    public async Task Ledger_rejects_wrong_company_account_currency_and_unknown_immutable_money_kind()
    {
        var company = Resolved(Company);
        await using var db = Context(company);
        db.BankAccounts.Add(new BankAccountRow { Id = Bank, CompanyId = Company, CurrencyId = Currency,
            DisplayName = "SENTETIK banka", Status = 2, Version = 2 });
        await db.SaveChangesAsync();
        var reader = new FinanceTreasuryLedger(db, company);
        Assert.Null(await reader.ReadAsync(Guid.NewGuid(), ExpensePaymentSourceKind.Bank, Bank, Currency, CancellationToken.None));
        Assert.Null(await reader.ReadAsync(Company, ExpensePaymentSourceKind.Bank, Bank, Guid.NewGuid(), CancellationToken.None));
        Assert.Null(await reader.ReadAsync(Company, ExpensePaymentSourceKind.Bank, Guid.NewGuid(), Currency, CancellationToken.None));
        db.MoneyEvents.Add(Receipt(99, "1"));
        await db.SaveChangesAsync();
        Assert.Null(await reader.ReadAsync(Company, ExpensePaymentSourceKind.Bank, Bank, Currency, CancellationToken.None));
    }

    [Fact]
    public async Task Dated_bank_balance_uses_the_actual_credit_transaction_day_and_keeps_the_accounting_day_separate()
    {
        var company = Resolved(Company);
        await using var db = Context(company);
        db.BankAccounts.Add(new BankAccountRow { Id = Bank, CompanyId = Company, CurrencyId = Currency,
            DisplayName = "SENTETIK dated bank", Status = 2, Version = 2 });
        // These are isolated immutable-reader fixtures, not permission to propose different
        // accounting dates through a command that has its own source validation rules.
        var boundary = new DateOnly(2026, 11, 30);
        var drawdown = Credit(CreditMovementKindV1.Drawdown, "100.01", boundary, boundary.AddDays(2));
        var repayment = Credit(CreditMovementKindV1.Repayment, "0.01", boundary.AddDays(-1), boundary.AddDays(-1));
        var future = Credit(CreditMovementKindV1.Drawdown, "999", boundary.AddDays(1), boundary.AddDays(-1));
        var proposed = Credit(CreditMovementKindV1.Drawdown, "888", boundary, boundary); proposed.Status = 1;
        db.CreditMovements.AddRange(drawdown, repayment, future, proposed);
        await db.SaveChangesAsync();
        var originalSource = drawdown.SourceJson;
        var ledger = new FinanceTreasuryLedger(db, company);

        var dated = Assert.IsType<FinanceTreasuryBalance>(await ledger.ReadAsync(Company,
            ExpensePaymentSourceKind.Bank, Bank, Currency, default, boundary));
        Assert.Equal(("100.01", "0.01"), (dated.Incoming.Text, dated.Outgoing.Text));
        var current = Assert.IsType<FinanceTreasuryBalance>(await ledger.ReadAsync(Company,
            ExpensePaymentSourceKind.Bank, Bank, Currency, default));
        Assert.Equal(("1099.01", "0.01"), (current.Incoming.Text, current.Outgoing.Text));
        Assert.Equal(originalSource, drawdown.SourceJson);
        Assert.Empty(db.MoneyEvents); Assert.Empty(db.OutgoingMoneyEvents); Assert.Empty(db.OutboxMessages);
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("missing-date")]
    [InlineData("wrong-credit")]
    [InlineData("wrong-bank")]
    [InlineData("wrong-amount")]
    [InlineData("wrong-kind")]
    public async Task Dated_bank_balance_refuses_missing_or_mismatched_actual_credit_source(string defect)
    {
        var company = Resolved(Company);
        await using var db = Context(company);
        db.BankAccounts.Add(new BankAccountRow { Id = Bank, CompanyId = Company, CurrencyId = Currency,
            DisplayName = "SENTETIK source bank", Status = 2, Version = 2 });
        var date = new DateOnly(2026, 11, 30);
        var row = Credit(CreditMovementKindV1.Drawdown, "100", date, date);
        var source = JsonSerializer.Deserialize<CreditMovementProposeV1>(row.SourceJson, FinanceJson.CreateOptions())!;
        source = defect switch
        {
            "missing-date" => source with { TransactionDate = default },
            "wrong-credit" => source with { CreditAccountId = Guid.NewGuid() },
            "wrong-bank" => source with { BankEffect = source.BankEffect! with { BankAccountId = Guid.NewGuid() } },
            "wrong-amount" => source with { BankEffect = source.BankEffect! with { Amount = "101" } },
            "wrong-kind" => source with { Kind = CreditMovementKindV1.Repayment },
            _ => source
        };
        row.SourceJson = defect == "malformed" ? "{" : JsonSerializer.Serialize(source, FinanceJson.CreateOptions());
        db.CreditMovements.Add(row); await db.SaveChangesAsync();

        Assert.Null(await new FinanceTreasuryLedger(db, company).ReadAsync(Company,
            ExpensePaymentSourceKind.Bank, Bank, Currency, default, date));
    }

    [Fact]
    public async Task Cash_opening_is_not_a_zero_balance_even_when_no_expense_or_transfer_has_ever_existed()
    {
        var company = Resolved(Company);
        await using var db = Context(company);
        db.CashAccounts.Add(new CashAccountRow { Id = Bank, CompanyId = Company, CurrencyId = Currency,
            DisplayName = "SENTETIK kasa", Status = 2, Version = 2 });
        var opening = Opening(1, "75.25");
        opening.TargetKind = FinanceOpeningBalanceService.CashAccountKind;
        db.OpeningBalanceMovements.Add(opening);
        await db.SaveChangesAsync();
        var balance = await new FinanceTreasuryLedger(db, company)
            .ReadAsync(Company, ExpensePaymentSourceKind.Cash, Bank, Currency, CancellationToken.None);
        Assert.NotNull(balance);
        Assert.False(balance.IsZero);
        Assert.True(balance.Covers(Parse("75.25")));
        Assert.False(balance.Covers(Parse("75.2501")));
    }

    [Fact]
    public void Exact_ledger_does_not_round_a_fractional_cent_or_treat_negative_balance_as_zero()
    {
        var balance = Assert.IsType<FinanceTreasuryBalance>(FinanceTreasuryBalance.TryCreate(["0.1", "0.2"], ["0.2999999999999999999999999999999999999"]));
        Assert.True(balance.Covers(Parse("0.0000000000000000000000000000000000001")));
        Assert.False(balance.Covers(Parse("0.0000000000000000000000000000000000002")));
        Assert.False(balance.IsZero);
        var negative = Assert.IsType<FinanceTreasuryBalance>(FinanceTreasuryBalance.TryCreate(["1"], ["1.01"]));
        Assert.False(negative.IsZero);
        Assert.False(negative.Covers(Parse("0")));
        Assert.True(Assert.IsType<FinanceTreasuryBalance>(FinanceTreasuryBalance.TryCreate(["1.00"], ["1"])).IsZero);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1,00")]
    [InlineData("1e2")]
    [InlineData(" 1")]
    [InlineData("1.2.3")]
    public void Malformed_ledger_amount_is_unavailable_instead_of_a_zero_balance(string invalid)
    {
        Assert.Null(FinanceTreasuryBalance.TryCreate([invalid], []));
        Assert.Null(FinanceTreasuryBalance.TryCreate([], [invalid]));
    }

    [Fact]
    public async Task Real_guard_and_passivation_reader_refuse_an_unprotected_or_synthetic_transaction()
    {
        var company = Resolved(Company);
        await using var db = Context(company);
        var result = await new InternalLedgerTreasuryBalanceGuard(db, company).CheckOutgoingAsync(
            new FinanceContext(Company, "SENTETIK", "SENTETIK", null), ExpensePaymentSourceKind.Bank,
            Bank, new MoneyText("1", Currency), CancellationToken.None);
        Assert.Equal(TreasuryBalanceOutcome.Unavailable, result.Outcome);
        Assert.Equal(CashAccountBalanceState.Unavailable,
            await new TreasuryMovementAwareCashAccountBalanceReader(db, company).ReadAsync(Company, Bank, CancellationToken.None));
    }

    private static FinanceExactDecimalText Parse(string value)
    {
        Assert.True(FinanceExactDecimalText.TryParseNonNegative(value, out var result));
        return result;
    }

    private static CompanyContext Resolved(Guid id) { var context = new CompanyContext(); context.Set(id); return context; }
    private static FinanceDbContext Context(CompanyContext company) => new(
        new DbContextOptionsBuilder<FinanceDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options, company);
    private static FinanceCreditMovementRow Credit(CreditMovementKindV1 kind, string amount, DateOnly actualDate, DateOnly accountingDate)
    {
        var credit = Guid.NewGuid();
        var source = new CreditMovementProposeV1(credit, "2", kind, new("SENTETIK bank source", actualDate, "1"),
            actualDate, accountingDate, [new("principal", amount, null)], new(Bank, amount, "SENTETIK actual bank leg"), null, null, "SENTETIK source");
        return new() { Id = Guid.NewGuid(), CompanyId = Company, CreditAccountId = credit, CurrencyId = Currency,
            Status = 2, Version = 2, Kind = (int)kind, BankAccountId = Bank, BankAmountText = amount,
            SourceJson = JsonSerializer.Serialize(source, FinanceJson.CreateOptions()), SourceIdentityHash = new string('A', 64),
            MoneyProfileJson = "{}", ProposedBy = "SENTETIK maker", ProposedAtUtc = DateTimeOffset.UtcNow,
            DecidedBy = "SENTETIK checker", DecidedAtUtc = DateTimeOffset.UtcNow };
    }
    private static OpeningBalanceMovementRow Opening(int kind, string amount, Guid? openingId = null) => new()
    {
        Id = Guid.NewGuid(), CompanyId = Company, OpeningBalanceId = openingId ?? Guid.NewGuid(), Kind = kind,
        TargetKind = FinanceOpeningBalanceService.BankAccountKind, TargetId = Bank, CurrencyId = Currency,
        AmountText = amount, RecordedBy = "SENTETIK", OccurredOn = new DateOnly(2026, 1, 1),
    };
    private static MoneyEventRow Receipt(int kind, string amount, string? net = null) => new()
    {
        Id = Guid.NewGuid(), CompanyId = Company, SourceId = Guid.NewGuid(), Kind = kind,
        TreasuryAccountId = Bank, CurrencyId = Currency, AmountText = amount, PosBankNetAmountText = net,
        RecordedBy = "SENTETIK", OccurredOn = new DateOnly(2027, 1, 1),
    };
    private static OutgoingMoneyEventRow Outgoing(string amount) => new()
    {
        Id = Guid.NewGuid(), CompanyId = Company, TreasuryAccountId = Bank,
        CurrencyId = Currency, AmountText = amount,
    };
    private static ExpensePaymentRow Expense(int status, string amount) => new()
    {
        Id = Guid.NewGuid(), CompanyId = Company, Status = status, SourceKind = ExpensePaymentSourceKinds.Bank,
        TreasuryAccountId = Bank, CurrencyId = Currency, AmountText = amount,
    };
    private static CashTransferRow Transfer(bool outgoing, string amount) => new()
    {
        Id = Guid.NewGuid(), CompanyId = Company,
        SourceKind = CashTransferAccountKinds.Bank, SourceAccountId = outgoing ? Bank : Guid.NewGuid(),
        SourceCurrencyId = Currency, SourceAmountText = amount,
        TargetKind = CashTransferAccountKinds.Bank, TargetAccountId = outgoing ? Guid.NewGuid() : Bank,
        TargetCurrencyId = Currency, TargetAmountText = amount,
    };
}

/// <summary>Explicitly synthetic balance seam for existing isolated policy fixtures. Not a usable balance provider.</summary>
internal sealed class SyntheticSufficientTreasuryBalanceGuard : IFinanceTreasuryBalanceGuard
{
    public Task<TreasuryBalanceAnswer> CheckOutgoingAsync(FinanceContext context, ExpensePaymentSourceKind sourceKind,
        Guid treasuryAccountId, MoneyText money, CancellationToken cancellationToken) =>
        Task.FromResult(new TreasuryBalanceAnswer(TreasuryBalanceOutcome.Sufficient, null));
}
