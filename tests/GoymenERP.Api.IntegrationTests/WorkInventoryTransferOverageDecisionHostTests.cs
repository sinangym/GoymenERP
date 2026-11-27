using System.Reflection;
using System.Text.Json;
using GoymenERP.Api.WorkRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Shell;
using H = GoymenERP.Api.IntegrationTests.WorkInventoryTransferLossDecisionHostTests;

namespace GoymenERP.Api.IntegrationTests;

// Controlled owner receipts only; actual native SQL is covered by the relational source partial.
public sealed class WorkInventoryTransferOverageDecisionHostTests
{
    private static readonly Guid Company = Guid.NewGuid(), Transfer = Guid.NewGuid(), Line = Guid.NewGuid(), Item = Guid.NewGuid();
    private static readonly DateTimeOffset At = new(2026, 11, 27, 1, 0, 0, TimeSpan.Zero);
    private const string Actor = "SENTETIK-checker", Receiver = "SENTETIK-receiver", Maker = "SENTETIK-requester";
    private static WorkDecisionRequestV1 Body(bool approve = true) => new("2", approve, "  actual human reason  ", InventoryTransferOverage: new(2, At.AddHours(2)));

    [Theory]
    [InlineData(true, false)] [InlineData(false, false)] [InlineData(true, true)] [InlineData(false, true)]
    public async Task Both_decisions_preserve_native_version_number_time_reason_and_immutable_receipt(bool approve, bool replay)
    {
        var run = new Rig { Replay = replay };
        var preview = await run.Host.PreviewAsync(run.Source.Kind, Item, default);
        Assert.True(preview.ApprovalReasonRequired); Assert.True(preview.RejectionSupported);
        Assert.Equal(new WorkInventoryTransferOveragePreviewV1(2, At, Transfer, Line, "5", 0, "SENTETIK corrected reason", Maker, Receiver), preview.InventoryTransferOverage);
        var result = await run.Host.DecideAsync(run.Source.Kind, Item, Body(approve), "original-key", default);
        Assert.Equal((approve ? "Approved" : "Rejected", replay), (result.State, result.Replayed));
        Assert.Equal(new DecideInventoryTransferOverageV1("2", approve ? InventoryTransferDecisionKindV1.Approve : InventoryTransferDecisionKindV1.Reject,
            At.AddHours(2), "  actual human reason  ", 2), run.Captured);
    }

