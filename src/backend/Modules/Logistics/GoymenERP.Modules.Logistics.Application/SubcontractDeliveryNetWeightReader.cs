using System.Globalization;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;

namespace GoymenERP.Modules.Logistics.Application;

/// <summary>
/// F1 (STK-S4): the pure mapping of a rebuilt loading instruction and a requested weighing id to the
/// <see cref="ILogisticsSubcontractDeliveryNetWeightReadPortV1"/> answer. No I/O, no clock, no split: it states the stored
/// weighing's facts or a fail-closed status, never a zero net.
/// <list type="bullet">
/// <item><b>NoSource</b>: no instruction, an instruction of another company than the one asked, no weighing record, a
/// weighing whose id is not the requested one, or a cancelled instruction (a cancelled instruction has no shipment, so its
/// weighing is no delivery weighing).</item>
/// <item><b>Unavailable</b>: a record that contradicts itself — a non-positive or non-kilogram net, a non-kilogram line, a
/// share position outside the shares, or per-line loaded kilograms that do not sum to the weighing's loaded snapshot.</item>
/// <item><b>Available</b>: everything else, with the per-line and per-shipment facts.</item>
/// </list>
/// </summary>
public static class SubcontractDeliveryNetWeightReader
{
    public static LogisticsSubcontractDeliveryNetWeightReadV1 Read(Guid companyId, Guid loadingInstructionId, Guid weighingId,
        LoadingInstruction? instruction)
    {
        if (instruction is null || companyId == Guid.Empty || instruction.CompanyId != companyId || instruction.Id != loadingInstructionId)
        {
            return LogisticsSubcontractDeliveryNetWeightReadV1.NoSource(companyId, loadingInstructionId, weighingId,
                LogisticsSubcontractDeliveryNetWeightReasonCodesV1.InstructionNotFound);
        }

        var weighing = instruction.Weighing;
        if (weighing is null)
        {
            return LogisticsSubcontractDeliveryNetWeightReadV1.NoSource(companyId, loadingInstructionId, weighingId,
                LogisticsSubcontractDeliveryNetWeightReasonCodesV1.WeighingNotRecorded);
        }

        if (weighingId == Guid.Empty || weighing.WeighingId != weighingId)
        {
            return LogisticsSubcontractDeliveryNetWeightReadV1.NoSource(companyId, loadingInstructionId, weighingId,
                LogisticsSubcontractDeliveryNetWeightReasonCodesV1.WeighingMismatch);
        }

        if (instruction.Status == LoadingInstructionStatus.Cancelled)
        {
            return LogisticsSubcontractDeliveryNetWeightReadV1.NoSource(companyId, loadingInstructionId, weighingId,
                LogisticsSubcontractDeliveryNetWeightReasonCodesV1.InstructionCancelled);
        }

        // LOJ-D13: the last reasoned correction (if any) states the weighing in force; the loaded snapshot stays the original's.
        var corrections = instruction.WeighingCorrections;
        var correction = corrections.Count == 0 ? null : corrections[corrections.Count - 1];
        var net = correction?.Net ?? weighing.Net;
        var inconsistent = LogisticsSubcontractDeliveryNetWeightReadV1.Unavailable(companyId, loadingInstructionId, weighingId,
            LogisticsSubcontractDeliveryNetWeightReasonCodesV1.RecordInconsistent);
        if (instruction.Status != LoadingInstructionStatus.Issued
            || net.Unit != QuantityUnit.Kilogram || net.Value <= 0m
            || correction is not null && (correction.Revision != instruction.WeighingRevision
                || correction.SharesBefore < weighing.SharesBefore || correction.SharesBefore > instruction.Shares.Count)
            || weighing.LoadedAtWeighing.Unit != QuantityUnit.Kilogram
            || weighing.SharesBefore < 0 || weighing.SharesBefore > instruction.Shares.Count
            || instruction.Lines.Any(line => line.InstructedQuantity.Unit != QuantityUnit.Kilogram))
        {
            return inconsistent;
        }

        // The load shares recorded before the weighing, per line: the kilograms each line contributed to the loaded snapshot.
        var loadedBefore = instruction.Shares.Take(weighing.SharesBefore)
            .Where(share => share.Kind == LoadingShareKind.Loaded)
            .GroupBy(share => share.LineId)
            .ToDictionary(group => group.Key, group => group.Sum(share => share.Quantity.Value));
        if (loadedBefore.Keys.Any(lineId => instruction.Lines.All(line => line.Id != lineId))
            || loadedBefore.Values.Sum() != weighing.LoadedAtWeighing.Value)
        {
            return inconsistent;
        }

        var lines = instruction.Lines.Select(line => new LogisticsSubcontractDeliveryNetWeightLineV1(
                line.Id,
                line.Source.OrderId,
                line.Source.LineId,
                line.Source.Revision,
                line.ProductId,
                line.UnitId,
                ExactText(loadedBefore.GetValueOrDefault(line.Id)),
                ExactText(line.LoadedQuantity.Value),
                ExactText(line.ShippedQuantity.Value),
                ExactText(line.DeliveredQuantity.Value)))
            .ToArray();
        var shipments = instruction.Shares.Where(share => share.Kind == LoadingShareKind.Shipped)
            .Select(share => new LogisticsSubcontractDeliveryShipmentV1(share.ShareId, share.LineId, ExactText(share.Quantity.Value), share.RecordedAtUtc))
            .ToArray();

        return LogisticsSubcontractDeliveryNetWeightReadV1.Available(companyId, loadingInstructionId, weighingId,
            new LogisticsSubcontractDeliveryNetWeightV1(
                ExactText(net.Value),
                LogisticsSubcontractDeliveryNetWeightContractV1.KilogramUnit,
                LogisticsNetWeightMeasurementKindV1.ManualWeighbridge,
                (correction?.Direction ?? weighing.Direction).ToString(),
                ExactText(weighing.LoadedAtWeighing.Value),
                ExactText(correction?.DifferenceKg ?? weighing.DifferenceKg),
                instruction.WeighingRevision,
                instruction.Version,
                weighing.RecordedAtUtc,
                lines,
                shipments));
    }

    /// <summary>Plain invariant text with trailing zeros dropped (Ö2: equal quantities print equally), as the dispatch-note views print.</summary>
    internal static string ExactText(decimal value) => (value / 1.000000000000000000000000000000000m).ToString(CultureInfo.InvariantCulture);
}
