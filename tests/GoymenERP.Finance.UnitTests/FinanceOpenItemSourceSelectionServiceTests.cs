using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;

namespace GoymenERP.Finance.UnitTests;

public sealed class FinanceOpenItemSourceSelectionServiceTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private const string Actor = "finance-import-user";

    [Fact]
    public async Task Approved_company_batch_is_listed_after_fresh_batch_authority()
    {
        var batch = Batch(Company);
        var authority = new Authority(PermissionDecision.Allowed);
        var service = Service(new Catalog(new(ReadOutcome.Found,
            new ApprovedFinanceOpenItemSourcePage(Company, [batch], null))), authority);

        var result = await service.ListApprovedAsync(Context(), null, CancellationToken.None);

        Assert.Equal(ReadOutcome.Found, result.Outcome);
        Assert.Same(batch, Assert.Single(result.Resource!.Items));
        Assert.Equal([batch.BatchId], authority.BatchIds);
    }

    [Fact]
    public async Task Cross_company_or_malformed_provider_batch_fails_closed()
    {
        var otherCompany = Guid.NewGuid();
        var crossCompany = Batch(otherCompany);
        var malformed = Batch(Company) with { BatchChecksum = "not-a-checksum" };

        var crossResult = await Service(new Catalog(new(ReadOutcome.Found,
            new ApprovedFinanceOpenItemSourcePage(Company, [crossCompany], null))),
            new Authority(PermissionDecision.Allowed))
            .ListApprovedAsync(Context(), null, CancellationToken.None);
        var malformedResult = await Service(new Catalog(new(ReadOutcome.Found,
            new ApprovedFinanceOpenItemSourcePage(Company, [malformed], null))),
            new Authority(PermissionDecision.Allowed))
            .ListApprovedAsync(Context(), null, CancellationToken.None);

        Assert.Equal(ReadOutcome.Unavailable, crossResult.Outcome);
        Assert.Equal(ReadOutcome.Unavailable, malformedResult.Outcome);
        Assert.Null(crossResult.Resource);
        Assert.Null(malformedResult.Resource);
    }

    [Fact]
    public async Task Authority_revoked_after_catalog_read_discloses_no_source()
    {
        var batch = Batch(Company);
        var result = await Service(new Catalog(new(ReadOutcome.Found,
            new ApprovedFinanceOpenItemSourcePage(Company, [batch], null))),
            new Authority(PermissionDecision.Denied))
            .ListApprovedAsync(Context(), null, CancellationToken.None);

        Assert.Equal(ReadOutcome.Denied, result.Outcome);
        Assert.Null(result.Resource);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" cursor")]
    public async Task Noncanonical_cursor_is_refused_before_the_provider(string cursor)
    {
        var catalog = new Catalog(new(ReadOutcome.Found,
            new ApprovedFinanceOpenItemSourcePage(Company, [Batch(Company)], null)));

        var result = await Service(catalog, new Authority(PermissionDecision.Allowed))
            .ListApprovedAsync(Context(), cursor, CancellationToken.None);

        Assert.Equal(ReadOutcome.Denied, result.Outcome);
        Assert.Equal(0, catalog.Calls);
    }

    private static FinanceOpenItemSourceSelectionService Service(
        IApprovedFinanceOpenItemSourceCatalogPort catalog,
        IFinanceOpenItemSourceAuthority authority) =>
        new(new CompanyContext(Company), new User(Actor, Company), authority, catalog);

    private static FinanceContext Context() => new(Company, Actor, "correlation", null);

    private static ApprovedFinanceOpenItemSourceBatch Batch(Guid company) =>
        new(Guid.NewGuid(), company, "official-2026-09-v1", new string('a', 64),
        [
            new ApprovedFinanceOpenItemSourceRow(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "invoice-v1",
                FinanceOpenItemSourceKinds.OfficialInvoiceInstallmentDocument,
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "1250.50", new DateOnly(2026, 10, 31)),
        ]);

    private sealed class Catalog(ReadResult<ApprovedFinanceOpenItemSourcePage> result)
        : IApprovedFinanceOpenItemSourceCatalogPort
    {
        public int Calls { get; private set; }

        public Task<ReadResult<ApprovedFinanceOpenItemSourcePage>> ListApprovedAsync(
            FinanceContext context, string? cursor, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    private sealed class Authority(PermissionDecision decision) : IFinanceOpenItemSourceAuthority
    {
        public List<Guid> BatchIds { get; } = [];

        public Task<PermissionDecision> CheckAsync(
            FinanceContext context, Guid batchId, CancellationToken cancellationToken)
        {
            BatchIds.Add(batchId);
            return Task.FromResult(decision);
        }
    }

    private sealed class CompanyContext(Guid company) : ICompanyContext
    {
        public Guid CompanyId => company;
        public bool IsResolved => true;
        public void Set(Guid companyId) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
    }

    private sealed class User(string actor, Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => actor;
        public IReadOnlyCollection<Guid> CompanyIds => [company];
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid companyId) => companyId == company;
    }
}
