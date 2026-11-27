using System.Reflection;
using System.Text.Json;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Work.Infrastructure.Delegations;
using H = GoymenERP.Api.IntegrationTests.WorkInventoryTransferLossDecisionHostTests;

namespace GoymenERP.Api.IntegrationTests;

public sealed class WorkInventoryStockCountDecisionHostTests
{
    private static readonly Guid Company = Guid.NewGuid(), Id = Guid.NewGuid(), Label = Guid.NewGuid(), Location = Guid.NewGuid(), Product = Guid.NewGuid();
    private static readonly DateTimeOffset At = new(2026, 11, 27, 1, 0, 0, TimeSpan.Zero);
    private const string Actor = "SENTETIK-count-checker", Maker = "SENTETIK-count-maker";

    [Theory]
    [InlineData(true, false)] [InlineData(false, false)] [InlineData(true, true)] [InlineData(false, true)]
    public async Task Native_two_decisions_keep_full_1000_reason_and_exact_identity_key_and_receipt(bool approve, bool replay)
    {
        var r = new Rig { Replay = replay }; var reason = "  " + new string('x', 1000) + "  ";
        var preview = await r.Host.PreviewAsync(WorkDecisionHostV1.InventoryStockCount, Id, default);
        Assert.True(preview.ApprovalReasonRequired); Assert.True(preview.RejectionSupported);
        Assert.Equal("1", preview.SourceVersion); Assert.Equal(r.Source.Snapshot.InventoryStockCountProof!.Count, preview.InventoryStockCount);
        var result = await r.Host.DecideAsync(WorkDecisionHostV1.InventoryStockCount, Id, new("1", approve, reason), "same-key", default);
        Assert.Equal((approve ? "Approved" : "Rejected", replay), (result.State, result.Replayed));
        Assert.Equal(new DecideInventoryStockCountV1(approve ? InventoryStockCountDecisionKindV1.Approve : InventoryStockCountDecisionKindV1.Reject, reason), r.Body);
        Assert.Equal(Id, r.CapturedId); Assert.Equal("same-key", r.Metadata!.IdempotencyKey);
        Assert.Equal("SENTETIK-work", r.Metadata.CorrelationId); Assert.Equal(Actor, r.Metadata.ActorId);
        Assert.All(r.Authority.Calls, permission => Assert.Equal(InventoryPermissionManifestV1.StockCountApprove, permission));
    }

