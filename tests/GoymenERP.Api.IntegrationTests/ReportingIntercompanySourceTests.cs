using GoymenERP.Api.ReportingRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Reporting.Domain;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>SENTETIK. WP14 (YOL-D31): the host join of Counterparty group-company links with Finance sub-ledger items.</summary>
public sealed class ReportingIntercompanySourceTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Sister = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid SisterParty = Guid.Parse("e0000000-0000-4000-8000-000000000001");
    private static readonly Guid SelfParty = Guid.Parse("e0000000-0000-4000-8000-000000000002");
    private static readonly Guid Outsider = Guid.Parse("e0000000-0000-4000-8000-000000000003");
    private static readonly Guid Currency = Guid.Parse("e2000000-0000-4000-8000-000000000001");

    [Fact]
    public async Task Only_items_of_parties_linked_to_another_group_company_in_the_period_are_legs()
    {
        var finance = new FakeOpenItems(
            Item(SisterParty, new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero), "120.5"),
            Item(Outsider, new DateTimeOffset(2026, 11, 19, 9, 0, 0, TimeSpan.Zero), "9"),
            Item(SelfParty, new DateTimeOffset(2026, 11, 18, 9, 0, 0, TimeSpan.Zero), "8"),
            // 22:00 UTC on Oct 31 is Nov 1 in Istanbul: inside the period.
            Item(SisterParty, new DateTimeOffset(2026, 10, 31, 22, 0, 0, TimeSpan.Zero), "7"),
            Item(SisterParty, new DateTimeOffset(2026, 10, 30, 9, 0, 0, TimeSpan.Zero), "6"));
        var source = Source(finance, new CounterpartyGroupCompanyLinkV1(SisterParty, Sister), new CounterpartyGroupCompanyLinkV1(SelfParty, Company));
        var page = await source.ReadAsync(Company, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), 1, 50);
        Assert.Equal(ReportingSourceOutcomeV1.Found, page.Outcome);
        Assert.Equal(new[] { (new DateOnly(2026, 11, 1), "7"), (new DateOnly(2026, 11, 20), "120.5") },
            page.Rows.Select(row => (row.DocumentDate, row.InitialAmount)).ToArray());
        Assert.All(page.Rows, row => Assert.Equal((Sister, "receivable", "Open"), (row.CounterpartyCompanyId, row.Direction, row.Status)));
        Assert.False(page.WindowFull);
    }

    [Fact]
    public async Task A_redacted_amount_is_denied_and_no_link_answers_an_empty_found_dump()
    {
        var redacted = Item(SisterParty, new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero), "1") with
        {
            InitialAmount = new AmountView(AmountDisclosure.Redacted, null),
        };
        var denied = await Source(new FakeOpenItems(redacted), new CounterpartyGroupCompanyLinkV1(SisterParty, Sister))
            .ReadAsync(Company, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), 1, 50);
        Assert.Equal(ReportingSourceOutcomeV1.Denied, denied.Outcome);
        var none = await Source(new FakeOpenItems()).ReadAsync(Company, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), 1, 50);
        Assert.Equal((ReportingSourceOutcomeV1.Found, 0), (none.Outcome, none.Rows.Count));
    }

    private static FinanceOpenItemViewV1 Item(Guid party, DateTimeOffset createdAt, string amount) => new(Guid.NewGuid(), Company, "sales.invoice",
        Guid.NewGuid(), Guid.NewGuid(), "1", party, Guid.NewGuid(), Currency, "1", null, FinanceOpenItemStatusV1.Open, createdAt,
        new AmountView(AmountDisclosure.Visible, new MoneyText(amount, Currency)), new AmountView(AmountDisclosure.Visible, new MoneyText(amount, Currency)),
        FinanceOpenItemDirectionV1.Receivable);

    private static ReportingCounterpartyFinanceIntercompanySourceV1 Source(FakeOpenItems finance, params CounterpartyGroupCompanyLinkV1[] links)
    {
        var context = new CompanyContext();
        context.Set(Company);
        return new(new FakeLinks(links), finance, context, new User(), new Correlation());
    }

    private sealed class FakeLinks(CounterpartyGroupCompanyLinkV1[] links) : ICounterpartyGroupCompanyLinkReadPortV1
    {
        public Task<IReadOnlyList<CounterpartyGroupCompanyLinkV1>> ListLinksAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CounterpartyGroupCompanyLinkV1>>(links);
    }

    private sealed class FakeOpenItems(params FinanceOpenItemViewV1[] items) : IFinanceOpenItemReadServiceV1
    {
        public Task<ReadResult<FinanceOpenItemPageV1>> ListAuthorizedAsync(FinanceContext context, SourceListRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ReadResult<FinanceOpenItemPageV1>(ReadOutcome.Found, new FinanceOpenItemPageV1(Company, items, null)));

        public Task<ReadResult<FinanceOpenItemViewV1>> DetailAuthorizedAsync(FinanceContext context, Guid openItemId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class User : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => "a0000000-0000-4000-8000-000000000001";
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }

    private sealed class Correlation : ICorrelationContext
    {
        public bool IsResolved => true;
        public string CorrelationId => "corr";
        public string? CausationId => null;
        public string SourceChannel => "api";
        public string? ClientIpAddress => null;
        public string? UserAgent => null;
        public void Set(string correlationId, string? causationId, string sourceChannel, string? clientIpAddress, string? userAgent) { }
    }
}
