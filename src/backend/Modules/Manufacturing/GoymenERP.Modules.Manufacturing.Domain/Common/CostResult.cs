namespace GoymenERP.Modules.Manufacturing.Domain.Common;

public enum CostStatus
{
    NotCalculated = 1,
    WaitingPolicy = 2,

    /// <summary>
    /// Reserved (plan v2 R6). This slice cannot produce it: how a known cost carries its money —
    /// a currency identity rather than a code string — is decided with T25/Finance, not here.
    /// </summary>
    Known = 3,
}

/// <summary>
/// Plan v2 R6: detailed costing is deferred, so a production fact carries a cost STATUS, not a
/// number. An unknown cost has no amount — never zero — and this slice constructs no known cost.
/// </summary>
public sealed record CostResult
{
    private CostResult(CostStatus status)
    {
        Status = status;
    }

    public static CostResult NotCalculated { get; } = new(CostStatus.NotCalculated);

    public static CostResult WaitingPolicy { get; } = new(CostStatus.WaitingPolicy);

    public CostStatus Status { get; }

    /// <summary>Always null in this slice: only a <see cref="CostStatus.Known"/> result would carry one.</summary>
    public decimal? Amount => null;
}
