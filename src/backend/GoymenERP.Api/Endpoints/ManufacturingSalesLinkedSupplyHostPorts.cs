using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// Üretim U2-b / API-16 (SAT-D219): Manufacturing's adapter of Sales's <see cref="ISalesOrderLinkedSupplyReadPortV1"/> (SupplyKind
/// <see cref="SalesLinkedSupplyKindsV1.Production"/>), registered by the host because Manufacturing does not reference Sales. It
/// translates the Manufacturing-owned <see cref="IManufacturingSalesOrderSupplyReadPortV1"/> answer and grants nothing: the Sales
/// caller measures its own authority. Present carries every OPEN plan (no order yet) or order linked to the sales order — a
/// <see cref="SalesLinkedSupplyDocumentTypesV1.ProductionOrder"/> when the plan opened its order, else a
/// <see cref="SalesLinkedSupplyDocumentTypesV1.ProductionPlan"/>; None means Manufacturing read its rows and found none; any failure
/// (a closed runtime, an exception, a malformed row) is Unreadable with a reason code — never None (fail-closed). It creates nothing.
/// </summary>
public sealed class ManufacturingSalesOrderLinkedSupplyReadPort(IManufacturingSalesOrderSupplyReadPortV1 manufacturing)
    : ISalesOrderLinkedSupplyReadPortV1
{
    /// <summary>The Unreadable reason when Manufacturing could not read its own rows.</summary>
    public const string UnreadableReason = "manufacturing_linked_supply_unreadable";

    public string SupplyKind => SalesLinkedSupplyKindsV1.Production;

    public async Task<SalesOrderLinkedSupplyAnswerV1> ReadOrderLinkedSupplyAsync(
        SalesOrderLinkedSupplyQueryV1 query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        try
        {
            var items = await manufacturing.ListOpenForSalesOrderAsync(query.CompanyId, query.OrderId, query.OrderLineIds,
                cancellationToken);
            if (items is null || items.Any(item => item is null || item.CompanyId != query.CompanyId || item.PlanId == Guid.Empty
                    || item.SalesOrderLineId == Guid.Empty || string.IsNullOrWhiteSpace(item.PlanStatus)
                    || item.OrderId == Guid.Empty || item.OrderId is null != item.OrderStatus is null
                    || item.PlannedKg < 0m || item.ActualKg < 0m
                    || (query.OrderLineIds is { Count: > 0 } narrowing && !narrowing.Contains(item.SalesOrderLineId))))
            {
                return Unreadable(query);
            }

            if (items.Count == 0)
            {
                return Answer(SalesCancellationImpactStatusesV1.None, query, [], null);
            }

            // One plan opens at most one order, so a document appears once; a duplicate would be a double count, so it is not trusted.
            if (items.Select(item => item.OrderId ?? item.PlanId).Distinct().Count() != items.Count)
            {
                return Unreadable(query);
            }

            return Answer(SalesCancellationImpactStatusesV1.Present, query,
                [.. items.Select(item => new SalesOrderLinkedSupplyItemV1(
                    item.OrderId ?? item.PlanId, null,
                    item.OrderId is null ? SalesLinkedSupplyDocumentTypesV1.ProductionPlan : SalesLinkedSupplyDocumentTypesV1.ProductionOrder,
                    null, item.SalesOrderLineId, item.OrderStatus ?? item.PlanStatus, null, null,
                    item.PlannedKg.ToString(CultureInfo.InvariantCulture),
                    item.ActualKg?.ToString(CultureInfo.InvariantCulture)))], null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Unreadable(query);
        }
    }

    private static SalesOrderLinkedSupplyAnswerV1 Unreadable(SalesOrderLinkedSupplyQueryV1 query) =>
        Answer(SalesCancellationImpactStatusesV1.Unreadable, query, [], UnreadableReason);

    private static SalesOrderLinkedSupplyAnswerV1 Answer(string status, SalesOrderLinkedSupplyQueryV1 query,
        IReadOnlyList<SalesOrderLinkedSupplyItemV1> items, string? reason) =>
        new(status, query.CompanyId, query.OrderId, SalesLinkedSupplyKindsV1.Production, items, reason);
}

/// <summary>
/// Üretim U2-b / API-16 (SAT-D219): the declaration that Manufacturing RECORDS links to Sales order lines (a plan's sales order
/// link). It ships with <see cref="ManufacturingSalesOrderLinkedSupplyReadPort"/> in the same change, so Sales reads a recorded
/// kind without an adapter as Unreadable and never as "no source". It carries no authority and reads nothing.
/// </summary>
public sealed class ManufacturingSalesOrderLinkedSupplyRecorder : ISalesOrderLinkedSupplyRecorderV1
{
    public string SupplyKind => SalesLinkedSupplyKindsV1.Production;
}

/// <summary>
/// Üretim U2-b / API-16 (SAT-D219): the host's translation of Sales's approved-line port to Manufacturing's
/// <see cref="IManufacturingSalesOrderLineReferencePortV1"/>. Approved only when Sales answers Approved with that very order and
/// line in the context company, with the customer party and account, the product and the revision; an unknown outcome, a
/// mismatched line or any exception is Unavailable (fail-closed), never Approved. Reading creates nothing.
/// </summary>
public sealed class ManufacturingSalesOrderLineReferencePort(ISalesOrderLineReferencePortV1 salesLines, ICompanyContext companyContext)
    : IManufacturingSalesOrderLineReferencePortV1
{
    private static readonly ManufacturingSalesOrderLineAnswerV1 Unavailable =
        new(ManufacturingSalesOrderLineOutcomeV1.Unavailable, null);

    public async Task<ManufacturingSalesOrderLineAnswerV1> GetApprovedLineAsync(Guid salesOrderId, Guid salesOrderLineId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var answer = await salesLines.GetApprovedLineAsync(salesOrderId, salesOrderLineId, cancellationToken);
            return answer?.Outcome switch
            {
                SalesOrderLineReferenceOutcomeV1.Approved when answer.Line is { } line && line.OrderId == salesOrderId
                    && line.LineId == salesOrderLineId && companyContext.IsResolved && companyContext.CompanyId != Guid.Empty
                    && line.CompanyId == companyContext.CompanyId && line.PartyId != Guid.Empty
                    && line.CompanyAccountId != Guid.Empty && line.ProductId != Guid.Empty && line.Revision >= 0
                    => new ManufacturingSalesOrderLineAnswerV1(ManufacturingSalesOrderLineOutcomeV1.Approved,
                        new ManufacturingSalesOrderLineV1(line.CompanyId, line.OrderId, line.LineId, line.PartyId,
                            line.CompanyAccountId, line.ProductId, line.Revision)),
                SalesOrderLineReferenceOutcomeV1.NotApproved =>
                    new ManufacturingSalesOrderLineAnswerV1(ManufacturingSalesOrderLineOutcomeV1.NotApproved, null),
                SalesOrderLineReferenceOutcomeV1.NotFound =>
                    new ManufacturingSalesOrderLineAnswerV1(ManufacturingSalesOrderLineOutcomeV1.NotFound, null),
                _ => Unavailable,
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Unavailable;
        }
    }
}
