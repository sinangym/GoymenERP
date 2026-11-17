using GoymenERP.BuildingBlocks.Concurrency;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Api.InventoryRuntime;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using static GoymenERP.Modules.Logistics.Application.Contracts.LogisticsSubcontractDeliveryV1;
using LogisticsQuantity = GoymenERP.BuildingBlocks.Quantities.Quantity<GoymenERP.Modules.Logistics.Domain.Quantities.LogisticsQuantityRefusals>;

namespace GoymenERP.Api.LogisticsRuntime;

public sealed partial class LogisticsSubcontractDeliveryHost
{
    public async Task<LogisticsSubcontractInstructionV1> CreateAsync(CreateSubcontractDeliveryV1 request, LogisticsCommandMetadata metadata, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scope = await RequireAsync(LogisticsPermissionManifestV1.InstructionsCreate, [ErpPermissions.InventoryPhysicalAllocationWrite], token);
        if (request.InstructionId == Guid.Empty || request.EffectiveOn == default || request.Lines is null || request.Lines.Count is < 1 or > 500
            || request.Lines.Any(line => line is null) || request.Lines.Select(line => line.LineId).Distinct().Count() != request.Lines.Count
            || request.Lines.Select(line => line.AllocationId).Distinct().Count() != request.Lines.Count) throw Error(400, Invalid);
        var envelope = Envelope(scope, metadata); var hash = LogisticsRequestHash.Compute(LogisticsCommands.CreateV1, request.InstructionId, request);
        return await CommandAsync(scope, envelope, hash, LogisticsCommands.CreateV1, async ct =>
        {
            await LockLinesAsync(request.Lines.Select(line => line.SalesOrderLineId), ct);
            if (await store.FindSubcontractResultAsync(envelope, hash, LogisticsCommands.CreateV1, ct) is { } replay) return replay;
            await using var sales = await SalesAsync(ct); await using var manufacturing = await ManufacturingAsync(ct);
            await using var stock = await InventoryAsync(scope, ct);
            var sources = new Dictionary<Guid, LogisticsSubcontractSourceV1>();
            foreach (var line in request.Lines)
            {
                if (line.LineId == Guid.Empty || line.AllocationId == Guid.Empty || string.IsNullOrWhiteSpace(line.Reason)) throw Error(400, Invalid);
                var actual = await SourceAsync(scope, line.SalesOrderId, line.SalesOrderLineId, line.ManufacturingOrderId,
                    line.OutputFactId, line.LabelId, sales, manufacturing, stock, ct);
                var s = actual.Source;
                if (s.SalesOrderVersion != Version(line.ExpectedSalesOrderVersion) || s.SalesLineRevision != line.SalesLineRevision
                    || s.ManufacturingOrderVersion != Version(line.ExpectedManufacturingOrderVersion)
                    || s.ManufacturingPlanVersion != Version(line.ExpectedPlanVersion) || s.ManufacturingSalesLinkRevision != Version(line.ExpectedSalesLinkRevision)
                    || actual.Label.LabelVersion != line.ExpectedLabelVersion) throw Error(409, SourceConflict);
                _ = Quantity(line.QuantityKg); sources.Add(line.LineId, s);
            }
            var first = sources.Values.First();
            if (sources.Values.Any(s => s.CustomerPartyId != first.CustomerPartyId || s.CustomerAccountId != first.CustomerAccountId)) throw Error(422, Invalid);
            await RequireCustomerAsync(scope, first.CustomerPartyId, first.CustomerAccountId, ct);
            await stock.ReserveAsync(new(scope.CompanyId, request.InstructionId, request.Lines.Select(line => new InventorySubcontractReservationLineV1(
                line.AllocationId, line.LabelId, line.ExpectedLabelVersion, line.QuantityKg, line.Reason, InventorySource(sources[line.LineId]))).ToArray()),
                InventoryMetadata(scope, envelope), cancellation => StillAsync(scope, cancellation), ct);
            var labels = await LabelsAsync(stock, sources.Values, ct);
            var result = await store.StoreSubcontractInstructionAsync(envelope, hash, request.EffectiveOn, () =>
            {
                var instruction = LoadingInstruction.Create(request.InstructionId, scope.CompanyId, first.CustomerPartyId,
                    first.CustomerAccountId, clock.GetUtcNow(), LoadingInstructionShipmentKind.ContractProcessingDelivery);
                foreach (var line in request.Lines)
                {
                    var s = sources[line.LineId];
                    instruction.AddLine(instruction.Version, line.LineId, new(s.SalesOrderId, s.SalesOrderLineId, s.SalesLineRevision),
                        s.ProductId, SettingsUnitIdentitiesV1.Kilogram, line.AllocationId, LogisticsQuantity.Of(Quantity(line.QuantityKg), GoymenERP.BuildingBlocks.Quantities.QuantityUnit.Kilogram), clock.GetUtcNow());
                }
                return instruction;
            }, sources, ct);
            var response = await ViewWithReservationsAsync(result.Instruction.Instruction, sources, labels, false, envelope.CorrelationId, ct);
            await store.CompleteSubcontractResultAsync(envelope, hash, LogisticsCommands.CreateV1, response, ct); return response;
        }, token);
    }

