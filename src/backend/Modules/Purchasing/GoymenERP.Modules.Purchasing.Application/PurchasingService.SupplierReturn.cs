using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Returns;

namespace GoymenERP.Modules.Purchasing.Application;

/// <summary>
/// ALC-S5 (B-19; SAT-D306, SAT-D392, SAT-D393, SAT-D396): return to supplier. A return names one recorded goods acceptance of the
/// order; Inventory resolves the label and leaves stock at the original purchase line's unit cost. SAT-D392: that cost is the
/// invoice's unit cost — when it is readable the exit is staged at once. SAT-D393: when it is not, the return carries a manual
/// unit cost and waits; nothing leaves stock until the exception is approved under <c>purchasing.orders.approve</c> (SAT-D396),
/// in ONE host transaction with Inventory's exit. ALS-D91: the order quantity is not reopened.
/// </summary>
public sealed partial class PurchasingService
{
    // decimal(18,4): at most 14 integer digits and four decimals for a TL/kg unit cost (Inventory's "0.####").
    private const int UnitCostScale = 4;
    private const decimal UnitCostUpperBound = 100_000_000_000_000m;

    public async Task<PurchaseSupplierReturnResponseV1> CreateSupplierReturnAsync(Guid orderId, CreatePurchaseSupplierReturnV1 request,
        PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();
        var permission = bindings.SupplierReturnPermission;
        var scope = await RequireAsync(permission, cancellationToken);
        if (orderId == Guid.Empty) throw new PurchasingServiceException(404, "not_found");
        try { PurchaseSupplierReturnJsonV1.ValidateCreate(request); }
        catch (JsonException) { throw new PurchasingServiceException(400, "validation_failed"); }
        var returns = returnStore ?? throw ReturnUnavailable();
        var runner = returnTransactions ?? throw ReturnUnavailable();
        const string command = PurchaseSupplierReturnCommandsV1.CreateV1;
        var requestHash = PurchasingRequestHash.Compute(command, orderId, new { request });
        var envelope = Envelope(scope, metadata, permission!);
        var replay = await FindReturnReplayAsync(scope, permission, returns, envelope, requestHash, command, cancellationToken);
        if (replay is not null) return await ReturnReplayedAsync(scope, permission, returns, orderId, replay, cancellationToken);

        if (request.ReplacementExpected)
        {
            // ALS-D91: a replacement-expected return reopens the order quantity; that variant is not offered yet (ALS-D92 deferred).
            throw new PurchasingServiceException(422, PurchaseSupplierReturnCodesV1.ReplacementUnsupported,
                [new("replacementExpected", PurchaseSupplierReturnCodesV1.ReplacementUnsupported)]);
        }

        var stored = await GuardedAsync(scope, permission, () => store.FindOrderAsync(orderId, cancellationToken), cancellationToken)
            ?? throw new PurchasingServiceException(404, "not_found");
        if (stored.Order.Status != PurchaseOrderStatus.Approved)
        {
            throw new PurchasingServiceException(409, PurchaseAcceptanceParticipantCodes.OrderStateConflict);
        }

        var lineId = await GuardedAsync(scope, permission,
                () => returns.FindAcceptanceLineAsync(orderId, request.GoodsAcceptanceId, cancellationToken), cancellationToken)
            ?? throw new PurchasingServiceException(404, "not_found");
        if (await GuardedAsync(scope, permission, () => returns.ReturnIdExistsAsync(request.ReturnId, cancellationToken), cancellationToken))
        {
            return await ReturnTerminalAsync(scope, permission, returns, orderId, envelope, requestHash, command,
                new(409, PurchaseSupplierReturnCodesV1.ReturnIdTaken, [new("returnId", PurchaseSupplierReturnCodesV1.ReturnIdTaken)]),
                cancellationToken);
        }

        var invoiceCost = await ReadReturnInvoiceCostAsync(scope, permission, request.GoodsAcceptanceId, cancellationToken);
        PurchaseSupplierReturnStatus status;
        PurchaseSupplierReturnCostBasis basis;
        decimal unitCost;
        if (invoiceCost is { } invoiced)
        {
            if (request.ManualUnitCostTryPerKg is not null)
            {
                return await ReturnTerminalAsync(scope, permission, returns, orderId, envelope, requestHash, command,
                    new(409, PurchaseSupplierReturnCodesV1.InvoiceCostAvailable,
                        [new("manualUnitCostTryPerKg", PurchaseSupplierReturnCodesV1.InvoiceCostAvailable)]), cancellationToken);
            }

            (status, basis, unitCost) = (PurchaseSupplierReturnStatus.Recorded, PurchaseSupplierReturnCostBasis.InvoiceUnitCost, invoiced);
        }
        else
        {
            if (request.ManualUnitCostTryPerKg is null)
            {
                return await ReturnTerminalAsync(scope, permission, returns, orderId, envelope, requestHash, command,
                    new(409, PurchaseSupplierReturnCodesV1.CostRequired,
                        [new("manualUnitCostTryPerKg", PurchaseSupplierReturnCodesV1.CostRequired)]), cancellationToken);
            }

            (status, basis, unitCost) = (PurchaseSupplierReturnStatus.PendingCostException,
                PurchaseSupplierReturnCostBasis.ManualCostWithException, ParseUnitCost(request.ManualUnitCostTryPerKg));
        }

        var draft = new PurchaseSupplierReturnDraft(request.ReturnId, request.ReturnLineId, orderId, lineId, request.GoodsAcceptanceId,
            request.InventoryMovementId, status, request.SourceBlock, ParseKg(request.QuantityKg), basis, unitCost, ReplacementExpected: false,
            request.Reason.Trim(), request.OccurredAtUtc);
        try
        {
            StoredPurchaseSupplierReturn saved;
            if (status == PurchaseSupplierReturnStatus.PendingCostException)
            {
                // SAT-D393: no stock moves before the exception approval.
                await RequireStillAsync(scope, permission, cancellationToken);
                saved = await returns.SaveNewAsync(draft, envelope, requestHash, command, cancellationToken);
            }
            else
            {
                saved = await runner.ExecuteAsync(scope.CompanyId, async (exit, token) =>
                {
                    await StageReturnExitAsync(scope, exit, draft, envelope, token);
                    return await returns.SaveNewAsync(draft, envelope, requestHash, command, token);
                }, token => RequireStillAsync(scope, permission, token), cancellationToken);
            }

            return new(ReturnView(saved), IsReplay: false, envelope.CorrelationId);
        }
        catch (PurchasingStoreException exception) when (exception.Code == PurchasingStoreCodes.ConcurrentChange)
        {
            var won = await FindReturnReplayAsync(scope, permission, returns, envelope, requestHash, command, cancellationToken);
            return won is null ? throw Store(exception) : await ReturnReplayedAsync(scope, permission, returns, orderId, won, cancellationToken);
        }
        catch (PurchasingStoreException exception)
        {
            throw Store(exception);
        }
    }

