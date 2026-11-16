namespace GoymenERP.Modules.Sales.Domain.RiskExceptions;

/// <summary>
/// SAT-D285, LOJ-D19: what a mirrored Logistics event says about a single-shipment risk exception. Sales never decides either:
/// the Logistics gate consumes the exception in its own transaction and publishes the fact; Sales records it once, append-only.
/// </summary>
public enum SalesRiskExceptionUsageKind
{
    /// <summary>A definite shipment used the exception (SAT-D285 (1)).</summary>
    Consumed = 1,

    /// <summary>The shipment that used the exception was cancelled; the exception has dropped (SAT-D285 (2)).</summary>
    Dropped = 2,
}
