using System.Globalization;

namespace GoymenERP.Modules.Accounting.Application;

/// <summary>Accepts the Inventory owner's exact scalar-or-ratio carrier without deriving or rounding any quantity.</summary>
public static class ManufacturingCostDifferenceQuantityV1
{
    public static bool TryResolve(decimal? scalar, AccountingInventoryCostRatioV1? ratio, out AccountingInventoryCostRatioV1 exact)
    {
        exact = null!;
        if (ratio is not null)
        {
            if (!AccountingInventoryCostAmountsV1.TryRead(ratio, out var numerator, out _) || numerator.Sign < 0
                || scalar is { } value && AccountingInventoryCostAmountsV1.FromDecimal(value) != ratio) return false;
            exact = ratio; return true;
        }
        if (scalar is not { } old || old < 0) return false;
        exact = AccountingInventoryCostAmountsV1.FromDecimal(old); return true;
    }

    public static string Display(decimal? scalar, AccountingInventoryCostRatioV1? ratio)
    {
        if (!TryResolve(scalar, ratio, out var exact)) throw new InvalidOperationException("The recorded quantity is unreadable.");
        return AccountingInventoryCostAmountsV1.TryExactDecimal(exact, 28, out var value)
            ? value.ToString("0.############################", CultureInfo.InvariantCulture)
            : exact.Numerator + " / " + exact.Denominator;
    }
}
