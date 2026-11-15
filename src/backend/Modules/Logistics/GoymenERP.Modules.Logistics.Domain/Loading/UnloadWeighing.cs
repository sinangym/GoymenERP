using GoymenERP.Modules.Logistics.Domain.Quantities;

namespace GoymenERP.Modules.Logistics.Domain.Loading;

/// <summary>
/// LOJ-D17 (1)–(2), (4): the unload weighing (indirme tartısı) of a cancelled instruction's vehicle — the goods loaded on it
/// are taken off again after the cancel and the vehicle is weighed by hand (no weighbridge hardware or integration). Facts only:
/// the two weighings as entered, the direction, the net the backend calculated with the same <see cref="WeighbridgeCalculator"/>
/// as the loading weighing (WGT-001), the kilograms loaded on the instruction when it was cancelled, and the signed difference
/// net − loaded (WGT-002). A difference is a warning written to the audit trail; it never refuses or undoes the cancel. Not a
/// stock movement: the goods never left the warehouse (no shipment was recorded), so Inventory is never written.
/// </summary>
/// <param name="LoadedAtCancel">The kilograms loaded on the instruction's lines when it was cancelled (nothing moves after a cancel).</param>
/// <param name="RecordedBy">The authenticated actor who entered it (the operator).</param>
public sealed record UnloadWeighing(
    Guid WeighingId,
    WeighingDirection Direction,
    Quantity FirstWeighing,
    Quantity SecondWeighing,
    Quantity Net,
    Quantity LoadedAtCancel,
    decimal DifferenceKg,
    string RecordedBy,
    DateTimeOffset RecordedAtUtc)
{
    public Quantity Tare => Direction == WeighingDirection.TareFirst ? FirstWeighing : SecondWeighing;

    public Quantity Gross => Direction == WeighingDirection.TareFirst ? SecondWeighing : FirstWeighing;

    /// <summary>
    /// LOJ-D17 (2): the difference warns. No difference threshold is configured — LOJ-D17 leaves the threshold value unchosen and
    /// a threshold would come from Settings under a separate user decision — so, as LOJ-D17 says for "no threshold", every
    /// non-zero difference warns.
    /// </summary>
    public bool DifferenceWarning => DifferenceKg != 0m;
}

/// <summary>
/// LOJ-D17 (3): whether a cancelled instruction's unload is recorded. <see cref="Missing"/> is the "indirme kaydı yok" mark:
/// the instruction is cancelled, goods were loaded on it and no unload weighing is recorded — the cancel stays valid.
/// </summary>
public enum UnloadRecordState
{
    /// <summary>Not cancelled, or cancelled with nothing loaded: there is nothing to unload.</summary>
    NotApplicable = 0,

    /// <summary>Cancelled with goods loaded and no unload weighing: "indirme kaydı yok".</summary>
    Missing = 1,

    /// <summary>The unload weighing is recorded.</summary>
    Recorded = 2,
}
