using System.Globalization;
using GoymenERP.Modules.Logistics.Application;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Sales.Application.Contracts;
using static GoymenERP.Modules.Logistics.Application.Contracts.LogisticsSubcontractDeliveryV1;

namespace GoymenERP.Api.LogisticsRuntime;

public sealed partial class LogisticsSubcontractDeliveryHost
{
    public async Task<LogisticsSubcontractInstructionV1> LoadAsync(Guid instructionId, Guid lineId, RecordLoadRequest request,
        LogisticsCommandMetadata metadata, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scope = await RequireAsync(LogisticsPermissionManifestV1.LoadsRecord, [], token); RequireOwnedContext();
        if (instructionId == Guid.Empty || lineId == Guid.Empty || request.LoadId == Guid.Empty) throw Error(400, Invalid);
        var quantity = Quantity(request.Quantity); var expected = Version(request.ExpectedVersion);
        var measurement = request.Measurement switch { "Measured" => MeasurementKind.Measured,
            "AllocatedFromVehicleWeight" => MeasurementKind.AllocatedFromVehicleWeight, _ => throw Error(400, Invalid) };
        var plan = await store.ReadSubcontractSourcesAsync(instructionId, token); await StillAsync(scope, token);
        if (!plan.TryGetValue(lineId, out var binding)) throw Error(404, "not_found");
        var envelope = Envelope(scope, metadata); var hash = LogisticsRequestHash.Compute(LogisticsCommands.RecordLoadV1, instructionId, new { lineId, request });
        return await CommandAsync(scope, envelope, hash, LogisticsCommands.RecordLoadV1, async ct =>
        {
            await LockLinesAsync(plan.Values.Select(source => source.SalesOrderLineId), ct);
            if (await store.FindSubcontractResultAsync(envelope, hash, LogisticsCommands.RecordLoadV1, ct) is { } replay) return replay;
            await using var sales = await SalesAsync(ct); await using var manufacturing = await ManufacturingAsync(ct); await using var stock = await InventoryAsync(scope, ct);
            var current = await SourceAsync(scope, binding.SalesOrderId, binding.SalesOrderLineId, binding.ManufacturingOrderId, binding.OutputFactId, binding.LabelId,
                sales, manufacturing, stock, ct);
            var stored = await store.FindInstructionAsync(instructionId, ct) ?? throw Error(404, "not_found");
            var instruction = stored.Instruction; var sources = await store.ReadSubcontractSourcesAsync(instructionId, ct);
            if (!instruction.IsContractProcessingDelivery || instruction.Version != expected || !sources.TryGetValue(lineId, out var original)
                || original != binding || !SameBinding(original, current.Source)) throw Error(409, SourceConflict);
            await RequireCustomerAsync(scope, instruction.CustomerPartyId, instruction.CompanyAccountId, ct);
            var line = instruction.Lines.Single(value => value.Id == lineId);
            var owner = await sales.GetApprovedLineForLoadingAsync(scope.CompanyId, binding.SalesOrderId, binding.SalesOrderLineId, stored.EffectiveOn, ct); await StillAsync(scope, ct);
            if (owner.Outcome != SalesOrderLineReferenceOutcomeV1.Approved || owner.Line is not { } approved) throw Error(409, SourceConflict);
            if (approved.CompanyId != scope.CompanyId || approved.OrderId != binding.SalesOrderId || approved.LineId != binding.SalesOrderLineId
                || approved.PartyId != instruction.CustomerPartyId || approved.CompanyAccountId != instruction.CompanyAccountId
                || approved.ProductId != current.Source.SalesServiceProductId || approved.OrderUnitId != line.UnitId || approved.LoadingLimit is not { } limit
                || limit.CompanyId != scope.CompanyId || limit.OrderId != approved.OrderId || limit.LineId != approved.LineId || limit.UnitId != line.UnitId
                || limit.SalesOrderVersion != approved.Version || limit.EffectiveOn != stored.EffectiveOn || string.IsNullOrWhiteSpace(limit.SourceOwner)
                || string.IsNullOrWhiteSpace(limit.SourceId) || string.IsNullOrWhiteSpace(limit.SourceVersion)) throw Error(503, Unavailable);
            if (approved.RemainderState == SalesRemainderChangeNamesV1.Cancelled) throw Error(409, SourceConflict);
            if (approved.RemainderState is not (SalesRemainderChangeNamesV1.Open or SalesRemainderChangeNamesV1.Revised)) throw Error(503, Unavailable);
            var verified = await new LoadingUnitVerifier(settings).VerifyAsync(scope.CompanyId, line.UnitId, quantity, stored.EffectiveOn, ct); await StillAsync(scope, ct);
            if (!verified.IsVerified) throw Error(verified.Code == "settings_access_denied" ? 403
                : verified.Code?.Contains("unavailable", StringComparison.Ordinal) == true ? 503 : 422, Invalid);
            if (!decimal.TryParse(limit.MaximumAggregateQuantityText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var maximum) || maximum <= 0m)
                throw Error(503, Unavailable);
            var aggregate = new LogisticsOrderLineAggregateLoadLimit(lineId, scope.CompanyId, approved.OrderId, approved.LineId, line.UnitId, approved.Version,
                maximum, limit.MaximumAggregateQuantityText, quantity, request.Quantity, limit.SourceOwner, limit.SourceId, limit.SourceVersion, limit.EffectiveOn);
            if (line.LoadedQuantity.Value + quantity > line.InstructedQuantity.Value && request.PlanExcessUses is null)
                throw Error(409, LogisticsPlanExcessV1.ApprovalRequired);
            var recorded = await store.MutateWithPlanExcessAsync(envelope, hash, LogisticsCommands.RecordLoadV1, instructionId,
                (value, now) => value.RecordLoad(expected, request.LoadId, lineId, verified.Unit!.Quantity, measurement, now),
                new("logistics.loading-instruction.load-recorded.v1", "RecordLoad", "Müşteri malının fiilî fason yüklemesi kaydedildi"), aggregate, null,
                new Dictionary<Guid, IReadOnlyList<LogisticsPlanExcessUseV1>?> { [lineId] = request.PlanExcessUses }, ct) ?? throw Error(404, "not_found");
            var labels = await LabelsAsync(stock, sources.Values, ct);
            var reply = (await ViewWithReservationsAsync(recorded.Instruction.Instruction, sources, labels, false, envelope.CorrelationId, ct)) with { RecordedLoadId = request.LoadId };
            await store.CompleteSubcontractResultAsync(envelope, hash, LogisticsCommands.RecordLoadV1, reply, ct); return reply;
        }, token);
    }
}
