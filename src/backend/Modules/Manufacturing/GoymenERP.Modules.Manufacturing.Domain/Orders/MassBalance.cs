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
/// <para>
/// Every mass here is weighed kg (URT-D50, URT-D51 (2)): an input is a parent roll or label issued and returned in kg,
/// and every output — a coil, a strip or a package of pieces or metres (tube and profile especially) — is declared with
/// its weighed <see cref="Kilograms"/>, so a piece or metre output enters the comparison as its weighed kg equivalent.
/// An input consumed in pieces, litres or metres (paint litres and the like) is not an order input and never enters
/// this account (URT-D50 (1)); its cost stays outside the product as before (URT-D45 (1)). The theoretical weight
/// (pieces × the card's kg per piece) is not read here: it is a helper with a Settings deviation warning
/// (URT-D51 (3), its own slice), never the checked mass.
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

    /// <summary>
    /// URT-D41 (1), URT-D42 (1): more mass is accounted on this parent roll than was issued to it (BAL-003 below zero).
    /// The one definition the close (<see cref="OrderClosureAssessmentCalculatorV1"/>) and the read-only report of
    /// earlier closes use per roll; a process without a main roll input compares the order total instead
    /// (<see cref="OrderMassTotalV1"/>, URT-D49 (1)). "Output" here is the whole BAL-003 accounted mass: every output
    /// class, usable remnant included, the latest WIP reading and the recorded corrections (none of which can be
    /// recorded yet) all count on the output side (URT-D48 (1)-(2): the code already read it so; no change).
    /// </summary>
    public bool OutputExceedsInput => Unexplained < 0m;

    public Kilograms OutputOf(OutputClass outputClass) =>
        OutputsByClass.TryGetValue(outputClass, out var mass) ? mass : Kilograms.Zero;
}
