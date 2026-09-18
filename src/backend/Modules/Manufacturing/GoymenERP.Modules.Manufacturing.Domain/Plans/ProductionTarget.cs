using GoymenERP.Modules.Manufacturing.Domain.Common;

namespace GoymenERP.Modules.Manufacturing.Domain.Plans;

/// <summary>
/// What the plan's target counts. Plan v2 §7.3 allows a plan from an order's target quantity or
/// from raw material, so the plan must SAY which one it means; this module never picks a default
/// (coordinator decision on MFG-1, 2026-09-18).
/// </summary>
public enum TargetBasis
{
    /// <summary>Parent-roll mass to put into production.</summary>
    InputMass = 1,

    /// <summary>Good-product output mass to obtain.</summary>
    GoodOutputMass = 2,
}

public sealed record ProductionTarget
{
    public ProductionTarget(TargetBasis basis, Kilograms mass)
    {
        if (!Enum.IsDefined(basis))
        {
            throw new ArgumentOutOfRangeException(nameof(basis), basis, "Tanımsız hedef ölçüsü.");
        }

        if (mass.Value == 0m)
        {
            throw new ArgumentException("Hedef kütle sıfırdan büyük olmalıdır.", nameof(mass));
        }

        Basis = basis;
        Mass = mass;
    }

    public TargetBasis Basis { get; }

    public Kilograms Mass { get; }
}
