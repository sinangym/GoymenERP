using System.Reflection;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Work.Infrastructure.Delegations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>WP8 native command composition with synthetic owners. Not HTTP/SQL acceptance.</summary>
public sealed partial class WorkPurchasingRequestDecisionHostTests
{
    private static readonly Guid Company = Guid.Parse("d8230000-0000-4000-8000-000000000001");
    private static readonly Guid Item = Guid.Parse("d8230000-0000-4000-8000-000000000002");
    private const string Actor = "SENTETIK-reviewer";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Native_request_command_gets_original_version_full_reason_and_replay_metadata(bool approve)
    {
        var run = new Run(); run.Source.Pending = false;
        var reason = approve ? null : new string('x', 4000);
        var result = await run.Host.DecideAsync(WorkDecisionHostV1.PurchaseRequest, Item,
            new("7", approve, reason), "original-key", default);
        Assert.Equal(approve ? "Approved" : "Rejected", result.State);
        Assert.True(result.Replayed); Assert.Equal("SENTETIK-original", result.OriginalCorrelationId);
        var call = Assert.Single(run.Calls);
        Assert.Equal(approve ? "ApproveRequestAsync" : "RejectRequestAsync", call.Method);
        Assert.Equal(Item, call.Id);
        if (approve) Assert.Equal(new ApprovePurchaseRequestRequest("7"), call.Body);
        else Assert.Equal(new RejectPurchaseRequestRequest("7", reason!), call.Body);
        Assert.Equal(("original-key", "SENTETIK-correlation"), (call.Metadata.IdempotencyKey, call.Metadata.CorrelationId));
        Assert.All(run.Raw.Asked, permission => Assert.Equal(PurchasingPermissionManifestV1.RequestsApprove, permission));
    }

    [Fact]
    public async Task Native_terminal_refusal_replay_and_original_correlation_are_preserved()
    {
        var run = new Run { Refusal = new PurchasingServiceException(409, PurchasingProblemCodesV1.RequestHasApprovedOrder,
            isReplay: true, originalCorrelationId: "SENTETIK-first") };
        var refused = await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(WorkDecisionHostV1.PurchaseRequest,
            Item, new("7", false, "SENTETIK"), "same-key", default));
        Assert.Equal((409, PurchasingProblemCodesV1.RequestHasApprovedOrder, true, "SENTETIK-first"),
            (refused.Status, refused.Code, refused.Replayed, refused.OriginalCorrelationId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(4001)]
    public async Task Native_reason_validation_is_forwarded_without_truncation(int? length)
    {
        var run = new Run { Refusal = new PurchasingServiceException(422, "native_reason_validation") };
        var reason = length is null ? null : new string('x', length.Value);
        var refused = await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(WorkDecisionHostV1.PurchaseRequest,
            Item, new("7", false, reason), "invalid-reason", default));
        Assert.Equal(422, refused.Status);
        Assert.Equal(reason, Assert.IsType<RejectPurchaseRequestRequest>(Assert.Single(run.Calls).Body).Reason);
    }