    [Fact]
    public async Task Original_old_key_can_replay_when_current_request_belongs_to_checker_but_fresh_self_decision_is_forbidden()
    {
        var run = new Rig { Replay = true }; run.Source.Renewed = true; run.Source.CurrentMaker = Actor;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.PreviewAsync(run.Source.Kind, Item, default))).Status);
        Assert.True((await run.Host.DecideAsync(run.Source.Kind, Item, Body(false), "old-key", default)).Replayed);
        Assert.Equal(2, run.Captured!.RequestNumber);
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(run.Source.Kind, Item,
            Body() with { InventoryTransferOverage = new(3, At.AddHours(3)) }, "fresh-own", default))).Status);
        run.Transform = answer => answer with { CommandReceiptProven = false };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(run.Source.Kind, Item, Body(false), "different-key-domain-answer", default))).Status);
        run.Source.HistoricalMaker = Actor;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(run.Source.Kind, Item, Body(false), "old-own", default))).Status);
        run.Source.HistoricalMaker = Maker; run.Source.Receiver = Actor;
        Assert.Equal(403, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(run.Source.Kind, Item, Body(false), "receiver", default))).Status);
    }

    [Theory]
    [InlineData("missing")] [InlineData("mixed-loss")] [InlineData("mixed-mfg")] [InlineData("mixed-logistics")]
    [InlineData("reason")] [InlineData("time")] [InlineData("number")] [InlineData("version")]
    public async Task Invalid_or_mixed_typed_payload_never_reaches_owner(string fault)
    {
        var run = new Rig(); var body = Body();
        body = fault switch
        {
            "missing" => body with { InventoryTransferOverage = null }, "mixed-loss" => body with { InventoryTransferLoss = new(1, At) },
            "mixed-mfg" => body with { ManufacturingPlanReapproval = new([Guid.NewGuid()]) },
            "mixed-logistics" => body with { LogisticsPlanExcess = new("2") }, "reason" => body with { Reason = " " },
            "time" => body with { InventoryTransferOverage = new(2, At.ToOffset(TimeSpan.FromHours(3))) },
            "number" => body with { InventoryTransferOverage = new(99, At) }, _ => body with { ExpectedVersion = "02" }
        };
        var error = await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(run.Source.Kind, Item, body, "key", default));
        Assert.Equal(fault == "number" ? 409 : 400, error.Status); Assert.Null(run.Captured);
    }

    [Theory]
    [InlineData("company")] [InlineData("version")] [InlineData("identity")] [InlineData("maker")]
    [InlineData("reason")] [InlineData("movement")] [InlineData("time")] [InlineData("provenance")]
    public async Task Malformed_native_success_never_becomes_Work_success(string fault)
    {
        var run = new Rig(); run.Transform = answer => fault switch
        {
            "company" => answer with { Transfer = answer.Transfer with { CompanyId = Guid.NewGuid() } },
            "version" => answer with { Transfer = answer.Transfer with { Version = "3" } },
            "identity" => answer with { Facts = answer.Facts with { TransferId = Guid.NewGuid() } },
            "provenance" => answer with { CommandReceiptProven = false },
            _ => answer with { Facts = answer.Facts with { OverageRenewals = [answer.Facts.OverageRenewals[0] with
                { RequestedBy = fault == "maker" ? Actor : Maker, Reason = fault == "reason" ? "wrong" : "SENTETIK corrected reason",
                    MovementId = fault == "movement" ? null : Guid.NewGuid(), DecidedAtUtc = fault == "time" ? At : At.AddHours(2) }] } }
        };
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkDecisionException>(() => run.Host.DecideAsync(run.Source.Kind, Item, Body(), "key", default))).Status);
    }

    [Fact]
    public async Task Wire_and_staged_declaration_keep_owner_proof_private_and_existing_native_browse_available()
    {
        var run = new Rig(); var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Assert.DoesNotContain("inventoryTransferOverage", JsonSerializer.Serialize(new WorkDecisionRequestV1("2", true, null), options));
        var snapshotWire = JsonSerializer.Serialize(await run.Source.ReadDecisionAsync(new(Company, Actor, "SENTETIK", null), Item, default), options);
        Assert.DoesNotContain("inventoryTransferOverageProof", snapshotWire); Assert.DoesNotContain("corrected reason", snapshotWire);
        Assert.Contains("SENTETIK corrected reason", JsonSerializer.Serialize(await run.Host.PreviewAsync(run.Source.Kind, Item, default), options));
        var declarations = new InventoryShellCapabilityProvider().GetDeclarations();
        var row = Assert.Single(declarations, row => row.CapabilityId == "inventory.transfer-overage.work-decision");
        Assert.False(row.ImplementationAvailable); Assert.Null(row.ParentCapabilityId);
        Assert.Equal("/onaylarim/inventory.transfer-overage.v1/:approvalId", row.RoutePath);
        Assert.Equal([InventoryPermissionManifestV1.StockTransferOverageApprove], row.RequiredPermissions);
        Assert.Contains(declarations, row => row.RoutePath == "/stok/transferler/fazla-kabul-onaylari" && row.ImplementationAvailable);
    }

    private sealed class Rig
    {
        public readonly Source Source = new(); public bool Replay; public DecideInventoryTransferOverageV1? Captured;
        public Func<InventoryTransferReceiptResultV1, InventoryTransferReceiptResultV1>? Transform;
        public WorkDecisionHostV1 Host { get; }
        public Rig()
        {
            var company = new CompanyContext(); company.Set(Company);
            var owner = DispatchProxy.Create<IInventoryTransferReceiptCommandV1, H.OwnerProxy>();
            ((H.OwnerProxy)(object)owner).Handler = (method, args) =>
            {
                if (method.Name != nameof(IInventoryTransferReceiptCommandV1.DecideOverageAsync)) throw new NotSupportedException();
                Captured = (DecideInventoryTransferOverageV1)args[2]!; var approve = Captured.Decision == InventoryTransferDecisionKindV1.Approve;
                var transfer = new InventoryTransferViewV1(Transfer, Company, "2", "InTransit", Company, "Company", Company,
                    Company, "Company", Company, At, At, [new(Line, Company, "1", "10", "10", "0", "Received")]);
                var receipt = new InventoryTransferReceiptViewV1(Line, "10", "20", "0", "10", null, "SENTETIK first reason", At,
                    Receiver, "Rejected", "SENTETIK prior reason", At, Actor, null);
                var renewal = new InventoryTransferOverageRenewalViewV1(Line, 2, "5", "SENTETIK corrected reason", At, Maker,
                    approve ? "Approved" : "Rejected", Captured.Reason.Trim(), Captured.DecidedAtUtc, Actor, approve ? Guid.NewGuid() : null, 0);
                var answer = new InventoryTransferReceiptResultV1(transfer, new(Transfer, Company, "2", "InTransit", [receipt], [], [renewal]), Replay, "SENTETIK-original", true);
                Assert.DoesNotContain("commandReceiptProven", JsonSerializer.Serialize(answer, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
                return Task.FromResult(Transform?.Invoke(answer) ?? answer);
            };
            Host = H.CreateHost(company, new H.LossUser(Company, Actor), new H.LossAuthority(), Source, owner);
        }
    }
    private sealed class Source : IWorkApprovalDecisionSourceV1
    {
        public bool Renewed; public string CurrentMaker = Maker, HistoricalMaker = Maker, Receiver = WorkInventoryTransferOverageDecisionHostTests.Receiver;
        public string Kind => WorkDecisionHostV1.InventoryTransferOverage; public string Module => "inventory";
        public string ApproverPermission => InventoryPermissionManifestV1.StockTransferOverageApprove;
        public Task<WorkApprovalDecisionSnapshotV1?> ReadDecisionAsync(WorkInboxSourceContextV1 context, Guid itemId, CancellationToken token)
        {
            WorkInventoryTransferOverageRequestProofV1[] history = [new(1, At, Receiver, "Rejected", "10", null, "SENTETIK first reason"),
                new(2, At, Renewed ? HistoricalMaker : CurrentMaker, Renewed ? "Rejected" : "Pending", "5", 0, "SENTETIK corrected reason")];
            if (Renewed) history = [..history, new(3, At.AddHours(1), CurrentMaker, "Pending", "5", null, "SENTETIK new reason")];
            return Task.FromResult<WorkApprovalDecisionSnapshotV1?>(new(Company,
                new(Module, Kind, Item, "SENTETIK", At, "/stok/transferler/fazla-kabul-onaylari", CurrentMaker, [],
                    [new("inventory-transfer", "SENTETIK", Transfer, "/stok/transferler/fazla-kabul-onaylari")], CurrentMaker == Receiver ? null : [Receiver]), "2", true,
                InventoryTransferOverageProof: new(Transfer, Line, Receiver, history)));
        }
        public Task<WorkInboxSourceResultV1> ListPendingAsync(WorkInboxSourceContextV1 context, int maxCount, CancellationToken token) => throw new NotSupportedException();
    }
}