    [Theory]
    [InlineData(true, null)] [InlineData(false, null)] [InlineData(true, "")] [InlineData(false, " ")]
    [InlineData(true, "too-long")] [InlineData(false, "too-long")]
    public async Task Empty_or_over_1000_native_reason_never_calls_owner(bool approve, string? reason)
    {
        var r = new Rig(); if (reason == "too-long") reason = new string('x', 1001);
        Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Host.DecideAsync(
            WorkDecisionHostV1.InventoryStockCount, Id, new("1", approve, reason), "key", default))).Status);
        Assert.Null(r.Body);
    }

    [Theory]
    [InlineData("0")] [InlineData("01")] [InlineData("2")] [InlineData("7")]
    public async Task Only_original_logical_pending_version_can_target_native_decision_or_receipt(string version)
    {
        var r = new Rig();
        Assert.Equal(409, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Host.DecideAsync(
            WorkDecisionHostV1.InventoryStockCount, Id, new(version, true, "Doğrulandı"), "key", default))).Status);
        Assert.Null(r.Body);
    }

    [Fact]
    public async Task Terminal_preview_keeps_original_label_proof_and_accepts_only_genuine_original_key_replay()
    {
        var r = new Rig { Replay = true };
        r.Source.Snapshot = r.Source.Snapshot with { Pending = false, SourceVersion = "2",
            InventoryStockCountProof = r.Source.Snapshot.InventoryStockCountProof! with { Status = "Approved" } };
        var preview = await r.Host.PreviewAsync(WorkDecisionHostV1.InventoryStockCount, Id, default);
        Assert.False(preview.Pending); Assert.Equal("2", preview.SourceVersion); Assert.Equal("7", preview.InventoryStockCount!.LabelVersion);
        Assert.True((await r.Host.DecideAsync(WorkDecisionHostV1.InventoryStockCount, Id, new("1", true, "Doğrulandı"), "original", default)).Replayed);
        r.Transform = result => result with { CommandReceiptProven = false };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Host.DecideAsync(
            WorkDecisionHostV1.InventoryStockCount, Id, new("1", true, "Doğrulandı"), "different-key", default))).Status);
        r.Transform = null; r.Replay = false;
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Host.DecideAsync(
            WorkDecisionHostV1.InventoryStockCount, Id, new("1", true, "Doğrulandı"), "impossible-fresh", default))).Status);
    }

    [Theory]
    [InlineData("company")] [InlineData("count")] [InlineData("label-version")] [InlineData("quantity")]
    [InlineData("decision")] [InlineData("reason")] [InlineData("movement")] [InlineData("correlation")]
    [InlineData("missing-receipt")] [InlineData("time")] [InlineData("position")] [InlineData("count-reason")]
    public async Task Malformed_native_success_is_never_Work_success(string fault)
    {
        var r = new Rig();
        r.Transform = result => fault switch
        {
            "missing-receipt" => result with { CommandReceiptProven = false },
            "correlation" => result with { OriginalCorrelationId = "foreign-correlation" },
            _ => result with { Count = fault switch
            {
                "company" => result.Count with { CompanyId = Guid.NewGuid() }, "count" => result.Count with { CountId = Guid.NewGuid() },
                "label-version" => result.Count with { LabelVersion = "8" }, "quantity" => result.Count with { CountedKg = "102" },
                "position" => result.Count with { OwnerId = Guid.NewGuid() }, "count-reason" => result.Count with { Reason = "invented" },
                _ => result.Count with { Decision = fault switch
                { "decision" => result.Count.Decision! with { Decision = "Rejected" }, "reason" => result.Count.Decision! with { Reason = "invented" },
                  "movement" => result.Count.Decision! with { AdjustmentMovementId = null }, "time" => result.Count.Decision! with { DecidedAtUtc = default }, _ => throw new NotSupportedException() } },
            } },
        };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Host.DecideAsync(
            WorkDecisionHostV1.InventoryStockCount, Id, new("1", true, "Doğrulandı"), "key", default))).Status);
    }

    [Theory]
    [InlineData("maker", 403)] [InlineData("foreign-company", 503)] [InlineData("proof", 503)]
    [InlineData("version", 503)] [InlineData("scope", 503)] [InlineData("grant", 403)] [InlineData("companywide", 403)]
    public async Task Scope_maker_and_owner_proof_fail_closed_before_native_call(string fault, int status)
    {
        var r = new Rig(); var s = r.Source.Snapshot;
        r.Source.Snapshot = fault switch
        {
            "maker" => s with { Item = s.Item with { PreparedBy = Actor }, InventoryStockCountProof = s.InventoryStockCountProof! with { Count = s.InventoryStockCountProof.Count with { RecordedBy = Actor } } },
            "foreign-company" => s with { CompanyId = Guid.NewGuid() }, "proof" => s with { InventoryStockCountProof = null },
            "version" => s with { SourceVersion = "7" }, "scope" => s with { Item = s.Item with { Scope = new(null, Location, null) } }, _ => s,
        };
        if (fault == "grant") r.Authority.Granted = false;
        if (fault == "companywide") r.Authority.CompanyWide = false;
        Assert.Equal(status, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Host.DecideAsync(
            WorkDecisionHostV1.InventoryStockCount, Id, new("1", true, "Doğrulandı"), "key", default))).Status);
        Assert.Null(r.Body);
    }

    [Fact]
    public async Task Proof_is_preview_only_and_internal_receipt_flag_never_changes_native_wire()
    {
        var r = new Rig(); var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Assert.DoesNotContain("SENTETIK-private-count-reason", JsonSerializer.Serialize(r.Source.Snapshot, json));
        Assert.Contains("SENTETIK-private-count-reason", JsonSerializer.Serialize(await r.Host.PreviewAsync(WorkDecisionHostV1.InventoryStockCount, Id, default), json));
        await r.Host.DecideAsync(WorkDecisionHostV1.InventoryStockCount, Id, new("1", true, "Doğrulandı"), "key", default);
        Assert.Equal(JsonSerializer.Serialize(r.Answer! with { CommandReceiptProven = false }, json), JsonSerializer.Serialize(r.Answer, json));
        Assert.DoesNotContain("commandReceiptProven", JsonSerializer.Serialize(r.Answer, json));
        Assert.DoesNotContain("inventoryStockCount", JsonSerializer.Serialize(new WorkDecisionRequestV1("1", true, "Doğrulandı"), json));
    }

    private sealed class Rig
    {
        public Source Source { get; } = new(); public CountAuthority Authority { get; } = new();
        public bool Replay; public Guid CapturedId; public DecideInventoryStockCountV1? Body; public InventoryCommandMetadata? Metadata;
        public InventoryStockCountResultV1? Answer; public Func<InventoryStockCountResultV1, InventoryStockCountResultV1>? Transform;
        public WorkDecisionHostV1 Host { get; }
        public Rig()
        {
            var owner = DispatchProxy.Create<IInventoryStockCountCommandV1, H.OwnerProxy>();
            ((H.OwnerProxy)(object)owner).Handler = (method, args) =>
            {
                Assert.Equal("DecideAsync", method.Name); CapturedId = (Guid)args[0]!; Body = (DecideInventoryStockCountV1)args[1]!;
                Metadata = (InventoryCommandMetadata)args[2]!; var p = Source.Snapshot.InventoryStockCountProof!.Count;
                var approve = Body.Decision == InventoryStockCountDecisionKindV1.Approve;
                var count = new InventoryStockCountViewV1(Id, Company, p.LabelId, p.LabelVersion, p.OwnerKind, p.OwnerId,
                    p.CustodianKind, p.CustodianId, p.LocationId, p.ProductId, p.Block, p.BookKg, p.CountedKg, p.DifferenceKg,
                    p.CountedAtUtc, p.Reason, p.RecordedAtUtc, approve ? "Approved" : "Rejected",
                    new(approve ? "Approved" : "Rejected", At.AddHours(2), Body.Reason.Trim(), approve ? Guid.NewGuid() : null), p.BookPieceCount, p.CountedPieceCount);
                Answer = new(count, Replay, Replay ? "SENTETIK-original" : Metadata.CorrelationId, true);
                return Task.FromResult(Transform?.Invoke(Answer) ?? Answer);
            };
            var company = new CompanyContext(); company.Set(Company);
            var correlation = new CorrelationContext(); correlation.Set("SENTETIK-work", null, "Web", null, null);
            Host = new(company, new H.LossUser(Company, Actor), correlation, Authority, [Source], new CountDelegations(), new(), null!, null!, stockCounts: owner);
        }
    }
    private sealed class Source : IWorkApprovalDecisionSourceV1
    {
        public string Module => "inventory"; public string Kind => WorkDecisionHostV1.InventoryStockCount;
        public string ApproverPermission => InventoryPermissionManifestV1.StockCountApprove;
        public WorkApprovalDecisionSnapshotV1 Snapshot = new(Company,
            new("inventory", WorkDecisionHostV1.InventoryStockCount, Id, "SENTETIK-count", At.AddHours(1), "/onaylarim/inventory.stock-count.v1/" + Id,
                Maker, [], [new("inventory-label", "SENTETIK", Label, null), new("product", "SENTETIK", Product, null), new("location", "SENTETIK", Location, null)]),
            "1", true, InventoryStockCountProof: new(Company, Id, new(Label, "7", "Company", Company, "Company", Company,
                Location, Product, "Free", "100", "101", "1", At, "SENTETIK-private-count-reason", Maker, At.AddHours(1), null, null), "PendingApproval"));
        public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid id, CancellationToken token) => Task.FromResult<WorkApprovalDecisionSnapshotV1?>(Snapshot);
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 context, int maxCount, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class CountAuthority : IErpAuthorityRevalidationService
    {
        public bool Granted = true, CompanyWide = true; public List<string> Calls = [];
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid company, CancellationToken token = default)
        { Calls.Add(permission); return Task.FromResult(new PermissionScope(Granted, CompanyWide, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())); }
    }
    private sealed class CountDelegations : IWorkDelegationReaderV1
    { public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 item, CancellationToken token = default) => Task.FromResult<WorkDelegationViewV1?>(null); }
}
