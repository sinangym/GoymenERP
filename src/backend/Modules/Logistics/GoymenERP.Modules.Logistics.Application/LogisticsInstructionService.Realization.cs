using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Modules.Logistics.Application;

public sealed partial class LogisticsInstructionService
{
    public async Task<LoadingInstructionResponse> RecordShipmentAsync(Guid instructionId, Guid lineId,
        RecordShipmentRequest request, LogisticsCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        var permission = bindings.ShipmentPermission;
        var scope = await RequireAsync(permission, cancellationToken);
        ValidateRealization(() => LogisticsRealizationJson.Validate(request), instructionId, lineId);
        var envelope = Envelope(scope, metadata, permission!);
        var hash = LogisticsRequestHash.Compute(LogisticsCommands.RecordShipmentV1, instructionId, new { lineId, request });
        var replay = await FindRealizationReplayAsync(scope, permission, envelope, hash,
            LogisticsCommands.RecordShipmentV1, cancellationToken);
        if (replay is not null) return Replayed(replay);
        var runner = realization ?? throw new LogisticsInstructionServiceException(503, LogisticsRealizationCodesV1.Unavailable);

        return await RunRealizationAsync(scope, permission, envelope, hash, LogisticsCommands.RecordShipmentV1,
            () => runner.ExecuteShipmentAsync(scope.CompanyId, async (stock, token) =>
            {
                // A winner may have committed between the outer read and the transaction opening.
                var won = await FindRealizationReplayAsync(scope, permission, envelope, hash,
                    LogisticsCommands.RecordShipmentV1, token);
                if (won is not null) return Replayed(won);
                var stored = await ReadRealizationInstructionAsync(scope, permission, instructionId, token);
                var line = stored.Instruction.Lines.SingleOrDefault(candidate => candidate.Id == lineId)
                    ?? throw new LogisticsInstructionServiceException(404, "not_found");
                var quantity = await VerifyUnitAsync(scope, permission, line.UnitId, request.Quantity, stored.EffectiveOn, "shipment", token);
                await RequireShipmentSourceAsync(scope, permission, stored, line, token);
                var allocation = await GuardedAsync(scope, permission,
                    () => stock.FindAllocationAsync(line.Source.OrderId, line.Source.LineId, request.AllocationId, token), token);
                if (allocation is null) throw new LogisticsInstructionServiceException(404, "not_found");
                if (allocation.CompanyId != scope.CompanyId || allocation.AllocationId != request.AllocationId || allocation.LabelId == Guid.Empty)
                {
                    throw new LogisticsInstructionServiceException(503, LogisticsProblemCodesV1.ReferenceProviderInvalid);
                }

                if (allocation.SalesOrderId != line.Source.OrderId || allocation.SalesOrderLineId != line.Source.LineId
                    || allocation.ProductId != line.ProductId || line.AllocationId is { } bound && bound != allocation.AllocationId)
                {
                    throw new LogisticsInstructionServiceException(409, LogisticsRealizationCodesV1.AllocationSourceMismatch);
                }

                var expected = long.Parse(request.ExpectedVersion, CultureInfo.InvariantCulture);
                var result = await GuardedAsync(scope, permission,
                    () => store.MutateInstructionAsync(envelope, hash, LogisticsCommands.RecordShipmentV1, instructionId,
                        (instruction, _) => instruction.MarkShipped(expected, request.ShipmentId, lineId, quantity, request.OccurredAtUtc),
                        new LogisticsMutationAudit("logistics.loading-instruction.shipment-recorded.v1", "RecordShipment", "Definite shipment recorded"), token), token)
                    ?? throw new LogisticsInstructionServiceException(404, "not_found");

                var stockRequest = new LogisticsShipmentStockRequestV1(scope.CompanyId, request.ShipmentId, instructionId, lineId,
                    allocation.AllocationId, request.ExpectedAllocationVersion, allocation.LabelId, request.ExpectedLabelVersion,
                    request.MovementId, request.Quantity, request.OccurredAtUtc);
                var staged = await GuardedAsync(scope, permission, () => stock.StageAsync(stockRequest, envelope, token), token);
                if (staged is null || staged.CompanyId != scope.CompanyId || staged.ShipmentId != request.ShipmentId
                    || staged.AllocationId != allocation.AllocationId || staged.LabelId != allocation.LabelId
                    || staged.MovementId != request.MovementId || staged.OutboxMessageId == Guid.Empty
                    || staged.IsReplay != (result.Outcome == LogisticsWriteOutcome.Replayed)
                    || !string.Equals(staged.OriginalCorrelationId, result.OriginalCorrelationId, StringComparison.Ordinal))
                {
                    // An orphan/replay disagreement is never repaired by producing a second physical movement.
                    throw new LogisticsInstructionServiceException(503, LogisticsProblemCodesV1.ReferenceProviderInvalid);
                }

                return Response(result.Instruction, staged.IsReplay, result.OriginalCorrelationId);
            }, async token =>
            {
                await RequireStillAsync(scope, permission, token);
                await RequireStillAsync(scope, LogisticsRealizationDependenciesV1.InventoryPhysicalAllocationWritePermission, token);
            }, cancellationToken), cancellationToken);
    }

