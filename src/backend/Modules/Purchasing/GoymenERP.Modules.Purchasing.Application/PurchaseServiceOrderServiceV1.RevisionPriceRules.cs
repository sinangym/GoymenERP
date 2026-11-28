using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Modules.Purchasing.Application;

public sealed partial class PurchaseServiceOrderServiceV1
{
    // The native RevisePrices input validator, shared by the same owner participant; no new price/zero policy.
    public static IReadOnlyDictionary<Guid, decimal> ReadNativeRevisionPrices(IReadOnlyList<PurchaseServiceLinePriceV1> lines) =>
        new System.Collections.ObjectModel.ReadOnlyDictionary<Guid, decimal>(lines.ToDictionary(x => x.LineId, x => Decimal(x.UnitPrice, zero: true)));
}
