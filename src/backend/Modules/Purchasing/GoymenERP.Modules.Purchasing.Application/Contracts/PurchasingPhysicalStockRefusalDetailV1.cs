using System.Globalization;

namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>Only an immutable, server-produced SAT-D336 message may survive a terminal-refusal replay.</summary>
public static class PurchasingPhysicalStockRefusalDetailV1
{
    public static bool IsValid(int status, string code, string? detail)
    {
        if (detail is null) return true; // Old terminal JSON has no detail.
        if (status != 409 || !(code is "inventory_supplier_return_exit_stock_not_available" or "inventory_goods_receipt_reversal_stock_not_available") || detail.Length > 128
            || !detail.StartsWith("Sistemde ", StringComparison.Ordinal)
            || !detail.EndsWith(" kg istendi.", StringComparison.Ordinal)) return false;
        var body = detail["Sistemde ".Length..^" kg istendi.".Length];
        var split = body.Split(" kg var, ", StringSplitOptions.None);
        return split.Length == 2 && Mass(split[0], false, out var available)
            && Mass(split[1], true, out var requested) && available < requested;
    }
    private static bool Mass(string text, bool positive, out decimal amount)
    {
        amount = 0m;
        return text.Length is > 0 and <= 20
            && decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out amount)
            && amount >= -999999999999999.999m && amount <= 999999999999999.999m
            && (!positive || amount > 0m) && decimal.Round(amount, 3) == amount
            && text == amount.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