    [Fact]
    public async Task Makers_foreign_company_denied_authority_and_unregistered_order_kind_never_reach_native_owner()
    {
        var run = new Run(); run.Source.Maker = Actor;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.PreviewAsync(WorkDecisionHostV1.PurchaseRequest, Item, default))).Status);
        run.Source.Maker = "SENTETIK-maker"; run.Source.Submitter = Actor;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(WorkDecisionHostV1.PurchaseRequest, Item, new("7", true, null), "self", default))).Status);
        run.Source.Submitter = "SENTETIK-submitter"; run.Source.CompanyId = Guid.NewGuid();
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.PreviewAsync(WorkDecisionHostV1.PurchaseRequest, Item, default))).Status);
        run.Source.CompanyId = Company; run.Raw.Allowed = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.PreviewAsync(WorkDecisionHostV1.PurchaseRequest, Item, default))).Status);
        run.Raw.Allowed = true;
        Assert.Equal(404, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.PreviewAsync("purchasing.unregistered.v1", Item, default))).Status);
        Assert.Empty(run.Calls);
    }

    [Fact]
    public async Task Delegation_revocation_inside_native_owner_is_denied_at_the_owners_fence()
    {
        var run = new Run(); run.BeforeFence = () => run.Delegations.Allowed = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(WorkDecisionHostV1.PurchaseRequest,
            Item, new("7", true, null, Guid.NewGuid()), "late", default))).Status);
        Assert.False(run.NativeEffect); Assert.True(run.Delegations.Reads >= 2);
    }

    private sealed class Run
    {
        public readonly RawAuthority Raw = new();
        public readonly Source Source;
        public readonly Delegations Delegations;
        public readonly List<(string Method, Guid Id, object Body, PurchasingCommandMetadata Metadata)> Calls = [];
        public readonly WorkDecisionHostV1 Host;
        public PurchasingServiceException? Refusal;
        public Action? BeforeFence;
        public bool NativeEffect;
        public Guid ResultCompany = Company, ResultId = Item;
        public string OrderState = "Approved";
        public Run(bool order = false, bool ownerAvailable = true)
        {
            Source = new(order ? WorkDecisionHostV1.PurchaseOrder : WorkDecisionHostV1.PurchaseRequest);
            Delegations = new(Source.ApproverPermission);
            var company = new CompanyContext(); company.Set(Company);
            var correlation = new CorrelationContext(); correlation.Set("SENTETIK-correlation", null, "api", null, null);
            var fence = new WorkDecisionExecutionFenceV1(); var authority = new WorkDecisionAuthorityV1(Raw, fence);
            var owner = DispatchProxy.Create<IPurchasingService, NativeOwnerProxy>();
            async Task NativeFence(MethodInfo method, object?[] args)
            {
                Calls.Add((method.Name, (Guid)args[0]!, args[1]!, (PurchasingCommandMetadata)args[2]!));
                BeforeFence?.Invoke();
                if (!(await authority.RevalidatePermissionScopeAsync(Source.ApproverPermission, Company)).IsGranted)
                    throw new PurchasingServiceException(403, "forbidden");
                if (Refusal is not null) throw Refusal;
                NativeEffect = true;
            }
            async Task<PurchaseRequestResponse> AnswerRequest(MethodInfo method, object?[] args)
            {
                await NativeFence(method, args);
                return new(new(new(Item, Company, method.Name == "ApproveRequestAsync" ? "Approved" : "Rejected", "9",
                    new DateOnly(2026, 11, 27), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 1), []), true, "SENTETIK-original");
            }
            async Task<PurchaseOrderResponse> AnswerOrder(MethodInfo method, object?[] args)
            {
                await NativeFence(method, args);
                return new(new(new(ResultId, ResultCompany, OrderState, "9", Company, Company, null,
                    new DateOnly(2026, 11, 27), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 1), []), true, "SENTETIK-original");
            }
            ((NativeOwnerProxy)(object)owner).Handler = (method, args) => method.Name == "ApproveOrderAsync"
                ? AnswerOrder(method, args) : AnswerRequest(method, args);
            Host = new(company, new User(), correlation, authority, [Source], Delegations, fence, null!, null!, purchasing: ownerAvailable ? owner : null);
        }
    }
    public class NativeOwnerProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[], object?> Handler { get; set; } = (_, _) => throw new NotSupportedException();
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args!);
    }
    private sealed class RawAuthority : IErpAuthorityRevalidationService
    {
        public bool Allowed = true;
        public List<string> Asked = [];
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        { Asked.Add(permission); return Task.FromResult(Allowed ? new PermissionScope(true, true, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()) : PermissionScope.Denied); }
    }
    private sealed class Source(string kind) : IWorkApprovalDecisionSourceV1
    {
        public Guid CompanyId = Company;
        public bool Pending = true;
        public string Maker = "SENTETIK-maker", Submitter = "SENTETIK-submitter";
        public string Module => "purchasing";
        public string Kind => kind;
        public string ApproverPermission => kind == WorkDecisionHostV1.PurchaseOrder ? PurchasingPermissionManifestV1.OrdersApprove : PurchasingPermissionManifestV1.RequestsApprove;
        public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId, CancellationToken cancellationToken) =>
            Task.FromResult<WorkApprovalDecisionSnapshotV1?>(new(CompanyId, new(Module, Kind, itemId, "SENTETIK", DateTimeOffset.UtcNow,
                "/onaylarim/" + Kind + "/" + itemId, Maker, [], [], kind == WorkDecisionHostV1.PurchaseRequest ? [Submitter] : null), "8", Pending));
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 context, int maxCount, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class User : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => Actor;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([Company]);
        public bool CanAccessCompany(Guid companyId) => companyId == Company;
    }
    private sealed class Delegations(string permission) : IWorkDelegationReaderV1
    {
        public bool Allowed = true; public int Reads;
        public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 item, CancellationToken cancellationToken = default)
        { Reads++; return Task.FromResult<WorkDelegationViewV1?>(Allowed ? new(id, Company, item.Kind, permission,
            Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), "1", Guid.NewGuid(), DateTimeOffset.UtcNow, "SENTETIK", null, null, null) : null); }
    }
}