    public async Task<LogisticsSubcontractInstructionV1> ShipAsync(Guid instructionId, ShipSubcontractDeliveryV1 request,
        LogisticsCommandMetadata metadata, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scope = await RequireAsync(LogisticsPermissionManifestV1.ShipmentsRecord, [ErpPermissions.InventoryPhysicalAllocationWrite], token);
        if (instructionId == Guid.Empty || request.Lines is null || request.Lines.Count is < 1 or > 500 || request.Lines.Any(line => line is null)
            || request.OccurredAtUtc == default || request.OccurredAtUtc.Offset != TimeSpan.Zero || request.Lines.Select(line => line.LineId).Distinct().Count() != request.Lines.Count
            || request.Lines.Select(line => line.ShipmentId).Distinct().Count() != request.Lines.Count
            || request.Lines.Select(line => line.MovementId).Distinct().Count() != request.Lines.Count) throw Error(400, Invalid);
        var expected = Version(request.ExpectedVersion); var envelope = Envelope(scope, metadata); var hash = LogisticsRequestHash.Compute(ShipCommand, instructionId, request);
        return await CommandAsync(scope, envelope, hash, ShipCommand, async ct =>
        {
            await LockLinesAsync(request.Lines.Select(line => line.SalesOrderLineId), ct);
            if (await store.FindSubcontractResultAsync(envelope, hash, ShipCommand, ct) is { } replay) return replay;
            await using var sales = await SalesAsync(ct); await using var manufacturing = await ManufacturingAsync(ct); await using var stock = await InventoryAsync(scope, ct);
            var current = new Dictionary<Guid, LogisticsSubcontractSourceV1>();
            foreach (var line in request.Lines)
            {
                if (line.LineId == Guid.Empty || line.ShipmentId == Guid.Empty || line.MovementId == Guid.Empty) throw Error(400, Invalid);
                var actual = await SourceAsync(scope, line.SalesOrderId, line.SalesOrderLineId, line.ManufacturingOrderId,
                    line.OutputFactId, line.LabelId, sales, manufacturing, stock, ct);
                current.Add(line.LineId, actual.Source);
            }
            var stored = await store.FindInstructionAsync(instructionId, ct) ?? throw Error(404, "not_found");
            var instruction = stored.Instruction; var original = await store.ReadSubcontractSourcesAsync(instructionId, ct);
            if (!instruction.IsContractProcessingDelivery || instruction.Version != expected || instruction.Status != LoadingInstructionStatus.Issued
                || instruction.Weighing is null || instruction.Shares.Any(share => share.Kind == LoadingShareKind.Shipped)) throw Error(409, SourceConflict);
            var loaded = instruction.Lines.Where(line => !line.LoadedNotShipped.IsZero).ToArray();
            if (loaded.Length == 0 || loaded.Length != instruction.Lines.Count || loaded.Length != request.Lines.Count
                || loaded.Any(line => !current.ContainsKey(line.Id))) throw Error(422, Invalid);
            foreach (var line in loaded)
            {
                if (line.AllocationId is null || !original.TryGetValue(line.Id, out var old) || !SameBinding(old, current[line.Id])
                    || current[line.Id].CustomerPartyId != instruction.CustomerPartyId || current[line.Id].CustomerAccountId != instruction.CompanyAccountId)
                    throw Error(409, SourceConflict);
            }
            await RequireCustomerAsync(scope, instruction.CustomerPartyId, instruction.CompanyAccountId, ct);
            // Every order is read even after a business refusal. A technical/unreadable gate dominates all 409s.
            var gates = new List<LogisticsShipmentRiskGateFactsV1>(); string? refusal = null; var unreadable = false;
            foreach (var order in current.Values.Select(s => s.SalesOrderId).Distinct().Order())
            {
                var shipmentId = request.Lines.First(line => line.SalesOrderId == order).ShipmentId;
                try
                {
                    var answer = await risk.GetAsync(order, instructionId, ct); await StillAsync(scope, ct);
                    var decision = LogisticsShipmentRiskGate.Evaluate(answer, shipmentId, order, instructionId);
                    if (decision is null) unreadable = true;
                    else if (decision.Facts is { } facts) gates.Add(facts); else refusal ??= decision.RefusalCode;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (LogisticsInstructionServiceException error) when (error.Status is 401 or 403) { throw; }
                catch { unreadable = true; }
            }
            if (unreadable) throw Error(503, Unavailable);
            if (refusal is not null) throw Error(409, refusal);
            // Only explicit reservations made against this actual approval/line may accompany its original allocation.
            var reservations = await store.ReadPlanExcessReservationsAsync(instructionId, ct);
            foreach (var input in request.Lines)
            {
                if (input.AdditionalAllocations is not { } extras) continue;
                if (extras.Count is < 1 or > 500 || extras.Any(extra => extra is null)
                    || extras.Select(extra => extra.AllocationId).Distinct().Count() != extras.Count) throw Error(422, Invalid);
                var usedByException = new Dictionary<Guid, decimal>();
                foreach (var extra in extras)
                {
                    var bound = reservations.SingleOrDefault(item => item.AllocationId == extra.AllocationId && item.LineId == input.LineId
                        && item.LabelId == input.LabelId) ?? throw Error(409, SourceConflict);
                    usedByException[bound.ExceptionId] = usedByException.GetValueOrDefault(bound.ExceptionId) + Quantity(extra.QuantityKg);
                }
                if (input.PlanExcessUses is not { } uses || uses.Any(use => use is null)
                    || uses.Select(use => use.ExceptionId).Distinct().Count() != uses.Count
                    || uses.Count != usedByException.Count || uses.Any(use => !usedByException.TryGetValue(use.ExceptionId, out var amount)
                        || amount != Quantity(use.Quantity))) throw Error(409, LogisticsPlanExcessV1.Conflict);
            }
            var lines = request.Lines.Select(line => new InventorySubcontractShipmentLineV1(line.ShipmentId, line.LineId,
                instruction.Lines.Single(l => l.Id == line.LineId).AllocationId!.Value, line.ExpectedAllocationVersion, line.LabelId,
                line.ExpectedLabelVersion, line.MovementId, Kg(instruction.Lines.Single(l => l.Id == line.LineId).LoadedNotShipped.Value),
                InventorySource(current[line.LineId]), line.AdditionalAllocations?.Select(extra =>
                    new InventorySubcontractAdditionalAllocationV1(extra.AllocationId, extra.ExpectedVersion, extra.QuantityKg)).ToArray())).ToArray();
            await stock.ShipAsync(new(scope.CompanyId, instructionId, instruction.CustomerPartyId, request.OccurredAtUtc, lines),
                InventoryMetadata(scope, envelope), cancellation => StillAsync(scope, cancellation), ct);
            var labels = await LabelsAsync(stock, current.Values, ct);
            var result = await store.ShipSubcontractInstructionAsync(envelope, hash, instructionId, expected,
                request.Lines.ToDictionary(line => line.LineId, line => line.ShipmentId), gates, request.OccurredAtUtc, ct,
                request.Lines.ToDictionary(line => line.LineId, line => line.PlanExcessUses)) ?? throw Error(404, "not_found");
            var response = await ViewWithReservationsAsync(result.Instruction.Instruction, original, labels, false, envelope.CorrelationId, ct);
            await store.CompleteSubcontractResultAsync(envelope, hash, ShipCommand, response, ct); return response;
        }, token);
    }

    public async Task<LogisticsSubcontractInstructionV1> CorrectWeighingAsync(Guid instructionId, CorrectSubcontractWeighingV1 request,
        LogisticsCommandMetadata metadata, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scope = await RequireAsync(LogisticsPermissionManifestV1.WeighingsCorrect, [], token);
        if (instructionId == Guid.Empty || request.WeighingId == Guid.Empty || request.CorrectionId == Guid.Empty || request.ExpectedWeighingRevision < 1
            || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500) throw Error(422, Invalid);
        var expected = Version(request.ExpectedVersion); var direction = request.Direction switch
        { "TareFirst" => WeighingDirection.TareFirst, "GrossFirst" => WeighingDirection.GrossFirst, _ => throw Error(400, Invalid) };
        var first = Quantity(request.FirstWeighingKg); var second = Quantity(request.SecondWeighingKg);
        var envelope = Envelope(scope, metadata); var hash = LogisticsRequestHash.Compute(LogisticsCommands.CorrectWeighingV1, instructionId, request);
        return await CommandAsync(scope, envelope, hash, LogisticsCommands.CorrectWeighingV1, async ct =>
        {
            // SAT-D378: acquire the Sales range BEFORE any Logistics read, including a replay lookup.
            await using var claims = await SalesSubcontractInvoiceClaimReadSession.EnlistAsync(db.Database.GetDbConnection(), db.Database.CurrentTransaction!.GetDbTransaction(), company, ct);
            var claim = await claims.LockAndReadAsync(instructionId, request.WeighingId, ct); await StillAsync(scope, ct);
            if (await store.FindSubcontractResultAsync(envelope, hash, LogisticsCommands.CorrectWeighingV1, ct) is { } replay) return replay;
            if (claim.CompanyId != scope.CompanyId || claim.LoadingInstructionId != instructionId || claim.WeighingId != request.WeighingId
                || claim.Status != SalesSubcontractInvoiceClaimStatusV1.Available || claim.HasActiveInvoice is null
                || claim.ActiveInvoiceIds is null || claim.SourceVersion is not { Length: 64 }) throw Error(503, Unavailable);
            if (claim.HasActiveInvoice == true) throw Error(409, ActiveInvoice);
            var stored = await store.FindInstructionAsync(instructionId, ct) ?? throw Error(404, "not_found");
            var instruction = stored.Instruction;
            if (!instruction.IsContractProcessingDelivery || instruction.Weighing?.WeighingId != request.WeighingId
                || instruction.WeighingRevision != request.ExpectedWeighingRevision || instruction.Version != expected) throw Error(409, SourceConflict);
            var sources = await store.ReadSubcontractSourcesAsync(instructionId, ct);
            var result = await store.MutateInstructionAsync(envelope, hash, LogisticsCommands.CorrectWeighingV1, instructionId,
                (value, now) => value.CorrectWeighing(expected, request.CorrectionId, direction,
                    LogisticsQuantity.Of(first, GoymenERP.BuildingBlocks.Quantities.QuantityUnit.Kilogram),
                    LogisticsQuantity.Of(second, GoymenERP.BuildingBlocks.Quantities.QuantityUnit.Kilogram), request.Reason, scope.ActorId, now),
                new("logistics.loading-instruction.weighing-corrected.v1", "CorrectWeighing", request.Reason.Trim()), ct) ?? throw Error(404, "not_found");
            await using var stock = await InventoryAsync(scope, ct);
            var labels = await LabelsAsync(stock, sources.Values, ct);
            var response = await ViewWithReservationsAsync(result.Instruction.Instruction, sources, labels, false, envelope.CorrelationId, ct);
            await store.CompleteSubcontractResultAsync(envelope, hash, LogisticsCommands.CorrectWeighingV1, response, ct); return response;
        }, token);
    }

    private async Task<LogisticsSubcontractInstructionV1> CommandAsync(Scope scope, LogisticsWriteEnvelope envelope, string hash, string command,
        Func<CancellationToken, Task<LogisticsSubcontractInstructionV1>> work, CancellationToken token)
    {
        LogisticsInstructionServiceException? failure = null;
        try { return await TransactionAsync(scope, work, token); }
        catch (LogisticsInstructionServiceException error) { failure = error; }
        catch (InventoryDefiniteShipmentException error)
        {
            failure = new(error.Status, error.Code,
                detail: InventoryPhysicalStockRefusalProblem.Apply(error.Status, error, new Dictionary<string, object?>()));
        }
        catch (LogisticsVersionConflictException) { failure = Error(409, SourceConflict); }
        catch (LogisticsRuleException error)
        {
            failure = error.Code is LogisticsRuleCodes.WeighingDirectionInconsistent or LogisticsRuleCodes.WeighingDirectionRequired
                or LogisticsRuleCodes.WeighingUnitUnsupported or LogisticsRuleCodes.WeighingCorrectionReasonRequired
                ? Error(422, Invalid)
                : Error(409, error.Code == LogisticsShipmentRiskGateCodesV1.ExceptionUsed ? error.Code : SourceConflict);
        }
        catch (LogisticsStoreException error) when (error.Code == LogisticsStoreCodes.SameKeyPayloadMismatch) { failure = Error(409, LogisticsProblemCodesV1.SameKeyPayloadMismatch); }
        catch (LogisticsStoreException error) when (error.Code == LogisticsStoreCodes.ConcurrentChange) { throw Error(409, LogisticsProblemCodesV1.ConcurrentChange); }
        catch (LogisticsStoreException error) when (error.Code == LogisticsStoreCodes.AggregateLimitExceeded) { failure = Error(409, LogisticsProblemCodesV1.OrderLineAggregateLimitExceeded); }
        catch (SalesOrderLineBusyException) { throw Error(409, SalesOrderLineSerializationLockContractV1.BusyCode); }
        if (failure is { Status: 409, IsReplay: false } && failure.Code != LogisticsProblemCodesV1.SameKeyPayloadMismatch
            && failure.Code != SalesOrderLineSerializationLockContractV1.BusyCode && !failure.Code.Contains("concurrent", StringComparison.Ordinal))
        {
            await TransactionAsync(scope, async ct =>
            {
                await store.RecordRefusalAsync(envelope, hash, command, new(409, failure.Code, failure.Errors, failure.Detail), ct); return true;
            }, token);
        }
        throw failure!;
    }
    private async Task RequireCustomerAsync(Scope scope, Guid partyId, Guid accountId, CancellationToken token)
    {
        var answer = await parties.GetAuthorizedAsync(partyId, accountId, token); await StillAsync(scope, token);
        if (answer.Outcome == LogisticsPartyAccountOutcomeV1.Denied) throw Error(403, "forbidden");
        if (answer.Outcome != LogisticsPartyAccountOutcomeV1.Found || answer.Account is not { } account || account.CompanyId != scope.CompanyId
            || account.PartyId != partyId || account.CompanyAccountId != accountId) throw Error(503, Unavailable);
        if (!account.AccountIsActive || !account.PartyIsActive) throw Error(409, SourceConflict);
    }
    private static bool SameBinding(LogisticsSubcontractSourceV1 original, LogisticsSubcontractSourceV1 current) =>
        original.CompanyId == current.CompanyId && original.CustomerPartyId == current.CustomerPartyId && original.CustomerAccountId == current.CustomerAccountId
        && original.SalesOrderId == current.SalesOrderId && original.SalesOrderLineId == current.SalesOrderLineId
        && original.ProductId == current.ProductId && original.ProductVersion == current.ProductVersion && original.SalesServiceProductId == current.SalesServiceProductId
        && original.ManufacturingOrderId == current.ManufacturingOrderId && original.ManufacturingPlanId == current.ManufacturingPlanId
        && original.OutputFactId == current.OutputFactId && original.LabelId == current.LabelId;
}
