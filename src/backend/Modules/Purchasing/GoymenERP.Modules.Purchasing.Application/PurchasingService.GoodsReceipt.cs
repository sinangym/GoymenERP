using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Purchasing.Application;

public sealed partial class PurchasingService
{
    public async Task<PurchaseGoodsReceiptResponseV1> RecordGoodsReceiptAsync(Guid orderId, Guid lineId,
        RecordPurchaseGoodsReceiptV1 request, PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        var permission = bindings.GoodsReceiptPermission;
        var scope = await RequireAsync(permission, cancellationToken);
        if (orderId == Guid.Empty || lineId == Guid.Empty) throw new PurchasingServiceException(404, "not_found");
        try { PurchaseGoodsReceiptJsonV1.Validate(request); }
        catch (JsonException) { throw new PurchasingServiceException(400, "validation_failed"); }
        var runner = goodsReceipts ?? throw ReceiptUnavailable();
        var envelope = Envelope(scope, metadata, permission!);
        try
        {
            return await runner.ExecuteAsync(scope.CompanyId, async (participants, token) =>
            {
                var original = await GuardedAsync(scope, permission,
                    () => participants.ReadOriginalAsync(request.AcceptanceId, envelope, token), token);
                PurchaseReceiptSourceV1 source;
                if (original is not null)
                {
                    // Do not silently replay a different physical request by blindly forwarding OriginalRequest.
                    RequireOriginalReceipt(scope.CompanyId, orderId, lineId, request, original);
                    source = new(scope.CompanyId, orderId, lineId, request.ExpectedPurchaseOrderVersion,
                        original.ProductId, original.ExpectedProductVersion, original.PurchaseUnitId, default);
                }
                else
                {
                    source = await ReceiptSourceAsync(scope, permission, orderId, lineId, token);
                }

                var purchase = await GuardedAsync(scope, permission, () => participants.StagePurchaseAsync(
                    new(scope.CompanyId, request.AcceptanceId, orderId, lineId, request.ExpectedPurchaseOrderVersion,
                        source.ProductId, request.ExpectedProductVersion, source.UnitId, request.AcceptedQuantity, request.OccurredAtUtc), envelope, token), token);
                if (purchase is null || purchase.CompanyId != scope.CompanyId || purchase.AcceptanceId != request.AcceptanceId
                    || purchase.PurchaseOrderId != orderId || purchase.PurchaseOrderLineId != lineId
                    || purchase.AcceptedQuantity != request.AcceptedQuantity || !ReceiptVersion(purchase.PurchaseOrderVersion)
                    || purchase.IsReplay != (original is not null) || string.IsNullOrWhiteSpace(purchase.OriginalCorrelationId))
                {
                    throw ReceiptSourceInvalid();
                }

                PurchaseReceiptStockRequestV1 stockRequest;
                if (original is not null)
                {
                    if (original.PurchaseOrderVersionAfter != purchase.PurchaseOrderVersion) throw ReceiptSourceInvalid();
                    stockRequest = original;
                }
                else
                {
                    // Missing authorized position facts still roll back the staged acceptance. Document
                    // optionality is the explicit NEW-receipt decision, never applied to original replay facts.
                    var facts = await GuardedAsync(scope, permission, () => participants.ResolveFirstAsync(source, request.LocationId, token), token);
                    RequireReceiptFacts(source, request.LocationId, facts);
                    stockRequest = new(scope.CompanyId, request.AcceptanceId, orderId, lineId, purchase.PurchaseOrderVersion,
                        source.ProductId, request.ExpectedProductVersion, source.UnitId, request.LotId, request.LabelId,
                        request.LabelCode, request.HeatNumber, request.CoilNumber, PurchaseGoodsReceiptDocumentPolicyV1.RequiredForNewReceipt,
                        facts.OwnerKind, facts.OwnerId, facts.CustodianKind, facts.CustodianId, facts.LocationId,
                        request.MovementId, request.MeasuredQuantityKg, request.OccurredAtUtc);
                }

                var inventory = await GuardedAsync(scope, permission,
                    () => participants.StageInventoryAsync(stockRequest, envelope, token), token);
                if (inventory is null || inventory.CompanyId != scope.CompanyId || inventory.AcceptanceId != request.AcceptanceId
                    || inventory.PurchaseOrderId != orderId || inventory.PurchaseOrderLineId != lineId
                    || inventory.PurchaseOrderVersion != purchase.PurchaseOrderVersion || inventory.ProductId != source.ProductId
                    || inventory.ProductVersion != request.ExpectedProductVersion || inventory.PurchaseUnitId != source.UnitId
                    || inventory.LotId != request.LotId || inventory.LabelId != request.LabelId || !ReceiptVersion(inventory.LabelVersion)
                    || inventory.MovementId != request.MovementId || inventory.QuantityKg != request.MeasuredQuantityKg
                    || inventory.IsReplay != purchase.IsReplay || inventory.OriginalCorrelationId != purchase.OriginalCorrelationId)
                {
                    throw ReceiptSourceInvalid();
                }

                var receipt = new PurchaseGoodsReceiptViewV1(scope.CompanyId, request.AcceptanceId, orderId, lineId,
                    purchase.PurchaseOrderVersion, source.ProductId, inventory.ProductVersion, source.UnitId,
                    purchase.AcceptedQuantity, purchase.RemainingQuantity, inventory.LotId, inventory.LabelId,
                    request.LabelCode, inventory.LabelVersion, inventory.MovementId, request.LocationId,
                    inventory.QuantityKg, stockRequest.InitialBlock, request.OccurredAtUtc, inventory.StockItemQuantity);
                RequireQuantityProjection(receipt);
                return new PurchaseGoodsReceiptResponseV1(receipt, purchase.IsReplay, purchase.OriginalCorrelationId);
            }, token => RequireStillAsync(scope, permission, token), cancellationToken);
        }
        catch (PurchaseAcceptanceParticipantException exception)
        {
            throw new PurchasingServiceException(exception.StatusCode, exception.Code);
        }
        catch (PurchasingStoreException exception) { throw Store(exception); }
    }

