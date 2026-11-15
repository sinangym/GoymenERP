namespace GoymenERP.Modules.Logistics.Application.Contracts;

/// <summary>
/// F1 (STK-S4; LOJ-D9, SAT-D309, plan L-8): the Logistics-owned read of a loading instruction's delivery weighing — the
/// net kilograms the manual weighbridge record (AT-053, WGT-001) holds, keyed by <c>(loadingInstructionId, weighingId)</c> under
/// the server-resolved company. The delivery weighing is the instruction's single weighing record (L-8); a fason delivery
/// without a weighing cannot be recorded (LOJ-D12), so a missing record is <see cref="LogisticsSubcontractDeliveryNetWeightStatusV1.NoSource"/>,
/// never zero.
/// <para>
/// Facts only. Logistics does not split, round or price: the proportional split by declared kilograms (SAT-D312, LOJ-D12),
/// its residue and the money live only in the Sales invoice calculator, and which line quantity is the split denominator is
/// the consumer's documented choice, not this port's. The answer therefore carries every per-line fact the split may need —
/// the kilograms loaded at weighing time, the loaded, shipped and delivered totals, and each shipment share — without
/// preferring one. Scrap is not billed (LOJ-D9); this port carries no scrap fact and invoices nothing.
/// </para>
/// <para>
/// The port is company-filtered, untracked and side-effect free: it creates no attempt, audit, share or version effect and
/// carries no authority (LOG-4 D6) — the consumer measures its own permission around the call. Without a resolved company it
/// throws <see cref="InvalidOperationException"/> (never reads unscoped). A <c>companyId</c> other than the context company,
/// or an instruction of another company, answers <see cref="LogisticsSubcontractDeliveryNetWeightStatusV1.NoSource"/> — never
/// that company's data, never a 403. A stored record that cannot be rebuilt consistently answers
/// <see cref="LogisticsSubcontractDeliveryNetWeightStatusV1.Unavailable"/> (fail closed).
/// </para>
/// <para>
/// Corrections (LOJ-D13: a corrected weighing with its reason; after invoicing a Sales difference invoice) are not modelled
/// yet: the store keeps one append-only record per instruction, so every available answer is
/// <see cref="LogisticsSubcontractDeliveryNetWeightContractV1.OriginalWeighingRevision"/>. The revision field is in the
/// contract now so that a later correction raises it without a new contract version.
/// </para>
/// </summary>
public interface ILogisticsSubcontractDeliveryNetWeightReadPortV1
{
    Task<LogisticsSubcontractDeliveryNetWeightReadV1> GetAsync(Guid companyId, Guid loadingInstructionId, Guid weighingId,
        CancellationToken cancellationToken = default);
}

/// <summary>Stable names and constants of the F1 net-weight read contract.</summary>
public static class LogisticsSubcontractDeliveryNetWeightContractV1
{
    /// <summary>The versioned contract name.</summary>
    public const string ContractName = "logistics.subcontract-delivery-net-weight.v1";

    /// <summary>The unit every kilogram text of this contract is in.</summary>
    public const string KilogramUnit = "kg";

    /// <summary>The revision of an uncorrected weighing record (the only kind the store holds until LOJ-D13 is built).</summary>
    public const int OriginalWeighingRevision = 1;
}

/// <summary>The fail-closed status of one net-weight read; a consumer treats anything but <see cref="Available"/> as "no net kilograms".</summary>
public enum LogisticsSubcontractDeliveryNetWeightStatusV1
{
    /// <summary>The weighing record exists, matches the key and is consistent; <see cref="LogisticsSubcontractDeliveryNetWeightReadV1.Value"/> is set.</summary>
    Available = 1,

    /// <summary>No source fact for the key: the instruction is not the context company's (or does not exist), it has no weighing, the weighing id differs, or the instruction is cancelled.</summary>
    NoSource = 2,

    /// <summary>The stored record cannot be read consistently (corrupt or contradictory rows); nothing is answered.</summary>
    Unavailable = 3,
}

/// <summary>How the net kilograms were measured. Today only the hand-entered weighbridge record exists (OPEN-003 closed: manual entry).</summary>
public enum LogisticsNetWeightMeasurementKindV1
{
    ManualWeighbridge = 1,
}

/// <summary>Machine-readable reasons of a non-available answer (stable codes; no user text).</summary>
public static class LogisticsSubcontractDeliveryNetWeightReasonCodesV1
{
    public const string InstructionNotFound = "logistics_net_weight_instruction_not_found";
    public const string WeighingNotRecorded = "logistics_net_weight_weighing_not_recorded";
    public const string WeighingMismatch = "logistics_net_weight_weighing_mismatch";
    public const string InstructionCancelled = "logistics_net_weight_instruction_cancelled";
    public const string RecordUnreadable = "logistics_net_weight_record_unreadable";
    public const string RecordInconsistent = "logistics_net_weight_record_inconsistent";
}

