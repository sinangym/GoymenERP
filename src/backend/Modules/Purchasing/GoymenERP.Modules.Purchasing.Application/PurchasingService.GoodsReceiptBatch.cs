using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.GoodsReceipts;
using GoymenERP.Modules.Purchasing.Domain.Orders;

namespace GoymenERP.Modules.Purchasing.Application;

/// <summary>
/// ALC-S16 (F-027; KLN-D41, KLN-D43, KLN-D45): the bulk goods receipt. One receipt of one kg order line holds many labels; the
/// total kg is compared with the line's remaining kg under the Ayarlar tolerance. Inside the tolerance the user acknowledges the
/// warning and every label posts at once; outside it (or with no tolerance set) the batch waits for an approver who is not the
/// preparer. Each label posts through the existing single-label receipt participants, unchanged, in ONE host transaction.
/// </summary>
public sealed partial class PurchasingService
{
    public async Task<PurchaseGoodsReceiptBatchResponseV1> SubmitGoodsReceiptBatchAsync(Guid orderId, Guid lineId,
        SubmitPurchaseGoodsReceiptBatchV1 request, PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();
        var permission = bindings.GoodsReceiptPermission;
        var scope = await RequireAsync(permission, cancellationToken);
        if (orderId == Guid.Empty || lineId == Guid.Empty) throw new PurchasingServiceException(404, "not_found");
        try { PurchaseGoodsReceiptBatchJsonV1.ValidateSubmit(request); }
        catch (JsonException) { throw new PurchasingServiceException(400, "validation_failed"); }
        var batches = batchStore ?? throw BatchUnavailable();
        var runner = batchTransactions ?? throw BatchUnavailable();
        const string command = PurchaseGoodsReceiptBatchCommandsV1.SubmitV1;
        var requestHash = PurchasingRequestHash.Compute(command, orderId, new { lineId, request });
        var envelope = Envelope(scope, metadata, permission!);
        var replay = await FindBatchReplayAsync(scope, permission, batches, envelope, requestHash, command, cancellationToken);
        if (replay is not null) return await BatchReplayedAsync(scope, permission, batches, orderId, replay, cancellationToken);

        var stored = await GuardedAsync(scope, permission, () => store.FindOrderAsync(orderId, cancellationToken), cancellationToken)
            ?? throw new PurchasingServiceException(404, "not_found");
        var line = stored.Order.Lines.SingleOrDefault(candidate => candidate.Id == lineId)
            ?? throw new PurchasingServiceException(404, "not_found");
        if (stored.Order.Status != PurchaseOrderStatus.Approved)
        {
            throw new PurchasingServiceException(409, PurchaseAcceptanceParticipantCodes.OrderStateConflict);
        }

        var source = new PurchaseReceiptSourceV1(scope.CompanyId, orderId, lineId, stored.Order.Version.ToString(CultureInfo.InvariantCulture),
            line.ProductId, VersionOf(stored.ProductVersionsByLineId, lineId), line.UnitId, stored.EffectiveOn);
        if (!string.Equals(source.PurchaseOrderVersion, request.ExpectedPurchaseOrderVersion, StringComparison.Ordinal))
        {
            return await BatchTerminalAsync(scope, permission, batches, orderId, envelope, requestHash, command,
                new(409, PurchasingProblemCodesV1.VersionConflict, [new("expectedPurchaseOrderVersion", PurchasingProblemCodesV1.VersionConflict)]),
                cancellationToken);
        }

        if (!string.Equals(source.ProductVersion, request.ExpectedProductVersion, StringComparison.Ordinal))
        {
            return await BatchTerminalAsync(scope, permission, batches, orderId, envelope, requestHash, command,
                new(409, PurchasingProblemCodesV1.VersionConflict, [new("expectedProductVersion", PurchasingProblemCodesV1.VersionConflict)]),
                cancellationToken);
        }

        if (line.RemainingQuantity.Unit != QuantityUnit.Kilogram)
        {
            throw new PurchasingServiceException(409, PurchaseGoodsReceiptBatchCodesV1.UnitUnsupported, [new("lineId", PurchaseGoodsReceiptBatchCodesV1.UnitUnsupported)]);
        }

        var policy = await ReadReceiptPolicyAsync(scope, permission, cancellationToken);
        RequireDistinctIdentities(request.Labels);
        var quantities = request.Labels.Select(label => ParseKg(label.MeasuredQuantityKg)).ToArray();
        var received = quantities.Sum();
        var expected = line.RemainingQuantity.Value;
        if (expected <= 0m || received > expected)
        {
            // The existing acceptance rule holds: a receipt never exceeds the line's remaining quantity.
            throw new PurchasingServiceException(409, PurchaseGoodsReceiptBatchCodesV1.ExceedsRemaining, [new("labels", PurchaseGoodsReceiptBatchCodesV1.ExceedsRemaining)]);
        }

        var labels = await ResolveLabelCodesAsync(scope, permission, batches, request.Labels, quantities, policy, cancellationToken);
        if (await GuardedAsync(scope, permission, () => batches.BatchIdExistsAsync(request.BatchId, cancellationToken), cancellationToken))
        {
            throw new PurchasingServiceException(409, PurchaseGoodsReceiptBatchCodesV1.BatchIdTaken, [new("batchId", PurchaseGoodsReceiptBatchCodesV1.BatchIdTaken)]);
        }

        var outcome = GoodsReceiptKgVariance.Evaluate(expected, received, policy.KgTolerancePercent);
        if (outcome == GoodsReceiptKgVarianceOutcome.WithinTolerance && !request.VarianceAcknowledged)
        {
            // KLN-D45: inside the tolerance is a warning the user answers; nothing is recorded until then.
            throw new PurchasingServiceException(409, PurchaseGoodsReceiptBatchCodesV1.VarianceAcknowledgementRequired,
                [new("varianceAcknowledged", PurchaseGoodsReceiptBatchCodesV1.VarianceAcknowledgementRequired)]);
        }

        var status = GoodsReceiptKgVariance.PostsWithoutApproval(outcome)
            ? PurchaseGoodsReceiptBatchStatus.Posted
            : PurchaseGoodsReceiptBatchStatus.PendingApproval;
        var draft = new PurchaseGoodsReceiptBatchDraft(request.BatchId, orderId, lineId, source.ProductId, source.ProductVersion, source.UnitId,
            request.LocationId, request.OccurredAtUtc, status, expected, received, outcome, policy.KgTolerancePercent,
            request.VarianceAcknowledged, labels);

        // KLN-D94/KLN-D110: the future supply allocations Inventory released while posting the labels (only a posting has any).
        IReadOnlyList<PurchaseReceiptFutureSupplyReleaseV1> releases = [];
        try
        {
            StoredPurchaseGoodsReceiptBatch saved;
            if (status == PurchaseGoodsReceiptBatchStatus.PendingApproval)
            {
                // Outside the tolerance (or none set): no stock and no acceptance until an approver decides.
                await RequireStillAsync(scope, permission, cancellationToken);
                saved = await batches.SaveNewAsync(draft, envelope, requestHash, command, cancellationToken);
            }
            else
            {
                saved = await runner.ExecuteAsync(scope.CompanyId, async (participants, token) =>
                {
                    releases = await PostBatchLabelsAsync(scope, permission, participants, source, draft, source.PurchaseOrderVersion, envelope, token);
                    return await batches.SaveNewAsync(draft, envelope, requestHash, command, token);
                }, token => RequireStillAsync(scope, permission, token), cancellationToken);
            }

            return new(BatchView(saved), IsReplay: false, envelope.CorrelationId) { FutureSupplyReleases = releases };
        }
        catch (PurchaseAcceptanceParticipantException exception)
        {
            throw new PurchasingServiceException(exception.StatusCode, exception.Code);
        }
        catch (PurchasingStoreException exception) when (exception.Code == PurchasingStoreCodes.ConcurrentChange)
        {
            var won = await FindBatchReplayAsync(scope, permission, batches, envelope, requestHash, command, cancellationToken);
            return won is null ? throw Store(exception) : await BatchReplayedAsync(scope, permission, batches, orderId, won, cancellationToken);
        }
        catch (PurchasingStoreException exception)
        {
            throw Store(exception);
        }
    }