    public async Task<PurchaseGoodsReceiptOptionsV1> GetGoodsReceiptOptionsAsync(Guid orderId, Guid lineId,
        CancellationToken cancellationToken = default)
    {
        var scope = await RequireAsync(bindings.GoodsReceiptPermission, cancellationToken);
        var source = await ReceiptSourceAsync(scope, bindings.GoodsReceiptPermission, orderId, lineId, cancellationToken);
        var reads = receiptReads ?? throw ReceiptUnavailable();
        var answer = await GuardedAsync(scope, bindings.GoodsReceiptPermission,
            () => reads.GetOptionsAsync(source, cancellationToken), cancellationToken);
        if (answer is null || answer.Source != source || answer.Locations is null || answer.Locations.Count > 100
            || answer.Locations.Any(location => location is null || location.LocationId == Guid.Empty
                || string.IsNullOrWhiteSpace(location.LocationCode) || string.IsNullOrWhiteSpace(location.LocationName)
                || string.IsNullOrWhiteSpace(location.SiteName))
            || answer.Locations.Select(location => location.LocationId).Distinct().Count() != answer.Locations.Count)
        {
            throw ReceiptSourceInvalid();
        }
        return answer;
    }

    public async Task<PurchaseGoodsReceiptDetailV1> GetGoodsReceiptAsync(Guid orderId, Guid acceptanceId,
        CancellationToken cancellationToken = default)
    {
        var permission = bindings.ReadPermission;
        var scope = await RequireAsync(permission, cancellationToken);
        if (orderId == Guid.Empty || acceptanceId == Guid.Empty)
        {
            throw new PurchasingServiceException(404, "not_found");
        }

        var stored = await GuardedAsync(scope, permission,
            () => store.FindOrderAsync(orderId, cancellationToken), cancellationToken)
            ?? throw new PurchasingServiceException(404, "not_found");
        var acceptance = stored.Order.Acceptances.SingleOrDefault(candidate => candidate.AcceptanceId == acceptanceId)
            ?? throw new PurchasingServiceException(404, "not_found");
        var line = stored.Order.Lines.SingleOrDefault(candidate => candidate.Id == acceptance.LineId)
            ?? throw ReceiptSourceInvalid();
        var reads = receiptReads ?? throw ReceiptUnavailable();
        var receipt = await GuardedAsync(scope, permission,
            () => reads.GetReceiptAsync(orderId, acceptanceId, cancellationToken), cancellationToken)
            ?? throw ReceiptSourceInvalid();

        try
        {
            _ = PurchaseGoodsReceiptQuantityComparisonFactoryV1.Create(
                receipt,
                scope.CompanyId,
                orderId,
                acceptanceId,
                acceptance.LineId,
                line.ProductId,
                VersionOf(stored.ProductVersionsByLineId, line.Id),
                line.UnitId,
                Text(acceptance.Quantity),
                storedUnitIsKilogram: false,
                currentPurchaseOrderVersion: stored.Order.Version.ToString(CultureInfo.InvariantCulture));
        }
        catch (ArgumentException)
        {
            throw ReceiptSourceInvalid();
        }

        if (receipt.LotId == Guid.Empty || receipt.LabelId == Guid.Empty || receipt.MovementId == Guid.Empty
            || receipt.LocationId == Guid.Empty || !ReceiptToken(receipt.LabelCode)
            || !ReceiptVersion(receipt.LabelVersion) || !ReceiptNonNegativeQuantity(receipt.RemainingQuantity)
            || receipt.InitialBlock is not ("Free" or "Blocked") || receipt.OccurredAtUtc != acceptance.RecordedAtUtc)
        {
            throw ReceiptSourceInvalid();
        }
        RequireQuantityProjection(receipt);

        return new(receipt);
    }

