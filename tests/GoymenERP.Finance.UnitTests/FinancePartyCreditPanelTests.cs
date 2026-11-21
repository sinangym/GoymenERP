using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Finance.UnitTests;

/// <summary>
/// FIN-S3 (F018 plan §L3; FNS-D27 (2), FNS-D31 (4), FNS-D58) without a database: the read behind the Finance panel of the
/// Cari card. The panel opens under the credit limit read or the collateral read; each figure answers only under its own
/// authority and is otherwise withheld without an amount. The credit limit and the open receivables are the Sales risk
/// port's own figures; the held and weighted collateral, the utilization and the documentary risk are the collateral
/// calculator's. Under the default Settings catalog (no adapter) the weighted collateral and the utilization cannot be
/// calculated and say so. Gross risk and limit headroom are not answered. SENTETIK / NOT_ENV.
/// </summary>
public sealed class FinancePartyCreditPanelTests
{
    private static readonly Guid CompanyA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CompanyB = Guid.Parse("12121212-1212-1212-1212-121212121212");
    private static readonly Guid Lira = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Dollar = Guid.Parse("23232323-2323-2323-2323-232323232323");
    private static readonly Guid Party = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid OtherParty = Guid.Parse("34343434-3434-3434-3434-343434343434");
    private static readonly Guid Account = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid Guarantee = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly DateTimeOffset At = new(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 15);
    private const string Reader = "SENTETIK-panel-reader";

    private static readonly FinanceAction[] Everything =
    [
        FinanceAction.ReadCreditLimits, FinanceAction.ReadCollaterals, FinanceAction.ReadOpenItems,
        FinanceAction.ReadOpenItemAmount, FinanceAction.ReadCustomerInstruments, FinanceAction.ReadCustomerInstrumentAmount,
    ];

    [Fact]
    public async Task A_reader_of_every_figure_sees_the_limit_receivables_collateral_utilization_and_documentary_risk()
    {
        await using var run = new Run();
        await run.SeedLimitAsync(Limit("250000.50"));
        await run.SeedOpenItemAsync(OpenItem("1200.00"));
        await run.SeedOpenItemAsync(OpenItem("300"));
        await run.SeedCollateralAsync(Collateral("1000.00"));
        await run.SeedInstrumentAsync(CustomerInstrumentStatus.InPortfolio, "300");
        await run.SeedInstrumentAsync(CustomerInstrumentStatus.Collected, "5000");

        var result = await run.GetAsync(new AllowOnly(Everything), Catalog("80"));

        Assert.Equal(ReadOutcome.Found, result.Outcome);
        var view = result.Resource!;
        Assert.Equal((CompanyA, Party, Account, Today), (view.CompanyId, view.PartyId, view.CompanyAccountId, view.AsOf));
        AssertAmount(view.CreditLimit, Lira, "250000.50");
        AssertAmount(view.OpenReceivables, Lira, "1500.00");
        AssertAmount(view.HeldCollateral, Lira, "1000");
        // 1000.00 x 80%: the collateral the Settings rate counts.
        AssertAmount(view.WeightedCollateral, Lira, "800");
        // FNS-D31 (4): the receivables take the counted collateral up to its whole.
        AssertAmount(view.CollateralUtilization, Lira, "800");
        // FNS-D58: a collected instrument is no longer documentary risk; the figure is a breakdown, never added.
        AssertAmount(view.DocumentaryRisk, Lira, "300");
    }

    [Fact]
    public async Task Without_the_Settings_catalog_the_weighted_collateral_and_the_utilization_cannot_be_calculated()
    {
        await using var run = new Run();
        await run.SeedOpenItemAsync(OpenItem("100"));
        await run.SeedCollateralAsync(Collateral("1000.00"));

        var view = (await run.GetAsync(new AllowOnly(Everything), new UnavailableFinanceCollateralTypeCatalogReader()))
            .Resource!;

        AssertAmount(view.HeldCollateral, Lira, "1000");
        AssertState(view.WeightedCollateral, PartyCreditPanelFigureState.Unavailable,
            FinancePartyCollateralFigures.TypeRateUnavailable);
        AssertState(view.CollateralUtilization, PartyCreditPanelFigureState.Unavailable,
            FinancePartyCollateralFigures.UtilizationSourceUnavailable);
    }