    public Task<PurchaseGoodsReceiptBatchResponseV1> ApproveGoodsReceiptBatchAsync(Guid orderId, Guid batchId,
        DecidePurchaseGoodsReceiptBatchV1 request, PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default) =>
        DecideGoodsReceiptBatchAsync(orderId, batchId, request, metadata, PurchaseGoodsReceiptBatchCommandsV1.ApproveV1, approve: true, cancellationToken);

    public Task<PurchaseGoodsReceiptBatchResponseV1> RejectGoodsReceiptBatchAsync(Guid orderId, Guid batchId,
        DecidePurchaseGoodsReceiptBatchV1 request, PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default) =>
        DecideGoodsReceiptBatchAsync(orderId, batchId, request, metadata, PurchaseGoodsReceiptBatchCommandsV1.RejectV1, approve: false, cancellationToken);

    public async Task<PurchaseGoodsReceiptBatchDetailV1> GetGoodsReceiptBatchAsync(Guid orderId, Guid batchId,
        CancellationToken cancellationToken = default)
    {
        var permission = bindings.ReadPermission;
        var scope = await RequireAsync(permission, cancellationToken);
        if (orderId == Guid.Empty || batchId == Guid.Empty) throw new PurchasingServiceException(404, "not_found");
        var batches = batchStore ?? throw BatchUnavailable();
        var stored = await GuardedAsync(scope, permission, () => batches.FindAsync(orderId, batchId, cancellationToken), cancellationToken)
            ?? throw new PurchasingServiceException(404, "not_found");
        return new(BatchView(RequireOwnBatch(scope, orderId, stored)));
    }

