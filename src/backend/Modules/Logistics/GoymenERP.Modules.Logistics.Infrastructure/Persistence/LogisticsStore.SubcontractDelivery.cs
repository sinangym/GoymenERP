using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Domain.Quantities;

namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence;

public sealed partial class LogisticsStore
{
    private void AppendWeighingCorrection(Guid companyId, LoadingInstruction instruction,
        VehicleWeighingCorrection correction, string actorId)
    {
        if (instruction.Weighing is not { } original || !string.Equals(correction.CorrectedBy, actorId, StringComparison.Ordinal))
            throw new InvalidOperationException("Tartı düzeltmesini kaydeden bu komutun gerçek aktörüdür.");
        dbContext.WeighingCorrections.Add(new VehicleWeighingCorrectionRow
        {
            CompanyId = companyId, InstructionId = instruction.Id, CorrectionId = correction.CorrectionId,
            Revision = correction.Revision, Direction = (int)correction.Direction,
            FirstWeighingKg = RequireFits(correction.FirstWeighing.Value), SecondWeighingKg = RequireFits(correction.SecondWeighing.Value),
            NetKg = RequireFits(correction.Net.Value), PreviousNetKg = RequireFits(correction.PreviousNet.Value),
            LoadedKgAtWeighing = RequireFits(original.LoadedAtWeighing.Value),
            DifferenceKg = correction.DifferenceKg < 0m ? -RequireFits(-correction.DifferenceKg) : RequireFits(correction.DifferenceKg),
            Reason = correction.Reason, SharesBefore = correction.SharesBefore, CorrectedBy = actorId,
            CorrectedAtUtc = correction.CorrectedAtUtc,
        });
    }

    private static void RestoreWeighingCorrection(LoadingInstruction instruction, VehicleWeighingCorrectionRow row)
    {
        if (row.CompanyId != instruction.CompanyId || row.InstructionId != instruction.Id
            || instruction.Weighing?.LoadedAtWeighing.Value != row.LoadedKgAtWeighing)
            throw new InvalidOperationException("Kayıtlı tartı düzeltmesi aynı şirket/talimat ve özgün yükleme kanıtına aittir.");
        var correction = instruction.CorrectWeighing(instruction.Version, row.CorrectionId, (WeighingDirection)row.Direction,
            Quantity.Of(row.FirstWeighingKg, QuantityUnit.Kilogram), Quantity.Of(row.SecondWeighingKg, QuantityUnit.Kilogram),
            row.Reason, row.CorrectedBy, row.CorrectedAtUtc);
        if (correction.Revision != row.Revision || correction.Net.Value != row.NetKg || correction.PreviousNet.Value != row.PreviousNetKg
            || correction.DifferenceKg != row.DifferenceKg || correction.SharesBefore != row.SharesBefore
            || correction.Reason != row.Reason || correction.CorrectedBy != row.CorrectedBy || correction.CorrectedAtUtc != row.CorrectedAtUtc)
            throw new InvalidOperationException("Kayıtlı tartı düzeltmesi gerçek önceki tartı ve pay sırasından yeniden üretilemiyor.");
    }

    private static object[] CorrectionSnapshot(LoadingInstruction instruction) =>
        [.. instruction.WeighingCorrections.Select(correction => (object)new
        {
            correction.CorrectionId, correction.Revision, direction = correction.Direction.ToString(),
            first = correction.FirstWeighing.Value, second = correction.SecondWeighing.Value,
            net = correction.Net.Value, previousNet = correction.PreviousNet.Value, difference = correction.DifferenceKg,
            correction.Reason, correction.SharesBefore, correction.CorrectedBy, correction.CorrectedAtUtc,
        })];
}