    [Fact]
    public async Task No_approved_limit_is_not_set_and_no_receivables_or_collateral_is_a_recorded_zero()
    {
        await using var run = new Run();

        var view = (await run.GetAsync(new AllowOnly(Everything), Catalog("80"))).Resource!;

        // Never zero, never unlimited.
        AssertState(view.CreditLimit, PartyCreditPanelFigureState.NotSet, FinanceSalesPartyRiskReadPortV1.CreditLimitNotSet);
        foreach (var figure in new[] { view.OpenReceivables, view.HeldCollateral, view.WeightedCollateral,
            view.CollateralUtilization, view.DocumentaryRisk })
        {
            Assert.Equal((PartyCreditPanelFigureState.Available, 0, (string?)null),
                (figure.State, figure.Amounts.Count, figure.ReasonCode));
        }
    }

    [Fact]
    public async Task A_receivable_in_one_currency_beside_collateral_in_another_is_uncovered_and_the_utilization_is_zero()
    {
        await using var run = new Run();
        await run.SeedOpenItemAsync(OpenItem("900"));
        var foreign = Collateral("20");
        foreign.CurrencyId = Dollar;
        await run.SeedCollateralAsync(foreign);

        var view = (await run.GetAsync(new AllowOnly(Everything), Catalog("100"))).Resource!;

        AssertAmount(view.OpenReceivables, Lira, "900");
        AssertAmount(view.WeightedCollateral, Dollar, "20");
        // FNS-D74: the dollar collateral is zero cover for the lira receivable, a recorded zero rather than withheld.
        Assert.Equal((PartyCreditPanelFigureState.Available, 0, (string?)null),
            (view.CollateralUtilization.State, view.CollateralUtilization.Amounts.Count,
                view.CollateralUtilization.ReasonCode));
    }

    [Fact]
    public async Task The_panel_is_dated_on_the_Istanbul_business_day_not_the_UTC_day()
    {
        // FNS-D75: 22:30 UTC on 15 November is already 16 November in Istanbul; a collateral starting on the 16th is in force.
        await using var run = new Run();
        var tomorrow = Collateral("1000");
        tomorrow.ValidFrom = Today.AddDays(1);
        await run.SeedCollateralAsync(tomorrow);

        var late = new DateTimeOffset(2026, 11, 15, 22, 30, 0, TimeSpan.Zero);
        var view = (await run.GetAsync(new AllowOnly(Everything), Catalog("100"), at: late)).Resource!;

        Assert.Equal(Today.AddDays(1), view.AsOf);
        AssertAmount(view.HeldCollateral, Lira, "1000");
        AssertAmount(view.WeightedCollateral, Lira, "1000");

        // The same instant read as a UTC date would still be the 15th, before the collateral starts.
        var morning = (await run.GetAsync(new AllowOnly(Everything), Catalog("100"))).Resource!;
        Assert.Equal(Today, morning.AsOf);
        Assert.Equal((PartyCreditPanelFigureState.Available, 0), (morning.HeldCollateral.State,
            morning.HeldCollateral.Amounts.Count));
    }

