using GoymenERP.Api.DocumentsRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

public sealed class PurchasingDocumentOwnerLinkPortTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Order = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");

    [Fact]
    public async Task Exact_company_order_with_stable_actor_is_readable()
    {
        var (port, _, _) = Port((id, _) => Task.FromResult(Detail(id, Company)));

        Assert.Equal(DocumentOwnerLinkKindsV1.PurchasingPurchaseOrder, port.Kind);
        Assert.Equal(DocumentOwnerAccessV1.Readable, await port.CheckReadableAsync(Order));
    }

    [Theory]
    [InlineData(404, DocumentOwnerAccessV1.NotFound)]
    [InlineData(401, DocumentOwnerAccessV1.Denied)]
    [InlineData(403, DocumentOwnerAccessV1.Denied)]
    [InlineData(409, DocumentOwnerAccessV1.Conflict)]
    [InlineData(503, DocumentOwnerAccessV1.Unavailable)]
    [InlineData(500, DocumentOwnerAccessV1.Unavailable)]
    public async Task Purchasing_refusals_are_mapped_fail_closed(int status, DocumentOwnerAccessV1 expected)
    {
        var (port, _, _) = Port((_, _) => Task.FromException<PurchaseOrderDetailView>(new PurchasingServiceException(status, "SENTETIK")));

        Assert.Equal(expected, await port.CheckReadableAsync(Order));
    }

    [Fact]
    public async Task Malformed_owner_answer_and_unknown_provider_fault_are_unavailable()
    {
        var (wrongId, _, _) = Port((_, _) => Task.FromResult(Detail(Guid.NewGuid(), Company)));
        Assert.Equal(DocumentOwnerAccessV1.Unavailable, await wrongId.CheckReadableAsync(Order));

        var (wrongCompany, _, _) = Port((id, _) => Task.FromResult(Detail(id, Guid.NewGuid())));
        Assert.Equal(DocumentOwnerAccessV1.Unavailable, await wrongCompany.CheckReadableAsync(Order));

        var (fault, _, _) = Port((_, _) => Task.FromException<PurchaseOrderDetailView>(new InvalidOperationException("SENTETIK")));
        Assert.Equal(DocumentOwnerAccessV1.Unavailable, await fault.CheckReadableAsync(Order));
    }

    [Fact]
    public async Task Actor_or_company_access_drift_after_the_owner_read_is_denied()
    {
        MutableUser? actor = null;
        var (actorPort, user, _) = Port((id, _) =>
        {
            actor!.UserId = "SENTETIK-other";
            return Task.FromResult(Detail(id, Company));
        });
        actor = user;
        Assert.Equal(DocumentOwnerAccessV1.Denied, await actorPort.CheckReadableAsync(Order));

        MutableUser? companyActor = null;
        var (companyPort, companyUser, _) = Port((id, _) =>
        {
            companyActor!.AllowedCompanyIds.Clear();
            return Task.FromResult(Detail(id, Company));
        });
        companyActor = companyUser;
        Assert.Equal(DocumentOwnerAccessV1.Denied, await companyPort.CheckReadableAsync(Order));
    }

    [Fact]
    public async Task Cancellation_is_not_converted_to_unavailable()
    {
        var (port, _, _) = Port((_, token) => Task.FromCanceled<PurchaseOrderDetailView>(token));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => port.CheckReadableAsync(Order, cancellation.Token));
    }

    private static (PurchasingDocumentOwnerLinkPort Port, MutableUser User, CompanyContext Company) Port(
        Func<Guid, CancellationToken, Task<PurchaseOrderDetailView>> read)
    {
        var company = new CompanyContext();
        company.Set(Company);
        var user = new MutableUser { UserId = "SENTETIK-actor" };
        user.AllowedCompanyIds.Add(Company);
        return (new PurchasingDocumentOwnerLinkPort(company, user, new StubPurchasingService(read)), user, company);
    }

    private static PurchaseOrderDetailView Detail(Guid orderId, Guid companyId) => new(
        new PurchaseOrderSummaryView(orderId, companyId, "Draft", "1", Guid.NewGuid(), Guid.NewGuid(), null,
            new DateOnly(2026, 10, 9), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0), []);

    private sealed class StubPurchasingService(Func<Guid, CancellationToken, Task<PurchaseOrderDetailView>> read) : IPurchasingService
    {
        public Task<PurchaseOrderDetailView> GetOrderDetailAsync(Guid orderId, CancellationToken cancellationToken = default) =>
            read(orderId, cancellationToken);

        public Task<PurchaseRequestResponse> CreateRequestAsync(CreatePurchaseRequestRequest request, PurchasingCommandMetadata metadata,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PurchaseRequestResponse> SubmitRequestAsync(Guid requestId, SubmitPurchaseRequestRequest request,
            PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PurchaseRequestDetailView> GetRequestDetailAsync(Guid requestId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<PurchaseRequestListResponse> GetRequestListAsync(PurchaseRequestListQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<PurchaseOrderResponse> CreateOrderAsync(CreatePurchaseOrderRequest request, PurchasingCommandMetadata metadata,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PurchaseOrderResponse> ApproveOrderAsync(Guid orderId, ApprovePurchaseOrderRequest request,
            PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PurchaseOrderListResponse> GetOrderListAsync(PurchaseOrderListQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class MutableUser : ICurrentUser
    {
        public string? UserId { get; set; }
        public bool IsAuthenticated => UserId is not null;
        public HashSet<Guid> AllowedCompanyIds { get; } = [];
        IReadOnlySet<Guid> ICurrentUser.AllowedCompanyIds => AllowedCompanyIds;
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
}
