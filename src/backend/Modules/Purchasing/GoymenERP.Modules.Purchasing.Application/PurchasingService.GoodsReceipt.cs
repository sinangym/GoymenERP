using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.GoodsReceipts;
using GoymenERP.Modules.Purchasing.Domain.Orders;

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
        var piece = await PieceReceiptAsync(scope, permission, orderId, lineId, request, cancellationToken);
        // AK-3b: the comparison is evaluated once, for a first attempt, with the Ayarlar tolerance in force now. A replay answers
        // the comparison recorded with the acceptance, so an unusable policy read is only raised when a first attempt needs it.
        PurchaseGoodsReceiptPieceKilogramsV1? comparison = null;
        PurchasingServiceException? comparisonFailure = null;
        if (piece is not null)
        {
            try { comparison = piece.Compare(await ReadReceiptPolicyAsync(scope, permission, cancellationToken)); }
            catch (PurchasingServiceException exception) when (exception.Status != 403) { comparisonFailure = exception; }
        }

        try
        {
            return await runner.ExecuteAsync(scope.CompanyId, new(orderId, lineId, request.ExpectedPurchaseOrderVersion),
                request.AcceptanceId, envelope, async (participants, token) =>
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
                    if (comparisonFailure is not null) throw comparisonFailure;
                }

                var purchase = await GuardedAsync(scope, permission, () => participants.StagePurchaseAsync(
                    new(scope.CompanyId, request.AcceptanceId, orderId, lineId, request.ExpectedPurchaseOrderVersion,
                        source.ProductId, request.ExpectedProductVersion, source.UnitId, request.AcceptedQuantity, request.OccurredAtUtc,
                        piece is null ? null : request.MeasuredQuantityKg) { PieceKilograms = original is null ? comparison : null },
                    envelope, token), token);
                if (purchase is null || purchase.CompanyId != scope.CompanyId || purchase.AcceptanceId != request.AcceptanceId
                    || purchase.PurchaseOrderId != orderId || purchase.PurchaseOrderLineId != lineId
                    || purchase.AcceptedQuantity != request.AcceptedQuantity || !ReceiptVersion(purchase.PurchaseOrderVersion)
                    || purchase.IsReplay != (original is not null) || string.IsNullOrWhiteSpace(purchase.OriginalCorrelationId)
                    || !PieceReceiptFacts.Describes(piece, purchase.PieceKilograms) || !purchase.IsReplay && purchase.PieceKilograms != comparison)
                {
                    throw ReceiptSourceInvalid();
                }

                PurchaseReceiptStockRequestV1 stockRequest;
                if (original is not null)
                {
                    if (original.PurchaseOrderVersionAfter != purchase.PurchaseOrderVersion || original.PieceCount != piece?.PieceCount)
                        throw ReceiptSourceInvalid();
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
                        request.MovementId, request.MeasuredQuantityKg, request.OccurredAtUtc) { PieceCount = piece?.PieceCount };
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

                var releases = RequireFutureSupplyReleases(inventory.FutureSupplyReleases);
                return new PurchaseGoodsReceiptResponseV1(new(scope.CompanyId, request.AcceptanceId, orderId, lineId,
                    purchase.PurchaseOrderVersion, source.ProductId, inventory.ProductVersion, source.UnitId,
                    purchase.AcceptedQuantity, purchase.RemainingQuantity, inventory.LotId, inventory.LabelId,
                    request.LabelCode, inventory.LabelVersion, inventory.MovementId, request.LocationId,
                    inventory.QuantityKg, stockRequest.InitialBlock, request.OccurredAtUtc), purchase.IsReplay, purchase.OriginalCorrelationId)
                {
                    FutureSupplyReleases = releases,
                    // First attempt: the comparison just recorded. Replay: the recorded one (null for an acceptance older than AK-3b).
                    PieceKilograms = purchase.PieceKilograms,
                };
            }, token => RequireStillAsync(scope, permission, token), cancellationToken);
        }
        catch (PurchaseAcceptanceParticipantException exception)
        {
            await RequireStillAsync(scope, permission, cancellationToken);
            throw new PurchasingServiceException(exception.StatusCode, exception.Code);
        }
        catch (PurchasingStoreException exception)
        {
            await RequireStillAsync(scope, permission, cancellationToken);
            throw Store(exception);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            // Host preparation can refuse before any GuardedAsync callback read/stage. Recheck the original command's
            // authority/company before disclosing that owner/lock failure; never invoke beforeCommit an extra time.
            await RequireStillAsync(scope, permission, cancellationToken);
            throw;
        }
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
            || answer.Locations.Select(location => location.LocationId).Distinct().Count() != answer.Locations.Count
            || answer.FutureSupply is not { } future || future.CompanyId != scope.CompanyId
            || future.PurchaseOrderId != orderId || future.PurchaseOrderGeneralLineId != source.PurchaseOrderGeneralLineId
            || future.UnitId != source.UnitId || !ReceiptNonNegativeQuantity(future.ActiveAllocatedQuantity))
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
            line.ProductId, VersionOf(stored.ProductVersionsByLineId, lineId), line.UnitId, stored.EffectiveOn)
        {
            PurchaseOrderGeneralLineId = line.Kind switch
            {
                PurchaseOrderLineKind.General when line.ParentLineId is null => line.Id,
                PurchaseOrderLineKind.Technical when line.ParentLineId is { } parent && parent != Guid.Empty => parent,
                _ => throw ReceiptSourceInvalid(),
            },
        };
    }

    /// <summary>
    /// AK-3 (KLN-D119, BS-16/BS-17, golden TA4): the piece facts of a receipt on a piece-unit line, or null on a kilogram line. The
    /// accepted quantity must be a whole piece count; the line (a technical slice reads its general line) must carry the theoretical
    /// kg per piece recorded at approval (fail-closed 409 otherwise). The theoretical kilograms (pieces × kg/piece, URN-D9) are
    /// compared with the weighed kilograms against the Ayarlar tolerance by <see cref="PieceReceiptFacts.Compare"/>; the outcome is
    /// a warning, never a block, and the cost uses the weighed kilograms (ALS-D119).
    /// </summary>
    private async Task<PieceReceiptFacts?> PieceReceiptAsync(Scope scope, string? permission, Guid orderId, Guid lineId,
        RecordPurchaseGoodsReceiptV1 request, CancellationToken cancellationToken)
    {
        if (orderId == Guid.Empty || lineId == Guid.Empty) return null;
        var stored = await GuardedAsync(scope, permission, () => store.FindOrderAsync(orderId, cancellationToken), cancellationToken);
        var line = stored?.Order.Lines.SingleOrDefault(candidate => candidate.Id == lineId);
        if (stored is null || line is null || line.Quantity.Unit != QuantityUnit.Piece) return null;
        return PieceReceiptFacts.Of(stored.Order, line, request.AcceptedQuantity, request.MeasuredQuantityKg, "acceptedQuantity");
    }

    /// <summary>
    /// AK-3/AK-3b: one piece receipt's own facts (single receipt and each bulk-receipt label share them): whole pieces, the kg per
    /// piece recorded on the general line at approval, the theoretical kilograms and the weighed kilograms. No tolerance is read here.
    /// </summary>
    private sealed record PieceReceiptFacts(int PieceCount, decimal KilogramsPerUnit, decimal TheoreticalKg, decimal MeasuredKg)
    {
        public static PieceReceiptFacts Of(PurchaseOrder order, PurchaseOrderLine line, string acceptedQuantity, string measuredQuantityKg,
            string field)
        {
            var snapshot = KilogramsPerUnitOf(order, line);
            var pieces = decimal.Parse(acceptedQuantity, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
            if (decimal.Truncate(pieces) != pieces || pieces > int.MaxValue)
                throw new PurchasingServiceException(400, PurchaseGoodsReceiptCodesV1.PieceCountInvalid, [new(field, PurchaseGoodsReceiptCodesV1.PieceCountInvalid)]);
            var measured = decimal.Parse(measuredQuantityKg, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
            return new((int)pieces, snapshot, PurchasePieceKilograms.TheoreticalKilograms(pieces, snapshot), measured);
        }

        /// <summary>The kg per piece recorded at approval on the line's general line; a piece line without it takes no receipt (409).</summary>
        public static decimal KilogramsPerUnitOf(PurchaseOrder order, PurchaseOrderLine line)
        {
            var general = line.Kind == PurchaseOrderLineKind.General ? line
                : order.Lines.SingleOrDefault(candidate => candidate.Id == line.ParentLineId) ?? throw ReceiptSourceInvalid();
            return (general.TheoreticalKilograms
                ?? throw new PurchasingServiceException(409, PurchaseGoodsReceiptCodesV1.TheoreticalKilogramsMissing)).KilogramsPerUnit;
        }

        /// <summary>A recorded comparison belongs to this receipt: same pieces and weighed kg; a kilogram line has none.</summary>
        public static bool Describes(PieceReceiptFacts? facts, PurchaseGoodsReceiptPieceKilogramsV1? recorded) => recorded is null
            || facts is not null && recorded.PieceCount == facts.PieceCount && recorded.MeasuredQuantityKg == Exact(facts.MeasuredKg);

        /// <summary>BS-16/BS-17 (golden TA4): weighed against theoretical under the Ayarlar tolerance read by the caller.</summary>
        public PurchaseGoodsReceiptPieceKilogramsV1 Compare(PurchaseGoodsReceiptPolicyV1 policy) => new(PieceCount, Exact(KilogramsPerUnit),
            Exact(TheoreticalKg), Exact(MeasuredKg), Exact(MeasuredKg - TheoreticalKg),
            GoodsReceiptKgVariance.Evaluate(TheoreticalKg, MeasuredKg, policy.KgTolerancePercent).ToString(),
            policy.KgTolerancePercent is { } tolerance ? Exact(tolerance) : null);
    }

    private static string Exact(decimal value) => PurchaseAcceptanceParticipantContractV1.FormatQuantity(value);

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

    /// <summary>
    /// KLN-D94: the released future allocations are owner facts from Inventory's conversion in the same transaction. A malformed
    /// list is not shown as a partial warning; it fails closed like any other inconsistent receipt source (the receipt rolls back).
    /// </summary>
    private static IReadOnlyList<PurchaseReceiptFutureSupplyReleaseV1> RequireFutureSupplyReleases(
        IReadOnlyList<PurchaseReceiptFutureSupplyReleaseV1>? releases)
    {
        if (releases is null || releases.Count == 0) return [];
        var seen = new HashSet<Guid>();
        foreach (var release in releases)
        {
            if (release is null || release.FutureAllocationId == Guid.Empty || !seen.Add(release.FutureAllocationId)
                || !ReceiptPositiveQuantity(release.ReleasedQuantity)
                || release.FailureCode is null || !PurchaseReceiptFutureSupplyReleaseCodesV1.All.Contains(release.FailureCode))
            {
                throw ReceiptSourceInvalid();
            }
        }
        return releases.ToArray();
    }

    private static bool ReceiptPositiveQuantity(string? value) => value is not null && value == value.Trim()
        && decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var quantity) && quantity > 0m;

    private static bool ReceiptVersion(string value) => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var version)
        && version > 0 && version.ToString(CultureInfo.InvariantCulture) == value;
    private static bool ReceiptNonNegativeQuantity(string value) =>
        decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var quantity)
        && quantity >= 0m && value == PurchaseAcceptanceParticipantContractV1.FormatQuantity(quantity);
    private static bool ReceiptToken(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 64
        && value == value.Trim() && !value.Any(char.IsControl);
    private static PurchasingServiceException ReceiptUnavailable() => new(503, PurchaseGoodsReceiptCodesV1.Unavailable);
    private static PurchasingServiceException ReceiptSourceInvalid() => new(503, PurchaseGoodsReceiptCodesV1.SourceInvalid);
}