    public Task<PurchaseSupplierReturnResponseV1> ApproveSupplierReturnCostExceptionAsync(Guid orderId, Guid returnId,
        DecidePurchaseSupplierReturnV1 request, PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default) =>
        DecideSupplierReturnAsync(orderId, returnId, request, metadata, PurchaseSupplierReturnCommandsV1.ApproveCostExceptionV1, approve: true,
            cancellationToken);

    public Task<PurchaseSupplierReturnResponseV1> RejectSupplierReturnCostExceptionAsync(Guid orderId, Guid returnId,
        DecidePurchaseSupplierReturnV1 request, PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default) =>
        DecideSupplierReturnAsync(orderId, returnId, request, metadata, PurchaseSupplierReturnCommandsV1.RejectCostExceptionV1, approve: false,
            cancellationToken);

    public async Task<PurchaseSupplierReturnDetailV1> GetSupplierReturnAsync(Guid orderId, Guid returnId, CancellationToken cancellationToken = default)
    {
        var permission = bindings.ReadPermission;
        var scope = await RequireAsync(permission, cancellationToken);
        if (orderId == Guid.Empty || returnId == Guid.Empty) throw new PurchasingServiceException(404, "not_found");
        var returns = returnStore ?? throw ReturnUnavailable();
        var stored = await GuardedAsync(scope, permission, () => returns.FindAsync(orderId, returnId, cancellationToken), cancellationToken)
            ?? throw new PurchasingServiceException(404, "not_found");
        return new(ReturnView(RequireOwnReturn(scope, orderId, stored)));
    }

