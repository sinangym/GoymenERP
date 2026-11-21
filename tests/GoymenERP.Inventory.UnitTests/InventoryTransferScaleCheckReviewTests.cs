using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>
/// LOJ-D20, without a database: the review list of dispatches whose source scale setting could not be read is fenced like
/// the transfer read (401, 403, company-wide <c>inventory.balances.read</c> measured before and after), validates the page
/// size, maps a reader failure or a foreign page to 503, and the reader refuses to run without a resolved company. Which
/// dispatches it lists on real rows is proven on SQL Server (<c>InventorySqlServerRelationalAcceptanceTests</c>).
/// SENTETIK / NOT_ENV.
/// </summary>
public sealed class InventoryTransferScaleCheckReviewTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Other = Guid.Parse("10000000-0000-0000-0000-000000000002");

    [Fact]
    public async Task An_authorized_read_measures_the_balances_read_name_before_and_after_and_passes_the_default_limit()
    {
        var (service, _, _, authority, reader) = Service();

        var page = await service.ListNotCheckedAsync(null);

        Assert.Equal((Company, 1, InventoryTransferScaleCheckReviewApiV1.DefaultLimit), (page.CompanyId, page.Items.Count, reader.Limit));
        Assert.Equal(InventoryTransferScaleCheckStatusV1.NotChecked, Assert.Single(page.Items).ScaleCheckStatus);
        Assert.Equal(new[] { ErpPermissions.InventoryBalancesRead, ErpPermissions.InventoryBalancesRead }, authority.Measured);
    }

    [Fact]
    public async Task An_anonymous_caller_is_401_and_nothing_is_read()
    {
        var (service, _, user, _, reader) = Service();
        user.IsAuthenticated = false;

        var refusal = await Assert.ThrowsAsync<InventoryTransferException>(() => service.ListNotCheckedAsync(10));

        Assert.Equal((401, InventoryTransferCodesV1.Unauthenticated, 0), (refusal.Status, refusal.Code, reader.Calls));
    }

    [Fact]
    public async Task An_inaccessible_company_a_missing_grant_or_a_site_scoped_grant_is_403_and_nothing_is_read()
    {
        var (inaccessible, context, _, _, inaccessibleReader) = Service();
        context.CompanyId = Other;
        var (ungranted, _, _, denied, ungrantedReader) = Service();
        denied.Granted = false;
        var (scoped, _, _, siteOnly, scopedReader) = Service();
        siteOnly.CompanyWide = false;

        foreach (var service in new[] { inaccessible, ungranted, scoped })
        {
            var refusal = await Assert.ThrowsAsync<InventoryTransferException>(() => service.ListNotCheckedAsync(10));
            Assert.Equal((403, InventoryTransferCodesV1.Forbidden), (refusal.Status, refusal.Code));
        }

        Assert.Equal((0, 0, 0), (inaccessibleReader.Calls, ungrantedReader.Calls, scopedReader.Calls));
    }

    [Fact]
    public async Task A_grant_lost_during_the_read_is_403_and_the_page_is_not_returned()
    {
        var (service, _, _, authority, reader) = Service();
        reader.During = () => authority.Granted = false;

        var refusal = await Assert.ThrowsAsync<InventoryTransferException>(() => service.ListNotCheckedAsync(10));

        Assert.Equal((403, 1), (refusal.Status, reader.Calls));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(InventoryTransferScaleCheckReviewApiV1.MaximumLimit + 1)]
    public async Task A_limit_outside_one_to_the_maximum_is_400_validation_failed_and_nothing_is_read(int limit)
    {
        var (service, _, _, _, reader) = Service();

        var refusal = await Assert.ThrowsAsync<InventoryTransferException>(() => service.ListNotCheckedAsync(limit));

        Assert.Equal((400, InventoryTransferCodesV1.ValidationFailed, 0), (refusal.Status, refusal.Code, reader.Calls));
    }

    [Fact]
    public async Task The_maximum_limit_is_accepted_and_passed_through()
    {
        var (service, _, _, _, reader) = Service();

        await service.ListNotCheckedAsync(InventoryTransferScaleCheckReviewApiV1.MaximumLimit);

        Assert.Equal(InventoryTransferScaleCheckReviewApiV1.MaximumLimit, reader.Limit);
    }

    [Fact]
    public async Task A_reader_failure_is_503_unavailable_and_a_page_of_another_company_is_503_too()
    {
        var (failing, _, _, _, failingReader) = Service();
        failingReader.Failure = new InvalidOperationException("read failed");
        var (foreign, _, _, _, foreignReader) = Service();
        foreignReader.PageCompanyId = Other;

        var failed = await Assert.ThrowsAsync<InventoryTransferException>(() => failing.ListNotCheckedAsync(10));
        var mismatched = await Assert.ThrowsAsync<InventoryTransferException>(() => foreign.ListNotCheckedAsync(10));

        Assert.Equal((503, InventoryTransferCodesV1.Unavailable), (failed.Status, failed.Code));
        Assert.Equal((503, InventoryTransferCodesV1.Unavailable), (mismatched.Status, mismatched.Code));
    }

    [Fact]
    public async Task The_reader_refuses_without_a_resolved_company_before_any_connection_and_checks_its_limit()
    {
        var unresolved = new CompanyContext();
        await using var context = new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, unresolved);
        var reader = new InventoryTransferScaleCheckReviewReader(context, unresolved);

        // The connection string points nowhere: reaching the database would be a SqlException, not these refusals.
        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ListNotCheckedAsync(10));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => reader.ListNotCheckedAsync(0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => reader.ListNotCheckedAsync(InventoryTransferScaleCheckReviewApiV1.MaximumLimit + 1));
    }

    [Fact]
    public void Owner_registration_adds_the_review_reader_and_query_and_no_grant()
    {
        var services = new ServiceCollection().AddInventoryTransfers();

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IInventoryTransferScaleCheckReviewReaderV1)
            && descriptor.ImplementationType == typeof(InventoryTransferScaleCheckReviewReader)
            && descriptor.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IInventoryTransferScaleCheckReviewQueryV1)
            && descriptor.ImplementationType == typeof(InventoryTransferScaleCheckReviewQueryService)
            && descriptor.Lifetime == ServiceLifetime.Scoped);
        Assert.Equal(("/api/v1/inventory/transfers/scale-check-review", 50, 200, "NotChecked"),
            (InventoryTransferScaleCheckReviewApiV1.Path, InventoryTransferScaleCheckReviewApiV1.DefaultLimit,
                InventoryTransferScaleCheckReviewApiV1.MaximumLimit, InventoryTransferScaleCheckStatusV1.NotChecked));
    }

    private static (InventoryTransferScaleCheckReviewQueryService Service, TestContext Context, TestUser User,
        TestAuthority Authority, TestReader Reader) Service()
    {
        var context = new TestContext();
        var user = new TestUser();
        var authority = new TestAuthority();
        var reader = new TestReader();
        return (new InventoryTransferScaleCheckReviewQueryService(context, user, authority, reader), context, user, authority,
            reader);
    }

    private sealed class TestContext : ICompanyContext
    {
        public bool IsResolved => CompanyId != Guid.Empty;
        public Guid CompanyId { get; set; } = Company;
        public void Set(Guid companyId) => CompanyId = companyId;
    }

    private sealed class TestUser : ICurrentUser
    {
        public bool IsAuthenticated { get; set; } = true;
        public string? UserId { get; set; } = "actor";
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }

    private sealed class TestAuthority : IErpAuthorityRevalidationService
    {
        public bool Granted { get; set; } = true;
        public bool CompanyWide { get; set; } = true;
        public List<string> Measured { get; } = [];

        public Task<PermissionScope> RevalidatePermissionScopeAsync(
            string permission,
            Guid companyId,
            CancellationToken cancellationToken = default)
        {
            Measured.Add(permission);
            return Task.FromResult(new PermissionScope(Granted, CompanyWide, new HashSet<string>(StringComparer.Ordinal),
                new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
        }
    }

    private sealed class TestReader : IInventoryTransferScaleCheckReviewReaderV1
    {
        public int Calls { get; private set; }
        public int? Limit { get; private set; }
        public Action? During { get; set; }
        public Exception? Failure { get; set; }
        public Guid PageCompanyId { get; set; } = Company;

        public Task<InventoryTransferScaleCheckReviewPageV1> ListNotCheckedAsync(int limit,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Limit = limit;
            During?.Invoke();
            if (Failure is not null)
            {
                throw Failure;
            }

            var item = new InventoryTransferScaleCheckReviewItemV1(Guid.NewGuid(), "3", "InTransit", Guid.NewGuid(),
                Guid.NewGuid(), new DateTimeOffset(2026, 11, 15, 9, 0, 0, TimeSpan.Zero),
                InventoryTransferScaleCheckStatusV1.NotChecked);
            return Task.FromResult(new InventoryTransferScaleCheckReviewPageV1(PageCompanyId, [item], false));
        }
    }
}