    public async Task<PurchaseGoodsReceiptBatchPageV1> GetGoodsReceiptBatchesAsync(Guid orderId, int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var permission = bindings.ReadPermission;
        var scope = await RequireAsync(permission, cancellationToken);
        if (limit is < 1 or > MaximumPageSize) throw new PurchasingServiceException(400, "validation_failed");
        if (orderId == Guid.Empty) throw new PurchasingServiceException(404, "not_found");
        var batches = batchStore ?? throw BatchUnavailable();
        _ = await GuardedAsync(scope, permission, () => store.FindOrderAsync(orderId, cancellationToken), cancellationToken)
            ?? throw new PurchasingServiceException(404, "not_found");
        var rows = await GuardedAsync(scope, permission, () => batches.ListForOrderAsync(orderId, limit + 1, cancellationToken), cancellationToken);
        return new(rows.Take(limit).Select(row => BatchView(RequireOwnBatch(scope, orderId, row))).ToArray(), rows.Count > limit);
    }

    public async Task<PurchaseGoodsReceiptLabelCodeCheckV1> CheckGoodsReceiptLabelCodesAsync(IReadOnlyList<string> codes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(codes);
        var permission = bindings.GoodsReceiptPermission;
        var scope = await RequireAsync(permission, cancellationToken);
        if (codes.Count is 0 or > PurchaseGoodsReceiptBatchApiV1.MaximumCheckedCodes || codes.Any(code => !PurchaseGoodsReceiptBatchJsonV1.Token(code)))
        {
            throw new PurchasingServiceException(400, "validation_failed");
        }

        var batches = batchStore ?? throw BatchUnavailable();
        var policy = await ReadReceiptPolicyAsync(scope, permission, cancellationToken);
        var distinct = codes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var (taken, stockChecked) = await TakenCodesAsync(scope, permission, batches, distinct, cancellationToken);
        var items = new List<PurchaseGoodsReceiptLabelCodeAvailabilityV1>(codes.Count);
        foreach (var code in codes)
        {
            if (!taken.Contains(code))
            {
                items.Add(new(code, Available: true, Suggestion: null));
                continue;
            }

            items.Add(new(code, Available: false, await SuggestFreeCodeAsync(scope, permission, batches, code, policy, cancellationToken)));
        }

        return new(items, stockChecked);
    }

    public async Task<PurchaseGoodsReceiptLabelSearchPageV1> SearchGoodsReceiptLabelsAsync(string text, int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var permission = bindings.ReadPermission;
        var scope = await RequireAsync(permission, cancellationToken);
        var trimmed = text?.Trim();
        if (limit is < 1 or > MaximumPageSize || trimmed is null || trimmed.Length < PurchaseGoodsReceiptBatchApiV1.MinimumSearchLength
            || !PurchaseGoodsReceiptBatchJsonV1.Token(trimmed))
        {
            throw new PurchasingServiceException(400, "validation_failed");
        }

        var batches = batchStore ?? throw BatchUnavailable();
        var rows = await GuardedAsync(scope, permission, () => batches.SearchLabelsAsync(trimmed, limit + 1, cancellationToken), cancellationToken);
        return new(rows.Take(limit).ToArray(), rows.Count > limit);
    }

    // ---- decision --------------------------------------------------------------------------------