    [Fact]
    public async Task A_held_collateral_of_another_party_on_the_account_makes_both_collateral_figures_unavailable()
    {
        await using var run = new Run();
        var foreign = Collateral("500");
        foreign.PartyId = OtherParty;
        await run.SeedCollateralAsync(foreign);

        var view = (await run.GetAsync(new AllowOnly(Everything), Catalog("100"))).Resource!;

        AssertState(view.HeldCollateral, PartyCreditPanelFigureState.Unavailable,
            FinancePartyCollateralFigures.CollateralUnreadable);
        AssertState(view.WeightedCollateral, PartyCreditPanelFigureState.Unavailable,
            FinancePartyCollateralFigures.CollateralUnreadable);
        AssertState(view.CollateralUtilization, PartyCreditPanelFigureState.Unavailable,
            FinancePartyCollateralFigures.UtilizationSourceUnavailable);
    }

    [Fact]
    public async Task A_figure_the_caller_may_not_read_is_withheld_and_discloses_no_amount()
    {
        await using var run = new Run();
        await run.SeedLimitAsync(Limit("100.00"));
        await run.SeedOpenItemAsync(OpenItem("50"));
        await run.SeedCollateralAsync(Collateral("70"));
        await run.SeedInstrumentAsync(CustomerInstrumentStatus.InPortfolio, "30");

        var collateralOnly = (await run.GetAsync(new AllowOnly(FinanceAction.ReadCollaterals), Catalog("100"))).Resource!;
        AssertState(collateralOnly.CreditLimit, PartyCreditPanelFigureState.Withheld,
            FinancePartyCreditPanelService.CreditLimitWithheld);
        AssertState(collateralOnly.OpenReceivables, PartyCreditPanelFigureState.Withheld,
            FinancePartyCreditPanelService.OpenReceivablesWithheld);
        AssertState(collateralOnly.CollateralUtilization, PartyCreditPanelFigureState.Withheld,
            FinancePartyCreditPanelService.UtilizationWithheld);
        AssertState(collateralOnly.DocumentaryRisk, PartyCreditPanelFigureState.Withheld,
            FinancePartyCollateralFigures.DocumentaryRiskWithheld);
        AssertAmount(collateralOnly.HeldCollateral, Lira, "70");

        var limitOnly = (await run.GetAsync(new AllowOnly(FinanceAction.ReadCreditLimits), Catalog("100"))).Resource!;
        AssertAmount(limitOnly.CreditLimit, Lira, "100.00");
        foreach (var figure in new[] { limitOnly.HeldCollateral, limitOnly.WeightedCollateral })
        {
            AssertState(figure, PartyCreditPanelFigureState.Withheld, FinancePartyCreditPanelService.CollateralWithheld);
        }

        AssertState(limitOnly.DocumentaryRisk, PartyCreditPanelFigureState.Withheld,
            FinancePartyCollateralFigures.DocumentaryRiskWithheld);

        // The open item read without its amount read withholds the receivables (and so the utilization).
        var noAmount = (await run.GetAsync(new AllowOnly(FinanceAction.ReadCollaterals, FinanceAction.ReadOpenItems),
            Catalog("100"))).Resource!;
        AssertState(noAmount.OpenReceivables, PartyCreditPanelFigureState.Withheld,
            FinancePartyCreditPanelService.OpenReceivablesWithheld);
        AssertState(noAmount.CollateralUtilization, PartyCreditPanelFigureState.Withheld,
            FinancePartyCreditPanelService.UtilizationWithheld);
    }

    [Theory]
    [InlineData(PermissionDecision.Denied, PermissionDecision.Denied, ReadOutcome.Denied)]
    [InlineData(PermissionDecision.Unavailable, PermissionDecision.Denied, ReadOutcome.Unavailable)]
    [InlineData(PermissionDecision.Denied, PermissionDecision.Unavailable, ReadOutcome.Unavailable)]
    [InlineData(PermissionDecision.Unavailable, PermissionDecision.Unavailable, ReadOutcome.Unavailable)]
    public async Task Without_the_credit_limit_or_the_collateral_read_the_panel_does_not_open(PermissionDecision limit,
        PermissionDecision collateral, ReadOutcome expected)
    {
        await using var run = new Run();
        await run.SeedLimitAsync(Limit("100.00"));

        var result = await run.GetAsync(new PanelDecisions(limit, collateral), Catalog("100"), new NoPartyRead());

        Assert.Equal((expected, (PartyCreditPanelView?)null), (result.Outcome, result.Resource));
    }

