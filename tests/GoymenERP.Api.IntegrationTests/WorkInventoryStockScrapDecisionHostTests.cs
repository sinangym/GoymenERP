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

public sealed class WorkInventoryStockScrapDecisionHostTests
{
    private static readonly Guid Company = Guid.NewGuid(), Id = Guid.NewGuid(), Label = Guid.NewGuid(), Location = Guid.NewGuid(), Product = Guid.NewGuid();
    private static readonly DateTimeOffset At = new(2026, 11, 27, 1, 0, 0, TimeSpan.Zero);
    private const string Actor = "SENTETIK-scrap-checker", Maker = "SENTETIK-scrap-maker";

    [Theory]
    [InlineData(true, false)] [InlineData(false, false)] [InlineData(true, true)] [InlineData(false, true)]
    public async Task Native_two_decisions_keep_full_1000_reason_and_exact_identity_key_and_receipt(bool approve, bool replay)
    {
        var r = new Rig { Replay = replay }; var reason = "  " + new string('x', 1000) + "  ";
        var preview = await r.Host.PreviewAsync(WorkDecisionHostV1.InventoryStockScrap, Id, default);
        Assert.True(preview.ApprovalReasonRequired); Assert.True(preview.RejectionSupported);
        Assert.Equal("1", preview.SourceVersion); Assert.Equal(r.Source.Snapshot.InventoryStockScrapProof!.Scrap, preview.InventoryStockScrap);
        var result = await r.Host.DecideAsync(WorkDecisionHostV1.InventoryStockScrap, Id, new("1", approve, reason), "same-key", default);
        Assert.Equal((approve ? "Approved" : "Rejected", replay), (result.State, result.Replayed));
        Assert.Equal(new DecideInventoryStockScrapV1(approve ? InventoryStockScrapDecisionKindV1.Approve : InventoryStockScrapDecisionKindV1.Reject, reason), r.Body);
        Assert.Equal(Id, r.CapturedId); Assert.Equal("same-key", r.Metadata!.IdempotencyKey);
        Assert.Equal("SENTETIK-work", r.Metadata.CorrelationId); Assert.Equal(Actor, r.Metadata.ActorId);
        Assert.All(r.Authority.Calls, permission => Assert.Equal(InventoryPermissionManifestV1.StockScrapApprove, permission));
    }