    private async Task<PurchaseGoodsReceiptBatchResponseV1> DecideGoodsReceiptBatchAsync(Guid orderId, Guid batchId,
        DecidePurchaseGoodsReceiptBatchV1 request, PurchasingCommandMetadata metadata, string command, bool approve,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();
        // KLN-D45: the approver holds the variance permission; a rejection is the same approver's decision.
        var permission = bindings.GoodsReceiptVarianceApprovePermission;
        var scope = await RequireAsync(permission, cancellationToken);
        if (orderId == Guid.Empty || batchId == Guid.Empty) throw new PurchasingServiceException(404, "not_found");
        try { PurchaseGoodsReceiptBatchJsonV1.ValidateDecision(request); }
        catch (JsonException) { throw new PurchasingServiceException(400, "validation_failed"); }
        var expectedVersion = ParseVersion(request.ExpectedVersion);
        var batches = batchStore ?? throw BatchUnavailable();
        var requestHash = PurchasingRequestHash.Compute(command, batchId, new { orderId, request });
        var envelope = Envelope(scope, metadata, permission!);
        var replay = await FindBatchReplayAsync(scope, permission, batches, envelope, requestHash, command, cancellationToken);
        if (replay is not null) return await BatchReplayedAsync(scope, permission, batches, orderId, replay, cancellationToken);

        var stored = await GuardedAsync(scope, permission, () => batches.FindAsync(orderId, batchId, cancellationToken), cancellationToken)
            ?? throw new PurchasingServiceException(404, "not_found");
        stored = RequireOwnBatch(scope, orderId, stored);
        var batch = new PurchaseGoodsReceiptBatch(batchId, stored.Batch.Status, stored.Version, stored.PreparedBy);
        var actor = scope.ActorId.Trim();
        try
        {
            if (approve) batch.Approve(expectedVersion, actor);
            else batch.Reject(expectedVersion, actor, request.Reason);
        }
        catch (PurchasingVersionConflictException)
        {
            return await BatchTerminalAsync(scope, permission, batches, orderId, envelope, requestHash, command,
                new(409, PurchasingProblemCodesV1.VersionConflict, [new("expectedVersion", PurchasingProblemCodesV1.VersionConflict)]), cancellationToken);
        }
        catch (PurchasingRuleException exception) when (exception.Code == PurchaseGoodsReceiptBatchRuleCodes.NotPending)
        {
            return await BatchTerminalAsync(scope, permission, batches, orderId, envelope, requestHash, command,
                new(409, exception.Code, [new("status", exception.Code)]), cancellationToken);
        }
        catch (PurchasingRuleException exception) when (exception.Code is PurchaseGoodsReceiptBatchRuleCodes.SameActor
                                                             or PurchaseGoodsReceiptBatchRuleCodes.MakerUnknown)
        {
            return await BatchTerminalAsync(scope, permission, batches, orderId, envelope, requestHash, command,
                new(409, exception.Code, [new("actor", exception.Code)]), cancellationToken);
        }
        catch (PurchasingRuleException exception)
        {
            throw new PurchasingServiceException(422, exception.Code, [new("reason", exception.Code)]);
        }

        // KLN-D94/KLN-D110: the future supply allocations Inventory released while posting the labels (only a posting has any).
        IReadOnlyList<PurchaseReceiptFutureSupplyReleaseV1> releases = [];
        try
        {
            StoredPurchaseGoodsReceiptBatch saved;
            if (!approve)
            {
                await RequireStillAsync(scope, permission, cancellationToken);
                saved = await batches.SaveDecisionAsync(orderId, batch, expectedVersion, envelope, requestHash, command, cancellationToken);
            }
            else
            {
                var runner = batchTransactions ?? throw BatchUnavailable();
                var source = await ApprovalSourceAsync(scope, permission, stored, cancellationToken);
                saved = await runner.ExecuteAsync(scope.CompanyId, async (participants, token) =>
                {
                    releases = await PostBatchLabelsAsync(scope, permission, participants, source, stored.Batch, source.PurchaseOrderVersion, envelope, token);
                    return await batches.SaveDecisionAsync(orderId, batch, expectedVersion, envelope, requestHash, command, token);
                }, token => RequireStillAsync(scope, permission, token), cancellationToken);
            }

            return new(BatchView(saved), IsReplay: false, envelope.CorrelationId) { FutureSupplyReleases = releases };
        }
        catch (PurchaseAcceptanceParticipantException exception)
        {
            throw new PurchasingServiceException(exception.StatusCode, exception.Code);
        }
        catch (PurchasingStoreException exception) when (exception.Code == PurchasingStoreCodes.ConcurrentChange)
        {
            var won = await FindBatchReplayAsync(scope, permission, batches, envelope, requestHash, command, cancellationToken);
            return won is null ? throw Store(exception) : await BatchReplayedAsync(scope, permission, batches, orderId, won, cancellationToken);
        }
        catch (PurchasingStoreException exception)
        {
            throw Store(exception);
        }
    }

    /// <summary>
    /// The approval posts against the order as it is NOW. The line must still be an approved kg line of the same product version
    /// and the batch total must still fit the remaining quantity (the existing acceptance rule); the variance itself was decided
    /// at submission and is what the approver approves.
    /// </summary>
    private async Task<PurchaseReceiptSourceV1> ApprovalSourceAsync(Scope scope, string? permission, StoredPurchaseGoodsReceiptBatch stored,
        CancellationToken cancellationToken)
    {
        var batch = stored.Batch;
        var order = await GuardedAsync(scope, permission, () => store.FindOrderAsync(batch.PurchaseOrderId, cancellationToken), cancellationToken)
            ?? throw ReceiptSourceInvalid();
        var line = order.Order.Lines.SingleOrDefault(candidate => candidate.Id == batch.PurchaseOrderLineId) ?? throw ReceiptSourceInvalid();
        if (order.Order.Status != PurchaseOrderStatus.Approved)
        {
            throw new PurchasingServiceException(409, PurchaseAcceptanceParticipantCodes.OrderStateConflict);
        }

        var productVersion = VersionOf(order.ProductVersionsByLineId, line.Id);
        if (line.ProductId != batch.ProductId || line.UnitId != batch.UnitId || !string.Equals(productVersion, batch.ProductVersion, StringComparison.Ordinal))
        {
            throw new PurchasingServiceException(409, PurchasingProblemCodesV1.VersionConflict, [new("productVersion", PurchasingProblemCodesV1.VersionConflict)]);
        }

        if (line.RemainingQuantity.Unit != QuantityUnit.Kilogram || batch.ReceivedQuantityKg > line.RemainingQuantity.Value)
        {
            throw new PurchasingServiceException(409, PurchaseGoodsReceiptBatchCodesV1.ExceedsRemaining, [new("labels", PurchaseGoodsReceiptBatchCodesV1.ExceedsRemaining)]);
        }

        return new(scope.CompanyId, batch.PurchaseOrderId, batch.PurchaseOrderLineId, order.Order.Version.ToString(CultureInfo.InvariantCulture),
            line.ProductId, productVersion, line.UnitId, order.EffectiveOn);
    }