    public async Task<LoadingInstructionResponse> RecordDeliveryAsync(Guid instructionId, Guid lineId,
        RecordDeliveryRequest request, LogisticsCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        var permission = bindings.DeliveryPermission;
        var scope = await RequireAsync(permission, cancellationToken);
        ValidateRealization(() => LogisticsRealizationJson.Validate(request), instructionId, lineId);
        var envelope = Envelope(scope, metadata, permission!);
        var hash = LogisticsRequestHash.Compute(LogisticsCommands.RecordDeliveryV1, instructionId, new { lineId, request });
        var replay = await FindRealizationReplayAsync(scope, permission, envelope, hash,
            LogisticsCommands.RecordDeliveryV1, cancellationToken);
        if (replay is not null) return Replayed(replay);
        var runner = realization ?? throw new LogisticsInstructionServiceException(503, LogisticsRealizationCodesV1.Unavailable);

        return await RunRealizationAsync(scope, permission, envelope, hash, LogisticsCommands.RecordDeliveryV1,
            () => runner.ExecuteDeliveryAsync(scope.CompanyId, async token =>
            {
                var won = await FindRealizationReplayAsync(scope, permission, envelope, hash,
                    LogisticsCommands.RecordDeliveryV1, token);
                if (won is not null) return Replayed(won);
                var stored = await ReadRealizationInstructionAsync(scope, permission, instructionId, token);
                var line = stored.Instruction.Lines.SingleOrDefault(candidate => candidate.Id == lineId)
                    ?? throw new LogisticsInstructionServiceException(404, "not_found");
                var quantity = await VerifyUnitAsync(scope, permission, line.UnitId, request.Quantity, stored.EffectiveOn, "delivery", token);
                var expected = long.Parse(request.ExpectedVersion, CultureInfo.InvariantCulture);
                // Existing domain policy permits delivery of an already shipped share even if the line was later blocked.
                // It binds this exact source shipment and caps its remainder; no account/order/Inventory re-write.
                var result = await GuardedAsync(scope, permission,
                    () => store.MutateInstructionAsync(envelope, hash, LogisticsCommands.RecordDeliveryV1, instructionId,
                        (instruction, _) => instruction.MarkDelivered(expected, request.DeliveryId, lineId, request.SourceShipmentId,
                            quantity, request.OccurredAtUtc),
                        new LogisticsMutationAudit("logistics.loading-instruction.delivery-recorded.v1", "RecordDelivery", "Shipment delivery recorded"), token), token)
                    ?? throw new LogisticsInstructionServiceException(404, "not_found");
                return Response(result.Instruction, result.Outcome == LogisticsWriteOutcome.Replayed, result.OriginalCorrelationId);
            }, token => RequireStillAsync(scope, permission, token), cancellationToken), cancellationToken);
    }

