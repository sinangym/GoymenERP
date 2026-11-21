using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Returns;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>Synthetic service protocol only; no claim of a shared SQL transaction or real Inventory cost evidence.</summary>
public sealed partial class PurchasingServiceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Supplier_return_price_disclosure_covers_create_reads_decisions_and_original_replays(bool visible)
    {
        foreach (var approve in new[] { true, false })
        {
            var (world, order, returns) = await ReturnWorldAsync();
            if (!visible) world.Authority.Denied.Add(PurchasingPermissionManifestV1.OrdersPriceRead);
            var host = new ReturnAdjustmentHost();
            var service = world.Service(reversals: host, returns: returns);
            var request = ReturnRequest();
            var created = await service.CreateSupplierReturnAsync(order, request, Metadata("disclosure-create"));
            AssertReturnPrice(created.Return, visible);
            AssertReturnPrice((await service.GetSupplierReturnAsync(order, request.ReturnId)).Return, visible);
            AssertReturnPrice(Assert.Single((await service.GetSupplierReturnsAsync(order)).Items), visible);
            async Task<PurchaseSupplierReturnResponseV1> Decide() => approve
                ? await service.ApproveSupplierReturnCostExceptionAsync(order, request.ReturnId, new("1", null), Metadata("disclosure-decision"))
                : await service.RejectSupplierReturnCostExceptionAsync(order, request.ReturnId, new("1", "SENTETIK rejected"), Metadata("disclosure-decision"));
            AssertReturnPrice((await world.AsApprover(Decide)).Return, visible);
            var decisionReplay = await world.AsApprover(Decide);
            Assert.True(decisionReplay.IsReplay);
            AssertReturnPrice(decisionReplay.Return, visible);
            var creationReplay = await service.CreateSupplierReturnAsync(order, request, Metadata("disclosure-create"));
            Assert.True(creationReplay.IsReplay);
            Assert.Equal("PendingCostException", creationReplay.Return.Status);
            AssertReturnPrice(creationReplay.Return, visible);
            Assert.Equal(12.3456m, returns.Stored!.Return.UnitCostTryPerKg);
            Assert.Equal((1, 1), (returns.NewWrites, returns.DecisionWrites));
        }
    }

    [Theory]
    [InlineData("detail")]
    [InlineData("list")]
    [InlineData("replay")]
    [InlineData("legacy-replay")]
    [InlineData("create")]
    [InlineData("decision")]
    public async Task Supplier_return_price_loss_during_the_store_await_is_measured_at_disclosure(string operation)
    {
        var (world, order, returns) = await ReturnWorldAsync();
        var host = new ReturnAdjustmentHost();
        var service = world.Service(reversals: host, returns: returns);
        var request = ReturnRequest();
        if (operation != "create") await service.CreateSupplierReturnAsync(order, request, Metadata("price-revoke"));
        returns.LegacyReplay = operation == "legacy-replay";
        returns.AfterAccess = () => world.Authority.Denied.Add(PurchasingPermissionManifestV1.OrdersPriceRead);
        var view = operation switch
        {
            "detail" => (await service.GetSupplierReturnAsync(order, request.ReturnId)).Return,
            "list" => Assert.Single((await service.GetSupplierReturnsAsync(order)).Items),
            "decision" => (await world.AsApprover(() => service.ApproveSupplierReturnCostExceptionAsync(
                order, request.ReturnId, new("1", null), Metadata("price-revoke-decision")))).Return,
            _ => (await service.CreateSupplierReturnAsync(order, request, Metadata("price-revoke"))).Return,
        };
        AssertReturnPrice(view, visible: false);
        Assert.Equal(12.3456m, returns.Stored!.Return.UnitCostTryPerKg);
    }

    private static void AssertReturnPrice(PurchaseSupplierReturnViewV1 view, bool visible)
    {
        Assert.Equal(visible ? PurchasingPriceDisclosureV1.Visible : PurchasingPriceDisclosureV1.Withheld, view.PriceDisclosure);
        Assert.Equal(visible ? "12.3456" : null, view.UnitCostTryPerKg);
        if (!visible)
        {
            using var json = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(view));
            Assert.Equal(System.Text.Json.JsonValueKind.Null, json.RootElement.GetProperty("UnitCostTryPerKg").ValueKind);
        }
    }

    [Fact]
    public async Task Replacement_expected_manual_cost_return_waits_without_opening_quantity_or_posting_stock()
    {
        var (world, order, returns) = await ReturnWorldAsync(); var host = new ReturnAdjustmentHost();
        var request = ReturnRequest();
        var answer = await world.Service(reversals: host, returns: returns).CreateSupplierReturnAsync(order, request, Metadata("pending-return"));
        Assert.Equal(("PendingCostException", true, "3", "2"),
            (answer.Return.Status, answer.Return.ReplacementExpected, answer.Return.ExpectedPurchaseOrderVersion, answer.Return.UndoQuantity));
        Assert.Equal(0, host.Calls);
        Assert.Equal(1, returns.NewWrites);
    }

    [Theory]
    [InlineData(null, "3")]
    [InlineData("9", "9")]
    public async Task Return_approval_uses_explicit_or_original_expected_order_version_without_rewriting_the_draft(string? explicitVersion, string expected)
    {
        var (world, order, returns) = await ReturnWorldAsync(); var host = new ReturnAdjustmentHost();
        var request = ReturnRequest();
        await world.Service(reversals: host, returns: returns).CreateSupplierReturnAsync(order, request, Metadata("pending-return"));
        var approved = await world.AsApprover(() => world.Service(reversals: host, returns: returns)
            .ApproveSupplierReturnCostExceptionAsync(order, request.ReturnId, new("1", null, explicitVersion), Metadata("approve-return")));
        Assert.Equal(("Recorded", "2"), (approved.Return.Status, approved.Return.Version));
        Assert.Equal((expected, "2", "1.9", true, true), (host.Last!.Request.ExpectedPurchaseOrderVersion,
            host.Last.Request.UndoQuantity, host.Last.Request.MeasuredUndoQuantityKg, host.Last.IsSupplierReturn, host.Last.ReplacementExpected));
        Assert.Equal(PurchaseSupplierReturnCommandsV1.ApproveCostExceptionV1, host.Last.CommandName);
        Assert.Equal("3", approved.Return.ExpectedPurchaseOrderVersion);
        Assert.Equal((1, 1), (host.Callbacks, returns.DecisionWrites));
    }

    [Fact]
    public async Task Return_invoice_cost_posts_both_owner_adjustments_before_the_single_return_callback()
    {
        var (world, order, returns) = await ReturnWorldAsync(); var host = new ReturnAdjustmentHost();
        var request = ReturnRequest() with { ManualUnitCostTryPerKg = null };
        var answer = await world.Service(reversals: host, returns: returns, returnCosts: new FixedReturnCost())
            .CreateSupplierReturnAsync(order, request, Metadata("invoice-return"));
        Assert.Equal("Recorded", answer.Return.Status);
        Assert.Equal((1, 1, "12.3456"), (host.Callbacks, returns.NewWrites, host.Last!.OriginalUnitCostTryPerKg));
        Assert.Equal(PurchaseSupplierReturnCommandsV1.CreateV1, host.Last.CommandName);
        Assert.Equal(PurchasingRequestHash.Compute(PurchaseSupplierReturnCommandsV1.CreateV1, order, new { request }), host.Last.RequestHash);
    }

    [Fact]
    public async Task A_stale_original_order_version_is_terminal_and_replays_without_deciding_the_pending_return()
    {
        var (world, order, returns) = await ReturnWorldAsync();
        var host = new ReturnAdjustmentHost { Failure = new(409, PurchasingProblemCodesV1.VersionConflict) };
        var request = ReturnRequest();
        await world.Service(reversals: host, returns: returns).CreateSupplierReturnAsync(order, request, Metadata("pending-return"));
        var first = await world.AsApprover(() => Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service(reversals: host, returns: returns)
            .ApproveSupplierReturnCostExceptionAsync(order, request.ReturnId, new("1", null), Metadata("stale-order"))));
        Assert.False(first.IsReplay);
        host.Failure = null;
        var replay = await world.AsApprover(() => Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service(reversals: host, returns: returns)
            .ApproveSupplierReturnCostExceptionAsync(order, request.ReturnId, new("1", null), Metadata("stale-order", "later"))));
        Assert.True(replay.IsReplay);
        Assert.Equal((1, 0, PurchaseSupplierReturnStatus.PendingCostException), (host.Calls, returns.DecisionWrites, returns.Stored!.Return.Status));
    }

    [Fact]
    public async Task Return_rejection_and_same_actor_approval_never_enter_the_adjustment_host()
    {
        var (world, order, returns) = await ReturnWorldAsync(); var host = new ReturnAdjustmentHost(); var request = ReturnRequest();
        await world.Service(reversals: host, returns: returns).CreateSupplierReturnAsync(order, request, Metadata("pending-return"));
        var refused = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service(reversals: host, returns: returns)
            .ApproveSupplierReturnCostExceptionAsync(order, request.ReturnId, new("1", null), Metadata("self-return")));
        Assert.Equal(PurchaseSupplierReturnRuleCodes.SameActor, refused.Code);
        var rejected = await world.AsApprover(() => world.Service(reversals: host, returns: returns)
            .RejectSupplierReturnCostExceptionAsync(order, request.ReturnId, new("1", "SENTETIK cost rejected"), Metadata("reject-return")));
        Assert.Equal("Rejected", rejected.Return.Status);
        Assert.Equal(0, host.Calls);
    }

    [Theory]
    [InlineData("permission")]
    [InlineData("company")]
    [InlineData("actor")]
    public async Task Return_adjustment_keeps_fresh_authority_before_the_return_callback(string fault)
    {
        var (world, order, returns) = await ReturnWorldAsync();
        var host = new ReturnAdjustmentHost { BeforeFence = () =>
        {
            if (fault == "permission") world.Authority.Granted = false;
            if (fault == "company") world.Company.Set(Guid.NewGuid());
            if (fault == "actor") world.User.CurrentActor = "another-synthetic-actor";
        }};
        var request = ReturnRequest() with { ManualUnitCostTryPerKg = null };
        var refused = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service(reversals: host, returns: returns, returnCosts: new FixedReturnCost())
            .CreateSupplierReturnAsync(order, request, Metadata("return-authority")));
        Assert.Equal((403, 0, 0), (refused.Status, host.Callbacks, returns.NewWrites));
    }

    [Fact]
    public async Task A_pending_creation_replay_preserves_its_original_response_after_later_approval()
    {
        var (world, order, returns) = await ReturnWorldAsync(); var host = new ReturnAdjustmentHost(); var request = ReturnRequest();
        await world.Service(reversals: host, returns: returns).CreateSupplierReturnAsync(order, request, Metadata("pending-original"));
        await world.AsApprover(() => world.Service(reversals: host, returns: returns)
            .ApproveSupplierReturnCostExceptionAsync(order, request.ReturnId, new("1", null), Metadata("approve-original")));
        var replay = await world.Service(reversals: host, returns: returns).CreateSupplierReturnAsync(order, request, Metadata("pending-original", "later"));
        Assert.Equal((true, "PendingCostException", "1", "synthetic-correlation"),
            (replay.IsReplay, replay.Return.Status, replay.Return.Version, replay.OriginalCorrelationId));
        Assert.Equal(PurchaseSupplierReturnStatus.Recorded, returns.Stored!.Return.Status);
    }

    [Fact]
    public async Task Missing_adjustment_host_cannot_fall_back_to_an_ordinary_stock_exit()
    {
        var (world, order, returns) = await ReturnWorldAsync(); var request = ReturnRequest() with { ManualUnitCostTryPerKg = null };
        var refused = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service(returns: returns, returnCosts: new FixedReturnCost())
            .CreateSupplierReturnAsync(order, request, Metadata("return-no-host")));
        Assert.Equal((503, 0), (refused.Status, returns.NewWrites));
    }

    private static CreatePurchaseSupplierReturnV1 ReturnRequest() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        "Free", "1.9", "12.3456", true, "SENTETIK replacement return", DateTimeOffset.UnixEpoch, "3", "2");

    private static async Task<(World World, Guid Order, ReturnStoreFake Returns)> ReturnWorldAsync()
    {
        var world = new World();
        var order = await world.Service().CreateOrderAsync(OrderOf(null, null, "10"), Metadata("return-order"));
        await world.AsApprover(() => world.Service().ApproveOrderAsync(order.Detail.Order.Id, new("2"), Metadata("return-order-approve")));
        return (world, order.Detail.Order.Id, new ReturnStoreFake(order.Detail.Lines[0].LineId));
    }

    private sealed class FixedReturnCost : IPurchaseSupplierReturnInvoiceCostReadPortV1
    {
        public Task<PurchaseSupplierReturnInvoiceCostV1> ReadAsync(Guid companyId, Guid goodsAcceptanceId, CancellationToken cancellationToken) =>
            Task.FromResult(new PurchaseSupplierReturnInvoiceCostV1(12.3456m));
    }

    private sealed class ReturnAdjustmentHost : IPurchaseReceiptReversalTransactionV1
    {
        public int Calls { get; private set; }
        public int Callbacks { get; private set; }
        public PurchaseReceiptReversalWriteV1? Last { get; private set; }
        public PurchasingServiceException? Failure { get; set; }
        public Action? BeforeFence { get; init; }
        public Task<PurchaseReceiptReversalResponseV1> ExecuteAsync(Guid companyId, PurchaseReceiptReversalWriteV1 write,
            PurchasingWriteEnvelope envelope, Func<CancellationToken, Task> freshAuthority, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Return must enlist its owner callback.");
        public async Task<T> ExecuteWithWriteAsync<T>(Guid companyId, PurchaseReceiptReversalWriteV1 write, PurchasingWriteEnvelope envelope,
            Func<PurchaseReceiptReversalResponseV1, CancellationToken, Task<T>> writeReturn, Func<CancellationToken, Task> freshAuthority,
            CancellationToken cancellationToken = default)
        {
            Calls++; Last = write; BeforeFence?.Invoke(); await freshAuthority(cancellationToken);
            if (Failure is not null) throw Failure;
            Callbacks++;
            return await writeReturn(null!, cancellationToken); // Protocol test: the service does not derive quantities from this answer.
        }
    }

    private sealed class ReturnStoreFake(Guid lineId) : IPurchaseSupplierReturnStore
    {
        private readonly Dictionary<(string Command, string Key), (string Actor, string Hash, PurchaseSupplierReturnReplay Replay)> attempts = [];
        public StoredPurchaseSupplierReturn? Stored { get; private set; }
        public int NewWrites { get; private set; }
        public int DecisionWrites { get; private set; }
        public Action? AfterAccess { get; set; }
        public bool LegacyReplay { get; set; }
        public Task<PurchaseSupplierReturnReplay?> FindReplayAsync(PurchasingWriteEnvelope envelope, string hash, string command, CancellationToken cancellationToken = default)
        {
            if (!attempts.TryGetValue((command, envelope.IdempotencyKey), out var found)) return Task.FromResult<PurchaseSupplierReturnReplay?>(null);
            if (found.Actor != envelope.ActorId || found.Hash != hash) throw new PurchasingStoreException(PurchasingStoreCodes.SameKeyPayloadMismatch, "SENTETIK");
            AfterAccess?.Invoke();
            return Task.FromResult<PurchaseSupplierReturnReplay?>(LegacyReplay ? found.Replay with { Original = null } : found.Replay);
        }
        public Task<StoredPurchaseSupplierReturn?> FindAsync(Guid order, Guid id, CancellationToken cancellationToken = default)
        {
            AfterAccess?.Invoke();
            return Task.FromResult(Stored is { } stored && stored.Return.ReturnId == id && stored.Return.PurchaseOrderId == order ? stored : null);
        }
        public Task<bool> ReturnIdExistsAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Stored?.Return.ReturnId == id);
        public Task<IReadOnlyList<StoredPurchaseSupplierReturn>> ListForOrderAsync(Guid order, int take, CancellationToken cancellationToken = default)
        {
            AfterAccess?.Invoke();
            return Task.FromResult<IReadOnlyList<StoredPurchaseSupplierReturn>>(Stored is null ? [] : [Stored]);
        }
        public Task<Guid?> FindAcceptanceLineAsync(Guid order, Guid id, CancellationToken cancellationToken = default) => Task.FromResult<Guid?>(lineId);
        public Task<StoredPurchaseSupplierReturn> SaveNewAsync(PurchaseSupplierReturnDraft draft, PurchasingWriteEnvelope envelope, string hash, string command, CancellationToken cancellationToken = default)
        {
            NewWrites++; Stored = new(Company, draft, 1, envelope.ActorId, DateTimeOffset.UnixEpoch, null, null, null);
            Remember(envelope, hash, command, new(draft.ReturnId, null, envelope.CorrelationId, Stored));
            AfterAccess?.Invoke();
            return Task.FromResult(Stored);
        }
        public Task<StoredPurchaseSupplierReturn> SaveDecisionAsync(Guid order, PurchaseSupplierReturn decided, long version, PurchasingWriteEnvelope envelope,
            string hash, string command, CancellationToken cancellationToken = default)
        {
            DecisionWrites++; Stored = Stored! with { Return = Stored!.Return with { Status = decided.Status }, Version = decided.Version,
                DecidedBy = envelope.ActorId, DecidedAtUtc = DateTimeOffset.UnixEpoch, DecisionReason = decided.Reason };
            Remember(envelope, hash, command, new(decided.Id, null, envelope.CorrelationId, Stored));
            AfterAccess?.Invoke();
            return Task.FromResult(Stored);
        }
        public Task RecordRefusalAsync(PurchasingWriteEnvelope envelope, string hash, string command, PurchasingTerminalRefusal refusal, CancellationToken cancellationToken = default)
        { Remember(envelope, hash, command, new(null, refusal, envelope.CorrelationId)); return Task.CompletedTask; }
        private void Remember(PurchasingWriteEnvelope envelope, string hash, string command, PurchaseSupplierReturnReplay replay) =>
            attempts.Add((command, envelope.IdempotencyKey), (envelope.ActorId, hash, replay));
    }
}
