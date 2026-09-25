using System.Text.Json;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Purchasing.UnitTests;

// Synthetic owner-protocol evidence only. Real SQL rollback/reopen and actual authority remain independent host gates.
public sealed partial class PurchasingServiceTests
{
    [Fact]
    public async Task Receipt_uses_post_stage_purchase_version_and_Free_with_two_independent_quantities()
    {
        var (world, runner, orderId, lineId) = await ReceiptWorldAsync();
        var request = ReceiptRequest();
        var result = await world.Service(runner).RecordGoodsReceiptAsync(orderId, lineId, request, Metadata("receipt-1"));
        Assert.Equal(("4", "2", "1.9", "Free"), (result.Receipt.PurchaseOrderVersion, result.Receipt.AcceptedQuantity,
            result.Receipt.MeasuredQuantityKg, result.Receipt.InitialBlock));
        Assert.Equal("4", runner.Stock!.PurchaseOrderVersionAfter);
        Assert.Equal("Free", runner.Stock.InitialBlock);
        Assert.False(PurchaseGoodsReceiptDocumentPolicyV1.MandatoryDocumentsEnabled);
        Assert.Empty(runner.Stock.RequiredDocuments);
        Assert.Equal(["receipt-read", "purchase", "first-source", "inventory", "fence", "commit"], runner.Events);
    }

    [Fact]
    public async Task New_receipt_uses_explicit_optional_policy_not_legacy_document_requirements()
    {
        var (world, runner, orderId, lineId) = await ReceiptWorldAsync();
        runner.RequiredDocuments = ["SYNTHETIC-LEGACY-DOCUMENT"];
        await world.Service(runner).RecordGoodsReceiptAsync(orderId, lineId, ReceiptRequest(), Metadata("receipt-1"));
        Assert.Empty(runner.Stock!.RequiredDocuments);
        Assert.Equal("Free", runner.Stock.InitialBlock);
        Assert.Equal(["receipt-read", "purchase", "first-source", "inventory", "fence", "commit"], runner.Events);
    }