    private static void ValidateRealization(Action validate, Guid instructionId, Guid lineId)
    {
        if (instructionId == Guid.Empty || lineId == Guid.Empty) throw new LogisticsInstructionServiceException(404, "not_found");
        try { validate(); }
        catch (JsonException) { throw new LogisticsInstructionServiceException(400, "validation_failed"); }
    }

    private async Task<StoredLoadingInstruction> ReadRealizationInstructionAsync(Scope scope, string? permission,
        Guid instructionId, CancellationToken cancellationToken) =>
        await GuardedAsync(scope, permission, () => store.FindInstructionAsync(instructionId, cancellationToken), cancellationToken)
        ?? throw new LogisticsInstructionServiceException(404, "not_found");

    private async Task RequireShipmentSourceAsync(Scope scope, string? permission, StoredLoadingInstruction stored,
        LoadingInstructionLine line, CancellationToken cancellationToken)
    {
        SalesOrderLineReferenceAnswerV1 answer;
        try
        {
            answer = await GuardedAsync(scope, permission,
                () => orderLines.GetApprovedLineAsync(line.Source.OrderId, line.Source.LineId, cancellationToken), cancellationToken);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or LogisticsInstructionServiceException))
        {
            throw new LogisticsInstructionServiceException(503, LogisticsProblemCodesV1.SalesProviderUnavailable);
        }

        if (answer is null || answer.Outcome == SalesOrderLineReferenceOutcomeV1.Approved && answer.Line is null)
        {
            throw new LogisticsInstructionServiceException(503, LogisticsProblemCodesV1.ReferenceProviderInvalid);
        }

        if (answer.Outcome is SalesOrderLineReferenceOutcomeV1.NotFound or SalesOrderLineReferenceOutcomeV1.NotApproved)
        {
            throw new LogisticsInstructionServiceException(409, LogisticsProblemCodesV1.OrderLineState);
        }

        var approved = answer.Line;
        if (answer.Outcome != SalesOrderLineReferenceOutcomeV1.Approved || approved is null
            || approved.CompanyId != scope.CompanyId || approved.OrderId != line.Source.OrderId || approved.LineId != line.Source.LineId)
        {
            throw new LogisticsInstructionServiceException(503, LogisticsProblemCodesV1.ReferenceProviderInvalid);
        }

