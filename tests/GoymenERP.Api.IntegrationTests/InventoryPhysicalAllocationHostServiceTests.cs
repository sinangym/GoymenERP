using GoymenERP.Api.Endpoints;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.AspNetCore.Http;

namespace GoymenERP.Api.IntegrationTests;

public sealed class InventoryPhysicalAllocationHostServiceTests
{
    private static readonly Guid CompanyA = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid CompanyB = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid OrderId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid LineId = Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly Guid ProductId = Guid.Parse("30000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task Approved_line_is_derived_and_fresh_company_wide_authority_reaches_the_writer()
    {
        var fixture = Fixture(Granted("ROLE-OLD"), Granted("ROLE-FINAL-B", "ROLE-FINAL-A"));
        var result = await fixture.Service.CreateAsync(CreateRequest(), Metadata(), CancellationToken.None);

        Assert.False(result.IsReplay);
        Assert.Equal(2, fixture.Authority.Calls.Count);
        Assert.All(fixture.Authority.Calls, call => Assert.Equal(
            (ErpPermissions.InventoryPhysicalAllocationWrite, CompanyA), call));
        Assert.Equal((ProductId, "7", OrderId, LineId),
            (fixture.Writer.CreateRequest!.ProductId, fixture.Writer.CreateRequest.ProductVersion,
                fixture.Writer.CreateRequest.SalesOrderId, fixture.Writer.CreateRequest.SalesOrderLineId));
        Assert.Equal((fixture.User.UserId, InventoryPermissionManifestV1.PhysicalAllocationWrite),
            (fixture.Writer.Metadata!.ActorId, fixture.Writer.Metadata.PermissionCode));
        Assert.Equal(["ROLE-FINAL-A", "ROLE-FINAL-B"], fixture.Writer.Metadata.RoleCodes);
    }

    [Fact]
    public async Task Revocation_after_the_sales_read_returns_403_and_never_calls_the_writer()
    {
        var fixture = Fixture(Granted(), PermissionScope.Denied);

        var exception = await Assert.ThrowsAsync<InventoryPhysicalAllocationHostException>(() =>
            fixture.Service.CreateAsync(CreateRequest(), Metadata(), CancellationToken.None));

        Assert.Equal(StatusCodes.Status403Forbidden, exception.Status);
        Assert.Equal(2, fixture.Authority.Calls.Count);
        Assert.Null(fixture.Writer.CreateRequest);
    }

    [Fact]
    public async Task Actor_change_after_the_sales_read_returns_403_before_a_second_authority_answer_is_used()
    {
        var fixture = Fixture();
        fixture.OrderLines.AfterRead = () => fixture.User.UserId = "SENTETIK-other-actor";

        var exception = await Assert.ThrowsAsync<InventoryPhysicalAllocationHostException>(() =>
            fixture.Service.CreateAsync(CreateRequest(), Metadata(), CancellationToken.None));

        Assert.Equal(StatusCodes.Status403Forbidden, exception.Status);
        Assert.Single(fixture.Authority.Calls);
        Assert.Null(fixture.Writer.CreateRequest);
    }

    [Fact]
    public async Task Company_change_after_the_sales_read_returns_403_and_never_calls_the_writer()
    {
        var fixture = Fixture();
        fixture.OrderLines.AfterRead = () => fixture.Company.CompanyId = CompanyB;

        var exception = await Assert.ThrowsAsync<InventoryPhysicalAllocationHostException>(() =>
            fixture.Service.CreateAsync(CreateRequest(), Metadata(), CancellationToken.None));

        Assert.Equal(StatusCodes.Status403Forbidden, exception.Status);
        Assert.Single(fixture.Authority.Calls);
        Assert.Null(fixture.Writer.CreateRequest);
    }

    [Fact]
    public async Task Sales_provider_failure_returns_503_and_never_calls_the_writer()
    {
        var fixture = Fixture();
        fixture.OrderLines.Exception = new InvalidOperationException("SENTETIK provider failure");

        var exception = await Assert.ThrowsAsync<InventoryPhysicalAllocationHostException>(() =>
            fixture.Service.CreateAsync(CreateRequest(), Metadata(), CancellationToken.None));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, exception.Status);
        Assert.Single(fixture.Authority.Calls);
        Assert.Null(fixture.Writer.CreateRequest);
    }

