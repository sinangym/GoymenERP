using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// SAT-S13 (STK-S24 → satis; SAT-D343, SAT-D319 (6), SAT-D389): the host adapter between the Logistics-owned weighing-difference
/// read (<see cref="ILogisticsWeighingDifferenceReadPortV1"/>) and the Sales consumer seam. It exposes no endpoint, carries no
/// authority (the Sales services fence around it), writes nothing and maps facts only: the owner's kilograms stay verbatim, nothing is
/// split, rounded or recomputed here. Anything the seam cannot state consistently — a key that is not echoed, an unknown wire name,
/// a negative total, a difference that is not scale − theoretical, a "may proceed" that contradicts the state — is
/// <see cref="SalesDispatchWeighingDifferenceStatusV1.Unavailable"/>: fail closed, never a silent zero. The owner's own refusals
/// (another company, store failure) propagate to the Sales caller, which fails closed as well.
/// </summary>
public sealed class SalesDispatchWeighingDifferenceHost(
    ILogisticsWeighingDifferenceReadPortV1 source) : ISalesDispatchWeighingDifferenceReadPortV1
{
    public async Task<SalesDispatchWeighingDifferenceReadV1> ReadAsync(Guid companyId, Guid dispatchNoteId,
        CancellationToken cancellationToken = default)
    {
        var answer = await source.FindByDispatchNoteAsync(companyId, dispatchNoteId, cancellationToken);
        if (answer is null)
        {
            return new SalesDispatchWeighingDifferenceReadV1(SalesDispatchWeighingDifferenceStatusV1.NotRecorded, companyId,
                dispatchNoteId, null);
        }

        if (answer.CompanyId != companyId
            || answer.DispatchNoteId != dispatchNoteId
            || answer.RecordId == Guid.Empty
            || answer.Version < 1
            || answer.TheoreticalKg < 0m
            || answer.ScaleKg < 0m
            || answer.TotalExcessKg < 0m
            || answer.ScaleKg - answer.TheoreticalKg != answer.DifferenceKg
            || !TryParse<SalesWeighingDifferenceBandView>(answer.BandLevel, out var band)
            || !TryParse<SalesWeighingDifferenceDecisionView>(answer.Decision, out var decision)
            || !TryParse<SalesWeighingDifferenceRecordStateView>(answer.Status, out var state)
            || answer.SaleMayProceed == (state == SalesWeighingDifferenceRecordStateView.PendingApproval))
        {
            return new SalesDispatchWeighingDifferenceReadV1(SalesDispatchWeighingDifferenceStatusV1.Unavailable, companyId,
                dispatchNoteId, null);
        }

        return new SalesDispatchWeighingDifferenceReadV1(SalesDispatchWeighingDifferenceStatusV1.Recorded, companyId, dispatchNoteId,
            new SalesDispatchWeighingDifferenceV1(answer.RecordId, answer.TheoreticalKg, answer.ScaleKg, answer.DifferenceKg,
                answer.TotalExcessKg, band, decision, state, answer.SaleMayProceed, answer.Version));
    }

    /// <summary>The owner's wire name, exactly (no case folding, no numbers): anything else is not a fact the seam can state.</summary>
    private static bool TryParse<TEnum>(string? value, out TEnum parsed)
        where TEnum : struct, Enum
    {
        parsed = default;
        return !string.IsNullOrEmpty(value)
            && Enum.TryParse(value, ignoreCase: false, out parsed)
            && Enum.IsDefined(parsed)
            && string.Equals(parsed.ToString(), value, StringComparison.Ordinal);
    }
}