    [Theory]
    [InlineData(ReadOutcome.NotFound, ReadOutcome.NotFound)]
    [InlineData(ReadOutcome.Denied, ReadOutcome.Denied)]
    [InlineData(ReadOutcome.Unavailable, ReadOutcome.Unavailable)]
    public async Task The_party_account_must_be_one_Counterparty_answers_for_the_caller(ReadOutcome party, ReadOutcome expected)
    {
        await using var run = new Run();

        var result = await run.GetAsync(new AllowOnly(Everything), Catalog("100"), new FixedParty(party));

        Assert.Equal((expected, (PartyCreditPanelView?)null), (result.Outcome, result.Resource));
    }

    [Fact]
    public async Task An_empty_id_or_an_account_held_for_another_party_is_not_found()
    {
        await using var run = new Run();

        Assert.Equal(ReadOutcome.NotFound, (await run.GetAsync(new AllowOnly(Everything), Catalog("100"),
            new NoPartyRead(), party: Guid.Empty)).Outcome);
        Assert.Equal(ReadOutcome.NotFound, (await run.GetAsync(new AllowOnly(Everything), Catalog("100"),
            new NoPartyRead(), account: Guid.Empty)).Outcome);
        Assert.Equal(ReadOutcome.NotFound, (await run.GetAsync(new AllowOnly(Everything), Catalog("100"),
            new FixedParty(ReadOutcome.Found, OtherParty))).Outcome);
    }

    [Fact]
    public async Task Another_company_never_sees_the_figures_and_a_request_for_a_company_not_the_callers_is_denied()
    {
        await using var run = new Run();
        await run.SeedLimitAsync(Limit("100.00"));
        await run.SeedOpenItemAsync(OpenItem("50"));
        await run.SeedCollateralAsync(Collateral("70"));

        var other = (await run.GetAsync(new AllowOnly(Everything), Catalog("100"), company: CompanyB)).Resource!;
        Assert.Equal(CompanyB, other.CompanyId);
        Assert.Equal(PartyCreditPanelFigureState.NotSet, other.CreditLimit.State);
        Assert.Empty(other.OpenReceivables.Amounts);
        Assert.Empty(other.HeldCollateral.Amounts);

        // The request names a company the resolved context and the caller do not share.
        var foreign = await run.GetAsync(new AllowOnly(Everything), Catalog("100"), new NoPartyRead(),
            requestCompany: CompanyB);
        Assert.Equal(ReadOutcome.Denied, foreign.Outcome);
    }

    private static void AssertAmount(PartyCreditPanelFigureView figure, Guid currency, string amount)
    {
        Assert.Equal((PartyCreditPanelFigureState.Available, (string?)null), (figure.State, figure.ReasonCode));
        var single = Assert.Single(figure.Amounts);
        Assert.Equal((currency, amount), (single.CurrencyId, single.Amount));
    }

    private static void AssertState(PartyCreditPanelFigureView figure, PartyCreditPanelFigureState state, string reason)
    {
        Assert.Equal((state, 0, reason), (figure.State, figure.Amounts.Count, figure.ReasonCode));
    }

    private static FixedCatalog Catalog(string rate) =>
        new(new FinanceCollateralTypeCatalogAnswer(FinanceCollateralTypeCatalogState.Available,
            [new FinanceCollateralTypeCatalogItem(Guarantee, "SENTETIK-TUR", "SENTETIK teminat türü", IsActive: true, rate,
                "7", IsNegotiableInstrument: false)]));

