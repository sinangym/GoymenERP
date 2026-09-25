using GoymenERP.Modules.Manufacturing.Domain.Common;

namespace GoymenERP.Modules.Manufacturing.Domain.Plans;

/// <summary>
/// A parent roll the plan intends to process, and how much of it. The roll is the Inventory
/// label's identity; this module keeps only its id.
/// </summary>
public sealed record PlannedInput
{
    public PlannedInput(Guid parentRollId, Kilograms plannedMass, long? expectedLabelVersion = null)
    {
        if (parentRollId == Guid.Empty)
        {
            throw new ArgumentException("Ana rulo kimliği boş olamaz.", nameof(parentRollId));
        }

        if (plannedMass.Value == 0m)
        {
            throw new ArgumentException("Planlanan kütle sıfırdan büyük olmalıdır.", nameof(plannedMass));
        }

        if (expectedLabelVersion is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedLabelVersion), expectedLabelVersion,
                "Etiket sürümü pozitif olmalıdır.");
        }

        ParentRollId = parentRollId;
        PlannedMass = plannedMass;
        ExpectedLabelVersion = expectedLabelVersion;
    }

    public Guid ParentRollId { get; }

    public Kilograms PlannedMass { get; }

    /// <summary>
    /// Inventory'nin plan kaydında doğruladığı etiket sürümü. Eski kayıtlar null kalır ve fiziksel
    /// gerçekleşmeye katılamaz; yeni planlar bu sürümü emir snapshot'ına taşır.
    /// </summary>
    public long? ExpectedLabelVersion { get; }
}
