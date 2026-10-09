using GoymenERP.Api.DocumentsRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Documents.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

public sealed class SalesDocumentOwnerLinkPortTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Order = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");

    [Fact]
    public async Task Exact_company_order_with_stable_actor_is_readable()
    {
        var (port, _, _) = Port((id, _) => Task.FromResult(Detail(id, Company)));

        Assert.Equal(DocumentOwnerLinkKindsV1.SalesOrder, port.Kind);
        Assert.Equal(DocumentOwnerAccessV1.Readable, await port.CheckReadableAsync(Order));
    }

    [Theory]
    [InlineData(404, DocumentOwnerAccessV1.NotFound)]
    [InlineData(401, DocumentOwnerAccessV1.Denied)]
    [InlineData(403, DocumentOwnerAccessV1.Denied)]
    [InlineData(409, DocumentOwnerAccessV1.Conflict)]
    [InlineData(503, DocumentOwnerAccessV1.Unavailable)]
    [InlineData(500, DocumentOwnerAccessV1.Unavailable)]
    public async Task Sales_refusals_are_mapped_fail_closed(int status, DocumentOwnerAccessV1 expected)
    {
        var (port, _, _) = Port((_, _) => Task.FromException<SalesOrderDraftDetailView>(
            new SalesDraftServiceException(status, "SENTETIK")));

        Assert.Equal(expected, await port.CheckReadableAsync(Order));
    }

    [Fact]
    public async Task Malformed_owner_answer_and_unknown_provider_fault_are_unavailable()
    {
        var (wrongId, _, _) = Port((_, _) => Task.FromResult(Detail(Guid.NewGuid(), Company)));
        Assert.Equal(DocumentOwnerAccessV1.Unavailable, await wrongId.CheckReadableAsync(Order));

        var (wrongCompany, _, _) = Port((id, _) => Task.FromResult(Detail(id, Guid.NewGuid())));
        Assert.Equal(DocumentOwnerAccessV1.Unavailable, await wrongCompany.CheckReadableAsync(Order));

        var (fault, _, _) = Port((_, _) => Task.FromException<SalesOrderDraftDetailView>(new InvalidOperationException("SENTETIK")));
        Assert.Equal(DocumentOwnerAccessV1.Unavailable, await fault.CheckReadableAsync(Order));
    }

    [Fact]
    public async Task Actor_company_or_access_drift_after_the_owner_read_is_denied()
    {
        MutableUser? actor = null;
        var (actorPort, user, _) = Port((id, _) =>
        {
            actor!.UserId = "SENTETIK-other";
            return Task.FromResult(Detail(id, Company));
        });
        actor = user;
        Assert.Equal(DocumentOwnerAccessV1.Denied, await actorPort.CheckReadableAsync(Order));

        MutableCompany? company = null;
        var (companyPort, _, companyContext) = Port((id, _) =>
        {
            company!.Set(Guid.NewGuid());
            return Task.FromResult(Detail(id, Company));
        });
        company = companyContext;
        Assert.Equal(DocumentOwnerAccessV1.Denied, await companyPort.CheckReadableAsync(Order));

        MutableUser? companyActor = null;
        var (accessPort, accessUser, _) = Port((id, _) =>
        {
            companyActor!.AllowedCompanyIds.Clear();
            return Task.FromResult(Detail(id, Company));
        });
        companyActor = accessUser;
        Assert.Equal(DocumentOwnerAccessV1.Denied, await accessPort.CheckReadableAsync(Order));
    }

    [Fact]
    public async Task Cancellation_is_not_converted_to_unavailable()
    {
        var (port, _, _) = Port((_, token) => Task.FromCanceled<SalesOrderDraftDetailView>(token));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => port.CheckReadableAsync(Order, cancellation.Token));
    }

    private static (SalesDocumentOwnerLinkPort Port, MutableUser User, MutableCompany Company) Port(
        Func<Guid, CancellationToken, Task<SalesOrderDraftDetailView>> read)
    {
        var company = new MutableCompany();
        company.Set(Company);
        var user = new MutableUser { UserId = "SENTETIK-actor" };
        user.AllowedCompanyIds.Add(Company);
        return (new SalesDocumentOwnerLinkPort(company, user, new StubSalesDraftService(read)), user, company);
    }

    private static SalesOrderDraftDetailView Detail(Guid orderId, Guid companyId)
    {
        var summary = new SalesOrderDraftSummaryView(
            orderId, companyId, "SO-2026-000001", "Order", "Draft", "1", "Rev.0",
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 10, 9),
            DateTimeOffset.UtcNow, 0, "Preparing");
        return new SalesOrderDraftDetailView(
            summary, null, null, [], null, null, null, null, null, null,
            new SalesDraftApprovalV1(null, null, null, null, null),
            new SalesDraftCustomerAcceptanceV1(false, null, null, null, false), false, false);
    }

    private sealed class StubSalesDraftService(
        Func<Guid, CancellationToken, Task<SalesOrderDraftDetailView>> read) : ISalesDraftService
    {
        public Task<SalesOrderDraftDetailView> GetDraftDetailAsync(Guid draftId, CancellationToken cancellationToken = default) =>
            read(draftId, cancellationToken);

        public Task<SalesOrderDraftResponse> CreateDraftAsync(CreateSalesOrderDraftRequest request, SalesCommandMetadata metadata,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SalesOrderDraftListResponse> GetDraftListAsync(SalesOrderDraftListQuery query,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SalesOrderDraftResponse> SubmitDraftAsync(Guid draftId, SalesDraftStateCommandRequest request,
            SalesCommandMetadata metadata, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SalesOrderDraftResponse> ApproveDraftAsync(Guid draftId, SalesDraftStateCommandRequest request,
            SalesCommandMetadata metadata, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SalesOrderDraftResponse> RecordCustomerAcceptanceAsync(Guid draftId, SalesDraftStateCommandRequest request,
            SalesCommandMetadata metadata, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SalesOrderDraftResponse> WithdrawDraftAsync(Guid draftId, SalesDraftStateCommandRequest request,
            SalesCommandMetadata metadata, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SalesOrderDraftResponse> RejectDraftAsync(Guid draftId, SalesDraftStateCommandRequest request,
            SalesCommandMetadata metadata, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SalesOrderDraftResponse> ReviseCommercialAsync(Guid draftId, SalesDraftCommercialRevisionRequest request,
            SalesCommandMetadata metadata, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SalesOrderDraftResponse> ApprovePriceFloorExceptionsAsync(Guid draftId,
            SalesDraftPriceFloorExceptionApprovalRequest request, SalesCommandMetadata metadata,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SalesOrderDraftLinePricingResponse> GetDraftLinePricingAsync(Guid draftId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SalesOrderDraftPriceReferencesResponse> GetLinePriceReferencesAsync(Guid draftId, Guid lineId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SalesOrderDraftRevisionsResponse> GetDraftRevisionsAsync(Guid draftId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SalesOrderDraftResponse> ReviseQuantityAsync(Guid draftId, SalesDraftQuantityRevisionRequest request,
            SalesCommandMetadata metadata, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SalesOrderDraftResponse> RescheduleDeliveryAsync(Guid draftId, SalesDraftDeliveryScheduleRequest request,
            SalesCommandMetadata metadata, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SalesOrderDraftDeliverySchedulesResponse> GetDraftDeliverySchedulesAsync(Guid draftId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SalesOrderDraftDeliveryProgressResponse> GetDraftDeliveryProgressAsync(Guid draftId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SalesOrderDraftCancellationImpactResponse> GetCancellationImpactAsync(Guid draftId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SalesOrderDraftResponse> CancelDraftAsync(Guid draftId, SalesDraftCancelRequest request,
            SalesCommandMetadata metadata, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SalesOrderDraftCancellationResponse> GetDraftCancellationAsync(Guid draftId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SalesOrderDraftResponse> RequestRemainderChangeAsync(Guid draftId, SalesDraftRemainderChangeRequest request,
            SalesCommandMetadata metadata, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SalesOrderDraftResponse> ApproveRemainderChangeAsync(Guid draftId, int changeNo,
            SalesDraftRemainderDecisionRequest request, SalesCommandMetadata metadata,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SalesOrderDraftResponse> RejectRemainderChangeAsync(Guid draftId, int changeNo,
            SalesDraftRemainderDecisionRequest request, SalesCommandMetadata metadata,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SalesOrderDraftRemainderChangesResponse> GetRemainderChangesAsync(Guid draftId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class MutableCompany : ICompanyContext
    {
        public bool IsResolved { get; private set; }
        public Guid CompanyId { get; private set; }
        public void Set(Guid companyId)
        {
            CompanyId = companyId;
            IsResolved = companyId != Guid.Empty;
        }
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
