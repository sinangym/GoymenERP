using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>Host composition of existing Settings profile and Organization location identities.
/// Called only after validating the actual purchase cost source inside its owner transaction.</summary>
public interface IInventoryFirstCostPrerequisitesV1
{
    Task<InventoryFirstCostPrerequisitesV1> ReadAsync(Guid companyId, DateOnly effectiveOn,
        IReadOnlyCollection<Guid> locationIds, CancellationToken cancellationToken = default);
}

public sealed record InventoryFirstCostPrerequisitesV1(MoneyProfileV1 Profile, IReadOnlyDictionary<Guid, string> LocationCodes);