    // ---- posting ---------------------------------------------------------------------------------

    /// <summary>
    /// Posts every label of the batch through a FRESH pair of the existing receipt participants, in label order, inside the host's
    /// single transaction. Each label carries a derived idempotency key (batch + line number), so the acceptance and stock rows of
    /// one label never collide with another label's; the order version chains from one label to the next.
    /// </summary>
    private async Task<IReadOnlyList<PurchaseReceiptFutureSupplyReleaseV1>> PostBatchLabelsAsync(Scope scope, string? permission,
        IPurchaseGoodsReceiptBatchParticipantsV1 participants, PurchaseReceiptSourceV1 source, PurchaseGoodsReceiptBatchDraft batch,
        string startOrderVersion, PurchasingWriteEnvelope envelope, CancellationToken cancellationToken)
    {
        var orderVersion = startOrderVersion;
        var releases = new List<PurchaseReceiptFutureSupplyReleaseV1>();
        for (var index = 0; index < batch.Labels.Count; index++)
        {
            var label = batch.Labels[index];
            var field = $"labels[{index}]";
            var kg = PurchaseAcceptanceParticipantContractV1.FormatQuantity(label.MeasuredQuantityKg);
            var labelEnvelope = envelope with { IdempotencyKey = LabelIdempotencyKey(batch.BatchId, label.LineNo) };
            var receipt = await participants.EnlistLabelAsync(index, cancellationToken);
            try
            {
                var original = await GuardedAsync(scope, permission,
                    () => receipt.ReadOriginalAsync(label.AcceptanceId, labelEnvelope, cancellationToken), cancellationToken);
                if (original is not null)
                {
                    throw new PurchasingServiceException(409, PurchaseGoodsReceiptBatchCodesV1.DuplicateLabel,
                        [new($"{field}.acceptanceId", PurchaseGoodsReceiptBatchCodesV1.DuplicateLabel)]);
                }

                var purchase = await GuardedAsync(scope, permission, () => receipt.StagePurchaseAsync(
                    new(scope.CompanyId, label.AcceptanceId, batch.PurchaseOrderId, batch.PurchaseOrderLineId, orderVersion,
                        source.ProductId, batch.ProductVersion, source.UnitId, kg, batch.OccurredAtUtc), labelEnvelope, cancellationToken), cancellationToken);
                if (purchase is null || purchase.CompanyId != scope.CompanyId || purchase.AcceptanceId != label.AcceptanceId
                    || purchase.PurchaseOrderId != batch.PurchaseOrderId || purchase.PurchaseOrderLineId != batch.PurchaseOrderLineId
                    || purchase.AcceptedQuantity != kg || !ReceiptVersion(purchase.PurchaseOrderVersion) || purchase.IsReplay
                    || string.IsNullOrWhiteSpace(purchase.OriginalCorrelationId))
                {
                    throw ReceiptSourceInvalid();
                }

                var facts = await GuardedAsync(scope, permission,
                    () => receipt.ResolveFirstAsync(source, batch.LocationId, cancellationToken), cancellationToken);
                RequireReceiptFacts(source, batch.LocationId, facts);
                var stockRequest = new PurchaseReceiptStockRequestV1(scope.CompanyId, label.AcceptanceId, batch.PurchaseOrderId,
                    batch.PurchaseOrderLineId, purchase.PurchaseOrderVersion, source.ProductId, batch.ProductVersion, source.UnitId,
                    label.LotId, label.LabelId, label.LabelCode, label.HeatNumber, label.CoilNumber,
                    PurchaseGoodsReceiptDocumentPolicyV1.RequiredForNewReceipt, facts.OwnerKind, facts.OwnerId, facts.CustodianKind,
                    facts.CustodianId, facts.LocationId, label.MovementId, kg, batch.OccurredAtUtc);
                var inventory = await GuardedAsync(scope, permission,
                    () => receipt.StageInventoryAsync(stockRequest, labelEnvelope, cancellationToken), cancellationToken);
                if (inventory is null || inventory.CompanyId != scope.CompanyId || inventory.AcceptanceId != label.AcceptanceId
                    || inventory.PurchaseOrderId != batch.PurchaseOrderId || inventory.PurchaseOrderLineId != batch.PurchaseOrderLineId
                    || inventory.PurchaseOrderVersion != purchase.PurchaseOrderVersion || inventory.ProductId != source.ProductId
                    || inventory.ProductVersion != batch.ProductVersion || inventory.PurchaseUnitId != source.UnitId
                    || inventory.LotId != label.LotId || inventory.LabelId != label.LabelId || !ReceiptVersion(inventory.LabelVersion)
                    || inventory.MovementId != label.MovementId || inventory.QuantityKg != kg || inventory.IsReplay)
                {
                    throw ReceiptSourceInvalid();
                }

                // KLN-D94 (same rule as the single receipt path): a label whose acceptance could not be linked to a future supply
                // allocation releases that allocation's whole remainder. The list is validated against the same code list
                // (PurchaseReceiptFutureSupplyReleaseCodesV1.All) and an allocation can be released at most once per batch; anything
                // else is an inconsistent answer and the whole batch fails closed rather than hiding a dropped Sales reservation.
                if (!PurchaseGoodsReceiptBatchFutureSupplyReleasesV1.TryAppend(releases,
                        RequireFutureSupplyReleases(inventory.FutureSupplyReleases)))
                {
                    throw ReceiptSourceInvalid();
                }

                orderVersion = purchase.PurchaseOrderVersion;
            }
            catch (PurchaseAcceptanceParticipantException exception)
            {
                throw new PurchasingServiceException(exception.StatusCode, exception.Code, [new(field, exception.Code)]);
            }
        }

        return releases;
    }

