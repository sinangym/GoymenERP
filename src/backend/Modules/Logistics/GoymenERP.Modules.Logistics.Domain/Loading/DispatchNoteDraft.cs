using GoymenERP.Modules.Logistics.Domain.Quantities;

namespace GoymenERP.Modules.Logistics.Domain.Loading;

/// <summary>
/// The only dispatch-note state decided so far. Finalising (kesinleştirme), cancelling or deleting a draft, an official
/// number and any e-İrsaliye submission are not decided (DEC-INT-001 / OPEN-016: no official e-document integration in the
/// first release), so no other value exists.
/// </summary>
public enum DispatchNoteStatus
{
    Draft = 1,
}

/// <summary>
/// One line of a dispatch-note draft: exactly one definite shipment of the instruction and its shipped quantity, as recorded
/// (never edited, never split). <paramref name="InstructionLineId"/> is the instruction line the shipment belongs to, so the
/// Sales order line binding (D4), product and unit are that line's.
/// </summary>
public sealed record DispatchNoteLine(int LineNo, Guid ShipmentId, Guid InstructionLineId, Quantity Quantity);

/// <summary>
/// A sales dispatch-note (irsaliye) DRAFT prepared from the definite shipments of one loading instruction (master v3 §7.8:
/// "fark kontrolü → irsaliye taslağı → onay/kesim"; L-08 Satış İrsaliyesi Hazırlığı — "gerçekleşmeden belgeye"). One
/// instruction is one company and one customer, so a draft never mixes customers (dispatch spec: "farklı cariler için ayrı
/// irsaliye"). Its lines are the chosen shipments' shipped quantities only — no instructed, loaded or delivered quantity and
/// no price. AT-055: a shipment is linked to at most one dispatch note (the aggregate refuses a second link; SQL Server
/// holds the same rule as a unique key). No number, no finalisation, no cancellation and no official submission exist yet.
/// </summary>
/// <param name="SharesBefore">How many shares the instruction had when the draft was made; the rebuild replays it at that point.</param>
/// <param name="CreatedBy">The authenticated actor who prepared it.</param>
public sealed record DispatchNoteDraft(
    Guid DispatchNoteId,
    DispatchNoteStatus Status,
    IReadOnlyList<DispatchNoteLine> Lines,
    int SharesBefore,
    string CreatedBy,
    DateTimeOffset CreatedAtUtc);
