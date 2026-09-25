using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Engines;
using GoymenERP.Modules.Manufacturing.Domain.Engines.Balance;

namespace GoymenERP.Modules.Manufacturing.Domain.Orders;

/// <summary>
/// The mass account of one parent roll on one order:
/// <c>issued − returned = Σ outputs (by class) + WIP + unexplained</c>.
/// <para>
/// <see cref="Unexplained"/> is what the declarations do not account for, with its sign: a
/// positive value is mass nobody has declared yet, a negative one is more declared than was
/// issued. It is never folded into <see cref="OutputClass.ProcessLoss"/> or any other class —
/// "açıklanamayan fark otomatik fire olmaz" (plan v2 §7.3). Whether a difference is inside an
/// approved tolerance is Ö7's process and needs an approved profile; this slice only measures.
/// </para>
/// <para>
/// The difference is master v3 §28.9 BAL-003, computed by the one mass-balance calculator
/// (<see cref="MassBalanceEngineV1"/>, <c>GDC-BAL/1</c>) that the close, the order read and any report use. The
/// order's facts map onto its categories one to one: net issued → actual input; good product, second
/// quality, usable remnant (the recoverable by-product), scrap and process loss (the non-stock process loss)
/// → their classes; the latest WIP reading → remaining WIP. The approved measurement correction is the sum
/// of the order's recorded corrections: the order has no correction fact yet, so that sum is 0 — a fact of
/// the data, not a default.
/// </para>
/// </summary>
public sealed record MassBalance(
    Guid ParentRollId,
    Kilograms Issued,
    Kilograms Returned,
    IReadOnlyDictionary<OutputClass, Kilograms> OutputsByClass,
    Kilograms WorkInProgress)
{
    /// <summary>Σ of the order's recorded approved measurement corrections for this roll: none can be recorded yet.</summary>
    public static Kilograms RecordedMeasurementCorrections => Kilograms.Zero;

    public Kilograms NetIssued => new(Issued.Value - Returned.Value);

    public Kilograms DeclaredOutputs =>
        OutputsByClass.Values.Aggregate(Kilograms.Zero, (sum, mass) => sum + mass);

    /// <summary>BAL-003 for this roll, through <see cref="MassBalanceEngineV1"/>.</summary>
    public MassDifferenceV1 Reconciliation
    {
        get
        {
            var result = MassBalanceEngineV1.Difference(new MassDifferenceInputV1(
                NetIssued.Value,
                OutputOf(OutputClass.GoodProduct).Value,
                OutputOf(OutputClass.SecondQuality).Value,
                OutputOf(OutputClass.UsableRemnant).Value,
                OutputOf(OutputClass.Scrap).Value,
                OutputOf(OutputClass.ProcessLoss).Value,
                WorkInProgress.Value,
                RecordedMeasurementCorrections.Value));

            // Every input is a Kilograms (never negative, at most three decimals): the engine cannot refuse them.
            return result.Outcome == EngineOutcome.Computed
                ? result.Value!
                : throw new InvalidOperationException(
                    $"GDC-BAL/1 refused an order's own masses ({result.RefusalCode} on {result.RefusedParameter}).");
        }
    }

    public decimal Unexplained => Reconciliation.DifferenceKg;

    public bool IsFullyAccounted => Reconciliation.IsFullyAccounted;

    public Kilograms OutputOf(OutputClass outputClass) =>
        OutputsByClass.TryGetValue(outputClass, out var mass) ? mass : Kilograms.Zero;
}
