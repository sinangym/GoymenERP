using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Costing;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.ManufacturingRuntime;

/// <summary>Actual owner-port composition, not the invoice FX carried by a stock-cost stamp.
/// The own plan decides whether an order link exists; every unlinked input reads its own first-entry proof.</summary>
internal sealed class ManufacturingInputRatePriorityReaderV1(ICompanyContext company, ICurrentUser user,
    IManufacturingOrderStore orders, IManufacturingPlanStore plans,
    IManufacturingInputOrderRateReadPortV1 orderRates, IInventoryFirstReceiptExchangeRateReadPortV1 receipts)
    : IManufacturingInputRatePriorityReadPortV1
{
    public async Task<ManufacturingInputRateProofV1?> ReadAsync(Guid companyId, Guid manufacturingOrderId, Guid issueFactId,
        Guid parentLabelId, IssueCostCurrencyInput recordedCurrency, CancellationToken ct)
    {
        var actor = user.UserId;
        if (!Scope() || manufacturingOrderId == Guid.Empty || issueFactId == Guid.Empty || parentLabelId == Guid.Empty
            || recordedCurrency.CurrencyId is not { } currency || currency == Guid.Empty || currency == SettingsCurrencyIdentitiesV1.TurkishLira) return null;
        try
        {
            var order = await orders.FindOrderAsync(manufacturingOrderId, ct);
            if (!Scope() || order is null || order.CompanyId != companyId || order.Id != manufacturingOrderId
                || !order.Issues.Any(fact => fact.IssueId == issueFactId && fact.ParentRollId == parentLabelId)) return null;
            var plan = await plans.FindPlanAsync(order.PlanId, ct);
            if (!Scope() || plan is null || plan.CompanyId != companyId || plan.Id != order.PlanId
                || plan.SalesOrderLink is { } invalidLink && !invalidLink.IsWellFormed()) return null;
            var orderVersion = order.Version; var linkRevision = plan.SalesOrderLinkRevision; var link = plan.SalesOrderLink;
            decimal? rate = null; ProductionExactRatioV1? ratio = null; string source; string? missing = null; object evidence;
            if (link is not null)
            {
                var answer = await orderRates.ReadAsync(companyId, link.SalesOrderId, link.SalesOrderLineId, link.LineRevision,
                    currency, SettingsCurrencyIdentitiesV1.TurkishLira, ct);
                if (!Scope() || answer is null || answer.CompanyId != companyId || answer.SalesOrderId != link.SalesOrderId
                    || answer.SalesOrderLineId != link.SalesOrderLineId || answer.LineRevision != link.LineRevision
                    || answer.SourceCurrencyId != currency || answer.TargetCurrencyId != SettingsCurrencyIdentitiesV1.TurkishLira
                    || string.IsNullOrWhiteSpace(answer.SourceVersion)) return null;
                if (answer.Outcome == ManufacturingInputOrderRateOutcomeV1.Available)
                {
                    if (answer.Reason is not null || !Rate(answer.Rate, answer.RateRatio)) return null;
                    rate = answer.Rate; ratio = answer.RateRatio;
                }
                else if (answer.Outcome is ManufacturingInputOrderRateOutcomeV1.Unavailable or ManufacturingInputOrderRateOutcomeV1.NotFound
                    && answer.Rate is null && answer.RateRatio is null) missing = ProductionInputRateCodesV1.OrderRateUnavailable;
                else return null;
                source = answer.SourceVersion; evidence = new { sourceKind = "sales-order", answer };
                // FNS-D107: even a missing owner response must not ask the first-receipt reader.
            }
            else
            {
                var answer = await receipts.GetAsync(companyId, parentLabelId, ct);
                if (!Scope() || answer is null || answer.CompanyId != companyId || answer.LabelId != parentLabelId) return null;
                if (answer.Status == InventoryFirstReceiptExchangeRateStatusV1.Rated)
                {
                    if (answer.SourceCurrencyId != currency || answer.TargetCurrencyId != SettingsCurrencyIdentitiesV1.TurkishLira
                        || string.IsNullOrWhiteSpace(answer.SourceVersion) || answer.RateRatio is null) return null;
                    ratio = new(answer.RateRatio.Numerator, answer.RateRatio.Denominator); rate = answer.Rate?.Rate;
                    if (!Rate(rate, ratio)) return null;
                    source = answer.SourceVersion;
                }
                else if (answer.Status is InventoryFirstReceiptExchangeRateStatusV1.Unavailable or InventoryFirstReceiptExchangeRateStatusV1.NotFound)
                { missing = ProductionInputRateCodesV1.FirstReceiptRateUnavailable; source = "inventory.first-entry-rate.v1/unavailable"; }
                else return null;
                evidence = new { sourceKind = "first-receipt", answer };
            }
            var freshOrder = await orders.FindOrderAsync(manufacturingOrderId, ct);
            var freshPlan = await plans.FindPlanAsync(order.PlanId, ct);
            if (!Scope() || freshOrder is null || freshOrder.Id != manufacturingOrderId || freshOrder.PlanId != order.PlanId
                || freshOrder.CompanyId != companyId || freshOrder.Version != orderVersion
                || freshPlan is null || freshPlan.Id != plan.Id || freshPlan.CompanyId != companyId
                || freshPlan.SalesOrderLinkRevision != linkRevision || freshPlan.SalesOrderLink != link) return null;
            var json = JsonSerializer.Serialize(new { companyId, manufacturingOrderId, orderVersion, planId = plan.Id,
                linkRevision, issueFactId, parentLabelId, source, missing, evidence });
            var version = "manufacturing.input-rate-priority.v1/" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
            return new(new(missing is null ? IssueCostCurrencyStatus.Converted : IssueCostCurrencyStatus.RateMissing, currency,
                recordedCurrency.ForeignUnitCostPerKg, missing is null ? rate : null, recordedCurrency.ForeignUnitCostRatio,
                missing is null ? ratio : null), version, json);
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
        bool Scope() => companyId != Guid.Empty && company.IsResolved && company.CompanyId == companyId
            && user.IsAuthenticated && !string.IsNullOrWhiteSpace(actor) && user.UserId == actor && user.CanAccessCompany(companyId);
    }

    private static bool Rate(decimal? scalar, ProductionExactRatioV1? ratio) =>
        scalar is > 0m && (ratio is null || ratio.IsValid() && ratio.Represents(scalar.Value)) || scalar is null && ratio?.IsValid() == true;
}
