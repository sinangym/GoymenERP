using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Modules.Manufacturing.Domain.Orders;

/// <summary>
/// How much of the plan's declared target is still open. Without a declared target there is no
/// answer — <see cref="IsAvailable"/> is false and every figure is null — rather than a guess at
/// which quantity was meant.
/// <para>
/// <see cref="Remaining"/> keeps its sign: a negative value means the target was exceeded, which
/// is reported, not corrected.
/// </para>
/// </summary>
public sealed record RemainingTarget
{
    private RemainingTarget(TargetBasis? basis, Kilograms? target, Kilograms? achieved)
    {
        Basis = basis;
        Target = target;
        Achieved = achieved;
    }

    public static RemainingTarget Unavailable { get; } = new(null, null, null);

    public bool IsAvailable => Basis is not null;

    public TargetBasis? Basis { get; }

    public Kilograms? Target { get; }

    public Kilograms? Achieved { get; }

    public decimal? Remaining => Target is null || Achieved is null ? null : Target.Value.Value - Achieved.Value.Value;

    internal static RemainingTarget Of(ProductionTarget target, Kilograms achieved) =>
        new(target.Basis, target.Mass, achieved);
}
