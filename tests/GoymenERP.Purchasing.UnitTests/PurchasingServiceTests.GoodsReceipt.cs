using System.Text.Json;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Quantities;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Purchasing.UnitTests;

// Synthetic owner-protocol evidence only. Real SQL rollback/reopen and actual authority remain independent host gates.
public sealed partial class PurchasingServiceTests
{
    [Fact]
    public async Task Receipt_detail_reopens_the_exact_owner_source_without_mutating_the_order()
    {
        var (world, reader, orderId, _, receipt) = await ReceiptDetailWorldAsync();
        var stored = await world.Store.FindOrderAsync(orderId);
        var version = stored!.Order.Version;
        var acceptances = stored.Order.Acceptances.Count;

        var detail = await world.Service(reads: reader).GetGoodsReceiptAsync(orderId, receipt.AcceptanceId);

        Assert.Equal(receipt, detail.Receipt);
        Assert.Equal(ReceiptQuantity(receipt), detail.Receipt.StockItemQuantity);
        Assert.Equal(1, reader.Calls);
        Assert.Equal((version, acceptances), (stored.Order.Version, stored.Order.Acceptances.Count));
    }

    [Fact]
    public async Task Receipt_list_returns_the_same_quantity_snapshot_and_legacy_null_remains_explicit()
    {
        var (world, reader, orderId, _, receipt) = await ReceiptDetailWorldAsync();

        var page = await world.Service(reads: reader).GetGoodsReceiptsAsync(orderId);
        Assert.Equal(receipt, Assert.Single(page.Items));

        reader.Receipt = receipt with { StockItemQuantity = null };
        var legacy = await world.Service(reads: reader).GetGoodsReceiptsAsync(orderId);
        Assert.Null(Assert.Single(legacy.Items).StockItemQuantity);
    }

    [Fact]
    public async Task Receipt_detail_rechecks_read_authority_after_the_owner_read()
    {
        var (world, reader, orderId, _, receipt) = await ReceiptDetailWorldAsync();
        reader.BeforeReturn = () => world.Authority.Granted = false;

        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service(reads: reader).GetGoodsReceiptAsync(orderId, receipt.AcceptanceId));