    public async Task<PurchaseGoodsReceiptPageV1> GetGoodsReceiptsAsync(Guid orderId, int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var scope = await RequireAsync(bindings.ReadPermission, cancellationToken);
        if (limit is < 1 or > 100) throw new PurchasingServiceException(400, "validation_failed");
        var order = await GuardedAsync(scope, bindings.ReadPermission, () => store.FindOrderAsync(orderId, cancellationToken), cancellationToken)
            ?? throw new PurchasingServiceException(404, "not_found");
        var receipts = new List<PurchaseGoodsReceiptViewV1>();
        foreach (var acceptance in order.Order.Acceptances.TakeLast(limit).Reverse())
        {
            var reads = receiptReads ?? throw ReceiptUnavailable();
            var receipt = await GuardedAsync(scope, bindings.ReadPermission,
                () => reads.GetReceiptAsync(orderId, acceptance.AcceptanceId, cancellationToken), cancellationToken);
            // A share without an Inventory source is not silently advertised as an atomic physical receipt.
            if (receipt is null || receipt.CompanyId != scope.CompanyId || receipt.PurchaseOrderId != orderId
                || receipt.AcceptanceId != acceptance.AcceptanceId || receipt.PurchaseOrderLineId != acceptance.LineId
                || receipt.AcceptedQuantity != Text(acceptance.Quantity) || receipt.LabelId == Guid.Empty
                || receipt.LotId == Guid.Empty || receipt.MovementId == Guid.Empty || string.IsNullOrWhiteSpace(receipt.LabelCode)
                || receipt.InitialBlock is not ("Free" or "Blocked"))
            {
                throw ReceiptSourceInvalid();
            }
            var line = order.Order.Lines.SingleOrDefault(candidate => candidate.Id == acceptance.LineId)
                ?? throw ReceiptSourceInvalid();
            try
            {
                _ = PurchaseGoodsReceiptQuantityComparisonFactoryV1.Create(
                    receipt,
                    scope.CompanyId,
                    orderId,
                    acceptance.AcceptanceId,
                    acceptance.LineId,
                    line.ProductId,
                    VersionOf(order.ProductVersionsByLineId, line.Id),
                    line.UnitId,
                    Text(acceptance.Quantity),
                    storedUnitIsKilogram: false,
                    currentPurchaseOrderVersion: order.Order.Version.ToString(CultureInfo.InvariantCulture));
            }
            catch (ArgumentException)
            {
                throw ReceiptSourceInvalid();
            }
            RequireQuantityProjection(receipt);
            receipts.Add(receipt);
        }
        return new(receipts, order.Order.Acceptances.Count > limit);
    }

    private async Task<PurchaseReceiptSourceV1> ReceiptSourceAsync(Scope scope, string? permission, Guid orderId, Guid lineId,
        CancellationToken cancellationToken)
    {
        var stored = await GuardedAsync(scope, permission, () => store.FindOrderAsync(orderId, cancellationToken), cancellationToken)
            ?? throw new PurchasingServiceException(404, "not_found");
        var line = stored.Order.Lines.SingleOrDefault(candidate => candidate.Id == lineId)
            ?? throw new PurchasingServiceException(404, "not_found");
        if (stored.Order.Status != PurchaseOrderStatus.Approved) throw new PurchasingServiceException(409, PurchaseAcceptanceParticipantCodes.OrderStateConflict);
        return new(scope.CompanyId, orderId, lineId, stored.Order.Version.ToString(CultureInfo.InvariantCulture),
            line.ProductId, VersionOf(stored.ProductVersionsByLineId, lineId), line.UnitId, stored.EffectiveOn);
    }

