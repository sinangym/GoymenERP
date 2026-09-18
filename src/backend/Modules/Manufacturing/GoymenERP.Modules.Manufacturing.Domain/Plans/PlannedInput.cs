using GoymenERP.Modules.Manufacturing.Domain.Common;

namespace GoymenERP.Modules.Manufacturing.Domain.Plans;

/// <summary>
/// A parent roll the plan intends to process, and how much of it. The roll is the Inventory
/// label's identity; this module keeps only its id.
/// </summary>
public sealed record PlannedInput
{
    public PlannedInput(Guid parentRollId, Kilograms plannedMass)
    {
        if (parentRollId == Guid.Empty)
        {
            throw new ArgumentException("Ana rulo kimliği boş olamaz.", nameof(parentRollId));
        }

        if (plannedMass.Value == 0m)
        {
            throw new ArgumentException("Planlanan kütle sıfırdan büyük olmalıdır.", nameof(plannedMass));
        }

        ParentRollId = parentRollId;
        PlannedMass = plannedMass;
    }

    public Guid ParentRollId { get; }

    public Kilograms PlannedMass { get; }
}