/// <summary>
/// One answer of <see cref="ILogisticsSubcontractDeliveryNetWeightReadPortV1"/>. The key is echoed as asked;
/// <see cref="Value"/> is set only when <see cref="Status"/> is <see cref="LogisticsSubcontractDeliveryNetWeightStatusV1.Available"/>,
/// and <see cref="ReasonCode"/> only when it is not.
/// </summary>
public sealed record LogisticsSubcontractDeliveryNetWeightReadV1(
    LogisticsSubcontractDeliveryNetWeightStatusV1 Status,
    string? ReasonCode,
    Guid CompanyId,
    Guid LoadingInstructionId,
    Guid WeighingId,
    LogisticsSubcontractDeliveryNetWeightV1? Value)
{
    public static LogisticsSubcontractDeliveryNetWeightReadV1 NoSource(Guid companyId, Guid loadingInstructionId, Guid weighingId, string reasonCode) =>
        new(LogisticsSubcontractDeliveryNetWeightStatusV1.NoSource, reasonCode, companyId, loadingInstructionId, weighingId, null);

    public static LogisticsSubcontractDeliveryNetWeightReadV1 Unavailable(Guid companyId, Guid loadingInstructionId, Guid weighingId, string reasonCode) =>
        new(LogisticsSubcontractDeliveryNetWeightStatusV1.Unavailable, reasonCode, companyId, loadingInstructionId, weighingId, null);

    public static LogisticsSubcontractDeliveryNetWeightReadV1 Available(Guid companyId, Guid loadingInstructionId, Guid weighingId,
        LogisticsSubcontractDeliveryNetWeightV1 value) =>
        new(LogisticsSubcontractDeliveryNetWeightStatusV1.Available, null, companyId, loadingInstructionId, weighingId,
            value ?? throw new ArgumentNullException(nameof(value)));
}

/// <summary>
/// The facts of one available delivery weighing. Every kilogram value is an exact invariant decimal text in
/// <see cref="LogisticsSubcontractDeliveryNetWeightContractV1.KilogramUnit"/> with trailing zeros dropped (equal kilograms print
/// equally). <see cref="NetKg"/> is always positive.
/// </summary>
/// <param name="NetKg">The net the backend calculated from the two weighings and the direction (WGT-001): the fee kilograms of LOJ-D9.</param>
/// <param name="Direction">Which weighing was the tare (<c>TareFirst</c> / <c>GrossFirst</c>), as recorded.</param>
/// <param name="LoadedKgAtWeighing">The instruction's loaded kilograms when it was weighed (WGT-002 snapshot).</param>
/// <param name="DifferenceKg">The signed difference net − loaded at weighing time (WGT-002); no tolerance verdict (OPEN-004).</param>
/// <param name="WeighingRevision">The correction revision of the weighing record (LOJ-D13); <see cref="LogisticsSubcontractDeliveryNetWeightContractV1.OriginalWeighingRevision"/> until corrections exist.</param>
/// <param name="InstructionVersion">The instruction's stored version at read time; a consumer may keep it to notice a later change.</param>
/// <param name="Lines">Every line of the instruction, in line order, with its kilogram facts.</param>
/// <param name="Shipments">Every recorded shipment share (the declared kilograms per shipment line), in recording order.</param>
public sealed record LogisticsSubcontractDeliveryNetWeightV1(
    string NetKg,
    string Unit,
    LogisticsNetWeightMeasurementKindV1 MeasurementKind,
    string Direction,
    string LoadedKgAtWeighing,
    string DifferenceKg,
    int WeighingRevision,
    long InstructionVersion,
    DateTimeOffset RecordedAtUtc,
    IReadOnlyList<LogisticsSubcontractDeliveryNetWeightLineV1> Lines,
    IReadOnlyList<LogisticsSubcontractDeliveryShipmentV1> Shipments);

/// <summary>
/// One instruction line's kilogram facts. <see cref="LoadedKgAtWeighing"/> is the sum of the line's load shares recorded before
/// the weighing; the lines' values sum to <see cref="LogisticsSubcontractDeliveryNetWeightV1.LoadedKgAtWeighing"/>. The source
/// binding is the Sales order line the instruction line serves (identities only, LOG-4 D4).
/// </summary>
public sealed record LogisticsSubcontractDeliveryNetWeightLineV1(
    Guid LineId,
    Guid SourceOrderId,
    Guid SourceOrderLineId,
    int SourceOrderRevision,
    Guid ProductId,
    Guid UnitId,
    string LoadedKgAtWeighing,
    string LoadedKg,
    string ShippedKg,
    string DeliveredKg);

/// <summary>One recorded shipment share: the shipment identity, its instruction line and the declared (shipped) kilograms.</summary>
public sealed record LogisticsSubcontractDeliveryShipmentV1(
    Guid ShipmentId,
    Guid LineId,
    string ShippedKg,
    DateTimeOffset RecordedAtUtc);
