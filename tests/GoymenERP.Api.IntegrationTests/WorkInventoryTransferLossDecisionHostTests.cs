using System.Reflection;
using System.Text.Json;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Work.Infrastructure.Delegations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>Host boundary over controlled owner receipts; real native SQL composition is in the relational partial.</summary>
public sealed class WorkInventoryTransferLossDecisionHostTests
{
    private static readonly Guid Company = Guid.Parse("e8250000-0000-4000-8000-000000000001");
    private static readonly Guid Item = Guid.Parse("e8250000-0000-4000-8000-000000000002");
    private static readonly Guid Transfer = Guid.Parse("e8250000-0000-4000-8000-000000000003");
    private static readonly Guid Line = Guid.Parse("e8250000-0000-4000-8000-000000000004");
    private const string Actor = "SENTETIK-loss-checker", Maker = "SENTETIK-loss-requester";
    private static readonly DateTimeOffset At = new(2026, 11, 27, 1, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(true, false)] [InlineData(false, false)] [InlineData(true, true)] [InlineData(false, true)]
    public async Task Approve_and_reject_keep_exact_native_request_time_reason_and_receipt(bool approve, bool replay)
    {
        var run = new Rig(); run.Replayed = replay;
        var preview = await run.Host.PreviewAsync(WorkDecisionHostV1.InventoryTransferLoss, Item, default);
        Assert.True(preview.ApprovalReasonRequired); Assert.True(preview.RejectionSupported);
        Assert.Equal(new WorkInventoryTransferLossOptionsV1(1, At), preview.InventoryTransferLoss);
        var result = await run.Host.DecideAsync(WorkDecisionHostV1.InventoryTransferLoss, Item, Body(approve), "native-key", default);
        Assert.Equal((approve ? "Approved" : "Rejected", replay), (result.State, result.Replayed));
        Assert.Equal(new DecideInventoryTransferLossV1("2", approve ? InventoryTransferDecisionKindV1.Approve : InventoryTransferDecisionKindV1.Reject,
            At.AddHours(1), "  actual human reason  ", 1), run.Captured);
    }

