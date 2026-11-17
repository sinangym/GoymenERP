using GoymenERP.Modules.Manufacturing.Domain.Costing;

namespace GoymenERP.Modules.Manufacturing.Application;

public enum ManufacturingInputOrderRateOutcomeV1 { Available = 1, Unavailable = 2, NotFound = 3 }

/// <summary>Actual Sales-owner rate for the exact approved order/line named by Manufacturing's own plan link.
/// FNS-D73/D83/D107: an unreadable linked order never falls back to a first receipt, invoice FX or current rate.</summary>
public interface IManufacturingInputOrderRateReadPortV1
{
    Task<ManufacturingInputOrderRateV1> ReadAsync(Guid companyId, Guid salesOrderId, Guid salesOrderLineId,
        int lineRevision, Guid sourceCurrencyId, Guid targetCurrencyId, CancellationToken cancellationToken);
}
public sealed record ManufacturingInputOrderRateV1(ManufacturingInputOrderRateOutcomeV1 Outcome,
    Guid CompanyId, Guid SalesOrderId, Guid SalesOrderLineId, int LineRevision,
    Guid SourceCurrencyId, Guid TargetCurrencyId, decimal? Rate, ProductionExactRatioV1? RateRatio,
    string SourceVersion, string? Reason = null);

/// <summary>Explicit missing owner composition; never an available quote. Root replaces this registration with the real owner adapter.</summary>
public sealed class UnavailableManufacturingInputOrderRateReadPortV1 : IManufacturingInputOrderRateReadPortV1
{
    public Task<ManufacturingInputOrderRateV1> ReadAsync(Guid companyId, Guid orderId, Guid lineId, int revision,
        Guid sourceCurrencyId, Guid targetCurrencyId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(new ManufacturingInputOrderRateV1(ManufacturingInputOrderRateOutcomeV1.Unavailable,
            companyId, orderId, lineId, revision, sourceCurrencyId, targetCurrencyId, null, null,
            "manufacturing.linked-order-rate.v1/unavailable", ProductionInputRateCodesV1.OrderRateUnavailable));
    }
}

public interface IManufacturingInputRatePriorityReadPortV1
{
    Task<ManufacturingInputRateProofV1?> ReadAsync(Guid companyId, Guid manufacturingOrderId, Guid issueFactId,
        Guid parentLabelId, IssueCostCurrencyInput recordedCurrency, CancellationToken cancellationToken);
}
public sealed record ManufacturingInputRateProofV1(IssueCostCurrencyInput Currency, string SourceVersion, string EvidenceJson);
