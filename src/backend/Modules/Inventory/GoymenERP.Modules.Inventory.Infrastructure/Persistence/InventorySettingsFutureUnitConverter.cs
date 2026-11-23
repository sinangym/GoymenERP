using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// KLN-D66 (F-017 unit conversion) over the Settings-owned unit identities (<see cref="ISettingsUnitIdentityCatalogV1"/>). It
/// converts only what it can prove without a product weight: the same unit, and a unit Settings marks as the kilogram
/// (<see cref="UnitIdentityV1.IsKilogram"/>), which is kilograms as is. Every other pair answers
/// <see cref="InventoryFutureUnitConversionOutcomeV1.NoSource"/> and the caller refuses 422 (KLN-D66 (3), fail-closed):
/// <list type="bullet">
/// <item>ton↔kg is a fixed factor (KLN-D66 (2)), but Settings has no ton identity yet, so no unit id can be proven to be the ton;</item>
/// <item>piece/metre↔kg needs the product card's weight (kg/m, dimensions; URN-D11/URN-D16) through the shared deterministic
/// unit calculator (KLN-D66 (4)); Product exposes no conversion port Inventory may call, and Settings has no metre or piece
/// identity either.</item>
/// </list>
/// When those ports exist, a converter that calls them replaces this one (the seam is <see cref="IInventoryFutureUnitConverterV1"/>);
/// nothing here re-implements a conversion formula. An unreadable identity is NoSource as well, never a guess.
/// </summary>
public sealed class InventorySettingsFutureUnitConverterV1(ISettingsUnitIdentityCatalogV1 units) : IInventoryFutureUnitConverterV1
{
    public async Task<InventoryFutureUnitConversionV1> ConvertAsync(Guid productId, string productVersion, decimal quantity,
        Guid fromUnitId, Guid toUnitId, CancellationToken cancellationToken = default)
    {
        if (fromUnitId == Guid.Empty || toUnitId == Guid.Empty)
        {
            return NoSource;
        }

        if (fromUnitId == toUnitId)
        {
            return Converted(quantity);
        }

        return await IsKilogramAsync(fromUnitId, cancellationToken) && await IsKilogramAsync(toUnitId, cancellationToken)
            ? Converted(quantity)
            : NoSource;
    }

    public async Task<InventoryFutureUnitConversionV1> ConvertToKilogramsAsync(Guid productId, string productVersion,
        decimal quantity, Guid fromUnitId, CancellationToken cancellationToken = default) =>
        fromUnitId != Guid.Empty && await IsKilogramAsync(fromUnitId, cancellationToken) ? Converted(quantity) : NoSource;

    private static InventoryFutureUnitConversionV1 NoSource { get; } =
        new(InventoryFutureUnitConversionOutcomeV1.NoSource, null);

    private static InventoryFutureUnitConversionV1 Converted(decimal quantity) =>
        new(InventoryFutureUnitConversionOutcomeV1.Converted, quantity);

    private async Task<bool> IsKilogramAsync(Guid unitId, CancellationToken cancellationToken)
    {
        try
        {
            return await units.ReadAsync(unitId, cancellationToken) is { } identity
                && identity.UnitId == unitId && identity.IsKilogram;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A malformed Settings source is refused by Settings itself; here it only means "not provably the kilogram".
            return false;
        }
    }
}