    [Fact]
    public async Task Renew_same_item_same_version_never_remaps_old_request_and_only_original_native_replay_can_succeed()
    {
        var run = new Rig(); run.Source.Renew(); run.Replayed = true;
        var result = await run.Host.DecideAsync(WorkDecisionHostV1.InventoryTransferLoss, Item, Body(false), "old-original-key", default);
        Assert.True(result.Replayed); Assert.Equal(1, run.Captured!.RequestNumber);
        run.Replayed = false;
        Assert.Equal(409, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(
            WorkDecisionHostV1.InventoryTransferLoss, Item, Body(false), "different-key", default))).Status);
        Assert.Equal(1, run.Captured.RequestNumber);
    }

    [Fact]
    public async Task Original_terminal_key_replay_survives_current_request_becoming_the_checkers_own_request_without_allowing_a_new_self_decision()
    {
        var run = new Rig { Replayed = true }; run.Source.Renew(); run.Source.CurrentMaker = Actor;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.PreviewAsync(WorkDecisionHostV1.InventoryTransferLoss, Item, default))).Status);
        var replay = await run.Host.DecideAsync(WorkDecisionHostV1.InventoryTransferLoss, Item, Body(false), "original-terminal-key", default);
        Assert.True(replay.Replayed); Assert.Equal(1, run.Captured!.RequestNumber);
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(WorkDecisionHostV1.InventoryTransferLoss, Item,
            Body(true) with { InventoryTransferLoss = new(2, At.AddHours(2)) }, "fresh-own-request", default))).Status);
        Assert.Equal(1, run.Captured.RequestNumber);
        run.Transform = answer => answer with { CommandReceiptProven = false };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(WorkDecisionHostV1.InventoryTransferLoss, Item,
            Body(false), "different-domain-key", default))).Status);
    }

    [Fact]
    public async Task Retained_receipt_delegation_uses_historical_maker_while_live_expiry_or_revocation_and_current_self_guards_remain()
    {
        var delegation = new MakerAwareDelegation(); var run = new Rig(delegation) { Replayed = true };
        run.Source.Renew(); run.Source.CurrentMaker = Actor;
        var request = Body(false) with { DelegationId = Guid.NewGuid() };
        Assert.True((await run.Host.DecideAsync(WorkDecisionHostV1.InventoryTransferLoss, Item, request, "original-delegated-key", default)).Replayed);
        Assert.Equal(Maker, Assert.Single(delegation.Items).PreparedBy);
        Assert.Equal(Item, delegation.Items[0].ItemId); Assert.Equal(WorkDecisionHostV1.InventoryTransferLoss, delegation.Items[0].Kind);
        delegation.Allowed = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(
            WorkDecisionHostV1.InventoryTransferLoss, Item, request, "original-delegated-key", default))).Status);
        delegation.Allowed = true;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(WorkDecisionHostV1.InventoryTransferLoss, Item,
            request with { InventoryTransferLoss = new(2, At.AddHours(2)) }, "fresh-self", default))).Status);
    }

    [Theory]
    [InlineData("other-exclusion", 403)] [InlineData("historical-own", 403)]
    [InlineData("unknown-number", 403)] [InlineData("missing-proof", 503)]
    public async Task Historical_routing_exception_never_bypasses_other_exclusions_own_history_or_invalid_proof(string fault, int status)
    {
        var run = new Rig { Replayed = true }; run.Source.Renew(); run.Source.CurrentMaker = Actor;
        if (fault == "other-exclusion") run.Source.Excluded = [Actor];
        if (fault == "historical-own") run.Source.HistoricalMaker = Actor;
        if (fault == "missing-proof") run.Source.MissingProof = true;
        var body = fault == "unknown-number" ? Body(false) with { InventoryTransferLoss = new(3, At.AddHours(1)) } : Body(false);
        Assert.Equal(status, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(
            WorkDecisionHostV1.InventoryTransferLoss, Item, body, "not-a-bypass", default))).Status);
        Assert.Null(run.Captured);
    }

    [Fact]
    public async Task Domain_prior_decision_with_replay_flag_but_without_key_receipt_is_never_Work_success()
    {
        var run = new Rig { Replayed = true, Transform = answer => answer with { CommandReceiptProven = false } };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(
            WorkDecisionHostV1.InventoryTransferLoss, Item, Body(false), "different-key", default))).Status);
    }

    [Theory]
    [InlineData("company")] [InlineData("transfer")] [InlineData("line")] [InlineData("request")]
    [InlineData("decision")] [InlineData("reason")] [InlineData("time")] [InlineData("actor")]
    [InlineData("movement")] [InlineData("version")] [InlineData("maker")] [InlineData("request-time")]
    public async Task Malformed_native_owner_success_never_becomes_Work_success(string fault)
    {
        var run = new Rig();
        run.Transform = answer => fault switch
        {
            "company" => answer with { Facts = answer.Facts with { CompanyId = Guid.NewGuid() } },
            "transfer" => answer with { Transfer = answer.Transfer with { TransferId = Guid.NewGuid() } },
            "version" => answer with { Transfer = answer.Transfer with { Version = "4" }, Facts = answer.Facts with { Version = "4" } },
            _ => answer with { Facts = answer.Facts with { Losses = answer.Facts.Losses.Select(row => fault switch
            {
                "line" => row with { LineId = Guid.NewGuid() }, "request" => row with { RequestNumber = 2 },
                "decision" => row with { Status = "Rejected" }, "reason" => row with { DecisionReason = "invented" },
                "time" => row with { DecidedAtUtc = At }, "actor" => row with { DecidedBy = Maker },
                "movement" => row with { MovementId = null }, "maker" => row with { RequestedBy = "other" },
                "request-time" => row with { RequestedAtUtc = At.AddMinutes(1) }, _ => throw new NotSupportedException(),
            }).ToArray() } },
        };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(
            WorkDecisionHostV1.InventoryTransferLoss, Item, Body(true), "bad-receipt", default))).Status);
    }

    [Theory]
    [InlineData("missing")] [InlineData("zero-request")] [InlineData("unknown-request")]
    [InlineData("non-utc")] [InlineData("no-reason")] [InlineData("other-typed")]
    public async Task Invalid_or_missing_typed_native_input_is_refused_without_calling_owner(string fault)
    {
        var run = new Rig(); var body = Body(true);
        body = fault switch
        {
            "missing" => body with { InventoryTransferLoss = null },
            "zero-request" => body with { InventoryTransferLoss = new(0, At) },
            "unknown-request" => body with { InventoryTransferLoss = new(2, At) },
            "non-utc" => body with { InventoryTransferLoss = new(1, At.ToOffset(TimeSpan.FromHours(3))) },
            "no-reason" => body with { Reason = null },
            "other-typed" => body with { ManufacturingPlanReapproval = new([Guid.NewGuid()]) },
            _ => throw new NotSupportedException(),
        };
        var error = await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(WorkDecisionHostV1.InventoryTransferLoss, Item, body, "invalid", default));
        Assert.Equal(fault == "unknown-request" ? 409 : 400, error.Status); Assert.Null(run.Captured);
    }

    [Fact]
    public async Task Maker_scope_and_revoked_delegation_are_refused_before_owner_effects()
    {
        var run = new Rig(); run.Source.CurrentMaker = Actor;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.PreviewAsync(WorkDecisionHostV1.InventoryTransferLoss, Item, default))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(WorkDecisionHostV1.InventoryTransferLoss, Item, Body(true), "own", default))).Status);
        run.Source.CurrentMaker = Maker; run.Raw.CompanyWide = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.PreviewAsync(WorkDecisionHostV1.InventoryTransferLoss, Item, default))).Status);
        run.Raw.CompanyWide = true;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(WorkDecisionHostV1.InventoryTransferLoss, Item,
            Body(true) with { DelegationId = Guid.NewGuid() }, "no-delegation", default))).Status);
        Assert.Null(run.Captured);
    }

    [Fact]
    public void Loss_purpose_capability_keeps_existing_native_loss_screen_and_permission()
    {
        var declarations = new GoymenERP.Modules.Inventory.Infrastructure.Shell.InventoryShellCapabilityProvider().GetDeclarations();
        var purpose = Assert.Single(declarations, row => row.CapabilityId == "inventory.transfer-loss.work-decision");
        Assert.True(purpose.ImplementationAvailable); Assert.Null(purpose.ParentCapabilityId);
        Assert.Equal("/onaylarim/inventory.transfer-loss.v1/:approvalId", purpose.RoutePath);
        Assert.Equal([InventoryPermissionManifestV1.StockTransferLossApprove], purpose.RequiredPermissions);
        Assert.Contains(declarations, row => row.RoutePath == "/stok/transferler/kayip-onaylari" && row.ImplementationAvailable);
    }

    [Fact]
    public void Additive_wire_members_are_omitted_for_legacy_and_manufacturing_requests()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Assert.DoesNotContain("inventoryTransferLoss", JsonSerializer.Serialize(new WorkDecisionRequestV1("2", true, null), options));
        Assert.DoesNotContain("inventoryTransferLoss", JsonSerializer.Serialize(new WorkDecisionRequestV1("2", true, null,
            ManufacturingPlanReapproval: new([Guid.NewGuid()])), options));
        Assert.DoesNotContain("manufacturingPlanReapproval", JsonSerializer.Serialize(Body(true), options));
        Assert.Contains("\"requestNumber\":1", JsonSerializer.Serialize(Body(true), options));
    }

    private static WorkDecisionRequestV1 Body(bool approve) => new("2", approve, "  actual human reason  ", InventoryTransferLoss: new(1, At.AddHours(1)));
    private sealed class Rig
    {
        public readonly LossSource Source = new(); public readonly LossAuthority Raw = new();
        public DecideInventoryTransferLossV1? Captured; public bool Replayed;
        public Func<InventoryTransferReceiptResultV1, InventoryTransferReceiptResultV1>? Transform;
        public WorkDecisionHostV1 Host { get; }
        public Rig(IWorkDelegationReaderV1? delegations = null)
        {
            var company = new CompanyContext(); company.Set(Company);
            var owner = DispatchProxy.Create<IInventoryTransferReceiptCommandV1, OwnerProxy>();
            ((OwnerProxy)(object)owner).Handler = (method, args) =>
            {
                if (method.Name != "DecideLossAsync") throw new NotSupportedException();
                Captured = (DecideInventoryTransferLossV1)args[2]!;
                var approve = Captured.Decision == InventoryTransferDecisionKindV1.Approve;
                var version = approve ? "3" : "2"; var status = approve ? "Closed" : "InTransit";
                var transfer = new InventoryTransferViewV1(Transfer, Company, version, status, Company, "Company", Company,
                    Company, "Company", Company, At, At, [new(Line, Company, "1", "10", "10", "0", approve ? "WrittenOff" : "InTransit")]);
                var facts = new InventoryTransferReceiptFactsViewV1(Transfer, Company, version, status, [],
                    [new(Line, Captured.RequestNumber!.Value, "SENTETIK", At, Maker, approve ? "Approved" : "Rejected",
                        Captured.Reason.Trim(), Captured.DecidedAtUtc, Actor, approve ? Guid.NewGuid() : null)], []);
                var answer = new InventoryTransferReceiptResultV1(transfer, facts, Replayed, "SENTETIK-original", CommandReceiptProven: true);
                var wire = JsonSerializer.Serialize(answer, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                Assert.DoesNotContain("commandReceiptProven", wire);
                Assert.Equal(JsonSerializer.Serialize(answer with { CommandReceiptProven = false }, new JsonSerializerOptions(JsonSerializerDefaults.Web)), wire);
                return Task.FromResult(Transform?.Invoke(answer) ?? answer);
            };
            Host = CreateHost(company, new LossUser(Company, Actor), Raw, Source, owner, delegations: delegations);
        }
    }
    internal static WorkDecisionHostV1 CreateHost(ICompanyContext company, ICurrentUser user, IErpAuthorityRevalidationService authority,
        IWorkInboxSourceV1 source, IInventoryTransferReceiptCommandV1 owner, WorkDecisionExecutionFenceV1? execution = null, IWorkDelegationReaderV1? delegations = null)
    {
        var correlation = new CorrelationContext(); correlation.Set("SENTETIK-work", null, "Web", null, null);
        return new(company, user, correlation, authority, [source], delegations ?? new NoDelegations(), execution ?? new(), null!, null!, inventoryTransferReceipts: owner);
    }
    public class OwnerProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[], object?> Handler { get; set; } = (_, _) => throw new NotSupportedException();
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args!);
    }
    private sealed class LossSource : IWorkApprovalDecisionSourceV1
    {
        public string CurrentMaker = Maker, HistoricalMaker = Maker; public IReadOnlyList<string>? Excluded; public bool MissingProof; private bool renewed;
        public void Renew() => renewed = true;
        public string Module => "inventory"; public string Kind => WorkDecisionHostV1.InventoryTransferLoss;
        public string ApproverPermission => InventoryPermissionManifestV1.StockTransferLossApprove;
        public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId, CancellationToken cancellationToken) =>
            Task.FromResult<WorkApprovalDecisionSnapshotV1?>(new(Company, new(Module, Kind, Item, "SENTETIK", At, "/stok/transferler/kayip-onaylari", CurrentMaker, [], [], Excluded),
                "2", true, InventoryTransferLoss: new(renewed ? 2 : 1, At), InventoryTransferLossProof: MissingProof ? null : new(Transfer, Line,
                    renewed ? [new(1, At, HistoricalMaker, "Rejected"), new(2, At, CurrentMaker, "Pending")] : [new(1, At, CurrentMaker, "Pending")])));
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 context, int maxCount, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    internal sealed class LossUser(Guid company, string actor) : ICurrentUser
    {
        public bool IsAuthenticated => true; public string UserId => actor;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([company]);
        public bool CanAccessCompany(Guid companyId) => companyId == company;
    }
    internal sealed class LossAuthority : IErpAuthorityRevalidationService
    {
        public bool CompanyWide = true;
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PermissionScope(true, CompanyWide, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
    }
    private sealed class MakerAwareDelegation : IWorkDelegationReaderV1
    {
        public bool Allowed = true; public List<WorkInboxItemV1> Items = [];
        public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 item, CancellationToken cancellationToken = default)
        {
            Items.Add(item);
            return Task.FromResult<WorkDelegationViewV1?>(Allowed && !item.Excludes(Actor) ? new(id, Company, item.Kind,
                InventoryPermissionManifestV1.StockTransferLossApprove, Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddDays(1), "1", Guid.NewGuid(), DateTimeOffset.UtcNow, "SENTETIK live delegation", null, null, null) : null);
        }
    }
    private sealed class NoDelegations : IWorkDelegationReaderV1
    {
        public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 item, CancellationToken cancellationToken = default) => Task.FromResult<WorkDelegationViewV1?>(null);
    }
}