        Assert.Equal(403, refusal.Status);
        Assert.Equal(1, reader.Calls);
    }

    [Fact]
    public async Task Receipt_detail_does_not_ask_the_owner_for_an_unknown_acceptance()
    {
        var (world, reader, orderId, _, _) = await ReceiptDetailWorldAsync();

        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service(reads: reader).GetGoodsReceiptAsync(orderId, Guid.NewGuid()));

        Assert.Equal((404, "not_found"), (refusal.Status, refusal.Code));
        Assert.Equal(0, reader.Calls);
    }

    [Theory]
    [InlineData("company")]
    [InlineData("order")]
    [InlineData("acceptance")]
    [InlineData("line")]
    [InlineData("product")]
    [InlineData("product-version")]
    [InlineData("unit")]
    [InlineData("accepted")]
    [InlineData("order-version")]
    [InlineData("lot")]
    [InlineData("label")]
    [InlineData("label-code")]
    [InlineData("label-version")]
    [InlineData("movement")]
    [InlineData("location")]
    [InlineData("measured")]
    [InlineData("remaining")]
    [InlineData("block")]
    [InlineData("time")]
    [InlineData("quantity-company")]
    [InlineData("quantity-subject-kind")]
    [InlineData("quantity-product")]
    [InlineData("quantity-source-kind")]
    [InlineData("quantity-source")]
    [InlineData("quantity-source-version")]
    [InlineData("quantity-value")]
    [InlineData("quantity-unit")]
    [InlineData("quantity-profile")]
    [InlineData("quantity-profile-version")]
    [InlineData("quantity-dimension")]
    [InlineData("quantity-scale")]
    [InlineData("quantity-alternate")]
    public async Task Receipt_detail_refuses_an_inconsistent_owner_source(string fault)
    {
        var (world, reader, orderId, _, receipt) = await ReceiptDetailWorldAsync();
        reader.Receipt = fault switch
        {
            "company" => receipt with { CompanyId = Guid.NewGuid() },
            "order" => receipt with { PurchaseOrderId = Guid.NewGuid() },
            "acceptance" => receipt with { AcceptanceId = Guid.NewGuid() },
            "line" => receipt with { PurchaseOrderLineId = Guid.NewGuid() },
            "product" => receipt with { ProductId = Guid.NewGuid() },
            "product-version" => receipt with { ProductVersion = "8" },
            "unit" => receipt with { UnitId = Guid.NewGuid() },
            "accepted" => receipt with { AcceptedQuantity = "1.9" },
            "order-version" => receipt with { PurchaseOrderVersion = "5" },
            "lot" => receipt with { LotId = Guid.Empty },
            "label" => receipt with { LabelId = Guid.Empty },
            "label-code" => receipt with { LabelCode = " " },
            "label-version" => receipt with { LabelVersion = "01" },
            "movement" => receipt with { MovementId = Guid.Empty },
            "location" => receipt with { LocationId = Guid.Empty },
            "measured" => receipt with { MeasuredQuantityKg = "1.900" },
            "remaining" => receipt with { RemainingQuantity = "08" },
            "block" => receipt with { InitialBlock = "Unknown" },
            "time" => receipt with { OccurredAtUtc = receipt.OccurredAtUtc.AddMinutes(1) },
            "quantity-company" => WithQuantity(receipt, quantity => quantity with { CompanyId = Guid.NewGuid() }),
            "quantity-subject-kind" => WithQuantity(receipt, quantity => quantity with
            {
                Subject = quantity.Subject with { Kind = "Service" }
            }),
            "quantity-product" => WithQuantity(receipt, quantity => quantity with
            {
                Subject = quantity.Subject with { Id = Guid.NewGuid() }
            }),
            "quantity-source-kind" => WithQuantity(receipt, quantity => quantity with
            {
                Source = quantity.Source with { Kind = "other" }
            }),
            "quantity-source" => WithQuantity(receipt, quantity => quantity with
            {
                Source = quantity.Source with { Id = Guid.NewGuid() }
            }),
            "quantity-source-version" => WithQuantity(receipt, quantity => quantity with
            {
                Source = quantity.Source with { Version = "8" }
            }),
            "quantity-value" => WithQuantity(receipt, quantity => quantity with
            {
                BaseQuantity = quantity.BaseQuantity with { Value = "1.8" }
            }),
            "quantity-unit" => WithQuantity(receipt, quantity => quantity with
            {
                BaseQuantity = quantity.BaseQuantity with { UnitId = Guid.NewGuid() }
            }),
            "quantity-profile" => WithQuantity(receipt, quantity => quantity with
            {
                BaseQuantity = quantity.BaseQuantity with { ProfileId = Guid.Empty }
            }),
            "quantity-profile-version" => WithQuantity(receipt, quantity => quantity with
            {
                BaseQuantity = quantity.BaseQuantity with { ProfileVersion = "01" }
            }),
            "quantity-dimension" => WithQuantity(receipt, quantity => quantity with
            {
                BaseQuantity = quantity.BaseQuantity with { Dimension = "Volume" }
            }),
            "quantity-scale" => WithQuantity(receipt, quantity => quantity with
            {
                BaseQuantity = quantity.BaseQuantity with { Scale = 4 }
            }),
            "quantity-alternate" => WithQuantity(receipt, quantity => quantity with
            {
                AlternateQuantity = new(quantity.BaseQuantity, quantity.Source)
            }),
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        };

        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service(reads: reader).GetGoodsReceiptAsync(orderId, receipt.AcceptanceId));

        Assert.Equal((503, PurchaseGoodsReceiptCodesV1.SourceInvalid), (refusal.Status, refusal.Code));
    }

    [Fact]
    public async Task Receipt_uses_post_stage_purchase_version_and_Free_with_two_independent_quantities()
    {
        var (world, runner, orderId, lineId) = await ReceiptWorldAsync();
        var request = ReceiptRequest();
        var result = await world.Service(runner).RecordGoodsReceiptAsync(orderId, lineId, request, Metadata("receipt-1"));
        Assert.Equal(("4", "2", "1.9", "Free"), (result.Receipt.PurchaseOrderVersion, result.Receipt.AcceptedQuantity,
            result.Receipt.MeasuredQuantityKg, result.Receipt.InitialBlock));
        Assert.Equal(ReceiptQuantity(result.Receipt), result.Receipt.StockItemQuantity);
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
        Assert.Equal(ReceiptQuantity(result.Receipt), result.Receipt.StockItemQuantity);
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

    private static async Task<(World World, ReceiptReader Reader, Guid Order, Guid Line, PurchaseGoodsReceiptViewV1 Receipt)>
        ReceiptDetailWorldAsync()
    {
        var (world, _, orderId, lineId) = await ReceiptWorldAsync();
        var request = ReceiptRequest();
        var stored = await world.Store.FindOrderAsync(orderId) ?? throw new InvalidOperationException("Synthetic order is missing.");
        var line = stored.Order.Lines.Single(candidate => candidate.Id == lineId);
        stored.Order.RecordAcceptance(stored.Order.Version, request.AcceptanceId, lineId,
            Quantity.Of(2m, QuantityUnit.Kilogram), request.OccurredAtUtc);
        var receipt = new PurchaseGoodsReceiptViewV1(
            Company,
            request.AcceptanceId,
            orderId,
            lineId,
            stored.Order.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
            line.ProductId,
            "7",
            line.UnitId,
            "2",
            "8",
            request.LotId,
            request.LabelId,
            request.LabelCode,
            "1",
            request.MovementId,
            request.LocationId,
            request.MeasuredQuantityKg,
            "Free",
            request.OccurredAtUtc,
            ReceiptQuantity(Company, line.ProductId, request.AcceptanceId,
                stored.Order.Version.ToString(System.Globalization.CultureInfo.InvariantCulture), request.MeasuredQuantityKg));
        return (world, new ReceiptReader(receipt), orderId, lineId, receipt);
    }

    private sealed class ReceiptReader(PurchaseGoodsReceiptViewV1 receipt) : IPurchaseGoodsReceiptReadsV1
    {
        public PurchaseGoodsReceiptViewV1? Receipt { get; set; } = receipt;
        public Action? BeforeReturn { get; set; }
        public int Calls { get; private set; }

        public Task<PurchaseGoodsReceiptOptionsV1> GetOptionsAsync(PurchaseReceiptSourceV1 source,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<PurchaseGoodsReceiptViewV1?> GetReceiptAsync(Guid orderId, Guid acceptanceId,
            CancellationToken cancellationToken)
        {
            Calls++;
            BeforeReturn?.Invoke();
            return Task.FromResult(Receipt);
        }
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
                Original is not null, "synthetic-correlation",
                ReceiptQuantity(Company, request.ProductId, request.AcceptanceId, request.PurchaseOrderVersionAfter,
                    request.QuantityKg)));
        }
    }

    private static PurchaseGoodsReceiptViewV1 WithQuantity(PurchaseGoodsReceiptViewV1 receipt,
        Func<PurchaseGoodsReceiptQuantityV2, PurchaseGoodsReceiptQuantityV2> mutate) =>
        receipt with { StockItemQuantity = mutate(Assert.IsType<PurchaseGoodsReceiptQuantityV2>(receipt.StockItemQuantity)) };

    private static PurchaseGoodsReceiptQuantityV2 ReceiptQuantity(PurchaseGoodsReceiptViewV1 receipt) =>
        ReceiptQuantity(receipt.CompanyId, receipt.ProductId, receipt.AcceptanceId, receipt.PurchaseOrderVersion,
            receipt.MeasuredQuantityKg);

    private static PurchaseGoodsReceiptQuantityV2 ReceiptQuantity(Guid companyId, Guid productId, Guid acceptanceId,
        string sourceVersion, string value) => new(
        companyId,
        new(PurchaseGoodsReceiptApiV1.QuantitySubjectKind, productId),
        new(PurchaseGoodsReceiptApiV1.QuantitySourceKind, acceptanceId, sourceVersion),
        new(value, SettingsUnitIdentitiesV1.Kilogram,
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), "3",
            PurchaseGoodsReceiptApiV1.QuantityDimension, PurchaseGoodsReceiptApiV1.MaximumKilogramScale),
        null);
}
