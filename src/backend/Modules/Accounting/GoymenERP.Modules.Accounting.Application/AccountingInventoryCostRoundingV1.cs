using System.Globalization;
using GoymenERP.BuildingBlocks.Monetary;

namespace GoymenERP.Modules.Accounting.Application;

/// <summary>Valuation already rounded by its owner, with the stock remainder fixed from that amount. The posting profile is separate; never re-round the source.</summary>
public sealed record AccountingInventoryCostRecordedRoundingV1(Guid CurrencyId, Guid MoneyProfileId,
    long MoneyProfileVersion, int MoneyScale, AccountingInventoryCostRoundingV1 Rounding);

/// <summary>One source component, one rounding. Exact = posted + residue; residue does not create another journal or stock effect.</summary>
public sealed record AccountingInventoryCostRoundingV1(AccountingInventoryCostRatioV1 ExactSignedAmount,
    string RoundedSignedAmount, AccountingInventoryCostRatioV1 ExactSignedResidue, string RuleVersion)
{
    public string ExactSignedAmountText => AccountingInventoryCostRoundingV1Rules.Display(ExactSignedAmount);
    public string ExactSignedResidueText => AccountingInventoryCostRoundingV1Rules.Display(ExactSignedResidue);
}

public static class AccountingInventoryCostRoundingV1Rules
{
    public const string Version = "accounting.inventory-cost-component-total.v1";

    public static bool IsValidRecorded(AccountingInventoryCostRatioV1 amount, AccountingInventoryCostRecordedRoundingV1 recorded)
    {
        return recorded.CurrencyId != Guid.Empty && recorded.MoneyProfileId != Guid.Empty && recorded.MoneyProfileVersion > 0
            && recorded.Rounding is { } rounding && TryRound(rounding.ExactSignedAmount, recorded.CurrencyId,
                recorded.MoneyScale, out var rounded, out var expected) && rounding == expected
            && AccountingInventoryCostAmountsV1.FromDecimal(rounded) == amount;
    }

    public static bool TryResolve(AccountingInventoryCostComponentV1 component, Guid currencyId, Guid profileId,
        long profileVersion, int scale, out decimal amount, out AccountingInventoryCostRoundingV1? evidence)
    {
        if (component.RecordedRounding is not { } recorded)
            return TryRound(component.SignedTlAmount, currencyId, scale, out amount, out evidence);
        amount = default; evidence = null;
        if (recorded.CurrencyId != currencyId || profileId == Guid.Empty || profileVersion <= 0
            || !IsValidRecorded(component.SignedTlAmount, recorded)
            || !AccountingInventoryCostAmountsV1.TryExactDecimal(component.SignedTlAmount, scale, out amount)) return false;
        // The old source profile remains in RecordedRounding. The selected open-period journal profile
        // may differ, but its scale must represent this exact owner-rounded amount without another rounding.
        evidence = recorded.Rounding;
        return true;
    }

    public static bool TryRound(AccountingInventoryCostRatioV1 exact, Guid currencyId, int scale,
        out decimal amount, out AccountingInventoryCostRoundingV1? evidence)
    {
        amount = default; evidence = null;
        if (!AccountingInventoryCostAmountsV1.TryRead(exact, out var numerator, out var denominator)) return false;
        try
        {
            amount = Money.Round(numerator, denominator, currencyId, scale).Amount;
            var residue = AccountingInventoryCostAmountsV1.Add(exact, AccountingInventoryCostAmountsV1.Multiply(
                AccountingInventoryCostAmountsV1.FromDecimal(amount), AccountingInventoryCostAmountsV1.FromDecimal(-1m)));
            evidence = new(exact, amount.ToString("0.############################", CultureInfo.InvariantCulture), residue, Version);
            return true;
        }
        catch (Exception error) when (error is OverflowException or ArgumentException or MoneyRuleException)
        { return false; }
    }

    public static string Display(AccountingInventoryCostRatioV1 value) => AccountingInventoryCostAmountsV1.TryExactDecimal(value, 28, out var amount)
        ? amount.ToString("0.############################", CultureInfo.InvariantCulture) : value.Numerator + " / " + value.Denominator;
}
