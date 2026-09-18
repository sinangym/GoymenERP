using GoymenERP.Modules.Manufacturing.Domain.Common;

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
/// </summary>
public sealed record MassBalance(
    Guid ParentRollId,
    Kilograms Issued,
    Kilograms Returned,
    IReadOnlyDictionary<OutputClass, Kilograms> OutputsByClass,
    Kilograms WorkInProgress)
{
    public Kilograms NetIssued => new(Issued.Value - Returned.Value);

    public Kilograms DeclaredOutputs =>
        OutputsByClass.Values.Aggregate(Kilograms.Zero, (sum, mass) => sum + mass);

    public decimal Unexplained => NetIssued.Value - DeclaredOutputs.Value - WorkInProgress.Value;

    public bool IsFullyAccounted => Unexplained == 0m;

    public Kilograms OutputOf(OutputClass outputClass) =>
        OutputsByClass.TryGetValue(outputClass, out var mass) ? mass : Kilograms.Zero;
}