    public async Task<PurchaseSupplierReturnPageV1> GetSupplierReturnsAsync(Guid orderId, int limit = 50, CancellationToken cancellationToken = default)
    {
        var permission = bindings.ReadPermission;
        var scope = await RequireAsync(permission, cancellationToken);
        if (limit is < 1 or > MaximumPageSize) throw new PurchasingServiceException(400, "validation_failed");
        if (orderId == Guid.Empty) throw new PurchasingServiceException(404, "not_found");
        var returns = returnStore ?? throw ReturnUnavailable();
        _ = await GuardedAsync(scope, permission, () => store.FindOrderAsync(orderId, cancellationToken), cancellationToken)
            ?? throw new PurchasingServiceException(404, "not_found");
        var rows = await GuardedAsync(scope, permission, () => returns.ListForOrderAsync(orderId, limit + 1, cancellationToken), cancellationToken);
        return new(rows.Take(limit).Select(row => ReturnView(RequireOwnReturn(scope, orderId, row))).ToArray(), rows.Count > limit);
    }

    // ---- cost exception decision ----------------------------------------------------------------

    private async Task<PurchaseSupplierReturnResponseV1> DecideSupplierReturnAsync(Guid orderId, Guid returnId,
        DecidePurchaseSupplierReturnV1 request, PurchasingCommandMetadata metadata, string command, bool approve,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();
        // SAT-D396: the cost exception is approved (or refused) under the order approval permission; the decision is audited.
        var permission = bindings.ApprovePermission;
        var scope = await RequireAsync(permission, cancellationToken);
        if (orderId == Guid.Empty || returnId == Guid.Empty) throw new PurchasingServiceException(404, "not_found");
        try { PurchaseSupplierReturnJsonV1.ValidateDecision(request); }
        catch (JsonException) { throw new PurchasingServiceException(400, "validation_failed"); }
        var expectedVersion = ParseVersion(request.ExpectedVersion);
        var returns = returnStore ?? throw ReturnUnavailable();
        var requestHash = PurchasingRequestHash.Compute(command, returnId, new { orderId, request });
        var envelope = Envelope(scope, metadata, permission!);
        var replay = await FindReturnReplayAsync(scope, permission, returns, envelope, requestHash, command, cancellationToken);
        if (replay is not null) return await ReturnReplayedAsync(scope, permission, returns, orderId, replay, cancellationToken);

        var stored = await GuardedAsync(scope, permission, () => returns.FindAsync(orderId, returnId, cancellationToken), cancellationToken)
            ?? throw new PurchasingServiceException(404, "not_found");
        stored = RequireOwnReturn(scope, orderId, stored);
        var supplierReturn = new PurchaseSupplierReturn(returnId, stored.Return.Status, stored.Version, stored.RequestedBy);
        var allowSameActor = (returnPolicy ?? PurchaseSupplierReturnPolicyV1.Default).AllowSameActorCostExceptionDecision;
        var actor = scope.ActorId.Trim();
        try
        {
            if (approve) supplierReturn.Approve(expectedVersion, actor, allowSameActor);
            else supplierReturn.Reject(expectedVersion, actor, request.Reason, allowSameActor);
        }
        catch (PurchasingVersionConflictException)
        {
            return await ReturnTerminalAsync(scope, permission, returns, orderId, envelope, requestHash, command,
                new(409, PurchasingProblemCodesV1.VersionConflict, [new("expectedVersion", PurchasingProblemCodesV1.VersionConflict)]), cancellationToken);
        }
        catch (PurchasingRuleException exception) when (exception.Code == PurchaseSupplierReturnRuleCodes.NotPending)
        {
            return await ReturnTerminalAsync(scope, permission, returns, orderId, envelope, requestHash, command,
                new(409, exception.Code, [new("status", exception.Code)]), cancellationToken);
        }
        catch (PurchasingRuleException exception) when (exception.Code is PurchaseSupplierReturnRuleCodes.SameActor
                                                             or PurchaseSupplierReturnRuleCodes.MakerUnknown)
        {
            return await ReturnTerminalAsync(scope, permission, returns, orderId, envelope, requestHash, command,
                new(409, exception.Code, [new("actor", exception.Code)]), cancellationToken);
        }
        catch (PurchasingRuleException exception)
        {
            throw new PurchasingServiceException(422, exception.Code, [new("reason", exception.Code)]);
        }

        try
        {
            StoredPurchaseSupplierReturn saved;
            if (!approve)
            {
                await RequireStillAsync(scope, permission, cancellationToken);
                saved = await returns.SaveDecisionAsync(orderId, supplierReturn, expectedVersion, envelope, requestHash, command, cancellationToken);
            }
            else
            {
                var runner = returnTransactions ?? throw ReturnUnavailable();
                saved = await runner.ExecuteAsync(scope.CompanyId, async (exit, token) =>
                {
                    await StageReturnExitAsync(scope, exit, stored.Return, envelope, token);
                    return await returns.SaveDecisionAsync(orderId, supplierReturn, expectedVersion, envelope, requestHash, command, token);
                }, token => RequireStillAsync(scope, permission, token), cancellationToken);
            }

            return new(ReturnView(saved), IsReplay: false, envelope.CorrelationId);
        }
        catch (PurchasingStoreException exception) when (exception.Code == PurchasingStoreCodes.ConcurrentChange)
        {
            var won = await FindReturnReplayAsync(scope, permission, returns, envelope, requestHash, command, cancellationToken);
            return won is null ? throw Store(exception) : await ReturnReplayedAsync(scope, permission, returns, orderId, won, cancellationToken);
        }
        catch (PurchasingStoreException exception)
        {
            throw Store(exception);
        }
    }