    /// <summary>The idempotency key of one label's acceptance/stock rows: stable per batch and line, at most 48 characters.</summary>
    internal static string LabelIdempotencyKey(Guid batchId, int lineNo) =>
        string.Create(CultureInfo.InvariantCulture, $"grb:{batchId:N}:{lineNo}");

    // ---- label codes (KLN-D41) -----------------------------------------------------------------------

    /// <summary>
    /// KLN-D41: an entered code wins, then the supplier's label number (kept separately and searchable), then a code generated from
    /// the Ayarlar format. A code taken in the company is a conflict the user resolves (the availability check suggests a suffix);
    /// it is never silently renamed.
    /// </summary>
    private async Task<IReadOnlyList<PurchaseGoodsReceiptBatchLabelRecord>> ResolveLabelCodesAsync(Scope scope, string? permission,
        IPurchaseGoodsReceiptBatchStore batches, IReadOnlyList<PurchaseGoodsReceiptBatchLabelInputV1> inputs, IReadOnlyList<decimal> quantities,
        PurchaseGoodsReceiptPolicyV1 policy, CancellationToken cancellationToken)
    {
        var codes = new string?[inputs.Count];
        var sources = new GoodsReceiptLabelCodeSource[inputs.Count];
        var missing = new List<int>();
        for (var index = 0; index < inputs.Count; index++)
        {
            var input = inputs[index];
            if (input.LabelCode is not null)
            {
                (codes[index], sources[index]) = (input.LabelCode, GoodsReceiptLabelCodeSource.Entered);
            }
            else if (input.SupplierLabelNumber is not null)
            {
                (codes[index], sources[index]) = (input.SupplierLabelNumber, GoodsReceiptLabelCodeSource.SupplierLabelNumber);
            }
            else
            {
                missing.Add(index);
            }
        }

        RequireDistinctCodes(codes);
        var given = codes.Where(code => code is not null).Select(code => code!).ToArray();
        if (given.Length > 0)
        {
            var (taken, _) = await TakenCodesAsync(scope, permission, batches, given, cancellationToken);
            var conflicts = Enumerable.Range(0, inputs.Count).Where(index => codes[index] is { } code && taken.Contains(code))
                .Select(index => new PurchasingFieldError($"labels[{index}].labelCode", PurchaseGoodsReceiptBatchCodesV1.LabelCodeConflict)).ToArray();
            if (conflicts.Length > 0) throw new PurchasingServiceException(409, PurchaseGoodsReceiptBatchCodesV1.LabelCodeConflict, conflicts);
        }

        var generated = new long?[inputs.Count];
        if (missing.Count > 0)
        {
            if (policy.GeneratedLabelCodeFormat is not { } format)
            {
                throw new PurchasingServiceException(422, PurchaseGoodsReceiptBatchCodesV1.LabelCodeRequired, missing
                    .Select(index => new PurchasingFieldError($"labels[{index}].labelCode", PurchaseGoodsReceiptBatchCodesV1.LabelCodeRequired)).ToArray());
            }

            var number = await GuardedAsync(scope, permission, () => batches.NextGeneratedNumberAsync(cancellationToken), cancellationToken);
            var used = new HashSet<string>(given, StringComparer.OrdinalIgnoreCase);
            foreach (var index in missing)
            {
                var attempts = 0;
                while (true)
                {
                    if (attempts++ >= GoodsReceiptLabelCodes.MaximumSuffixAttempts || number < 1)
                    {
                        throw new PurchasingServiceException(409, PurchaseGoodsReceiptBatchCodesV1.LabelCodeConflict,
                            [new($"labels[{index}].labelCode", PurchaseGoodsReceiptBatchCodesV1.LabelCodeConflict)]);
                    }

                    var candidate = GoodsReceiptLabelCodes.Format(format, number);
                    var current = number++;
                    if (used.Contains(candidate)) continue;
                    var (taken, _) = await TakenCodesAsync(scope, permission, batches, [candidate], cancellationToken);
                    if (taken.Contains(candidate)) continue;
                    used.Add(candidate);
                    (codes[index], sources[index], generated[index]) = (candidate, GoodsReceiptLabelCodeSource.Generated, current);
                    break;
                }
            }
        }

        return Enumerable.Range(0, inputs.Count).Select(index =>
        {
            var input = inputs[index];
            return new PurchaseGoodsReceiptBatchLabelRecord(index + 1, input.AcceptanceId, input.LotId, input.LabelId, input.MovementId,
                codes[index]!, sources[index], generated[index], input.SupplierLabelNumber, quantities[index], input.HeatNumber, input.CoilNumber);
        }).ToArray();
    }