    private static void RequireOriginalReceipt(Guid companyId, Guid orderId, Guid lineId, RecordPurchaseGoodsReceiptV1 request,
        PurchaseReceiptStockRequestV1 original)
    {
        if (original.CompanyId != companyId || original.ProductId == Guid.Empty || original.PurchaseUnitId == Guid.Empty
            || original.InitialBlock is not ("Free" or "Blocked"))
            throw ReceiptSourceInvalid();
        if (original.AcceptanceId != request.AcceptanceId || original.PurchaseOrderId != orderId || original.PurchaseOrderLineId != lineId
            || original.ExpectedProductVersion != request.ExpectedProductVersion || original.LotId != request.LotId
            || original.LabelId != request.LabelId || original.LabelCode != request.LabelCode || original.HeatNumber != request.HeatNumber
            || original.CoilNumber != request.CoilNumber || original.LocationId != request.LocationId
            || original.MovementId != request.MovementId || original.QuantityKg != request.MeasuredQuantityKg
            || original.OccurredAtUtc != request.OccurredAtUtc)
        {
            throw new PurchasingServiceException(409, PurchasingProblemCodesV1.SameKeyPayloadMismatch);
        }
    }

    private static void RequireReceiptFacts(PurchaseReceiptSourceV1 source, Guid locationId, PurchaseReceiptFactsV1 facts)
    {
        if (facts is null || facts.CompanyId != source.CompanyId || facts.ProductId != source.ProductId
            || facts.ProductVersion != source.ProductVersion || facts.LocationId != locationId || locationId == Guid.Empty
            || facts.OwnerId == Guid.Empty || facts.CustodianId == Guid.Empty
            || facts.OwnerKind is not ("Company" or "Counterparty") || facts.CustodianKind is not ("Company" or "Counterparty"))
        {
            throw ReceiptSourceInvalid();
        }
    }

    private static bool ReceiptVersion(string value) => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var version)
        && version > 0 && version.ToString(CultureInfo.InvariantCulture) == value;
    private static bool ReceiptNonNegativeQuantity(string value) =>
        decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var quantity)
        && quantity >= 0m && value == PurchaseAcceptanceParticipantContractV1.FormatQuantity(quantity);
    private static bool ReceiptToken(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 64
        && value == value.Trim() && !value.Any(char.IsControl);

    private static void RequireQuantityProjection(PurchaseGoodsReceiptViewV1 receipt)
    {
        var quantity = receipt.StockItemQuantity;
        if (quantity is null) return;
        if (quantity.Subject is null || quantity.Source is null || quantity.BaseQuantity is null
            || quantity.CompanyId != receipt.CompanyId
            || quantity.Subject.Kind != PurchaseGoodsReceiptApiV1.QuantitySubjectKind
            || quantity.Subject.Id != receipt.ProductId
            || quantity.Source.Kind != PurchaseGoodsReceiptApiV1.QuantitySourceKind
            || quantity.Source.Id != receipt.AcceptanceId
            || quantity.Source.Version != receipt.PurchaseOrderVersion
            || quantity.BaseQuantity.Value != receipt.MeasuredQuantityKg
            || quantity.BaseQuantity.UnitId != SettingsUnitIdentitiesV1.Kilogram
            || quantity.BaseQuantity.ProfileId == Guid.Empty
            || !ReceiptVersion(quantity.BaseQuantity.ProfileVersion)
            || quantity.BaseQuantity.Dimension != PurchaseGoodsReceiptApiV1.QuantityDimension
            || quantity.BaseQuantity.Scale is < 0 or > PurchaseGoodsReceiptApiV1.MaximumKilogramScale
            || quantity.AlternateQuantity is not null)
        {
            throw ReceiptSourceInvalid();
        }
    }

    private static PurchasingServiceException ReceiptUnavailable() => new(503, PurchaseGoodsReceiptCodesV1.Unavailable);
    private static PurchasingServiceException ReceiptSourceInvalid() => new(503, PurchaseGoodsReceiptCodesV1.SourceInvalid);
}
