using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
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
                item.HeatNumber, item.CoilNumber, item.PhysicalKg, item.EntryAtUtc, item.EntryInherited)).ToArray()
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

/// <summary>
/// YOL-D30: the company's open items of one direction through Finance's own authorized open-item read (its read and amount
/// authorities apply). Only Open items with a disclosed, positive outstanding amount are passed on; a redacted amount is Denied.
/// More than the technical bound sets WindowFull (the centre then refuses rather than show a cut sub-ledger).
/// </summary>
internal sealed class ReportingFinancePartyAgingSourceV1(IFinanceOpenItemReadServiceV1 owner, ICompanyContext company, ICurrentUser user,
    ICorrelationContext correlation) : IReportingPartyAgingSourceV1
{
    public async Task<ReportingPartyAgingSourceV1> ReadAsync(Guid companyId, bool receivable, CancellationToken cancellationToken = default)
    {
        if (!company.IsResolved || company.CompanyId != companyId || user.UserId is not { Length: > 0 } actor)
            return new(ReportingSourceOutcomeV1.Denied, companyId, [], false);
        var context = new FinanceContext(companyId, actor, correlation.IsResolved ? correlation.CorrelationId : Guid.NewGuid().ToString("N"),
            correlation.IsResolved ? correlation.CausationId : null);
        var direction = receivable ? FinanceOpenItemDirectionV1.Receivable : FinanceOpenItemDirectionV1.Payable;
        var items = new List<ReportingPartyAgingItemV1>();
        var scanned = 0;
        string? cursor = null;
        do
        {
            var result = await owner.ListAuthorizedAsync(context, new SourceListRequest(cursor), cancellationToken);
            if (result.Outcome != ReadOutcome.Found || result.Resource is not { } page)
                return new(result.Outcome == ReadOutcome.Denied ? ReportingSourceOutcomeV1.Denied : ReportingSourceOutcomeV1.Unavailable, companyId, [], false);
            if (page.CompanyId != companyId) return new(ReportingSourceOutcomeV1.Unavailable, companyId, [], false);
            foreach (var item in page.Items)
            {
                if (item.CompanyId != companyId) return new(ReportingSourceOutcomeV1.Unavailable, companyId, [], false);
                if (++scanned > IReportingPartyAgingSourceV1.MaximumItems) return new(ReportingSourceOutcomeV1.Found, companyId, items, true);
                if (item.Direction != direction) continue;
                if (item.Status is null || item.OutstandingAmount.Disclosure != AmountDisclosure.Visible || item.OutstandingAmount.Money is not { } money)
                    return new(ReportingSourceOutcomeV1.Denied, companyId, [], false);
                if (item.Status != FinanceOpenItemStatusV1.Open) continue;
                if (money.CurrencyId != item.CurrencyId) return new(ReportingSourceOutcomeV1.Unavailable, companyId, [], false);
                items.Add(new ReportingPartyAgingItemV1(item.PartyId, item.CompanyAccountId, item.CurrencyId, item.DueOn, money.Amount));
            }

            cursor = page.NextCursor;
        }
        while (cursor is not null);

        return new(ReportingSourceOutcomeV1.Found, companyId, items, false);
    }
}

public static class ReportingRuntimeServiceCollectionExtensions
{
    /// <summary>
    /// WP14: the report centre with its connected owner sources. OTIF (no owner read of promised day × dated shipment per order
    /// line yet) and the intercompany dump (YOL-D31's party ↔ company link is not recorded by Counterparty yet) stay "not connected".
    /// </summary>
    public static IServiceCollection AddReportingRuntimeV1(this IServiceCollection services)
    {
        services.AddScoped<IReportingStockAgingSourceV1, ReportingInventoryStockAgingSourceV1>();
        services.AddScoped<IReportingRecallSourceV1, ReportingInventoryRecallSourceV1>();
        services.AddScoped<IReportingTrialBalanceSourceV1, ReportingAccountingTrialBalanceSourceV1>();
        services.AddScoped<IReportingPartyAgingSourceV1, ReportingFinancePartyAgingSourceV1>();
        return services;
    }
}