    /// <summary>
    /// SAT-D306: Inventory stages the exit of the named acceptance's label at the recorded unit cost. Inventory owns the quantity cap
    /// (never more than the receipt), the allocation guard (KLN-D93) and the cost-difference guard (SAT-D371); its refusals reach
    /// the caller with its own code through the host. An answer for another movement fails closed.
    /// </summary>
    private static async Task StageReturnExitAsync(Scope scope, IPurchaseSupplierReturnExitV1 exit, PurchaseSupplierReturnDraft draft,
        PurchasingWriteEnvelope envelope, CancellationToken cancellationToken)
    {
        var result = await exit.StageExitAsync(new PurchaseSupplierReturnExitRequestV1(scope.CompanyId, draft.ReturnId, draft.ReturnLineId,
            draft.GoodsAcceptanceId, draft.InventoryMovementId, draft.SourceBlock, Kg(draft.QuantityKg), UnitCostText(draft.UnitCostTryPerKg),
            draft.OccurredAtUtc), envelope, cancellationToken);
        if (result is null || result.InventoryMovementId != draft.InventoryMovementId) throw ReturnUnavailable();
    }

    /// <summary>
    /// SAT-D392: the invoice unit cost, or null when it cannot be read (no reader is registered until supplier invoices are recorded).
    /// An unusable answer (non-positive, more than four decimals or out of range) is fail-closed, never rounded.
    /// </summary>
    private async Task<decimal?> ReadReturnInvoiceCostAsync(Scope scope, string? permission, Guid goodsAcceptanceId, CancellationToken cancellationToken)
    {
        if (returnInvoiceCosts is null) return null;
        var answer = await GuardedAsync(scope, permission, () => returnInvoiceCosts.ReadAsync(scope.CompanyId, goodsAcceptanceId, cancellationToken),
            cancellationToken);
        if (answer is null) throw new PurchasingServiceException(503, PurchaseSupplierReturnCodesV1.InvoiceCostInvalid);
        if (answer.UnitCostTryPerKg is not { } cost) return null;
        if (cost <= 0m || cost >= UnitCostUpperBound || decimal.Round(cost, UnitCostScale) != cost)
        {
            throw new PurchasingServiceException(503, PurchaseSupplierReturnCodesV1.InvoiceCostInvalid);
        }

        return cost;
    }

