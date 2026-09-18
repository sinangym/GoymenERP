namespace GoymenERP.Modules.Manufacturing.Domain.Common;

public enum CostStatus
{
    NotCalculated = 1,
    WaitingPolicy = 2,
    Known = 3,
}

/// <summary>
/// Plan v2 R6: detailed costing is deferred, so a production fact carries a cost STATUS, not a
/// number. An amount exists only when the status is <see cref="CostStatus.Known"/>; an unknown
/// cost is null, never zero.
/// </summary>
public sealed record CostResult
{
    private CostResult(CostStatus status, decimal? amount, string? currency)
    {
        Status = status;
        Amount = amount;
        Currency = currency;
    }

    public static CostResult NotCalculated { get; } = new(CostStatus.NotCalculated, null, null);

    public static CostResult WaitingPolicy { get; } = new(CostStatus.WaitingPolicy, null, null);

    public CostStatus Status { get; }

    public decimal? Amount { get; }

    public string? Currency { get; }

    public static CostResult Known(decimal amount, string currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new ArgumentException("Bilinen maliyetin para birimi olmalıdır.", nameof(currency));
        }

        return new CostResult(CostStatus.Known, amount, currency.Trim());
    }
}
