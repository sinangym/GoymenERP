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

    private sealed class Run : IAsyncDisposable
    {
        private readonly string _database = $"party-risk-{Guid.NewGuid():N}";
        public FinanceDbContext Db { get; private set; } = null!;

        public static async Task<Run> CreateAsync(params FinanceOpenItemRow[] rows)
        {
            var run = new Run();
            foreach (var group in rows.GroupBy(row => row.CompanyId))
            {
                var seed = new CompanyContext();
                seed.Set(group.Key);
                await using var db = run.Context(seed);
                db.OpenItems.AddRange(group);
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