    /// <summary>The codes taken in the company: Purchasing's unreleased labels, plus Inventory's register when that port is wired.</summary>
    private async Task<(IReadOnlySet<string> Taken, bool StockChecked)> TakenCodesAsync(Scope scope, string? permission,
        IPurchaseGoodsReceiptBatchStore batches, IReadOnlyCollection<string> codes, CancellationToken cancellationToken)
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        taken.UnionWith(await GuardedAsync(scope, permission, () => batches.TakenLabelCodesAsync(codes, cancellationToken), cancellationToken));
        if (labelRegister is null) return (taken, false);
        var registered = await GuardedAsync(scope, permission,
            () => labelRegister.RegisteredAsync(scope.CompanyId, codes, cancellationToken), cancellationToken) ?? throw ReceiptSourceInvalid();
        taken.UnionWith(registered);
        return (taken, true);
    }

    private async Task<string?> SuggestFreeCodeAsync(Scope scope, string? permission, IPurchaseGoodsReceiptBatchStore batches, string code,
        PurchaseGoodsReceiptPolicyV1 policy, CancellationToken cancellationToken)
    {
        if (policy.LabelCodeSuffixFormat is not { } suffix) return null;
        var candidates = new List<string>();
        _ = GoodsReceiptLabelCodes.SuggestSuffix(code, suffix, candidate =>
        {
            candidates.Add(candidate);
            return true;
        });
        if (candidates.Count == 0) return null;
        var (taken, _) = await TakenCodesAsync(scope, permission, batches, candidates, cancellationToken);
        return GoodsReceiptLabelCodes.SuggestSuffix(code, suffix, taken.Contains);
    }

    private static void RequireDistinctCodes(IReadOnlyList<string?> codes)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicates = new List<PurchasingFieldError>();
        for (var index = 0; index < codes.Count; index++)
        {
            if (codes[index] is { } code && !seen.Add(code))
            {
                duplicates.Add(new($"labels[{index}].labelCode", PurchaseGoodsReceiptBatchCodesV1.DuplicateLabel));
            }
        }

        if (duplicates.Count > 0) throw new PurchasingServiceException(422, PurchaseGoodsReceiptBatchCodesV1.DuplicateLabel, duplicates);
    }

    private static void RequireDistinctIdentities(IReadOnlyList<PurchaseGoodsReceiptBatchLabelInputV1> labels)
    {
        var seen = new HashSet<Guid>();
        var duplicates = new List<PurchasingFieldError>();
        for (var index = 0; index < labels.Count; index++)
        {
            var label = labels[index];
            if (!seen.Add(label.AcceptanceId) | !seen.Add(label.LotId) | !seen.Add(label.LabelId) | !seen.Add(label.MovementId))
            {
                duplicates.Add(new($"labels[{index}]", PurchaseGoodsReceiptBatchCodesV1.DuplicateLabel));
            }
        }

        if (duplicates.Count > 0) throw new PurchasingServiceException(422, PurchaseGoodsReceiptBatchCodesV1.DuplicateLabel, duplicates);
    }

    // ---- policy / replay / views ---------------------------------------------------------------------

    /// <summary>KLN-D45/KLN-D41: the Ayarlar values; an unusable answer is fail-closed (503), a missing one is <c>NotSet</c>.</summary>
    private async Task<PurchaseGoodsReceiptPolicyV1> ReadReceiptPolicyAsync(Scope scope, string? permission, CancellationToken cancellationToken)
    {
        if (receiptPolicy is null) return PurchaseGoodsReceiptPolicyV1.NotSet;
        var policy = await GuardedAsync(scope, permission, () => receiptPolicy.ReadAsync(scope.CompanyId, cancellationToken), cancellationToken);
        if (policy is null
            || policy.KgTolerancePercent is { } tolerance && !GoodsReceiptKgVariance.IsValidTolerancePercent(tolerance)
            || policy.LabelCodeSuffixFormat is { } suffix && !GoodsReceiptLabelCodes.IsValidFormat(suffix)
            || policy.GeneratedLabelCodeFormat is { } generated && !GoodsReceiptLabelCodes.IsValidFormat(generated))
        {
            throw new PurchasingServiceException(503, PurchaseGoodsReceiptBatchCodesV1.PolicyInvalid);
        }

        return policy;
    }

    private async Task<PurchaseGoodsReceiptBatchReplay?> FindBatchReplayAsync(Scope scope, string? permission, IPurchaseGoodsReceiptBatchStore batches,
        PurchasingWriteEnvelope envelope, string requestHash, string commandName, CancellationToken cancellationToken)
    {
        try
        {
            return await GuardedAsync(scope, permission, () => batches.FindReplayAsync(envelope, requestHash, commandName, cancellationToken), cancellationToken);
        }
        catch (PurchasingStoreException exception)
        {
            throw Store(exception);
        }
    }

    private async Task<PurchaseGoodsReceiptBatchResponseV1> BatchReplayedAsync(Scope scope, string? permission, IPurchaseGoodsReceiptBatchStore batches,
        Guid orderId, PurchaseGoodsReceiptBatchReplay replay, CancellationToken cancellationToken)
    {
        if (replay.Refusal is { } refusal)
        {
            throw new PurchasingServiceException(refusal.Status, refusal.Code, refusal.Errors, isReplay: true, originalCorrelationId: replay.OriginalCorrelationId);
        }

        if (replay.BatchId is not { } batchId) throw new PurchasingServiceException(503, "service_unavailable");
        var stored = await GuardedAsync(scope, permission, () => batches.FindAsync(orderId, batchId, cancellationToken), cancellationToken)
            ?? throw new PurchasingServiceException(503, "service_unavailable");
        return new(BatchView(RequireOwnBatch(scope, orderId, stored)), IsReplay: true, replay.OriginalCorrelationId);
    }

    /// <summary>§7/§8: a terminal refusal is recorded under the key before it is answered; a same-key winner is answered as a replay.</summary>
    private async Task<PurchaseGoodsReceiptBatchResponseV1> BatchTerminalAsync(Scope scope, string? permission, IPurchaseGoodsReceiptBatchStore batches,
        Guid orderId, PurchasingWriteEnvelope envelope, string requestHash, string commandName, PurchasingTerminalRefusal refusal,
        CancellationToken cancellationToken)
    {
        await RequireStillAsync(scope, permission, cancellationToken);
        try
        {
            await batches.RecordRefusalAsync(envelope, requestHash, commandName, refusal, cancellationToken);
        }
        catch (PurchasingStoreException exception) when (exception.Code == PurchasingStoreCodes.ConcurrentChange)
        {
            var won = await FindBatchReplayAsync(scope, permission, batches, envelope, requestHash, commandName, cancellationToken);
            if (won is not null) return await BatchReplayedAsync(scope, permission, batches, orderId, won, cancellationToken);
        }
        catch (PurchasingStoreException exception)
        {
            throw Store(exception);
        }

        throw new PurchasingServiceException(refusal.Status, refusal.Code, refusal.Errors);
    }

    private static StoredPurchaseGoodsReceiptBatch RequireOwnBatch(Scope scope, Guid orderId, StoredPurchaseGoodsReceiptBatch stored) =>
        stored.CompanyId != scope.CompanyId || stored.Batch.PurchaseOrderId != orderId ? throw ReceiptSourceInvalid() : stored;

    private static PurchaseGoodsReceiptBatchViewV1 BatchView(StoredPurchaseGoodsReceiptBatch stored)
    {
        var batch = stored.Batch;
        return new(stored.CompanyId, batch.BatchId, batch.PurchaseOrderId, batch.PurchaseOrderLineId, batch.Status.ToString(),
            stored.Version.ToString(CultureInfo.InvariantCulture), Kg(batch.ExpectedQuantityKg), Kg(batch.ReceivedQuantityKg),
            Kg(batch.ReceivedQuantityKg - batch.ExpectedQuantityKg), batch.VarianceOutcome.ToString(),
            batch.TolerancePercent is { } tolerance ? Kg(tolerance) : null, batch.VarianceAcknowledged, batch.LocationId, batch.OccurredAtUtc,
            stored.PreparedBy, stored.PreparedAtUtc, stored.DecidedBy, stored.DecidedAtUtc, stored.DecisionReason,
            batch.Labels.OrderBy(label => label.LineNo).Select(label => new PurchaseGoodsReceiptBatchLabelViewV1(label.LineNo, label.AcceptanceId,
                label.LotId, label.LabelId, label.MovementId, label.LabelCode, label.CodeSource.ToString(), label.SupplierLabelNumber,
                Kg(label.MeasuredQuantityKg), label.HeatNumber, label.CoilNumber)).ToArray());
    }

    private static string Kg(decimal value) => PurchaseAcceptanceParticipantContractV1.FormatQuantity(value);

    private static decimal ParseKg(string text) => decimal.Parse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);

    private static PurchasingServiceException BatchUnavailable() => new(503, PurchaseGoodsReceiptBatchCodesV1.Unavailable);
}
