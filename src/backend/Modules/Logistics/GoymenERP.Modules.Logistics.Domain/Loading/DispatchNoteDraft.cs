using GoymenERP.Modules.Logistics.Domain.Quantities;

namespace GoymenERP.Modules.Logistics.Domain.Loading;

/// <summary>
/// The dispatch-note (irsaliye) lifecycle (user decision 2026-09-30): a <see cref="Draft"/> is approved (finalised) into an
/// <see cref="Approved"/> note that carries the company's gap-free system series number; an approved note is never deleted,
/// only <see cref="Cancelled"/>; a draft may be cancelled too. Cancelling releases the note's shipments, so they can be put on a
/// new note (correction = cancel + new draft). Official e-İrsaliye submission is not part of this (DEC-INT-001 / OPEN-016).
/// </summary>
public enum DispatchNoteStatus
{
    Draft = 1,
    Approved = 2,
    Cancelled = 3,
}

/// <summary>
/// One line of a dispatch note: exactly one definite shipment of the instruction and its shipped quantity, as recorded
/// (never edited, never split). <paramref name="InstructionLineId"/> is the instruction line the shipment belongs to, so the
/// Sales order line binding (D4), product and unit are that line's.
/// </summary>
public sealed record DispatchNoteLine(int LineNo, Guid ShipmentId, Guid InstructionLineId, Quantity Quantity);

/// <summary>
/// The system (ERP) number of an approved dispatch note: the company's series and its gap-free sequence, assigned once on
/// approval in the same save (never reused, never renumbered; a cancelled note keeps it). Separate from the official
/// e-İrsaliye number, which is entered by hand (or by a later integration) and never derived from this one.
/// </summary>
public sealed record DispatchNoteNumber(string SeriesCode, long SequenceNo)
{
    /// <summary>The default series of a company (ASSUMPTION-AT055-SERIES: one series per company until a series setting exists).</summary>
    public const string DefaultSeriesCode = "IRS";

    public const int SeriesCodeMaximumLength = 8;

    /// <summary>The printed number: series, a dash and the sequence, at least six digits ("IRS-000001").</summary>
    public string Text => $"{SeriesCode}-{SequenceNo.ToString("D6", System.Globalization.CultureInfo.InvariantCulture)}";

    public static DispatchNoteNumber Of(string seriesCode, long sequenceNo)
    {
        if (string.IsNullOrWhiteSpace(seriesCode) || seriesCode.Length > SeriesCodeMaximumLength || seriesCode != seriesCode.Trim())
        {
            throw new ArgumentException("İrsaliye serisi boş olamaz ve en fazla sekiz karakterdir.", nameof(seriesCode));
        }

        return sequenceNo < 1
            ? throw new ArgumentOutOfRangeException(nameof(sequenceNo), "İrsaliye sıra numarası birden başlar.")
            : new DispatchNoteNumber(seriesCode, sequenceNo);
    }
}

/// <summary>
/// A sales dispatch note (irsaliye) prepared from the definite shipments of one loading instruction (master v3 §7.8:
/// "fark kontrolü → irsaliye taslağı → onay/kesim"; L-08 Satış İrsaliyesi Hazırlığı — "gerçekleşmeden belgeye"). One
/// instruction is one company and one customer, so a note never mixes customers. Its lines are the chosen shipments' shipped
/// quantities only — no price. AT-055: a shipment is on at most one NON-CANCELLED note (the aggregate refuses a second link;
/// SQL Server holds the same rule as a filtered unique key over the lines that are not released). The note is immutable; each
/// transition replaces it in the instruction.
/// </summary>
/// <param name="SharesBefore">How many shares the instruction had when the draft was made; the rebuild replays it at that point.</param>
/// <param name="CreatedBy">The authenticated actor who prepared it.</param>
/// <param name="CreatedStep">The instruction's dispatch-note step (1, 2, …) that created it; every create/approve/cancel/e-number is one step.</param>
public sealed record DispatchNoteDraft(
    Guid DispatchNoteId,
    DispatchNoteStatus Status,
    IReadOnlyList<DispatchNoteLine> Lines,
    int SharesBefore,
    string CreatedBy,
    DateTimeOffset CreatedAtUtc,
    int CreatedStep = 1)
{
    /// <summary>The system number; set on approval (null on a draft and on a draft cancelled before approval).</summary>
    public DispatchNoteNumber? Number { get; init; }

    public string? ApprovedBy { get; init; }
    public DateTimeOffset? ApprovedAtUtc { get; init; }
    public int? ApprovedStep { get; init; }

    public string? CancelledBy { get; init; }
    public DateTimeOffset? CancelledAtUtc { get; init; }
    public string? CancelReason { get; init; }
    public int? CancelledStep { get; init; }

    /// <summary>The official e-İrsaliye number, entered by hand on an approved note (unique per company); never the system number.</summary>
    public string? EDispatchNumber { get; init; }
    public string? EDispatchNumberSetBy { get; init; }
    public DateTimeOffset? EDispatchNumberSetAtUtc { get; init; }
    public int? EDispatchNumberStep { get; init; }

    /// <summary>A cancelled note holds none of its shipments any more.</summary>
    public bool HoldsShipments => Status != DispatchNoteStatus.Cancelled;
}