    private static FinanceOpenItemRow OpenItem(string outstanding) => new()
    {
        Direction = FinanceOpenItemDirectionCodes.Receivable,
        Id = Guid.NewGuid(),
        CompanyId = CompanyA,
        SourceKind = FinanceOpenItemSourceKinds.ApprovedOfficialInvoiceInstallment,
        SourceId = Guid.NewGuid(),
        SourceLineId = Guid.NewGuid(),
        SourceVersion = "invoice:v1",
        SourceBatchId = Guid.NewGuid(),
        SourceBatchVersion = "batch:v1",
        SourceBatchChecksum = new string('a', 64),
        PartyId = Party,
        CompanyAccountId = Account,
        CurrencyId = Lira,
        OriginalAmountText = outstanding,
        OutstandingAmountText = outstanding,
        DueOn = Today.AddDays(-14),
        Status = FinanceOpenItemStatuses.Open,
        Version = 1,
        CreatedBy = "SENTETIK-seed",
        CreatedAtUtc = At.AddDays(-19),
        UpdatedBy = "SENTETIK-seed",
        UpdatedAtUtc = At.AddDays(-19),
    };

    private static PartyCreditLimitRow Limit(string amount) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = CompanyA,
        PartyId = Party,
        CompanyAccountId = Account,
        Kind = FinancePartyCreditLimitService.SetKind,
        CurrencyId = Lira,
        AmountText = amount,
        Status = (int)PartyCreditLimitStatus.Approved + 1,
        IsDirect = false,
        BaseRevision = 0,
        AccountRevision = 1,
        Version = 2,
        ProposedBy = "SENTETIK-proposer",
        ProposedAtUtc = At.AddDays(-3),
        DecidedBy = "SENTETIK-approver",
        DecidedAtUtc = At.AddDays(-3),
        UpdatedAtUtc = At.AddDays(-3),
    };

    private static PartyCollateralRow Collateral(string amount) => new()
    {
        Id = Guid.NewGuid(), CompanyId = CompanyA, CollateralId = Guid.NewGuid(), Revision = 1,
        Direction = FinancePartyCollateralService.Received, SourceKind = FinancePartyCollateralService.ManualEntry,
        PartyId = Party, CompanyAccountId = Account, Kind = FinancePartyCollateralService.RecordKind,
        CollateralTypeId = Guarantee, CollateralTypeVersion = "7", CurrencyId = Lira, AmountText = amount,
        ValidFrom = Today.AddDays(-10), Status = FinancePartyCollateralService.Effective, Version = 1,
        RecordedBy = "SENTETIK-recorder", RecordedAtUtc = At, UpdatedAtUtc = At,
    };

    /// <summary>The real panel over one in-memory store; every call is a fresh scope like a request, on a fixed clock.</summary>
    private sealed class Run : IAsyncDisposable
    {
        private readonly string databaseName = $"finance-credit-panel-{Guid.NewGuid():N}";

        public Task SeedLimitAsync(PartyCreditLimitRow row) => SeedAsync(context => context.PartyCreditLimits.Add(row));

        public Task SeedOpenItemAsync(FinanceOpenItemRow row) => SeedAsync(context => context.OpenItems.Add(row));

        public Task SeedCollateralAsync(PartyCollateralRow row) => SeedAsync(context => context.PartyCollaterals.Add(row));

        public Task SeedInstrumentAsync(CustomerInstrumentStatus status, string amount) =>
            SeedAsync(context => context.CustomerInstruments.Add(new CustomerInstrumentRow
            {
                Id = Guid.NewGuid(), CompanyId = CompanyA, Version = 1, Kind = 1, Status = (int)status, PartyId = Party,
                CompanyAccountId = Account, SerialNumber = $"SENTETIK-{Guid.NewGuid():N}", Drawer = "SENTETIK keşideci",
                DueOn = Today.AddDays(30), ReceivedOn = Today, CurrencyId = Lira, AmountText = amount,
                ProfileId = "SENTETIK-profile", ProfileVersion = "1", StatusChangedOn = Today, ReceivedBy = Reader,
                ReceivedAtUtc = At, UpdatedBy = Reader, UpdatedAtUtc = At,
            }));

        public async Task<ReadResult<PartyCreditPanelView>> GetAsync(IFinancePermissionPort permissions,
            IFinanceCollateralTypeCatalogReader catalog, IFinancePartyAccountPort? parties = null, Guid? company = null,
            Guid? requestCompany = null, Guid? party = null, Guid? account = null, DateTimeOffset? at = null)
        {
            var companyId = company ?? CompanyA;
            await using var context = Memory(companyId);
            var companyContext = new CompanyContext();
            companyContext.Set(companyId);
            var service = new FinancePartyCreditPanelService(context, companyContext, new FixedUser(Reader, companyId),
                permissions, parties ?? new FixedParty(), catalog, new FixedClock(at ?? At));
            return await service.GetAsync(
                new FinanceContext(requestCompany ?? companyId, Reader, "SENTETIK-correlation", null),
                party ?? Party, account ?? Account, CancellationToken.None);
        }

        public async ValueTask DisposeAsync()
        {
            await using var context = Memory(CompanyA);
            await context.Database.EnsureDeletedAsync();
        }

        private async Task SeedAsync(Action<FinanceDbContext> add)
        {
            await using var context = Memory(CompanyA);
            add(context);
            await context.SaveChangesAsync();
        }

        private FinanceDbContext Memory(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            return new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
                .UseInMemoryDatabase(databaseName).Options, companyContext);
        }
    }

    private sealed class FixedClock(DateTimeOffset at) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => at;
    }

    private sealed class FixedCatalog(FinanceCollateralTypeCatalogAnswer answer) : IFinanceCollateralTypeCatalogReader
    {
        public Task<FinanceCollateralTypeCatalogAnswer> ListAsync(Guid companyId, DateOnly asOf,
            CancellationToken cancellationToken) =>
            Task.FromResult(answer);
    }

    /// <summary>Allows the named actions and denies every other.</summary>
    private sealed class AllowOnly(params FinanceAction[] allowed) : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) =>
            Task.FromResult(allowed.Contains(action) ? PermissionDecision.Allowed : PermissionDecision.Denied);
    }

    /// <summary>The two panel authorities' answers; every other action is denied.</summary>
    private sealed class PanelDecisions(PermissionDecision limit, PermissionDecision collateral) : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? sourceId,
            CancellationToken cancellationToken) =>
            Task.FromResult(action switch
            {
                FinanceAction.ReadCreditLimits => limit,
                FinanceAction.ReadCollaterals => collateral,
                _ => PermissionDecision.Denied,
            });
    }

    /// <summary>Counterparty's answer for the (party, company account) pair the request names.</summary>
    private sealed class FixedParty(ReadOutcome outcome = ReadOutcome.Found, Guid? party = null) : IFinancePartyAccountPort
    {
        public Task<ReadResult<PartyAccountEligibility>> GetAuthorizedAsync(FinanceContext context, Guid partyId,
            Guid companyAccountId, CancellationToken cancellationToken) =>
            Task.FromResult(outcome == ReadOutcome.Found
                ? new ReadResult<PartyAccountEligibility>(ReadOutcome.Found, new PartyAccountEligibility(party ?? partyId,
                    companyAccountId, context.CompanyId, IsActive: true, Label: null, IsPartyActive: true))
                : new ReadResult<PartyAccountEligibility>(outcome, null));
    }

    /// <summary>Never reached: the answer comes before Counterparty is asked.</summary>
    private sealed class NoPartyRead : IFinancePartyAccountPort
    {
        public Task<ReadResult<PartyAccountEligibility>> GetAuthorizedAsync(FinanceContext context, Guid partyId,
            Guid companyAccountId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Counterparty must not be asked before the answer.");
    }

    private sealed class FixedUser(string userId, Guid companyId) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => userId;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { companyId };
        public bool CanAccessCompany(Guid company) => AllowedCompanyIds.Contains(company);
    }
}
