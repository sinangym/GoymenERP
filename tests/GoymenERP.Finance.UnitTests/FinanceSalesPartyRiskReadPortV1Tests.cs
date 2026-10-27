using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// MOD-01: Finance's answer to the Sales-declared risk-input port over SYNTHETIC open-item rows. Open receivables are the
/// outstanding amounts of the customer account's OPEN items, summed per currency exactly; closed items and other customers,
/// accounts or companies never enter. A row Finance cannot read makes the figure unavailable (never a partial sum). The credit
/// limit and collateral have no master on main: "NoSource", never zero. The read writes nothing.
/// </summary>
public sealed class FinanceSalesPartyRiskReadPortV1Tests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid OtherCompany = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Party = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly Guid Account = Guid.Parse("77777777-7777-4777-8777-777777777777");
    private static readonly Guid Try = Guid.Parse("88888888-8888-4888-8888-888888888888");
    private static readonly Guid Usd = Guid.Parse("99999999-9999-4999-8999-999999999999");
    private static readonly Guid Eur = Guid.Parse("aaaaaaaa-9999-4999-8999-999999999999");

    [Fact]
    public async Task Open_receivables_are_the_outstanding_amounts_of_the_accounts_open_items_per_currency_and_limit_and_collateral_have_no_source()
    {
        await using var run = await Run.CreateAsync(
            Row("125.50", "75.25"),
            Row("10", "10.00"),
            Row("300.00", "0.00", FinanceOpenItemStatuses.Closed),
            Row("40.00", "40.00", currency: Usd),
            Row("999.00", "999.00", party: Guid.NewGuid()),
            Row("999.00", "999.00", account: Guid.NewGuid()),
            Row("999.00", "999.00", company: OtherCompany));

        var answer = await run.Port(Company).ReadPartyRiskAsync(Party, Account);

        Assert.Equal((Company, Party, Account), (answer.CompanyId, answer.PartyId, answer.CompanyAccountId));
        Assert.Equal((SalesPartyRiskFigureStateV1.Available, FinanceSalesPartyRiskReadPortV1.OpenItemsSource, (bool?)true, (string?)null),
            (answer.OpenReceivables.State, answer.OpenReceivables.Source, answer.OpenReceivables.IncludesTax, answer.OpenReceivables.ReasonCode));
        // 75.25 + 10.00 in TRY (exact, never rounded); USD apart, never converted; ordered by currency identity.
        Assert.Equal([new SalesPartyRiskAmountV1(Try, 85.25m), new SalesPartyRiskAmountV1(Usd, 40m)], answer.OpenReceivables.Amounts);
        Assert.Equal((SalesPartyRiskFigureStateV1.NoSource, FinanceSalesPartyRiskReadPortV1.CreditLimitSource, (bool?)null,
                FinanceSalesPartyRiskReadPortV1.CreditLimitOwnerMissing),
            (answer.CreditLimit.State, answer.CreditLimit.Source, answer.CreditLimit.IncludesTax, answer.CreditLimit.ReasonCode));
        Assert.Empty(answer.CreditLimit.Amounts);
        Assert.Equal((SalesPartyRiskFigureStateV1.NoSource, FinanceSalesPartyRiskReadPortV1.CollateralSource,
                FinanceSalesPartyRiskReadPortV1.CollateralOwnerMissing),
            (answer.Collateral.State, answer.Collateral.Source, answer.Collateral.ReasonCode));
        Assert.Empty(answer.Collateral.Amounts);
        Assert.False(run.Db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task Unallocated_customer_payments_are_deducted_per_currency_down_to_zero_and_reversed_ones_never()
    {
        var reversedReceipt = Guid.NewGuid();
        var pendingReversal = Guid.NewGuid();
        var allocatedCheque = Guid.NewGuid();
        await using var run = await Run.CreateAsync(
            [Row("125.50", "75.25"), Row("10", "10.00"), Row("40.00", "40.00", currency: Usd)],
            payments:
            [
                // A recorded bank receipt with no allocation yet: wholly unallocated.
                Payment(Guid.NewGuid(), "30.00"),
                // A COLLECTED cheque, 15.00 of it allocated to an invoice: 5.00 left.
                Payment(allocatedCheque, "20.00", MoneyEventKinds.CustomerInstrumentCollected),
                // A receipt with an APPROVED reversal: not a payment any more (its reversing event is not one either).
                Payment(reversedReceipt, "50.00"),
                Payment(Guid.NewGuid(), "50.00", MoneyEventKinds.ManualBankReceiptReversed),
                // A pending reversal proposal undoes nothing yet: still a payment.
                Payment(pendingReversal, "1.00"),
                // More USD paid than USD owed: the USD receivable stops at zero, never negative, never offset against TRY.
                Payment(Guid.NewGuid(), "100.00", currency: Usd),
                // A currency the customer owes nothing in: deducted from nothing.
                Payment(Guid.NewGuid(), "10.00", currency: Eur),
                // Another customer's and another company's payments never reach this one.
                Payment(Guid.NewGuid(), "500.00", party: Guid.NewGuid()),
                Payment(Guid.NewGuid(), "500.00", company: OtherCompany),
            ],
            states: [State(allocatedCheque, "20.00", "5.00"), State(reversedReceipt, "50.00", "0.00")],
            reversals: [Reversal(reversedReceipt, approved: true), Reversal(pendingReversal, approved: false)]);

        var answer = await run.Port(Company).ReadPartyRiskAsync(Party, Account);

        // TRY: 85.25 owed − (30.00 + 5.00 + 1.00) unallocated = 49.25. USD: 40.00 − 100.00 → 0 (not below).
        Assert.Equal(SalesPartyRiskFigureStateV1.Available, answer.OpenReceivables.State);
        Assert.Equal([new SalesPartyRiskAmountV1(Try, 49.25m), new SalesPartyRiskAmountV1(Usd, 0m)], answer.OpenReceivables.Amounts);
        Assert.False(run.Db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task An_unreadable_payment_or_allocation_state_makes_the_receivables_unavailable_never_a_wrong_deduction()
    {
        var source = Guid.NewGuid();
        foreach (var (payment, state) in new (MoneyEventRow, FinanceSourceAllocationStateRow?)[]
                 {
                     (Payment(source, "-1.00"), null),
                     (Payment(source, "abc"), null),
                     (Payment(source, "20.00"), State(source, "20.00", "25.00")),
                     (Payment(source, "20.00"), State(source, "19.00", "5.00")),
                     (Payment(source, "20.00"), State(source, "20.00", "5.00", currency: Usd)),
                     (Payment(source, "20.00"), State(source, "20.00", "x")),
                 })
        {
            await using var run = await Run.CreateAsync([Row("125.50", "75.25")], payments: [payment],
                states: state is null ? [] : [state]);

            var answer = await run.Port(Company).ReadPartyRiskAsync(Party, Account);

            Assert.Equal((SalesPartyRiskFigureStateV1.Unavailable, FinanceSalesPartyRiskReadPortV1.OpenItemUnreadable),
                (answer.OpenReceivables.State, answer.OpenReceivables.ReasonCode));
        }
    }

    [Fact]
    public async Task Another_company_sees_none_of_the_items_and_a_customer_without_open_items_is_a_recorded_zero()
    {
        await using var run = await Run.CreateAsync(Row("125.50", "75.25"));

        var foreign = await run.Port(OtherCompany).ReadPartyRiskAsync(Party, Account);
        Assert.Equal((OtherCompany, SalesPartyRiskFigureStateV1.Available), (foreign.CompanyId, foreign.OpenReceivables.State));
        Assert.Empty(foreign.OpenReceivables.Amounts);

        var unknown = await run.Port(Company).ReadPartyRiskAsync(Guid.NewGuid(), Account);
        Assert.Empty(unknown.OpenReceivables.Amounts);
        var empty = await run.Port(Company).ReadPartyRiskAsync(Guid.Empty, Guid.Empty);
        Assert.Equal(SalesPartyRiskFigureStateV1.Available, empty.OpenReceivables.State);
        Assert.Empty(empty.OpenReceivables.Amounts);
    }

    [Theory]
    [InlineData("-1.00", "official.invoice-installment.v1")]
    [InlineData("1,00", "official.invoice-installment.v1")]
    [InlineData(" 1.00", "official.invoice-installment.v1")]
    [InlineData("1.00", "synthetic.opening-balance.v1")]
    public async Task A_row_finance_cannot_read_makes_the_receivables_unavailable_never_a_partial_sum(string outstanding, string sourceKind)
    {
        var broken = Row("5.00", "5.00");
        broken.OutstandingAmountText = outstanding;
        broken.SourceKind = sourceKind;
        await using var run = await Run.CreateAsync(Row("125.50", "75.25"), broken);

        var answer = await run.Port(Company).ReadPartyRiskAsync(Party, Account);

        Assert.Equal((SalesPartyRiskFigureStateV1.Unavailable, FinanceSalesPartyRiskReadPortV1.OpenItemUnreadable),
            (answer.OpenReceivables.State, answer.OpenReceivables.ReasonCode));
        Assert.Empty(answer.OpenReceivables.Amounts);
    }

    [Fact]
    public async Task A_sum_an_exact_decimal_cannot_hold_is_unavailable_never_rounded()
    {
        // Finance keeps amounts without a digit limit: two of them can exceed System.Decimal.
        await using var run = await Run.CreateAsync(
            Row("79228162514264337593543950335", "79228162514264337593543950335"),
            Row("1", "1"));

        var answer = await run.Port(Company).ReadPartyRiskAsync(Party, Account);
        Assert.Equal(SalesPartyRiskFigureStateV1.Unavailable, answer.OpenReceivables.State);

        await using var precise = await Run.CreateAsync(Row("0.12345678901234567890123456789", "0.12345678901234567890123456789"));
        Assert.Equal(SalesPartyRiskFigureStateV1.Unavailable,
            (await precise.Port(Company).ReadPartyRiskAsync(Party, Account)).OpenReceivables.State);
    }

    [Fact]
    public async Task Without_a_resolved_company_nothing_is_read()
    {
        await using var run = await Run.CreateAsync(Row("1", "1"));
        var unresolved = new CompanyContext();
        var port = new FinanceSalesPartyRiskReadPortV1(run.Context(unresolved), unresolved);

        await Assert.ThrowsAsync<InvalidOperationException>(() => port.ReadPartyRiskAsync(Party, Account));
    }

    private static FinanceOpenItemRow Row(string original, string outstanding, int status = FinanceOpenItemStatuses.Open,
        Guid? currency = null, Guid? party = null, Guid? account = null, Guid? company = null) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = company ?? Company,
        SourceKind = "official.invoice-installment.v1",
        SourceId = Guid.NewGuid(),
        SourceLineId = Guid.NewGuid(),
        SourceVersion = "invoice:v1",
        SourceBatchId = Guid.NewGuid(),
        SourceBatchVersion = "batch:v1",
        SourceBatchChecksum = new string('a', 64),
        PartyId = party ?? Party,
        CompanyAccountId = account ?? Account,
        CurrencyId = currency ?? Try,
        OriginalAmountText = original,
        OutstandingAmountText = outstanding,
        DueOn = new DateOnly(2026, 11, 1),
        Status = status,
        Version = 1,
        CreatedBy = "SENTETIK-seed",
        CreatedAtUtc = new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero),
        UpdatedBy = "SENTETIK-seed",
        UpdatedAtUtc = new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero),
    };

    private static MoneyEventRow Payment(Guid sourceId, string amount, int kind = MoneyEventKinds.ManualBankReceiptRecorded,
        Guid? currency = null, Guid? party = null, Guid? company = null) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = company ?? Company,
        SourceId = sourceId,
        Kind = kind,
        PartyId = party ?? Party,
        CompanyAccountId = Account,
        TreasuryAccountId = Guid.NewGuid(),
        CurrencyId = currency ?? Try,
        AmountText = amount,
        ProfileId = "SENTETIK-profile",
        ProfileVersion = "1",
        OccurredOn = new DateOnly(2026, 10, 27),
        RecordedAtUtc = new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero),
        RecordedBy = "SENTETIK-seed",
        CorrelationId = "SENTETIK-correlation",
    };

    private static FinanceSourceAllocationStateRow State(Guid sourceId, string original, string unallocated, Guid? currency = null) => new()
    {
        CompanyId = Company,
        SourceId = sourceId,
        CurrencyId = currency ?? Try,
        OriginalAmountText = original,
        UnallocatedAmountText = unallocated,
        Version = 2,
        UpdatedBy = "SENTETIK-seed",
        UpdatedAtUtc = new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero),
    };

    private static ReversalProposalRow Reversal(Guid originalSourceId, bool approved) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = Company,
        OriginalSourceId = originalSourceId,
        OccurredOn = new DateOnly(2026, 10, 27),
        Reason = "SENTETIK ters kayıt",
        Status = approved ? ReversalProposalStatuses.Approved : ReversalProposalStatuses.Pending,
        ProposedBy = "SENTETIK-maker",
        ProposedAtUtc = new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero),
        DecidedBy = approved ? "SENTETIK-checker" : null,
        DecidedAtUtc = approved ? new DateTimeOffset(2026, 10, 27, 10, 0, 0, TimeSpan.Zero) : null,
        ReversalSourceId = approved ? Guid.NewGuid() : null,
        Version = approved ? 2 : 1,
    };

    private sealed class Run : IAsyncDisposable
    {
        private readonly string _database = $"party-risk-{Guid.NewGuid():N}";
        public FinanceDbContext Db { get; private set; } = null!;

        public static Task<Run> CreateAsync(params FinanceOpenItemRow[] rows) => CreateAsync(rows, [], [], []);

        public static async Task<Run> CreateAsync(FinanceOpenItemRow[] rows, MoneyEventRow[] payments,
            FinanceSourceAllocationStateRow[]? states = null, ReversalProposalRow[]? reversals = null)
        {
            var run = new Run();
            var companies = rows.Select(row => row.CompanyId).Concat(payments.Select(row => row.CompanyId)).Append(Company).Distinct();
            foreach (var company in companies)
            {
                var seed = new CompanyContext();
                seed.Set(company);
                await using var db = run.Context(seed);
                db.OpenItems.AddRange(rows.Where(row => row.CompanyId == company));
                db.MoneyEvents.AddRange(payments.Where(row => row.CompanyId == company));
                db.SourceAllocationStates.AddRange((states ?? []).Where(row => row.CompanyId == company));
                db.ReversalProposals.AddRange((reversals ?? []).Where(row => row.CompanyId == company));
                await db.SaveChangesAsync();
            }

            return run;
        }

        public FinanceSalesPartyRiskReadPortV1 Port(Guid company)
        {
            var context = new CompanyContext();
            context.Set(company);
            Db = Context(context);
            return new FinanceSalesPartyRiskReadPortV1(Db, context);
        }

        public FinanceDbContext Context(ICompanyContext company) => new(
            new DbContextOptionsBuilder<FinanceDbContext>().UseInMemoryDatabase(_database).Options, company);

        public async ValueTask DisposeAsync()
        {
            if (Db is not null)
            {
                await Db.DisposeAsync();
            }
        }
    }
}
