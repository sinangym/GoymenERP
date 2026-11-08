using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Sales.Application.Contracts;

// API-37 (Satış hazırlık planı v1 §3.3): Kontrol Merkezi — the read-only list of what needs a person's attention among the sales
// invoices and the shipments waiting for one: SAT-D95 (an uninvoiced shipment past the grace days), EK42 SAT-D223…D226 (a TL
// amount the group difference flipped or exceeded is flagged, never corrected) and SAT-D7/D86/D90/D172/D225/D229 (the official
// document comparison). It reads only; it writes nothing in any module and carries no money.

/// <summary>The wire constants of the Kontrol Merkezi read.</summary>
public static class SalesControlCenterApiV1
{
    public const string Path = "/api/v1/sales/control-center";

    /// <summary>
    /// SAT-D95 starting value (Sinan, 2026-10-28: 7 days from the shipment date, VUK 231/5). The Ayarlar-owned editable record is
    /// not on main (SAT-D209), so the Sales-owned <see cref="SalesControlCenterPolicy"/> carries this default until that lane binds
    /// it. OPEN-SAT-D95-SETTINGS: the Ayarlar binding is not decided here.
    /// </summary>
    public const int DefaultUninvoicedDispatchGraceDays = 7;

    /// <summary>Technical read bound (not a business threshold): invoices are read newest first in pages of this size.</summary>
    public const int InvoicePageSize = 100;

    /// <summary>Technical read bound: the most invoice pages one answer reads; beyond it the invoice checks say <c>truncated</c>.</summary>
    public const int MaximumInvoicePages = 20;
}

/// <summary>
/// The Sales-owned configuration of the read: the days after the shipment date an uninvoiced shipment is overdue (SAT-D95). A
/// value below 1 is a setup error and is refused at construction.
/// </summary>
public sealed record SalesControlCenterPolicy
{
    public SalesControlCenterPolicy(int uninvoicedDispatchGraceDays = SalesControlCenterApiV1.DefaultUninvoicedDispatchGraceDays)
    {
        UninvoicedDispatchGraceDays = uninvoicedDispatchGraceDays >= 1
            ? uninvoicedDispatchGraceDays
            : throw new ArgumentOutOfRangeException(nameof(uninvoicedDispatchGraceDays));
    }

    public int UninvoicedDispatchGraceDays { get; }
}

/// <summary>
/// What a check looks at. <see cref="OfficialDocumentDeviation"/> lists the recorded API-35 TL differences (SAT-D229); the due
/// date and rate comparison (SAT-D86) has no recorded source and is not claimed.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<SalesControlCenterCheckKindView>))]
public enum SalesControlCenterCheckKindView
{
    /// <summary>SAT-D95: an approved shipment on no active invoice, older than the grace days.</summary>
    UninvoicedDispatchOverdue = 1,

    /// <summary>EK42: an active invoice with a line whose TL amount the group difference flipped or exceeded.</summary>
    InvoiceNeedsReview = 2,

    /// <summary>SAT-D7: an approved invoice that the official document is not matched to.</summary>
    InvoiceAwaitingOfficialMatch = 3,

    /// <summary>SAT-D229: a matched invoice whose recorded TL difference against the official document is not zero.</summary>
    OfficialDocumentDeviation = 4,
}

/// <summary>The result classes of the dispatch/invoice design spec §3.5; all four are produced.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SalesControlCenterSeverityView>))]
public enum SalesControlCenterSeverityView
{
    Critical = 1,
    ApprovalRequired = 2,
    WithinTolerance = 3,
    Info = 4,
}

/// <summary>Whether a check was measured: never a silent empty list.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SalesControlCenterCheckStateView>))]
public enum SalesControlCenterCheckStateView
{
    /// <summary>The source answered; an empty item list is a recorded "nothing".</summary>
    Measured = 1,

    /// <summary>No source exists on main for the check's "already settled" half; the items listed are everything the check can see.</summary>
    NoSource = 2,

    /// <summary>The owner is not composed, refused, failed or answered something malformed: nothing is claimed.</summary>
    Unavailable = 3,
}

/// <summary>One check's state, and whether a technical read bound cut its source (the list may then be incomplete).</summary>
public sealed record SalesControlCenterCheckView(
    SalesControlCenterCheckKindView Kind,
    SalesControlCenterCheckStateView State,
    bool Truncated);

/// <summary>
/// One entry. <c>reference</c> is the dispatch note's system number or the invoice's document number; <c>referenceDate</c> is the
/// shipment day or the invoice's business day (Europe/Istanbul) and <c>ageDays</c> the whole days from it to today. <c>count</c> is
/// the number of lines it concerns (1 for a shipment). Identities the kind does not have are null. No price, amount or total.
/// </summary>
public sealed record SalesControlCenterItemView(
    SalesControlCenterCheckKindView Kind,
    SalesControlCenterSeverityView Severity,
    Guid PartyId,
    Guid CompanyAccountId,
    string Reference,
    DateOnly ReferenceDate,
    int AgeDays,
    int Count,
    Guid? InvoiceId,
    Guid? DispatchNoteId,
    Guid? ShipmentId,
    Guid? SalesOrderId);

/// <summary>API-37 answer: today (business day), the grace days applied, one check state per kind, and the items (most severe, then oldest, first).</summary>
public sealed record SalesControlCenterResponse(
    DateOnly BusinessDate,
    int UninvoicedDispatchGraceDays,
    IReadOnlyList<SalesControlCenterCheckView> Checks,
    IReadOnlyList<SalesControlCenterItemView> Items);

/// <summary>API-37 use case (read-only). Needs <c>sales.invoices.read</c> only: nothing it answers is a price.</summary>
public interface ISalesControlCenterService
{
    Task<SalesControlCenterResponse> GetControlCenterAsync(CancellationToken cancellationToken = default);
}