    [Fact]
    public async Task Approved_foreign_company_answer_is_a_provider_failure_and_never_calls_the_writer()
    {
        var fixture = Fixture();
        fixture.OrderLines.Answer = new(SalesOrderLineReferenceOutcomeV1.Approved,
            new SalesOrderLineReferenceV1(CompanyB, OrderId, LineId, 1, Guid.NewGuid(), Guid.NewGuid(),
                ProductId, "7", Guid.NewGuid(), "10", 0, 4), null);

        var exception = await Assert.ThrowsAsync<InventoryPhysicalAllocationHostException>(() =>
            fixture.Service.CreateAsync(CreateRequest(), Metadata(), CancellationToken.None));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, exception.Status);
        Assert.Equal(2, fixture.Authority.Calls.Count);
        Assert.Null(fixture.Writer.CreateRequest);
    }

    [Fact]
    public async Task Unknown_sales_outcome_is_a_provider_failure_and_never_calls_the_writer()
    {
        var fixture = Fixture();
        fixture.OrderLines.Answer = new((SalesOrderLineReferenceOutcomeV1)int.MaxValue, null, null);

        var exception = await Assert.ThrowsAsync<InventoryPhysicalAllocationHostException>(() =>
            fixture.Service.CreateAsync(CreateRequest(), Metadata(), CancellationToken.None));

        Assert.Equal((StatusCodes.Status503ServiceUnavailable, null), (exception.Status, exception.Code));
        Assert.Equal(2, fixture.Authority.Calls.Count);
        Assert.Null(fixture.Writer.CreateRequest);
    }

    [Theory]
    [InlineData(SalesOrderLineReferenceOutcomeV1.NotFound, StatusCodes.Status404NotFound, null)]
    [InlineData(SalesOrderLineReferenceOutcomeV1.NotApproved, StatusCodes.Status422UnprocessableEntity,
        InventoryAllocationCodesV1.InvalidReference)]
    public async Task A_non_approved_or_hidden_sales_line_is_refused_without_a_write(
        SalesOrderLineReferenceOutcomeV1 outcome,
        int status,
        string? code)
    {
        var fixture = Fixture();
        fixture.OrderLines.Answer = new(outcome, null, outcome == SalesOrderLineReferenceOutcomeV1.NotApproved
            ? "PendingApproval"
            : null);

        var exception = await Assert.ThrowsAsync<InventoryPhysicalAllocationHostException>(() =>
            fixture.Service.CreateAsync(CreateRequest(), Metadata(), CancellationToken.None));

        Assert.Equal((status, code), (exception.Status, exception.Code));
        Assert.Null(fixture.Writer.CreateRequest);
    }

    [Fact]
    public async Task Release_uses_the_same_exact_permission_and_fresh_fence()
    {
        var fixture = Fixture(Granted("ROLE-OLD"), Granted("ROLE-RELEASE-FINAL"));
        var allocationId = Guid.NewGuid();

        await fixture.Service.ReleaseAsync(allocationId,
            new ReleasePhysicalAllocationV1("1", "SENTETIK release"), Metadata(), CancellationToken.None);

        Assert.Equal(2, fixture.Authority.Calls.Count);
        Assert.Equal(allocationId, fixture.Writer.ReleaseId);
        Assert.Equal(InventoryPermissionManifestV1.PhysicalAllocationWrite,
            fixture.Writer.Metadata!.PermissionCode);
        Assert.Equal(["ROLE-RELEASE-FINAL"], fixture.Writer.Metadata.RoleCodes);
    }

    [Fact]
    public async Task Release_revocation_at_the_fresh_fence_returns_403_without_a_write()
    {
        var fixture = Fixture(Granted(), PermissionScope.Denied);

        var exception = await Assert.ThrowsAsync<InventoryPhysicalAllocationHostException>(() =>
            fixture.Service.ReleaseAsync(Guid.NewGuid(),
                new ReleasePhysicalAllocationV1("1", "SENTETIK release"), Metadata(), CancellationToken.None));

        Assert.Equal(StatusCodes.Status403Forbidden, exception.Status);
        Assert.Equal(2, fixture.Authority.Calls.Count);
        Assert.Null(fixture.Writer.ReleaseId);
    }

    [Fact]
    public async Task Sales_or_inventory_read_permissions_do_not_satisfy_the_allocation_permission()
    {
        var fixture = Fixture(PermissionScope.Denied);

        var exception = await Assert.ThrowsAsync<InventoryPhysicalAllocationHostException>(() =>
            fixture.Service.CreateAsync(CreateRequest(), Metadata(), CancellationToken.None));

        Assert.Equal(StatusCodes.Status403Forbidden, exception.Status);
        Assert.Equal((ErpPermissions.InventoryPhysicalAllocationWrite, CompanyA), Assert.Single(fixture.Authority.Calls));
        Assert.Equal(0, fixture.OrderLines.Calls);
        Assert.Null(fixture.Writer.CreateRequest);
    }

    [Fact]
    public async Task Change_derives_the_approved_line_product_and_passes_the_fresh_fence_to_the_writer()
    {
        var fixture = Fixture(Granted("ROLE-OLD"), Granted("ROLE-CHANGE-FINAL"));
        var allocationId = Guid.NewGuid();
        var target = Guid.NewGuid();
        var newId = Guid.NewGuid();

        var result = await fixture.Service.ChangeAsync(allocationId,
            new ChangeInventoryPhysicalAllocationRequestV1("3", OrderId, LineId, target, "9", "4.5", "SENTETIK change", newId),
            Metadata(), CancellationToken.None);

        Assert.False(result.IsReplay);
        Assert.Equal(2, fixture.Authority.Calls.Count);
        Assert.Equal(1, fixture.OrderLines.Calls);
        var (id, request) = fixture.Writer.Change!.Value;
        Assert.Equal((allocationId, ProductId, "7", OrderId, LineId, target, "9", "4.5", "3", newId),
            (id, request.ProductId, request.ProductVersion, request.SalesOrderId, request.SalesOrderLineId,
                request.TargetLabelId, request.ExpectedTargetLabelVersion, request.QuantityKg,
                request.ExpectedAllocationVersion, request.NewAllocationId));
        Assert.Equal((fixture.User.UserId, InventoryPermissionManifestV1.PhysicalAllocationWrite),
            (fixture.Writer.Metadata!.ActorId, fixture.Writer.Metadata.PermissionCode));
        Assert.Equal(["ROLE-CHANGE-FINAL"], fixture.Writer.Metadata.RoleCodes);
    }

    [Theory]
    [InlineData(SalesOrderLineReferenceOutcomeV1.NotFound, StatusCodes.Status404NotFound, null)]
    [InlineData(SalesOrderLineReferenceOutcomeV1.NotApproved, StatusCodes.Status422UnprocessableEntity,
        InventoryAllocationCodesV1.InvalidReference)]
    public async Task Change_of_an_allocation_whose_line_is_no_longer_approved_is_refused_without_a_write(
        SalesOrderLineReferenceOutcomeV1 outcome,
        int status,
        string? code)
    {
        var fixture = Fixture();
        fixture.OrderLines.Answer = new(outcome, null, outcome == SalesOrderLineReferenceOutcomeV1.NotApproved
            ? "Cancelled"
            : null);

        var exception = await Assert.ThrowsAsync<InventoryPhysicalAllocationHostException>(() =>
            fixture.Service.ChangeAsync(Guid.NewGuid(), ChangeRequest(), Metadata(), CancellationToken.None));

        Assert.Equal((status, code), (exception.Status, exception.Code));
        Assert.Null(fixture.Writer.Change);
    }

    [Fact]
    public async Task Change_revocation_at_the_fresh_fence_returns_403_without_a_write()
    {
        var fixture = Fixture(Granted(), PermissionScope.Denied);

        var exception = await Assert.ThrowsAsync<InventoryPhysicalAllocationHostException>(() =>
            fixture.Service.ChangeAsync(Guid.NewGuid(), ChangeRequest(), Metadata(), CancellationToken.None));

        Assert.Equal(StatusCodes.Status403Forbidden, exception.Status);
        Assert.Null(fixture.Writer.Change);
    }

    [Fact]
    public async Task Change_without_the_allocation_permission_never_reads_sales_or_writes()
    {
        var fixture = Fixture(PermissionScope.Denied);

        var exception = await Assert.ThrowsAsync<InventoryPhysicalAllocationHostException>(() =>
            fixture.Service.ChangeAsync(Guid.NewGuid(), ChangeRequest(), Metadata(), CancellationToken.None));

        Assert.Equal(StatusCodes.Status403Forbidden, exception.Status);
        Assert.Equal(0, fixture.OrderLines.Calls);
        Assert.Null(fixture.Writer.Change);
    }

    [Fact]
    public async Task Change_with_an_empty_new_allocation_id_is_400_before_the_sales_read()
    {
        var fixture = Fixture();

        var exception = await Assert.ThrowsAsync<InventoryPhysicalAllocationHostException>(() =>
            fixture.Service.ChangeAsync(Guid.NewGuid(), ChangeRequest() with { NewAllocationId = Guid.Empty }, Metadata(),
                CancellationToken.None));

        Assert.Equal((StatusCodes.Status400BadRequest, InventoryAllocationCodesV1.ValidationFailed),
            (exception.Status, exception.Code));
        Assert.Equal(0, fixture.OrderLines.Calls);
        Assert.Null(fixture.Writer.Change);
    }

    private static ChangeInventoryPhysicalAllocationRequestV1 ChangeRequest() => new(
        "1", OrderId, LineId, Guid.NewGuid(), "2", "3", "SENTETIK change", null);

    private static TestFixture Fixture(params PermissionScope[] scopes)
    {
        var company = new MutableCompanyContext(CompanyA);
        var user = new MutableCurrentUser("SENTETIK-actor", CompanyA, CompanyB);
        var authority = new FakeAuthority(scopes.Length == 0 ? [Granted(), Granted()] : scopes);
        var orderLines = new FakeOrderLines
        {
            Answer = new(SalesOrderLineReferenceOutcomeV1.Approved,
                new SalesOrderLineReferenceV1(CompanyA, OrderId, LineId, 1, Guid.NewGuid(), Guid.NewGuid(),
                    ProductId, "7", Guid.NewGuid(), "10", 0, 4), null),
        };
        var writer = new FakeWriter();
        return new(new(company, user, authority, orderLines, writer), company, user, authority, orderLines, writer);
    }

    private static PermissionScope Granted(params string[] roles) => new(true, true,
        new HashSet<string>(roles.Length == 0 ? ["ROLE-Z", "ROLE-A"] : roles, StringComparer.Ordinal),
        new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());

    private static CreateInventoryPhysicalAllocationRequestV1 CreateRequest() => new(
        Guid.NewGuid(), OrderId, LineId, Guid.NewGuid(), "1", "5", "SENTETIK allocation", null);

    private static InventoryCommandMetadata Metadata() => new(
        "transport-actor", "transport-permission", ["transport-role"], "SENTETIK-correlation", null,
        "SENTETIK-key", "test", null, null);

    private sealed record TestFixture(
        InventoryPhysicalAllocationHostService Service,
        MutableCompanyContext Company,
        MutableCurrentUser User,
        FakeAuthority Authority,
        FakeOrderLines OrderLines,
        FakeWriter Writer);

    private sealed class MutableCompanyContext(Guid companyId) : ICompanyContext
    {
        public Guid CompanyId { get; set; } = companyId;
        public bool IsResolved => CompanyId != Guid.Empty;
        public void Set(Guid companyId) => CompanyId = companyId;
    }

    private sealed class MutableCurrentUser(string userId, params Guid[] companies) : ICurrentUser
    {
        public bool IsAuthenticated { get; set; } = true;
        public string? UserId { get; set; } = userId;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = companies.ToHashSet();
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }

    private sealed class FakeAuthority(IEnumerable<PermissionScope> scopes) : IErpAuthorityRevalidationService
    {
        private readonly Queue<PermissionScope> _scopes = new(scopes);
        public List<(string Permission, Guid Company)> Calls { get; } = [];

        public Task<PermissionScope> RevalidatePermissionScopeAsync(
            string permission,
            Guid companyId,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((permission, companyId));
            return Task.FromResult(_scopes.Count > 0 ? _scopes.Dequeue() : PermissionScope.Denied);
        }
    }

    private sealed class FakeOrderLines : ISalesOrderLineReferencePortV1
    {
        public SalesOrderLineReferenceAnswerV1 Answer { get; set; } = null!;
        public Exception? Exception { get; set; }
        public Action? AfterRead { get; set; }
        public int Calls { get; private set; }

        public Task<SalesOrderLineReferenceAnswerV1> GetApprovedLineAsync(
            Guid orderId,
            Guid lineId,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Exception is not null)
            {
                throw Exception;
            }

            AfterRead?.Invoke();
            return Task.FromResult(Answer);
        }

        public Task<SalesApprovedLinePageV1> ListApprovedLinesAsync(
            int pageSize,
            string? cursor,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeWriter : IInventoryPhysicalAllocationWriterV1
    {
        public CreatePhysicalSalesAllocationV1? CreateRequest { get; private set; }
        public Guid? ReleaseId { get; private set; }
        public InventoryCommandMetadata? Metadata { get; private set; }

        public Task<InventoryPhysicalAllocationResultV1> CreateAsync(
            CreatePhysicalSalesAllocationV1 request,
            InventoryCommandMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            CreateRequest = request;
            Metadata = metadata;
            return Task.FromResult(Result(request.AllocationId, request.LabelId, request.ProductId));
        }

        public Task<InventoryPhysicalAllocationResultV1> ReleaseAsync(
            Guid allocationId,
            ReleasePhysicalAllocationV1 request,
            InventoryCommandMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            ReleaseId = allocationId;
            Metadata = metadata;
            return Task.FromResult(Result(allocationId, Guid.NewGuid(), ProductId));
        }

        public (Guid AllocationId, ChangePhysicalSalesAllocationV1 Request)? Change { get; private set; }

        public Task<InventoryPhysicalAllocationChangeResultV1> ChangeAsync(
            Guid allocationId,
            ChangePhysicalSalesAllocationV1 request,
            InventoryCommandMetadata metadata,
            CancellationToken cancellationToken = default)
        {
            Change = (allocationId, request);
            Metadata = metadata;
            var view = Result(allocationId, request.TargetLabelId, request.ProductId).Allocation;
            return Task.FromResult(new InventoryPhysicalAllocationChangeResultV1(
                new InventoryPhysicalAllocationChangeViewV1(InventoryAllocationChangeKindsV1.Resize, view, null),
                false,
                "SENTETIK-correlation"));
        }

        private static InventoryPhysicalAllocationResultV1 Result(Guid allocationId, Guid labelId, Guid productId) => new(
            new InventoryPhysicalAllocationViewV1(allocationId, CompanyA, "1", "Active", labelId, productId,
                InventoryAllocationDemandKindsV1.SalesOrderLine, OrderId, LineId, "7", "5", "5",
                "SENTETIK", null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch),
            false,
            "SENTETIK-correlation");
    }
}