        if (approved.PartyId != stored.Instruction.CustomerPartyId || approved.CompanyAccountId != stored.Instruction.CompanyAccountId
            || approved.ProductId != line.ProductId || approved.OrderUnitId != line.UnitId || approved.Revision != line.Source.Revision)
        {
            throw new LogisticsInstructionServiceException(409, LogisticsProblemCodesV1.OrderLineState);
        }
    }

    private async Task<LoadingInstructionResponse> RunRealizationAsync(Scope scope, string? permission,
        LogisticsWriteEnvelope envelope, string hash, string command, Func<Task<LoadingInstructionResponse>> execute,
        CancellationToken cancellationToken)
    {
        LogisticsTerminalRefusal terminal;
        try { return await execute(); }
        catch (LogisticsVersionConflictException)
        {
            terminal = new(409, LogisticsProblemCodesV1.VersionConflict, [new("expectedVersion", "version_conflict")]);
        }
        catch (LogisticsInstructionServiceException exception) when (
            exception.Status == 409 && IsTerminalRealizationConflict(exception.Code))
        {
            terminal = new(409, exception.Code, exception.Errors);
        }
        catch (LogisticsRuleException exception)
        {
            var code = exception.Code switch
            {
                LogisticsRuleCodes.InstructionNotIssued => LogisticsProblemCodesV1.InstructionState,
                LogisticsRuleCodes.LineBlocked => LogisticsProblemCodesV1.LineBlocked,
                LogisticsRuleCodes.DuplicateShareId => LogisticsProblemCodesV1.DuplicateShare,
                LogisticsRuleCodes.ShipmentExceedsLoaded => LogisticsRealizationCodesV1.ShipmentExceedsLoaded,
                LogisticsRuleCodes.DeliveryExceedsShipment => LogisticsRealizationCodesV1.DeliveryExceedsShipment,
                LogisticsRuleCodes.ShipmentNotFound or LogisticsRuleCodes.ShipmentOfAnotherLine => LogisticsRealizationCodesV1.ShipmentSourceInvalid,
                _ => null,
            };
            if (exception.Code == LogisticsRuleCodes.LineNotFound) throw new LogisticsInstructionServiceException(404, "not_found");
            if (code is null) throw new LogisticsInstructionServiceException(422, "validation_failed", [new("instruction", exception.Code)]);
            terminal = new(409, code, [new("instruction", exception.Code)]);
        }
        catch (LogisticsStoreException exception) when (exception.Code == LogisticsStoreCodes.ConcurrentChange)
        {
            var won = await FindRealizationReplayAsync(scope, permission, envelope, hash, command, cancellationToken);
            return won is null ? throw Store(exception) : Replayed(won);
        }
        catch (LogisticsStoreException exception) { throw Store(exception); }

        // The failed transaction has already rolled back. Record only the refusal in a new Logistics-only
        // transaction, and throw AFTER it commits. Lost authority must roll this write back as well.
        try
        {
            await realization!.ExecuteDeliveryAsync(scope.CompanyId, async token =>
            {
                await RequireTerminalRealizationAuthorityAsync(scope, permission, command, token);
                await store.RecordRefusalAsync(envelope, hash, command, terminal, token);
                return true;
            }, token => RequireTerminalRealizationAuthorityAsync(scope, permission, command, token), cancellationToken);
        }
        catch (LogisticsStoreException exception) when (exception.Code == LogisticsStoreCodes.ConcurrentChange)
        {
            var won = await FindRealizationReplayAsync(scope, permission, envelope, hash, command, cancellationToken);
            return won is null ? throw Store(exception) : Replayed(won);
        }
        catch (LogisticsStoreException exception) { throw Store(exception); }

        throw new LogisticsInstructionServiceException(terminal.Status, terminal.Code, terminal.Errors);
    }

    private async Task<LogisticsReplay?> FindRealizationReplayAsync(Scope scope, string? permission,
        LogisticsWriteEnvelope envelope, string hash, string command, CancellationToken cancellationToken)
    {
        if (string.Equals(command, LogisticsCommands.RecordShipmentV1, StringComparison.Ordinal))
        {
            // Shipment replay is the same protected operation as first execution. Revalidate the
            // Inventory half before reading any success, refusal or payload-mismatch oracle.
            await RequireStillAsync(scope,
                LogisticsRealizationDependenciesV1.InventoryPhysicalAllocationWritePermission, cancellationToken);
        }

        return await FindReplayAsync(scope, permission, envelope, hash, command, cancellationToken);
    }

    private async Task RequireTerminalRealizationAuthorityAsync(Scope scope, string? permission, string command,
        CancellationToken cancellationToken)
    {
        await RequireStillAsync(scope, permission, cancellationToken);
        if (string.Equals(command, LogisticsCommands.RecordShipmentV1, StringComparison.Ordinal))
        {
            await RequireStillAsync(scope,
                LogisticsRealizationDependenciesV1.InventoryPhysicalAllocationWritePermission, cancellationToken);
        }
    }

    private static bool IsTerminalRealizationConflict(string code) =>
        code is LogisticsRealizationCodesV1.AllocationSourceMismatch or LogisticsProblemCodesV1.OrderLineState
        || LogisticsRealizationDependenciesV1.InventoryTerminalConflictCodes.Contains(code);
}
