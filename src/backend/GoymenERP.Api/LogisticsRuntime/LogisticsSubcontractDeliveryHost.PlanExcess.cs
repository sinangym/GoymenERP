using System.Globalization;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;
using static GoymenERP.Modules.Logistics.Application.Contracts.LogisticsSubcontractDeliveryV1;

namespace GoymenERP.Api.LogisticsRuntime;

public sealed partial class LogisticsSubcontractDeliveryHost : ILogisticsSubcontractPlanExcessReservationHostV1
{
    public async Task<LogisticsSubcontractInstructionV1> ReservePlanExcessAsync(Guid instructionId, Guid lineId, Guid exceptionId,
        ReserveLogisticsPlanExcessV1 request, LogisticsCommandMetadata metadata, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scope = await RequireAsync(LogisticsPermissionManifestV1.LoadsRecord, [ErpPermissions.InventoryPhysicalAllocationWrite], token);
        RequireOwnedContext();
        if (instructionId == Guid.Empty || lineId == Guid.Empty || exceptionId == Guid.Empty || request.AllocationId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500) throw Error(400, Invalid);
        var amount = Quantity(request.QuantityKg); var expected = Version(request.ExpectedInstructionVersion);
        if (Version(request.ExpectedExceptionVersion) != 2) throw Error(409, LogisticsPlanExcessV1.Conflict);
        _ = Version(request.ExpectedLabelVersion);
        var envelope = Envelope(scope, metadata); var hash = LogisticsRequestHash.Compute(LogisticsPlanExcessV1.ReserveCommand,
            instructionId, new { lineId, exceptionId, request });
        var plan = await store.ReadSubcontractSourcesAsync(instructionId, token); await StillAsync(scope, token);
        return await CommandAsync(scope, envelope, hash, LogisticsPlanExcessV1.ReserveCommand, async ct =>
        {
            await LockLinesAsync(plan.Values.Select(source => source.SalesOrderLineId), ct);
            if (await store.FindSubcontractResultAsync(envelope, hash, LogisticsPlanExcessV1.ReserveCommand, ct) is { } replay) return replay;
            var sources = await store.ReadSubcontractSourcesAsync(instructionId, ct);
            if (!sources.TryGetValue(lineId, out var binding) || !plan.TryGetValue(lineId, out var original) || original != binding)
                throw Error(409, SourceConflict);
            await using var sales = await SalesAsync(ct); await using var manufacturing = await ManufacturingAsync(ct); await using var stock = await InventoryAsync(scope, ct);
            var actual = await SourceAsync(scope, binding.SalesOrderId, binding.SalesOrderLineId, binding.ManufacturingOrderId,
                binding.OutputFactId, binding.LabelId, sales, manufacturing, stock, ct);
            var stored = await store.FindInstructionAsync(instructionId, ct) ?? throw Error(404, "not_found");
            var instruction = stored.Instruction;
            if (!instruction.IsContractProcessingDelivery || instruction.Status != LoadingInstructionStatus.Issued || instruction.Version != expected
                || !SameBinding(binding, actual.Source) || actual.Label.LabelVersion != request.ExpectedLabelVersion)
                throw Error(409, SourceConflict);
            await RequireCustomerAsync(scope, instruction.CustomerPartyId, instruction.CompanyAccountId, ct);
            var (approved, reserved) = await store.ReadPlanExcessReservationCapacityAsync(instructionId, lineId, exceptionId, ct);
            if (approved.Version != request.ExpectedExceptionVersion || reserved + amount > decimal.Parse(approved.Quantity, CultureInfo.InvariantCulture))
                throw Error(409, LogisticsPlanExcessV1.Conflict);
            var result = await stock.ReserveAsync(new(scope.CompanyId, request.AllocationId,
                [new(request.AllocationId, binding.LabelId, request.ExpectedLabelVersion, request.QuantityKg, request.Reason.Trim(), InventorySource(actual.Source),
                    new(scope.CompanyId, exceptionId, instructionId, lineId, 2, approved.Quantity, request.QuantityKg, "logistics.plan-excess.v1"))]),
                InventoryMetadata(scope, envelope), cancellation => StillAsync(scope, cancellation), ct);
            await StillAsync(scope, ct);
            var allocation = result.Allocations.SingleOrDefault();
            if (result.CompanyId != scope.CompanyId || result.ReservationId != request.AllocationId || result.IsReplay || allocation is null
                || allocation.AllocationId != request.AllocationId || allocation.CompanyId != scope.CompanyId || allocation.LabelId != binding.LabelId || allocation.Version != "1"
                || allocation.AllocatedKg != request.QuantityKg || allocation.RemainingKg != request.QuantityKg
                || allocation.DemandKind != InventoryAllocationDemandKindsV1.SubcontractServiceOrderLine || allocation.DemandId != binding.SalesOrderId
                || allocation.DemandLineId != binding.SalesOrderLineId || allocation.ProductId != binding.ProductId) throw Error(503, Unavailable);
            var labels = await LabelsAsync(stock, sources.Values, ct);
            await store.RecordPlanExcessReservationAsync(envelope, hash, new(scope.CompanyId, exceptionId, instructionId, lineId,
                request.AllocationId, binding.LabelId, request.QuantityKg, allocation.Version, labels[binding.LabelId].LabelVersion), ct);
            var response = await ViewWithReservationsAsync(instruction, sources, labels, false, envelope.CorrelationId, ct);
            await store.CompleteSubcontractResultAsync(envelope, hash, LogisticsPlanExcessV1.ReserveCommand, response, ct);
            return response;
        }, token);
    }
    private async Task<LogisticsSubcontractInstructionV1> ViewWithReservationsAsync(LoadingInstruction instruction,
        IReadOnlyDictionary<Guid, LogisticsSubcontractSourceV1> sources, IReadOnlyDictionary<Guid, InventorySubcontractLabelReadV1>? labels,
        bool replay, string correlation, CancellationToken token)
    {
        var view = View(instruction, sources, labels, replay, correlation);
        var reservations = await store.ReadPlanExcessReservationsAsync(instruction.Id, token);
        return view with { Lines = view.Lines.Select(line => line with
        {
            AdditionalAllocations = reservations.Where(item => item.LineId == line.LineId).Select(item =>
            {
                var actual = labels?.GetValueOrDefault(item.LabelId)?.Allocations.SingleOrDefault(allocation => allocation.AllocationId == item.AllocationId);
                if (actual is null) throw Error(503, Unavailable);
                return new LogisticsPlanExcessAdditionalAllocationV1(actual.AllocationId, item.ExceptionId, actual.Version, actual.RemainingKg, actual.AllocatedKg);
            }).ToArray() is { Length: > 0 } values ? values : null
        }).ToArray() };
    }
}
