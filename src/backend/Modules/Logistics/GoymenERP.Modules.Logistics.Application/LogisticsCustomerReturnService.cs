using System.Globalization;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Quantities;
using GoymenERP.Modules.Logistics.Domain.Returns;

namespace GoymenERP.Modules.Logistics.Application;

/// <summary>
/// Dependency-ready customer-return orchestration. It is intentionally not registered until the
/// Inventory participant, persistent store and central permissions/endpoints are supplied by their owners.
/// </summary>
public sealed class LogisticsCustomerReturnService(
    ILogisticsCustomerReturnScopeV1 scopes,
    ILogisticsCustomerReturnSourceReadV1 sources,
    ILogisticsCustomerReturnStoreV1 store,
    ILogisticsCustomerReturnTransactionRunnerV1 transactions,
    LogisticsCustomerReturnBindingsV1 bindings,
    ICorrelationContext correlation,
    TimeProvider timeProvider) : ILogisticsCustomerReturnServiceV1
{
    public async Task<CustomerReturnResponseV1> RequestAsync(Guid instructionId, Guid lineId,
        RequestCustomerReturnV1 request, LogisticsCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        RequireId(instructionId);
        RequireId(lineId);
        RequireId(request.CustomerReturnId);
        RequireId(request.SourceDeliveryId);
        var expectedInstructionVersion = ParseVersion(request.ExpectedInstructionVersion);
        var scope = await scopes.RequireAsync(bindings.RequestPermission, cancellationToken);
        var envelope = Envelope(scope, bindings.RequestPermission, metadata);
        var hash = LogisticsRequestHash.Compute(LogisticsCustomerReturnCommandsV1.Request, instructionId, new { lineId, request });

        try
        {
            return await FaultFencedAsync(
                () => transactions.ExecuteLogisticsAsync(scope.CompanyId, async token =>
                {
                    var replay = await GuardedAsync(scope, bindings.RequestPermission,
                        () => store.FindReplayAsync(envelope, hash, LogisticsCustomerReturnCommandsV1.Request, token), token);
                    if (replay is not null) return Replayed(replay);

                    var source = await GuardedAsync(scope, bindings.RequestPermission,
                        () => sources.FindDeliveryAsync(instructionId, lineId, request.SourceDeliveryId, token), token);
                    RequireSource(scope.CompanyId, instructionId, lineId, expectedInstructionVersion, source);
                    var quantity = ParseQuantity(request.Quantity, source!.DeliveredQuantity.Unit);
                    var customerReturn = CustomerReturn.Request(request.CustomerReturnId, scope.CompanyId, instructionId, lineId,
                        source.SourceDeliveryId, source.SourceShipmentId, source.SourceOrderId, source.SourceOrderLineId,
                        source.UnitId, quantity, source.DeliveredQuantity, request.Reason, scope.ActorId, request.OccurredAtUtc,
                        timeProvider.GetUtcNow());
                    var stored = await store.StoreRequestAsync(envelope, hash, customerReturn, token);
                    return Response(stored.Return, stored.Outcome == LogisticsWriteOutcome.Replayed, stored.OriginalCorrelationId);
                }, token => scopes.RequireStillAsync(scope, bindings.RequestPermission, token), cancellationToken),
                () => scopes.RequireStillAsync(scope, bindings.RequestPermission, cancellationToken));
        }
        catch (CustomerReturnRuleException exception) { throw Rule(exception); }
        catch (ArgumentException exception) when (string.Equals(exception.ParamName, "reason", StringComparison.Ordinal))
        {
            throw new LogisticsCustomerReturnServiceException(422, "validation_failed",
                [new("reason", "validation_failed")]);
        }
        catch (LogisticsCustomerReturnServiceException exception) when (IsRecordableTerminal(exception))
        {
            return await TerminalAsync(scope, bindings.RequestPermission, envelope, hash,
                LogisticsCustomerReturnCommandsV1.Request, exception, cancellationToken);
        }
        catch (LogisticsStoreException exception) when (exception.Code == LogisticsStoreCodes.ConcurrentChange)
        {
            return await RecoverWinnerAsync(scope, bindings.RequestPermission, envelope, hash,
                LogisticsCustomerReturnCommandsV1.Request, exception, cancellationToken);
        }
    }

    public Task<CustomerReturnResponseV1> ApproveAsync(Guid customerReturnId, ApproveCustomerReturnV1 request,
        LogisticsCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return DecideAsync(customerReturnId, request.ExpectedVersion, request.InventoryMovementId, request.Note,
            true, metadata, cancellationToken);
    }

    public Task<CustomerReturnResponseV1> RejectAsync(Guid customerReturnId, RejectCustomerReturnV1 request,
        LogisticsCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return DecideAsync(customerReturnId, request.ExpectedVersion, null, request.Note,
            false, metadata, cancellationToken);
    }

    public async Task<CustomerReturnViewV1?> GetAsync(Guid customerReturnId, CancellationToken cancellationToken = default)
    {
        RequireId(customerReturnId);
        var scope = await scopes.RequireAsync(bindings.ReadPermission, cancellationToken);
        var found = await FaultFencedAsync(
            () => GuardedAsync(scope, bindings.ReadPermission,
                () => store.FindAsync(customerReturnId, cancellationToken), cancellationToken),
            () => scopes.RequireStillAsync(scope, bindings.ReadPermission, cancellationToken));
        return found is null || found.CompanyId != scope.CompanyId ? null : View(found);
    }

    public async Task<IReadOnlyList<CustomerReturnViewV1>> ListOrderReturnsAsync(Guid salesOrderId,
        CancellationToken cancellationToken = default)
    {
        RequireId(salesOrderId);
        var scope = await scopes.RequireAsync(bindings.ReadPermission, cancellationToken);
        var found = await FaultFencedAsync(
            () => GuardedAsync(scope, bindings.ReadPermission,
                () => store.ListOrderReturnsAsync(salesOrderId, cancellationToken), cancellationToken),
            () => scopes.RequireStillAsync(scope, bindings.ReadPermission, cancellationToken));
        if (found.Any(item => item.CompanyId != scope.CompanyId || item.SourceOrderId != salesOrderId))
            throw new LogisticsCustomerReturnServiceException(503, LogisticsCustomerReturnCodesV1.SourceInvalid);
        return found.Select(View).ToArray();
    }

    private async Task<CustomerReturnResponseV1> DecideAsync(Guid customerReturnId, string expectedVersionText,
        Guid? inventoryMovementId, string? note, bool approving, LogisticsCommandMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        RequireId(customerReturnId);
        if (approving) RequireId(inventoryMovementId ?? Guid.Empty);
        var expectedVersion = ParseVersion(expectedVersionText);
        var command = approving ? LogisticsCustomerReturnCommandsV1.Approve : LogisticsCustomerReturnCommandsV1.Reject;
        var scope = await scopes.RequireAsync(bindings.DecisionPermission, cancellationToken);
        var envelope = Envelope(scope, bindings.DecisionPermission, metadata);
        var hash = LogisticsRequestHash.Compute(command, customerReturnId, new { expectedVersionText, inventoryMovementId, note });

        try
        {
            if (!approving)
            {
                return await FaultFencedAsync(
                    () => transactions.ExecuteLogisticsAsync(scope.CompanyId, async token =>
                    {
                        var replay = await GuardedAsync(scope, bindings.DecisionPermission,
                            () => store.FindReplayAsync(envelope, hash, command, token), token);
                        if (replay is not null) return Replayed(replay);
                        var pending = await RequirePendingAsync(scope, bindings.DecisionPermission, customerReturnId, token);
                        pending.Reject(expectedVersion, scope.ActorId, timeProvider.GetUtcNow(), note);
                        var saved = await store.StoreDecisionAsync(envelope, hash, command, pending, token);
                        return Response(saved.Return, saved.Outcome == LogisticsWriteOutcome.Replayed, saved.OriginalCorrelationId);
                    }, token => scopes.RequireStillAsync(scope, bindings.DecisionPermission, token), cancellationToken),
                    () => RequireCommandStillAsync(scope, bindings.DecisionPermission, command, cancellationToken));
            }

            return await FaultFencedAsync(
                () => transactions.ExecuteApprovalAsync(scope.CompanyId, async (stock, token) =>
                {
                    // Inventory authority is checked before any replay oracle is released.
                    await stock.RequireStillAsync(token);
                    var replay = await GuardedAsync(scope, bindings.DecisionPermission,
                        () => store.FindReplayAsync(envelope, hash, command, token), token);
                    if (replay is not null) return Replayed(replay);
                    var pending = await RequirePendingAsync(scope, bindings.DecisionPermission, customerReturnId, token);
                    pending.RequireDecision(expectedVersion, scope.ActorId);

                    var source = await GuardedAsync(scope, bindings.DecisionPermission,
                        () => sources.FindDeliveryAsync(pending.LoadingInstructionId, pending.LoadingInstructionLineId,
                            pending.SourceDeliveryId, token), token);
                    RequireSource(scope.CompanyId, pending.LoadingInstructionId, pending.LoadingInstructionLineId, null, source);
                    if (source!.SourceShipmentId != pending.SourceShipmentId || source.SourceOrderId != pending.SourceOrderId
                        || source.SourceOrderLineId != pending.SourceOrderLineId || source.UnitId != pending.UnitId)
                        throw new LogisticsCustomerReturnServiceException(409, LogisticsCustomerReturnCodesV1.SourceInvalid);

                    var approved = await GuardedAsync(scope, bindings.DecisionPermission,
                        () => store.SumApprovedForDeliveryAsync(pending.SourceDeliveryId, pending.Quantity.Unit, token), token);
                    if (approved.Add(pending.Quantity).IsGreaterThan(source.DeliveredQuantity))
                        throw new LogisticsCustomerReturnServiceException(409, LogisticsCustomerReturnCodesV1.QuantityExceeded,
                            [new("quantity", LogisticsCustomerReturnCodesV1.QuantityExceeded)]);

                    var stockRequest = new LogisticsCustomerReturnStockRequestV1(scope.CompanyId, pending.Id,
                        pending.LoadingInstructionId, pending.LoadingInstructionLineId, pending.SourceDeliveryId,
                        pending.SourceShipmentId, pending.SourceOrderId, pending.SourceOrderLineId, pending.UnitId,
                        pending.Quantity.Value, inventoryMovementId!.Value, pending.OccurredAtUtc);
                    var staged = await stock.StageAsync(stockRequest, envelope, token);
                    if (staged.CompanyId != scope.CompanyId || staged.CustomerReturnId != pending.Id
                        || staged.SourceShipmentId != pending.SourceShipmentId || staged.InventoryMovementId != inventoryMovementId
                        || staged.OriginalShipmentMovementId == Guid.Empty || staged.OriginalLabelId == Guid.Empty
                        || staged.Quantity != pending.Quantity.Value)
                        throw new LogisticsCustomerReturnServiceException(503, LogisticsCustomerReturnCodesV1.InventoryInvalid);

                    pending.Approve(expectedVersion, scope.ActorId, staged.InventoryMovementId, staged.OriginalLabelId,
                        staged.OriginalShipmentMovementId, timeProvider.GetUtcNow(), note);
                    var saved = await store.StoreDecisionAsync(envelope, hash, command, pending, token);
                    if (staged.IsReplay != (saved.Outcome == LogisticsWriteOutcome.Replayed)
                        || !string.Equals(staged.OriginalCorrelationId, saved.OriginalCorrelationId, StringComparison.Ordinal))
                        throw new LogisticsCustomerReturnServiceException(503, LogisticsCustomerReturnCodesV1.InventoryInvalid);
                    return Response(saved.Return, staged.IsReplay, saved.OriginalCorrelationId);
                }, async (stock, token) =>
                {
                    await scopes.RequireStillAsync(scope, bindings.DecisionPermission, token);
                    await stock.RequireStillAsync(token);
                }, cancellationToken),
                () => RequireCommandStillAsync(scope, bindings.DecisionPermission, command, cancellationToken));
        }
        catch (CustomerReturnVersionConflictException exception)
        {
            return await TerminalAsync(scope, bindings.DecisionPermission, envelope, hash, command,
                VersionConflict(exception), cancellationToken);
        }
        catch (CustomerReturnRuleException exception) { throw Rule(exception); }
        catch (LogisticsCustomerReturnServiceException exception) when (IsRecordableTerminal(exception))
        {
            return await TerminalAsync(scope, bindings.DecisionPermission, envelope, hash, command,
                exception, cancellationToken);
        }
        catch (LogisticsStoreException exception) when (exception.Code == LogisticsStoreCodes.ConcurrentChange)
        {
            return await RecoverWinnerAsync(scope, bindings.DecisionPermission, envelope, hash, command,
                exception, cancellationToken);
        }
    }

    private async Task<CustomerReturn> RequirePendingAsync(LogisticsCustomerReturnScopeV1 scope, string permission,
        Guid customerReturnId, CancellationToken cancellationToken)
    {
        var found = await GuardedAsync(scope, permission, () => store.FindAsync(customerReturnId, cancellationToken), cancellationToken);
        if (found is null || found.CompanyId != scope.CompanyId)
            throw new LogisticsCustomerReturnServiceException(404, "not_found");
        return found;
    }

    private async Task<T> GuardedAsync<T>(LogisticsCustomerReturnScopeV1 scope, string permission,
        Func<Task<T>> read, CancellationToken cancellationToken)
    {
        var result = await read();
        await scopes.RequireStillAsync(scope, permission, cancellationToken);
        return result;
    }

    private static async Task<T> FaultFencedAsync<T>(Func<Task<T>> operation, Func<Task> requireStill)
    {
        try
        {
            return await operation();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            await requireStill();
            throw;
        }
    }

    private static void RequireSource(Guid companyId, Guid instructionId, Guid lineId, long? expectedVersion,
        LogisticsCustomerReturnSourceV1? source)
    {
        if (source is null) throw new LogisticsCustomerReturnServiceException(404, "not_found");
        if (source.CompanyId != companyId || source.LoadingInstructionId != instructionId
            || source.LoadingInstructionLineId != lineId || source.SourceDeliveryId == Guid.Empty
            || source.SourceShipmentId == Guid.Empty || source.SourceOrderId == Guid.Empty
            || source.SourceOrderLineId == Guid.Empty || source.UnitId == Guid.Empty)
            throw new LogisticsCustomerReturnServiceException(503, LogisticsCustomerReturnCodesV1.SourceInvalid);
        if (expectedVersion is not null && source.LoadingInstructionVersion != expectedVersion)
            throw new LogisticsCustomerReturnServiceException(409, LogisticsCustomerReturnCodesV1.VersionConflict,
                [new("expectedVersion", "version_conflict")], expectedVersion: expectedVersion,
                currentVersion: source.LoadingInstructionVersion);
    }

    private async Task<CustomerReturnResponseV1> TerminalAsync(LogisticsCustomerReturnScopeV1 scope, string permission,
        LogisticsWriteEnvelope envelope, string requestHash, string command,
        LogisticsCustomerReturnServiceException exception, CancellationToken cancellationToken)
    {
        var refusal = new CustomerReturnTerminalRefusalV1(exception.Status, exception.Code, exception.Errors,
            exception.ExpectedVersion, exception.CurrentVersion);
        try
        {
            var recorded = await FaultFencedAsync(
                () => transactions.ExecuteLogisticsAsync(scope.CompanyId, async token =>
                {
                    // A terminal result is disclosed or recorded only under a fresh measurement in this new transaction.
                    await RequireCommandStillAsync(scope, permission, command, token);
                    var winner = await GuardedAsync(scope, permission,
                        () => store.FindReplayAsync(envelope, requestHash, command, token), token);
                    if (winner is not null)
                        return new CustomerReturnReplayStoreResultV1(winner, LogisticsWriteOutcome.Replayed);
                    return await store.RecordRefusalAsync(envelope, requestHash, command, refusal, token);
                }, token => RequireCommandStillAsync(scope, permission, command, token), cancellationToken),
                () => RequireCommandStillAsync(scope, permission, command, cancellationToken));
            return Resolve(recorded.Replay, recorded.Outcome == LogisticsWriteOutcome.Replayed);
        }
        catch (LogisticsStoreException raced) when (raced.Code == LogisticsStoreCodes.ConcurrentChange)
        {
            return await RecoverWinnerAsync(scope, permission, envelope, requestHash, command, raced, cancellationToken);
        }
    }

    private async Task<CustomerReturnResponseV1> RecoverWinnerAsync(LogisticsCustomerReturnScopeV1 scope,
        string permission, LogisticsWriteEnvelope envelope, string requestHash, string command,
        LogisticsStoreException original, CancellationToken cancellationToken)
    {
        var winner = await FaultFencedAsync(
            () => transactions.ExecuteLogisticsAsync(scope.CompanyId, async token =>
            {
                await RequireCommandStillAsync(scope, permission, command, token);
                return await GuardedAsync(scope, permission,
                    () => store.FindReplayAsync(envelope, requestHash, command, token), token);
            }, token => RequireCommandStillAsync(scope, permission, command, token), cancellationToken),
            () => RequireCommandStillAsync(scope, permission, command, cancellationToken));
        if (winner is null) throw original;
        return Replayed(winner);
    }

    private static CustomerReturnResponseV1 Resolve(CustomerReturnReplayV1 replay, bool isReplay)
    {
        if (replay.Refusal is { } refusal)
            throw new LogisticsCustomerReturnServiceException(refusal.Status, refusal.Code, refusal.Errors, isReplay,
                replay.OriginalCorrelationId, refusal.ExpectedVersion, refusal.CurrentVersion);
        return Response(replay.Return ?? throw new InvalidOperationException("Iade tekrar sonucu bos."), true,
            replay.OriginalCorrelationId);
    }

    private static CustomerReturnResponseV1 Replayed(CustomerReturnReplayV1 replay) => Resolve(replay, isReplay: true);

    private async Task RequireCommandStillAsync(LogisticsCustomerReturnScopeV1 scope, string permission, string command,
        CancellationToken cancellationToken)
    {
        await scopes.RequireStillAsync(scope, permission, cancellationToken);
        if (string.Equals(command, LogisticsCustomerReturnCommandsV1.Approve, StringComparison.Ordinal))
            await scopes.RequireStillAsync(scope, bindings.ApprovalInventoryPermission, cancellationToken);
    }

    private static bool IsRecordableTerminal(LogisticsCustomerReturnServiceException exception) => !exception.IsReplay
        && ((exception.Status == 404 && string.Equals(exception.Code, "not_found", StringComparison.Ordinal))
            || (exception.Status == 409
                && string.Equals(exception.Code, LogisticsCustomerReturnCodesV1.VersionConflict, StringComparison.Ordinal)
                && exception.ExpectedVersion is > 0 && exception.CurrentVersion is > 0));

    private static LogisticsCustomerReturnServiceException VersionConflict(CustomerReturnVersionConflictException exception) =>
        new(409, LogisticsCustomerReturnCodesV1.VersionConflict, [new("expectedVersion", "version_conflict")],
            expectedVersion: exception.ExpectedVersion, currentVersion: exception.CurrentVersion);

    private static LogisticsCustomerReturnServiceException Rule(CustomerReturnRuleException exception) => exception.Code switch
    {
        CustomerReturnRuleCodesV1.QuantityExceedsDelivery => new(409, LogisticsCustomerReturnCodesV1.QuantityExceeded,
            [new("quantity", exception.Code)]),
        CustomerReturnRuleCodesV1.NotPending => new(409, LogisticsCustomerReturnCodesV1.NotPending,
            [new("status", exception.Code)]),
        CustomerReturnRuleCodesV1.OwnDecision => new(409, LogisticsCustomerReturnCodesV1.OwnDecision,
            [new("actor", exception.Code)]),
        _ => new(422, "validation_failed", [new("customerReturn", exception.Code)]),
    };

    private LogisticsWriteEnvelope Envelope(LogisticsCustomerReturnScopeV1 scope, string permission,
        LogisticsCommandMetadata metadata) => new(scope.ActorId, permission, scope.RoleCodes, metadata.CorrelationId,
        metadata.CausationId, metadata.IdempotencyKey, correlation.SourceChannel,
        correlation.ClientIpAddress, correlation.UserAgent);

    private static long ParseVersion(string text) => long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var version)
        && version > 0 ? version : throw new LogisticsCustomerReturnServiceException(422, "validation_failed");

    private static Quantity ParseQuantity(string text, QuantityUnit unit)
    {
        if (!decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var value))
            throw new LogisticsCustomerReturnServiceException(422, "validation_failed");

        try
        {
            return Quantity.Positive(value, unit);
        }
        catch (ArgumentException)
        {
            throw new LogisticsCustomerReturnServiceException(422, "validation_failed");
        }
    }

    private static void RequireId(Guid id)
    {
        if (id == Guid.Empty) throw new LogisticsCustomerReturnServiceException(422, "validation_failed");
    }

    private static CustomerReturnResponseV1 Response(CustomerReturn item, bool replay, string correlation) =>
        new(View(item), replay, correlation);

    private static CustomerReturnViewV1 View(CustomerReturn item) => new(item.Id, item.CompanyId,
        item.LoadingInstructionId, item.LoadingInstructionLineId, item.SourceDeliveryId, item.SourceShipmentId,
        item.SourceOrderId, item.SourceOrderLineId, item.UnitId, Text(item.Quantity), Text(item.SourceDeliveredQuantity),
        item.Status.ToString(), item.Version.ToString(CultureInfo.InvariantCulture), item.Reason, item.RequestedBy,
        item.OccurredAtUtc, item.RequestedAtUtc, item.DecidedBy, item.DecidedAtUtc, item.DecisionNote,
        item.InventoryMovementId, item.OriginalLabelId, item.OriginalShipmentMovementId);

    private static string Text(Quantity quantity) => quantity.Value.ToString(CultureInfo.InvariantCulture);
}
