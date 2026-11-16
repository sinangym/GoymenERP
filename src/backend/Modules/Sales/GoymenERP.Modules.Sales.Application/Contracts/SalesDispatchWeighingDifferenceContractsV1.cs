using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Sales.Application.Contracts;

/// <summary>
/// SAT-S13 (EK1 SAT-D343; SAT-D319 (6), SAT-D320 (3), SAT-D322 (1); EK15 SAT-D389; EK20 SAT-D405): the Sales consumer seam of the
/// Logistics-owned weighing-difference record of one sales dispatch note. Sales neither computes nor stores the difference: the
/// Logistics calculator (STK-S24) is the only source; Sales shows its note totals and waits on it before an invoice.
/// </summary>
public static class SalesDispatchWeighingDifferenceApiV1
{
    /// <summary>The read route of one dispatch note's weighing-difference summary (the API-30 read name).</summary>
    public const string Path = SalesDispatchApiV1.DispatchesPath + "/{dispatchNoteId:guid}/weighing-difference";
}

/// <summary>What the Logistics owner could state about one dispatch note's weighing difference.</summary>
public enum SalesDispatchWeighingDifferenceStatusV1
{
    /// <summary>A record exists and every fact of it is consistent.</summary>
    Recorded = 1,

    /// <summary>The owner answered that the note has no weighing-difference record (SAT-D389: the note waits).</summary>
    NotRecorded = 2,

    /// <summary>The owner is not composed, refused, failed or answered something Sales cannot state consistently.</summary>
    Unavailable = 3,
}

/// <summary>The owner's decision of the note total (SAT-D343 total excess against the SAT-D319 rate).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SalesWeighingDifferenceDecisionView>))]
public enum SalesWeighingDifferenceDecisionView
{
    NoExcess = 1,
    WithinRate = 2,
    ApprovalRequired = 3,
}

/// <summary>The owner's record state; only <see cref="PendingApproval"/> stops the sale (SAT-D319 (6)).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SalesWeighingDifferenceRecordStateView>))]
public enum SalesWeighingDifferenceRecordStateView
{
    Recorded = 1,
    PendingApproval = 2,
    Approved = 3,
}

/// <summary>The owner's band level of the difference ratio (Settings band; NotEvaluated when no band applied).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SalesWeighingDifferenceBandView>))]
public enum SalesWeighingDifferenceBandView
{
    NotEvaluated = 1,
    None = 2,
    Warning = 3,
    Approval = 4,
    Block = 5,
}

/// <summary>The owner's facts of one record, verbatim (kilograms as the owner stated them; nothing is rounded here).</summary>
public sealed record SalesDispatchWeighingDifferenceV1(
    Guid RecordId,
    decimal TheoreticalKg,
    decimal ScaleKg,
    decimal DifferenceKg,
    decimal TotalExcessKg,
    SalesWeighingDifferenceBandView BandLevel,
    SalesWeighingDifferenceDecisionView Decision,
    SalesWeighingDifferenceRecordStateView State,
    bool SaleMayProceed,
    int Version);

/// <summary>The seam's answer: the asked key echoed, the status and — only when recorded — the facts.</summary>
public sealed record SalesDispatchWeighingDifferenceReadV1(
    SalesDispatchWeighingDifferenceStatusV1 Status,
    Guid CompanyId,
    Guid DispatchNoteId,
    SalesDispatchWeighingDifferenceV1? Value);

/// <summary>
/// The consumer-defined read port (implemented by the host over the Logistics owner's read). It carries no authority: every Sales
/// caller fences around it. An answer whose key is not the asked one is treated as <see cref="SalesDispatchWeighingDifferenceStatusV1.Unavailable"/>.
/// </summary>
public interface ISalesDispatchWeighingDifferenceReadPortV1
{
    Task<SalesDispatchWeighingDifferenceReadV1> ReadAsync(Guid companyId, Guid dispatchNoteId, CancellationToken cancellationToken = default);
}

/// <summary>The read's state on the wire.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SalesDispatchWeighingDifferenceStateView>))]
public enum SalesDispatchWeighingDifferenceStateView
{
    Recorded = 1,
    NotRecorded = 2,
}

/// <summary>Why an invoice of the note waits on its weighing difference (the same codes as the invoice reasons).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SalesDispatchWeighingInvoiceWaitView>))]
public enum SalesDispatchWeighingInvoiceWaitView
{
    /// <summary>SAT-D389 / SAT-D405: no weighing-difference record — the note cannot be invoiced.</summary>
    NotRecorded = 1,

    /// <summary>SAT-D319 (6): above the rate the sale waits for the approval.</summary>
    PendingApproval = 2,

    /// <summary>
    /// SAT-D320 (3) / SAT-D322 (1): the sale passes at the scale kilograms, but the per-line scale kilograms are not readable from
    /// the owner yet (the V1 read carries the note totals only) — the invoice waits rather than guess a split.
    /// </summary>
    KgTransferPending = 3,
}

/// <summary>
/// The note's weighing-difference summary as the screen shows it: the note totals (theoretical, scale, signed difference) and the
/// SAT-D343 total excess (only kilograms leaving above the system stock, products never offsetting each other), the owner's
/// decision, state and band, and whether an invoice of the note waits and why. Kilograms are invariant-culture texts (≤ 3 decimals).
/// </summary>
public sealed record SalesDispatchWeighingDifferenceView(
    Guid DispatchNoteId,
    Guid CompanyId,
    SalesDispatchWeighingDifferenceStateView State,
    Guid? RecordId,
    string? TheoreticalKg,
    string? ScaleKg,
    string? DifferenceKg,
    string? TotalExcessKg,
    SalesWeighingDifferenceBandView? BandLevel,
    SalesWeighingDifferenceDecisionView? Decision,
    SalesWeighingDifferenceRecordStateView? RecordState,
    bool SaleMayProceed,
    SalesDispatchWeighingInvoiceWaitView? InvoiceWait,
    int? Version);

/// <summary>The weighing-difference read (implemented by the dispatch read service under the API-30 read name).</summary>
public interface ISalesDispatchWeighingDifferenceReadService
{
    Task<SalesDispatchWeighingDifferenceView> GetWeighingDifferenceAsync(Guid dispatchNoteId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The one rule both the screen read and the invoice gate apply to the seam's answer, so the two never disagree. Returns null when
/// nothing waits; <see cref="SalesDispatchWeighingDifferenceStatusV1.Unavailable"/> is not mapped here (each caller fails closed).
/// </summary>
public static class SalesDispatchWeighingInvoiceRule
{
    public static SalesDispatchWeighingInvoiceWaitView? Wait(SalesDispatchWeighingDifferenceReadV1 answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        if (answer.Status == SalesDispatchWeighingDifferenceStatusV1.NotRecorded)
        {
            return SalesDispatchWeighingInvoiceWaitView.NotRecorded;
        }

        var value = answer.Value ?? throw new InvalidOperationException("A recorded weighing difference carries its facts.");
        if (!value.SaleMayProceed || value.State == SalesWeighingDifferenceRecordStateView.PendingApproval)
        {
            return SalesDispatchWeighingInvoiceWaitView.PendingApproval;
        }

        // SAT-D322 (1): the invoice passes at the scale kilograms. With no difference the note kilograms are the scale kilograms;
        // any difference needs the owner's per-line scale kilograms, which the V1 read does not carry yet.
        return value.DifferenceKg == 0m && value.TotalExcessKg == 0m ? null : SalesDispatchWeighingInvoiceWaitView.KgTransferPending;
    }
}