    [Theory]
    [InlineData(true, null)] [InlineData(false, null)] [InlineData(true, "")] [InlineData(false, " ")]
    [InlineData(true, "too-long")] [InlineData(false, "too-long")]
    public async Task Empty_or_over_1000_native_reason_never_calls_owner(bool approve, string? reason)
    {
        var r = new Rig(); if (reason == "too-long") reason = new string('x', 1001);
        Assert.Equal(400, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Host.DecideAsync(
            WorkDecisionHostV1.InventoryStockScrap, Id, new("1", approve, reason), "key", default))).Status);
        Assert.Null(r.Body);
    }

    [Theory]
    [InlineData("0")] [InlineData("01")] [InlineData("2")] [InlineData("7")]
    public async Task Only_original_logical_pending_version_can_target_native_decision_or_receipt(string version)
    {
        var r = new Rig();
        Assert.Equal(409, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Host.DecideAsync(
            WorkDecisionHostV1.InventoryStockScrap, Id, new(version, true, "Doğrulandı"), "key", default))).Status);
        Assert.Null(r.Body);
    }

    [Fact]
    public async Task Terminal_preview_keeps_original_label_proof_and_accepts_only_genuine_original_key_replay()
    {
        var r = new Rig { Replay = true };
        r.Source.Snapshot = r.Source.Snapshot with { Pending = false, SourceVersion = "2",
            InventoryStockScrapProof = r.Source.Snapshot.InventoryStockScrapProof! with { Status = "Approved" } };
        var preview = await r.Host.PreviewAsync(WorkDecisionHostV1.InventoryStockScrap, Id, default);
        Assert.False(preview.Pending); Assert.Equal("2", preview.SourceVersion); Assert.Equal("7", preview.InventoryStockScrap!.LabelVersion);
        Assert.True((await r.Host.DecideAsync(WorkDecisionHostV1.InventoryStockScrap, Id, new("1", true, "Doğrulandı"), "original", default)).Replayed);
        r.Transform = result => result with { CommandReceiptProven = false };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Host.DecideAsync(
            WorkDecisionHostV1.InventoryStockScrap, Id, new("1", true, "Doğrulandı"), "different-key", default))).Status);
        r.Transform = null; r.Replay = false;
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Host.DecideAsync(
            WorkDecisionHostV1.InventoryStockScrap, Id, new("1", true, "Doğrulandı"), "impossible-fresh", default))).Status);
    }

    [Theory]
    [InlineData("company")] [InlineData("scrap")] [InlineData("label-version")] [InlineData("quantity")]
    [InlineData("decision")] [InlineData("reason")] [InlineData("movement")] [InlineData("correlation")]
    [InlineData("missing-receipt")] [InlineData("time")] [InlineData("position")] [InlineData("scrap-reason")]
    public async Task Malformed_native_success_is_never_Work_success(string fault)
    {
        var r = new Rig();
        r.Transform = result => fault switch
        {
            "missing-receipt" => result with { CommandReceiptProven = false },
            "correlation" => result with { OriginalCorrelationId = "foreign-correlation" },
            _ => result with { Scrap = fault switch
            {
                "company" => result.Scrap with { CompanyId = Guid.NewGuid() }, "scrap" => result.Scrap with { ScrapId = Guid.NewGuid() },
                "label-version" => result.Scrap with { LabelVersion = "8" }, "quantity" => result.Scrap with { ScrapKg = "102" },
                "position" => result.Scrap with { OwnerId = Guid.NewGuid() }, "scrap-reason" => result.Scrap with { Reason = "invented" },
                _ => result.Scrap with { Decision = fault switch
                { "decision" => result.Scrap.Decision! with { Decision = "Rejected" }, "reason" => result.Scrap.Decision! with { Reason = "invented" },
                  "movement" => result.Scrap.Decision! with { ScrapMovementId = null }, "time" => result.Scrap.Decision! with { DecidedAtUtc = default }, _ => throw new NotSupportedException() } },
            } },
        };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Host.DecideAsync(
            WorkDecisionHostV1.InventoryStockScrap, Id, new("1", true, "Doğrulandı"), "key", default))).Status);
    }

    [Theory]
    [InlineData("maker", 403)] [InlineData("foreign-company", 503)] [InlineData("proof", 503)]
    [InlineData("version", 503)] [InlineData("scope", 503)] [InlineData("grant", 403)] [InlineData("companywide", 403)]
    public async Task Scope_maker_and_owner_proof_fail_closed_before_native_call(string fault, int status)
    {
        var r = new Rig(); var s = r.Source.Snapshot;
        r.Source.Snapshot = fault switch
        {
            "maker" => s with { Item = s.Item with { PreparedBy = Actor }, InventoryStockScrapProof = s.InventoryStockScrapProof! with { Scrap = s.InventoryStockScrapProof.Scrap with { RecordedBy = Actor } } },
            "foreign-company" => s with { CompanyId = Guid.NewGuid() }, "proof" => s with { InventoryStockScrapProof = null },
            "version" => s with { SourceVersion = "7" }, "scope" => s with { Item = s.Item with { Scope = new(null, Location, null) } }, _ => s,
        };
        if (fault == "grant") r.Authority.Granted = false;
        if (fault == "companywide") r.Authority.CompanyWide = false;
        Assert.Equal(status, (await Assert.ThrowsAsync<WorkDecisionException>(() => r.Host.DecideAsync(
            WorkDecisionHostV1.InventoryStockScrap, Id, new("1", true, "Doğrulandı"), "key", default))).Status);
        Assert.Null(r.Body);
    }

    [Fact]
    public async Task Proof_is_preview_only_and_internal_receipt_flag_never_changes_native_wire()
    {
        var r = new Rig(); var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Assert.DoesNotContain("SENTETIK-private-scrap-reason", JsonSerializer.Serialize(r.Source.Snapshot, json));
        Assert.Contains("SENTETIK-private-scrap-reason", JsonSerializer.Serialize(await r.Host.PreviewAsync(WorkDecisionHostV1.InventoryStockScrap, Id, default), json));
        await r.Host.DecideAsync(WorkDecisionHostV1.InventoryStockScrap, Id, new("1", true, "Doğrulandı"), "key", default);
        Assert.Equal(JsonSerializer.Serialize(r.Answer! with { CommandReceiptProven = false }, json), JsonSerializer.Serialize(r.Answer, json));
        Assert.DoesNotContain("commandReceiptProven", JsonSerializer.Serialize(r.Answer, json));
        Assert.DoesNotContain("inventoryStockScrap", JsonSerializer.Serialize(new WorkDecisionRequestV1("1", true, "Doğrulandı"), json));
    }

    [Fact]
    public async Task Other_owner_kind_cannot_carry_private_scrap_proof_and_new_field_is_omitted_from_legacy_preview()
    {
        var r = new Rig();
        var foreign = r.Source.Snapshot with { Item = r.Source.Snapshot.Item with { Kind = "inventory.opening-cost.v1" } };
        var other = new OtherSource(foreign);
        var company = new CompanyContext(); company.Set(Company);
        var correlation = new CorrelationContext(); correlation.Set("SENTETIK-work", null, "Web", null, null);
        var host = new WorkDecisionHostV1(company, new H.LossUser(Company, Actor), correlation, r.Authority,
            [other], new ScrapDelegations(), new(), null!, null!);
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => host.PreviewAsync("inventory.opening-cost.v1", Id, default))).Status);
        var legacy = new WorkDecisionPreviewV1(Company, "inventory.opening-cost.v1", Id, "SENTETIK", "1", true, true, [], []);
        Assert.DoesNotContain("inventoryStockScrap", JsonSerializer.Serialize(legacy, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private sealed class OtherSource(WorkApprovalDecisionSnapshotV1 snapshot) : IWorkApprovalDecisionSourceV1
    {
        public string Module => "inventory";
        public string Kind => "inventory.opening-cost.v1";
        public string ApproverPermission => InventoryPermissionManifestV1.OpeningCostApprove;
        public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid id, CancellationToken token)
            => Task.FromResult<WorkApprovalDecisionSnapshotV1?>(snapshot);
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 context, int maxCount, CancellationToken token)
            => throw new NotSupportedException();
    }

    private sealed class Rig
    {
        public Source Source { get; } = new(); public ScrapAuthority Authority { get; } = new();
        public bool Replay; public Guid CapturedId; public DecideInventoryStockScrapV1? Body; public InventoryCommandMetadata? Metadata;
        public InventoryStockScrapResultV1? Answer; public Func<InventoryStockScrapResultV1, InventoryStockScrapResultV1>? Transform;
        public WorkDecisionHostV1 Host { get; }
        public Rig()
        {
            var owner = DispatchProxy.Create<IInventoryStockScrapCommandV1, H.OwnerProxy>();
            ((H.OwnerProxy)(object)owner).Handler = (method, args) =>
            {
                Assert.Equal("DecideAsync", method.Name); CapturedId = (Guid)args[0]!; Body = (DecideInventoryStockScrapV1)args[1]!;
                Metadata = (InventoryCommandMetadata)args[2]!; var p = Source.Snapshot.InventoryStockScrapProof!.Scrap;
                var approve = Body.Decision == InventoryStockScrapDecisionKindV1.Approve;
                var scrap = new InventoryStockScrapViewV1(Id, Company, p.LabelId, p.LabelVersion, p.OwnerKind, p.OwnerId,
                    p.CustodianKind, p.CustodianId, p.LocationId, p.ProductId, p.Block, p.BookKg, p.ScrapKg, p.RemainingKg,
                    p.ScrappedAtUtc, p.Reason, p.RecordedAtUtc, approve ? "Approved" : "Rejected",
                    new(approve ? "Approved" : "Rejected", At.AddHours(2), Body.Reason.Trim(), approve ? Guid.NewGuid() : null), p.BookPieceCount, p.ScrapPieceCount);
                Answer = new(scrap, Replay, Replay ? "SENTETIK-original" : Metadata.CorrelationId, true);
                return Task.FromResult(Transform?.Invoke(Answer) ?? Answer);
            };
            var company = new CompanyContext(); company.Set(Company);
            var correlation = new CorrelationContext(); correlation.Set("SENTETIK-work", null, "Web", null, null);
            Host = new(company, new H.LossUser(Company, Actor), correlation, Authority, [Source], new ScrapDelegations(), new(), null!, null!, stockScraps: owner);
        }
    }
    private sealed class Source : IWorkApprovalDecisionSourceV1
    {
        public string Module => "inventory"; public string Kind => WorkDecisionHostV1.InventoryStockScrap;
        public string ApproverPermission => InventoryPermissionManifestV1.StockScrapApprove;
        public WorkApprovalDecisionSnapshotV1 Snapshot = new(Company,
            new("inventory", WorkDecisionHostV1.InventoryStockScrap, Id, "SENTETIK-scrap", At.AddHours(1), "/onaylarim/inventory.stock-scrap.v1/" + Id,
                Maker, [], [new("inventory-label", "SENTETIK", Label, null), new("product", "SENTETIK", Product, null), new("location", "SENTETIK", Location, null)]),
            "1", true, InventoryStockScrapProof: new(Company, Id, new(Label, "7", "Company", Company, "Company", Company,
                Location, Product, "Free", "100", "1", "99", At, "SENTETIK-private-scrap-reason", Maker, At.AddHours(1), null, null), "PendingApproval"));
        public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid id, CancellationToken token) => Task.FromResult<WorkApprovalDecisionSnapshotV1?>(Snapshot);
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 context, int maxCount, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class ScrapAuthority : IErpAuthorityRevalidationService
    {
        public bool Granted = true, CompanyWide = true; public List<string> Calls = [];
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid company, CancellationToken token = default)
        { Calls.Add(permission); return Task.FromResult(new PermissionScope(Granted, CompanyWide, new HashSet<string>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())); }
    }
    private sealed class ScrapDelegations : IWorkDelegationReaderV1
    { public Task<WorkDelegationViewV1?> FindForActorAsync(Guid id, WorkInboxItemV1 item, CancellationToken token = default) => Task.FromResult<WorkDelegationViewV1?>(null); }
}
