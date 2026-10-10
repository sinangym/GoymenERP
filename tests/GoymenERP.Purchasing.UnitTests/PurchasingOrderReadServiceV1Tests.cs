using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Purchasing.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Purchasing.UnitTests;

public sealed class PurchasingOrderReadServiceV1Tests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid SupplierA = Guid.Parse("76000000-0000-0000-0000-000000000001");
    private static readonly Guid SupplierB = Guid.Parse("76000000-0000-0000-0000-000000000002");

    [Fact]
    public async Task List_and_detail_publish_exact_record_bound_supplier_presentations()
    {
        var owner = new StubOrderSource();
        var displays = new StubDisplaySource
        {
            Answer = new(PurchasingSupplierDisplayBatchOutcomeV1.Success,
            [
                new(SupplierA, "TED-001", "Tedarikçi A A.Ş.", "A Metal"),
                new(SupplierB, "TED-002", "Tedarikçi B A.Ş.", null),
            ]),
        };
        var fixture = new Fixture(owner, displays);

        var list = await fixture.Service.GetOrderListAsync(new PurchaseOrderListQuery());
        var detail = await fixture.Service.GetOrderDetailAsync(owner.First.Id);

        Assert.Equal([SupplierA, SupplierB], list.Items.Select(item => item.SupplierPartyId));
        Assert.Equal(["TED-001", "TED-002"], list.Items.Select(item => item.SupplierDisplay!.Code));
        Assert.Equal("Tedarikçi A A.Ş.", detail.Order.SupplierDisplay!.LegalName);
        Assert.Equal((Company, "purchasing-reader"),
            (displays.Requests[0].CompanyId, displays.Requests[0].ActorId));
        Assert.Equal([SupplierA, SupplierB], displays.Requests[0].PartyIds);
        Assert.Equal([SupplierA], displays.Requests[1].PartyIds);
    }

    [Theory]
    [InlineData(PurchasingSupplierDisplayBatchOutcomeV1.Denied, 403)]
    [InlineData(PurchasingSupplierDisplayBatchOutcomeV1.Unavailable, 503)]
    [InlineData(PurchasingSupplierDisplayBatchOutcomeV1.Success, 503)]
    public async Task Denied_unavailable_or_incomplete_owner_answers_fail_closed_without_raw_id_fallback(
        PurchasingSupplierDisplayBatchOutcomeV1 outcome,
        int expectedStatus)
    {
        var owner = new StubOrderSource();
        var displays = new StubDisplaySource
        {
            Answer = new(outcome, []),
        };
        var service = new Fixture(owner, displays).Service;

        var exception = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            service.GetOrderListAsync(new PurchaseOrderListQuery()));

        Assert.Equal(expectedStatus, exception.Status);
    }

    [Fact]
    public async Task Authority_actor_and_company_drift_are_refused_before_or_after_the_owner_read()
    {
        var owner = new StubOrderSource();
        var displays = new StubDisplaySource
        {
            Answer = new(PurchasingSupplierDisplayBatchOutcomeV1.Success,
                [new(SupplierA, "TED-001", "Tedarikçi A", null), new(SupplierB, "TED-002", "Tedarikçi B", null)]),
        };
        var fixture = new Fixture(owner, displays);
        fixture.Authority.Granted = false;
        var before = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            fixture.Service.GetOrderListAsync(new PurchaseOrderListQuery()));
        Assert.Equal(403, before.Status);
        Assert.Equal(0, owner.ListCalls);

        fixture.Authority.Granted = true;
        displays.OnRead = () => fixture.User.ActorId = "replacement-reader";
        var after = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            fixture.Service.GetOrderListAsync(new PurchaseOrderListQuery()));
        Assert.Equal(403, after.Status);
    }

    private sealed class Fixture(StubOrderSource owner, StubDisplaySource displays)
    {
        public TestCompanyContext CompanyContext { get; } = new(Company);
        public TestCurrentUser User { get; } = new("purchasing-reader", new HashSet<Guid> { Company });
        public TestAuthority Authority { get; } = new();
        public PurchasingOrderReadServiceV1 Service => new(
            owner,
            displays,
            CompanyContext,
            User,
            Authority,
            new PurchasingServiceBindings());
    }

    private sealed class StubOrderSource : IPurchasingOrderRecordReadSourceV1
    {
        public PurchaseOrderSummaryView First { get; } = Summary(Guid.NewGuid(), SupplierA);
        public PurchaseOrderSummaryView Second { get; } = Summary(Guid.NewGuid(), SupplierB);
        public int ListCalls { get; private set; }

        public Task<PurchaseOrderDetailView> GetOrderDetailAsync(Guid orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PurchaseOrderDetailView(First, []));

        public Task<PurchaseOrderListResponse> GetOrderListAsync(
            PurchaseOrderListQuery query,
            CancellationToken cancellationToken = default)
        {
            ListCalls++;
            return Task.FromResult(new PurchaseOrderListResponse([First, Second], null));
        }
    }

    private sealed class StubDisplaySource : IPurchasingSupplierDisplaySourceV1
    {
        public required PurchasingSupplierDisplayBatchAnswerV1 Answer { get; init; }
        public List<PurchasingSupplierDisplayBatchRequestV1> Requests { get; } = [];
        public Action? OnRead { get; set; }

        public Task<PurchasingSupplierDisplayBatchAnswerV1> GetAuthorizedBatchAsync(
            PurchasingSupplierDisplayBatchRequestV1 request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            OnRead?.Invoke();
            if (Answer.Outcome != PurchasingSupplierDisplayBatchOutcomeV1.Success)
            {
                return Task.FromResult(Answer);
            }

            var requested = request.PartyIds.ToHashSet();
            return Task.FromResult(Answer with
            {
                Items = Answer.Items.Where(item => requested.Contains(item.PartyId)).ToArray(),
            });
        }
    }

    private sealed class TestCompanyContext(Guid companyId) : ICompanyContext
    {
        public bool IsResolved => CompanyId != Guid.Empty;
        public Guid CompanyId { get; private set; } = companyId;
        public void Set(Guid value) => CompanyId = value;
    }

    private sealed class TestCurrentUser(string actorId, IReadOnlySet<Guid> companies) : ICurrentUser
    {
        public string ActorId { get; set; } = actorId;
        public bool IsAuthenticated => true;
        public string? UserId => ActorId;
        public IReadOnlySet<Guid> AllowedCompanyIds => companies;
        public bool CanAccessCompany(Guid companyId) => companies.Contains(companyId);
    }

    private sealed class TestAuthority : IErpAuthorityRevalidationService
    {
        public bool Granted { get; set; } = true;

        public Task<PermissionScope> RevalidatePermissionScopeAsync(
            string permission,
            Guid companyId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PermissionScope(
                Granted,
                true,
                new HashSet<string>(StringComparer.Ordinal) { "PURCHASING-READER" },
                new HashSet<Guid>(),
                new HashSet<Guid>(),
                new HashSet<Guid>()));
    }

    private static PurchaseOrderSummaryView Summary(Guid id, Guid supplierPartyId) =>
        new(id, Company, "Draft", "1", supplierPartyId, Guid.NewGuid(), null,
            new DateOnly(2026, 10, 10), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 0);
}
