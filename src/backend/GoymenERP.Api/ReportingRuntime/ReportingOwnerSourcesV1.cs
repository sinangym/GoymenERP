using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Reporting.Domain;
using GoymenERP.Modules.Reporting.Infrastructure;

namespace GoymenERP.Api.ReportingRuntime;

/// <summary>
/// WP14: the host adapters that answer the report centre's source ports through each OWNER's own read port. Reporting never
/// references an owner module; Inventory and Accounting never reference Reporting. A context or company mismatch is Denied;
/// an owner failure is Unavailable; nothing is defaulted.
/// </summary>
internal sealed class ReportingInventoryStockAgingSourceV1(IInventoryStockAgingReadPortV1 owner, ICompanyContext company)
    : IReportingStockAgingSourceV1
{
    public async Task<ReportingStockAgingSourcePageV1> ReadAsync(Guid companyId, int page, int pageSize, Guid? productId, Guid? locationId,
        CancellationToken cancellationToken = default)
    {
        if (!company.IsResolved || company.CompanyId != companyId) return new(ReportingSourceOutcomeV1.Denied, companyId, [], false);
        var answer = await owner.ReadAsync(new InventoryStockAgingQueryV1(companyId, page, pageSize, productId, locationId), cancellationToken);
        var outcome = answer.Outcome switch
        {
            InventoryStockAgingOutcomeV1.Found => ReportingSourceOutcomeV1.Found,
            InventoryStockAgingOutcomeV1.Denied => ReportingSourceOutcomeV1.Denied,
            _ => ReportingSourceOutcomeV1.Unavailable,
        };
        return new(outcome, answer.CompanyId, outcome == ReportingSourceOutcomeV1.Found
            ? answer.Items.Select(item => new ReportingStockAgingSourceRowV1(item.LabelId, item.LabelCode, item.ProductId, item.LocationId,
                item.HeatNumber, item.CoilNumber, item.PhysicalKg, item.LabelCreatedAtUtc, item.FirstIncomingAtUtc)).ToArray()
            : [], outcome == ReportingSourceOutcomeV1.Found && answer.HasMore);
    }
}

/// <summary>
/// Recall / traceability through Inventory's label trace (STK-S33). The trace measures its own label-detail permissions; its
/// 404 (missing or another company's label) is NotFound, its 401/403 Denied, anything else Unavailable.
/// </summary>
internal sealed class ReportingInventoryRecallSourceV1(IInventoryLabelTraceV1 owner, ICompanyContext company) : IReportingRecallSourceV1
{
    public async Task<ReportingRecallSourceV1> ReadAsync(Guid companyId, Guid labelId, bool forward, CancellationToken cancellationToken = default)
    {
        if (!company.IsResolved || company.CompanyId != companyId) return new(ReportingSourceOutcomeV1.Denied, companyId, [], false, []);
        InventoryLabelTraceV1 trace;
        try
        {
            trace = await owner.ReadAsync(labelId, forward ? InventoryLabelTraceDirectionV1.Forward : InventoryLabelTraceDirectionV1.Backward,
                cancellationToken);
        }
        catch (InventoryLabelTraceException exception)
        {
            var outcome = exception.StatusCode switch
            {
                404 => ReportingSourceOutcomeV1.NotFound,
                401 or 403 => ReportingSourceOutcomeV1.Denied,
                _ => ReportingSourceOutcomeV1.Unavailable,
            };
            return new(outcome, companyId, [], false, []);
        }

        var codes = trace.Labels.GroupBy(label => label.LabelId).ToDictionary(group => group.Key, group => group.First().Code);
        var parties = trace.Parties.GroupBy(party => party.PartyId).ToDictionary(group => group.Key, group => group.First());
        var rows = new List<ReportingRecallSourceRowV1>(trace.Events.Count);
        foreach (var item in trace.Events)
        {
            // An event on a label the walk did not list is a malformed owner answer, never a guessed code.
            if (!codes.TryGetValue(item.LabelId, out var code)) return new(ReportingSourceOutcomeV1.Unavailable, companyId, [], false, []);
            InventoryLabelTracePartyV1? party = item.PartyId is { } partyId && parties.TryGetValue(partyId, out var found) ? found : null;
            rows.Add(new ReportingRecallSourceRowV1(item.LabelId, code, item.Step, item.OccurredAtUtc, item.QuantityKg, party?.Role, party?.Code,
                party is null ? null : party.TradeName ?? party.LegalName, item.SourceKind, item.SourceId));
        }

        return new(ReportingSourceOutcomeV1.Found, companyId, rows, trace.WindowFull,
        [
            new("productionStatus", trace.ProductionStatus), new("partyStatus", trace.PartyStatus), new("partyNameStatus", trace.PartyNameStatus),
        ]);
    }
}

/// <summary>Mizan for the auditor package: Accounting's own trial balance service (its journal-read authority applies too).</summary>
internal sealed class ReportingAccountingTrialBalanceSourceV1(AccountingTrialBalanceServiceV1 owner, ICompanyContext company)
    : IReportingTrialBalanceSourceV1
{
    public async Task<ReportingTrialBalanceSourceV1> ReadAsync(Guid companyId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        if (!company.IsResolved || company.CompanyId != companyId) return new(ReportingSourceOutcomeV1.Denied, companyId, [], null);
        var result = await owner.ReadWithReceiptAsync(new TrialBalanceRequestV1(from, to), cancellationToken);
        if (result.Status != AccountingApiStatus.Ok || result.Resource is not { } evidence)
        {
            return new(result.Status == AccountingApiStatus.Denied ? ReportingSourceOutcomeV1.Denied : ReportingSourceOutcomeV1.Unavailable,
                companyId, [], null);
        }

        if (evidence.Receipt.CompanyId != companyId) return new(ReportingSourceOutcomeV1.Unavailable, companyId, [], null);
        return new(ReportingSourceOutcomeV1.Found, companyId, evidence.Report.Lines.Select(line => new ReportingTrialBalanceSourceRowV1(
            line.AccountId, line.CurrencyId, line.Debit, line.Credit, line.BalanceSide, line.Balance)).ToArray(), evidence.Receipt.SourceVersion);
    }
}

public static class ReportingRuntimeServiceCollectionExtensions
{
    /// <summary>
    /// WP14: the report centre with its connected owner sources. OTIF (no Sales owner read of requested date × shipment date
    /// yet) and the intercompany dump (no leg marking decided, EK-40) stay "not connected" — NEEDS-SINAN in the WP14 report.
    /// </summary>
    public static IServiceCollection AddReportingRuntimeV1(this IServiceCollection services)
    {
        services.AddScoped<IReportingStockAgingSourceV1, ReportingInventoryStockAgingSourceV1>();
        services.AddScoped<IReportingRecallSourceV1, ReportingInventoryRecallSourceV1>();
        services.AddScoped<IReportingTrialBalanceSourceV1, ReportingAccountingTrialBalanceSourceV1>();
        return services;
    }
}
