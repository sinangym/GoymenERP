using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

public sealed class SalesOrderCostRateSourceReadPortV1(ISalesOrderDraftStore store, ICompanyContext company)
    : ISalesOrderCostRateSourceReadPortV1
{
    public async Task<SalesOrderCostRateSourceV1> GetAsync(Guid companyId, Guid orderId, Guid lineId, int revision, CancellationToken ct = default)
    {
        RequireCompany();
        if (orderId == Guid.Empty || lineId == Guid.Empty || revision < 0) return Missing(SalesOrderCostRateSourceOutcomeV1.NotFound);
        var order = await store.FindDraftAsync(orderId, ct);
        RequireCompany();
        if (order is null || order.CompanyId != companyId || order.Id != orderId) return Missing(SalesOrderCostRateSourceOutcomeV1.NotFound);
        Guid? currency = null;
        if (order.Revision == revision && order.ApprovalState == InternalApprovalState.Approved
            && order.Lines.Any(line => line.Id == lineId)) currency = order.Header.CurrencyId;
        else if (order.ArchivedRevisions.SingleOrDefault(item => item.Revision == revision) is { } archived
            && archived.Approval.DecidedAtUtc is not null && !string.IsNullOrWhiteSpace(archived.Approval.DecidedBy)
            && archived.Lines.Any(line => line.LineId == lineId)) currency = archived.Header?.CurrencyId;
        // The approved historical commitment remains a cost source after closure/cancellation. No new allocation is authorized here.
        if (currency is null || currency == Guid.Empty || order.Version < 1 || order.Header.BusinessDate == default)
            return Missing(SalesOrderCostRateSourceOutcomeV1.Unavailable);
        const string rateType = "TcmbForexSelling";
        var frame = JsonSerializer.Serialize(new { companyId, orderId, lineId, revision, orderVersion = order.Version,
            orderCurrencyId = currency, rateDay = order.Header.BusinessDate, rateType });
        var source = "sales.order-cost-rate-source.v1/" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(frame))).ToLowerInvariant();
        return new(SalesOrderCostRateSourceOutcomeV1.Available, companyId, orderId, lineId, revision, order.Version,
            currency, order.Header.BusinessDate, rateType, source);

        SalesOrderCostRateSourceV1 Missing(SalesOrderCostRateSourceOutcomeV1 outcome) =>
            new(outcome, companyId, orderId, lineId, revision, null, null, null, null, "sales.order-cost-rate-source.v1/unavailable");
        void RequireCompany()
        {
            ct.ThrowIfCancellationRequested();
            if (companyId == Guid.Empty || !company.IsResolved || company.CompanyId != companyId)
                throw new InvalidOperationException("Sipariş maliyet kuru yalnız etkin şirketten okunabilir.");
        }
    }
}