    [Theory]
    [InlineData("Free")]
    [InlineData("Blocked")]
    public async Task Replay_preserves_original_block_and_physical_facts_not_current_masters(string originalBlock)
    {
        var (world, runner, orderId, lineId) = await ReceiptWorldAsync();
        var request = ReceiptRequest();
        await world.Service(runner).RecordGoodsReceiptAsync(orderId, lineId, request, Metadata("receipt-1"));
        runner.Original = runner.Stock! with { InitialBlock = originalBlock, RequiredDocuments = ["SYNTHETIC-ORIGINAL-DOCUMENT"] };
        runner.PositionSourceUnavailable = true;
        runner.Events.Clear();
        var result = await world.Service(runner).RecordGoodsReceiptAsync(orderId, lineId, request, Metadata("receipt-1", "retry"));
        Assert.True(result.IsReplay);
        Assert.Equal(originalBlock, result.Receipt.InitialBlock);
        Assert.Equal(originalBlock, runner.Stock!.InitialBlock);
        Assert.Equal(["SYNTHETIC-ORIGINAL-DOCUMENT"], runner.Stock.RequiredDocuments);
        Assert.Equal(["receipt-read", "purchase", "inventory", "fence", "commit"], runner.Events);
        Assert.Equal("synthetic-correlation", result.OriginalCorrelationId);
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("")]
    public async Task Replay_refuses_invalid_stored_block_before_either_participant(string originalBlock)
    {
        var (world, runner, orderId, lineId) = await ReceiptWorldAsync();
        var request = ReceiptRequest();
        await world.Service(runner).RecordGoodsReceiptAsync(orderId, lineId, request, Metadata("receipt-1"));
        runner.Original = runner.Stock! with { InitialBlock = originalBlock };
        runner.Events.Clear();
        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service(runner)
            .RecordGoodsReceiptAsync(orderId, lineId, request, Metadata("receipt-1")));
        Assert.Equal((503, PurchaseGoodsReceiptCodesV1.SourceInvalid), (refusal.Status, refusal.Code));
        Assert.Equal(["receipt-read"], runner.Events);
    }

    [Fact]
    public async Task A_changed_physical_payload_under_original_key_is_not_blindly_replayed()
    {
        var (world, runner, orderId, lineId) = await ReceiptWorldAsync();
        var request = ReceiptRequest();
        await world.Service(runner).RecordGoodsReceiptAsync(orderId, lineId, request, Metadata("receipt-1"));
        runner.Original = runner.Stock;
        runner.Events.Clear();
        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service(runner).RecordGoodsReceiptAsync(orderId, lineId,
            request with { MeasuredQuantityKg = "1.8" }, Metadata("receipt-1")));
        Assert.Equal((409, PurchasingProblemCodesV1.SameKeyPayloadMismatch), (refusal.Status, refusal.Code));
        Assert.Equal(["receipt-read"], runner.Events);
    }

    [Fact]
    public async Task Missing_position_source_never_defaults_to_browser_facts_or_commits_acceptance_alone()
    {
        var (world, runner, orderId, lineId) = await ReceiptWorldAsync();
        runner.PositionSourceUnavailable = true;
        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service(runner)
            .RecordGoodsReceiptAsync(orderId, lineId, ReceiptRequest(), Metadata("receipt-1")));
        Assert.Equal((503, PurchaseGoodsReceiptCodesV1.Unavailable), (refusal.Status, refusal.Code));
        Assert.DoesNotContain("inventory", runner.Events);
        Assert.DoesNotContain("commit", runner.Events);
    }

    [Fact]
    public async Task Unbound_receipt_authority_is_not_replaced_by_create_or_approve()
    {
        var (world, runner, orderId, lineId) = await ReceiptWorldAsync();
        world.Bindings = world.Bindings with { GoodsReceiptPermission = null };
        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service(runner)
            .RecordGoodsReceiptAsync(orderId, lineId, ReceiptRequest(), Metadata("receipt-1")));
        Assert.Equal((503, PurchasingProblemCodesV1.PermissionUnregistered), (refusal.Status, refusal.Code));
        Assert.Empty(runner.Events);
    }

    [Fact]
    public async Task Final_authority_loss_refuses_commit_after_both_stages()
    {
        var (world, runner, orderId, lineId) = await ReceiptWorldAsync();
        runner.BeforeFence = () => world.Authority.Granted = false;
        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service(runner)
            .RecordGoodsReceiptAsync(orderId, lineId, ReceiptRequest(), Metadata("receipt-1")));
        Assert.Equal(403, refusal.Status);
        Assert.Contains("inventory", runner.Events);
        Assert.DoesNotContain("commit", runner.Events);
    }

    [Theory]
    [InlineData("initialBlock", "Free")]
    [InlineData("initialBlock", "Blocked")]
    [InlineData("ownerId", "11111111-1111-1111-1111-111111111111")]
    [InlineData("requiredDocuments", "MTC")]
    public void Receipt_wire_rejects_browser_owned_policy_facts(string field, string value)
    {
        var json = JsonSerializer.Serialize(ReceiptRequest(), PurchasingJson.Options);
        Assert.Throws<JsonException>(() => PurchaseGoodsReceiptJsonV1.Read(json[..^1] + $",\"{field}\":\"{value}\"}}"));
    }

    private static RecordPurchaseGoodsReceiptV1 ReceiptRequest() => new("3", "7", Guid.NewGuid(), "2", "1.9",
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), Guid.NewGuid(), Guid.NewGuid(), "SYNTHETIC-LABEL", null, null,
        Guid.NewGuid(), new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.Zero));

    private static async Task<(World World, ReceiptRunner Runner, Guid Order, Guid Line)> ReceiptWorldAsync()
    {
        var world = new World { Bindings = new(CreatePermission, ReadPermission, ApprovePermission, CreatePermission) };
        var created = await world.Service().CreateOrderAsync(OrderOf(null, null, "10"), Metadata("create"));
        await world.Service().ApproveOrderAsync(created.Detail.Order.Id, new("2"), Metadata("approve"));
        return (world, new ReceiptRunner(), created.Detail.Order.Id, created.Detail.Lines[0].LineId);
    }

    private sealed class ReceiptRunner : IPurchaseGoodsReceiptTransactionV1, IPurchaseGoodsReceiptParticipantsV1
    {
        public List<string> Events { get; } = [];
        public PurchaseReceiptStockRequestV1? Stock { get; private set; }
        public PurchaseReceiptStockRequestV1? Original { get; set; }
        public bool PositionSourceUnavailable { get; set; }
        public IReadOnlyList<string>? RequiredDocuments { get; set; }
        public Action? BeforeFence { get; set; }

        public async Task<T> ExecuteAsync<T>(Guid companyId, Func<IPurchaseGoodsReceiptParticipantsV1, CancellationToken, Task<T>> operation,
            Func<CancellationToken, Task> beforeCommit, CancellationToken cancellationToken = default)
        {
            Assert.Equal(Company, companyId);
            var result = await operation(this, cancellationToken);
            BeforeFence?.Invoke();
            Events.Add("fence");
            await beforeCommit(cancellationToken);
            Events.Add("commit");
            return result;
        }

        public Task<PurchaseReceiptStockRequestV1?> ReadOriginalAsync(Guid acceptanceId, PurchasingWriteEnvelope envelope, CancellationToken cancellationToken)
        {
            Events.Add("receipt-read");
            return Task.FromResult(Original);
        }

        public Task<PurchaseReceiptFactsV1> ResolveFirstAsync(PurchaseReceiptSourceV1 source, Guid locationId, CancellationToken cancellationToken)
        {
            Events.Add("first-source");
            if (PositionSourceUnavailable) throw new PurchasingServiceException(503, PurchaseGoodsReceiptCodesV1.Unavailable);
            return Task.FromResult(new PurchaseReceiptFactsV1(Company, source.ProductId, source.ProductVersion, locationId,
                "Company", Company, "Company", Company, RequiredDocuments));
        }

        public Task<PurchaseAcceptanceStageResultV1> StagePurchaseAsync(PurchaseAcceptanceStageRequestV1 request, PurchasingWriteEnvelope envelope,
            CancellationToken cancellationToken)
        {
            Events.Add("purchase");
            return Task.FromResult(new PurchaseAcceptanceStageResultV1(Company, request.AcceptanceId, request.PurchaseOrderId,
                request.PurchaseOrderLineId, "4", request.AcceptedQuantity, "8", Original is not null, "synthetic-correlation"));
        }

        public Task<PurchaseReceiptStockResultV1> StageInventoryAsync(PurchaseReceiptStockRequestV1 request, PurchasingWriteEnvelope envelope,
            CancellationToken cancellationToken)
        {
            Events.Add("inventory");
            Stock = request;
            return Task.FromResult(new PurchaseReceiptStockResultV1(Company, request.AcceptanceId, request.PurchaseOrderId,
                request.PurchaseOrderLineId, request.PurchaseOrderVersionAfter, request.ProductId, request.ExpectedProductVersion,
                request.PurchaseUnitId, request.LotId, request.LabelId, "1", request.MovementId, request.QuantityKg,
                Original is not null, "synthetic-correlation"));
        }
    }
}
