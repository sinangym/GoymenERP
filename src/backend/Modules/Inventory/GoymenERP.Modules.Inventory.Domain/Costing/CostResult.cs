namespace GoymenERP.Modules.Inventory.Domain.Costing;

/// <summary>R6 (plan v2 §12): detailed costing is deferred, the facts are not.</summary>
public enum CostStatus
{
    /// <summary>Nobody has calculated it. This is not zero.</summary>
    NotCalculated = 1,

    /// <summary>A valuation policy is needed first. This is not zero either.</summary>
    WaitingPolicy = 2,

    /// <summary>Calculated; only then does <see cref="CostResult.Amount"/> mean anything.</summary>
    Known = 3,
}

/// <summary>
/// The cost attached to a stock movement. An unknown cost is <c>null</c> with a status that
/// says why — never <c>0</c>, which would read as "free" and turn into a fake profit later.
/// The movement itself is recorded whatever the cost status is: a missing valuation never
/// loses a physical event.
/// </summary>
public sealed record CostResult
{
    private CostResult(CostStatus status, decimal? amount, Guid? currencyId)
    {
        Status = status;
        Amount = amount;
        CurrencyId = currencyId;
    }

    public CostStatus Status { get; }

    /// <summary>Only when <see cref="Status"/> is <see cref="CostStatus.Known"/>; otherwise null.</summary>
    public decimal? Amount { get; }

    /// <summary>The currency's owner identity; only with a known amount.</summary>
    public Guid? CurrencyId { get; }

    public static CostResult NotCalculated { get; } = new(CostStatus.NotCalculated, null, null);

    public static CostResult WaitingPolicy { get; } = new(CostStatus.WaitingPolicy, null, null);

    public static CostResult Known(decimal amount, Guid currencyId)
    {
        if (amount < 0m)
        {
            throw new InventoryRuleException(InventoryRuleCodes.InvalidCostResult, "Bilinen maliyet negatif olamaz.");
        }

        if (currencyId == Guid.Empty)
        {
            throw new InventoryRuleException(
                InventoryRuleCodes.InvalidCostResult, "Bilinen maliyet para birimi kimliği olmadan olamaz.");
        }

        return new(CostStatus.Known, amount, currencyId);
    }
}