    // ---- replay / views ----------------------------------------------------------------------------

    private async Task<PurchaseSupplierReturnReplay?> FindReturnReplayAsync(Scope scope, string? permission, IPurchaseSupplierReturnStore returns,
        PurchasingWriteEnvelope envelope, string requestHash, string commandName, CancellationToken cancellationToken)
    {
        try
        {
            return await GuardedAsync(scope, permission, () => returns.FindReplayAsync(envelope, requestHash, commandName, cancellationToken), cancellationToken);
        }
        catch (PurchasingStoreException exception)
        {
            throw Store(exception);
        }
    }

    private async Task<PurchaseSupplierReturnResponseV1> ReturnReplayedAsync(Scope scope, string? permission, IPurchaseSupplierReturnStore returns,
        Guid orderId, PurchaseSupplierReturnReplay replay, CancellationToken cancellationToken)
    {
        if (replay.Refusal is { } refusal)
        {
            throw new PurchasingServiceException(refusal.Status, refusal.Code, refusal.Errors, isReplay: true, originalCorrelationId: replay.OriginalCorrelationId);
        }

        if (replay.ReturnId is not { } returnId) throw new PurchasingServiceException(503, "service_unavailable");
        var stored = await GuardedAsync(scope, permission, () => returns.FindAsync(orderId, returnId, cancellationToken), cancellationToken)
            ?? throw new PurchasingServiceException(503, "service_unavailable");
        return new(ReturnView(RequireOwnReturn(scope, orderId, stored)), IsReplay: true, replay.OriginalCorrelationId);
    }

    /// <summary>§7/§8: a terminal refusal is recorded under the key before it is answered; a same-key winner is answered as a replay.</summary>
    private async Task<PurchaseSupplierReturnResponseV1> ReturnTerminalAsync(Scope scope, string? permission, IPurchaseSupplierReturnStore returns,
        Guid orderId, PurchasingWriteEnvelope envelope, string requestHash, string commandName, PurchasingTerminalRefusal refusal,
        CancellationToken cancellationToken)
    {
        await RequireStillAsync(scope, permission, cancellationToken);
        try
        {
            await returns.RecordRefusalAsync(envelope, requestHash, commandName, refusal, cancellationToken);
        }
        catch (PurchasingStoreException exception) when (exception.Code == PurchasingStoreCodes.ConcurrentChange)
        {
            var won = await FindReturnReplayAsync(scope, permission, returns, envelope, requestHash, commandName, cancellationToken);
            if (won is not null) return await ReturnReplayedAsync(scope, permission, returns, orderId, won, cancellationToken);
        }
        catch (PurchasingStoreException exception)
        {
            throw Store(exception);
        }

        throw new PurchasingServiceException(refusal.Status, refusal.Code, refusal.Errors);
    }

    private static StoredPurchaseSupplierReturn RequireOwnReturn(Scope scope, Guid orderId, StoredPurchaseSupplierReturn stored) =>
        stored.CompanyId != scope.CompanyId || stored.Return.PurchaseOrderId != orderId ? throw ReturnUnavailable() : stored;

    private static PurchaseSupplierReturnViewV1 ReturnView(StoredPurchaseSupplierReturn stored)
    {
        var item = stored.Return;
        return new(stored.CompanyId, item.ReturnId, item.ReturnLineId, item.PurchaseOrderId, item.PurchaseOrderLineId, item.GoodsAcceptanceId,
            item.InventoryMovementId, item.Status.ToString(), stored.Version.ToString(CultureInfo.InvariantCulture), item.SourceBlock,
            Kg(item.QuantityKg), item.CostBasis.ToString(), UnitCostText(item.UnitCostTryPerKg), item.ReplacementExpected, item.Reason,
            item.OccurredAtUtc, stored.RequestedBy, stored.RequestedAtUtc, stored.DecidedBy, stored.DecidedAtUtc, stored.DecisionReason);
    }

    private static string UnitCostText(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    private static decimal ParseUnitCost(string text) => decimal.Parse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);

    private static PurchasingServiceException ReturnUnavailable() => new(503, PurchaseSupplierReturnCodesV1.Unavailable);
}
