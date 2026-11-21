using System.Globalization;

namespace GoymenERP.Modules.Manufacturing.Application;

/// <summary>
/// URT-D35: Inventory's refusal-time figures, carried unchanged. A fresh advisory read cannot replace this snapshot.
/// Free physical and reserved kg are deliberately not added to this public refusal contract.
/// </summary>
public sealed record ManufacturingPlanAllocationShortfallV1
{
    public ManufacturingPlanAllocationShortfallV1(Guid companyId, Guid planId, Guid labelId, Guid productId,
        string labelVersion, string requestedKg, string availableKg, string missingKg, int freePositionCount)
    {
        if (companyId == Guid.Empty || planId == Guid.Empty || labelId == Guid.Empty || productId == Guid.Empty)
            throw new ArgumentException("Allocation refusal identities must be present.");
        if (!long.TryParse(labelVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version < 1
            || version.ToString(CultureInfo.InvariantCulture) != labelVersion)
            throw new ArgumentException("The observed label version must be canonical.", nameof(labelVersion));
        if (freePositionCount != 1) throw new ArgumentOutOfRangeException(nameof(freePositionCount));
        RequireKg(requestedKg, true); RequireKg(availableKg, false); RequireKg(missingKg, true);
        CompanyId = companyId; PlanId = planId; LabelId = labelId; ProductId = productId; LabelVersion = labelVersion;
        RequestedKg = requestedKg; AvailableKg = availableKg; MissingKg = missingKg; FreePositionCount = freePositionCount;
    }
    public Guid CompanyId { get; }
    public Guid PlanId { get; }
    public Guid LabelId { get; }
    public Guid ProductId { get; }
    public string LabelVersion { get; }
    public string RequestedKg { get; }
    public string AvailableKg { get; }
    public string MissingKg { get; }
    public int FreePositionCount { get; }

    private static void RequireKg(string value, bool positive)
    {
        if (!decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var kg)
            || kg < 0m || positive && kg == 0m || kg.ToString("0.###", CultureInfo.InvariantCulture) != value)
            throw new ArgumentException("Owner kg text must be canonical and non-negative.", nameof(value));
    }
}
