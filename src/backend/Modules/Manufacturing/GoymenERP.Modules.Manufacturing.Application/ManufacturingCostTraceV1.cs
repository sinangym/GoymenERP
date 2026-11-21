using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Domain.Costing;
using GoymenERP.Modules.Manufacturing.Domain.Orders;

namespace GoymenERP.Modules.Manufacturing.Application;

// URT-S19 (B-27, DEC-PRDCT-009, API-42): the order's cost trace, read only. The raw data DEC-PRDCT-009 keeps (lot,
// line, fire, subcontract owner, sales-order link, the fact moments) comes from the order's own stored facts through
// ProductionCostTraceBuilderV1; the cost figures come only from the production cost port
// (IManufacturingScrapCostDistributionPortV1 over ProductionReceiptCostCalculatorV1), never from a second formula here.
// Until Inventory gives the stamped issue cost the port has no source, so the cost part answers "unavailable" with the
// port's own reason code, never a zero. Nothing is stored. OPEN-008 stays open.

/// <summary>One lot (parent roll) of the trace: planned, issued, returned and consumed kg as lossless invariant text.</summary>
public sealed record CostTraceLotViewV1(
    Guid ParentRollId,
    string PlannedKg,
    string IssuedKg,
    string ReturnedKg,
    string ConsumedKg,
    int IssueCount,
    int ReturnCount,
    DateTimeOffset? FirstIssuedAtUtc,
    DateTimeOffset? LastIssuedAtUtc,
    Guid? CommercialPartyId,
    Guid? CommercialPartyAccountId);

/// <summary>One declared output class, net of reversals.</summary>
public sealed record CostTraceOutputViewV1(string OutputClass, string NetKg, int DeclarationCount, int ReversalCount);

/// <summary>The fact moments; stoppage and duration carry a code while no time record exists (API-33).</summary>
public sealed record CostTraceTimelineViewV1(
    DateTimeOffset? FirstFactAtUtc,
    DateTimeOffset? LastFactAtUtc,
    DateTimeOffset? ClosedAtUtc,
    string StoppageCode,
    string DurationCode);

/// <summary>The plan's sales-order link (DEC-PRDCT-009 "sipariş bağlantısı"); the linking actor is not disclosed.</summary>
public sealed record CostTraceSalesOrderLinkViewV1(
    Guid SalesOrderId,
    Guid SalesOrderLineId,
    Guid CustomerPartyId,
    Guid ProductId,
    int LineRevision,
    DateTimeOffset LinkedAtUtc);

/// <summary>
/// The production cost port's answer for the order. Only <c>available</c> carries figures (TL per kg and TL, unrounded
/// as the port v1 gives them: the Money form and rounding are the port's own later version, so the figures are for
/// reading, never for posting). Any other status carries no figure, never zero, and <see cref="ReasonCode"/> says why.
/// </summary>
public sealed record CostTraceCostViewV1(
    string Status,
    string? CostBasis,
    string? CostPerKgTl,
    string? RawMaterialCostPerKgTl,
    string? WeighedScrapExpenseTl,
    IReadOnlyList<string> NoteCodes,
    string? ReasonCode);

/// <summary>The order's cost trace, version 1.</summary>
public sealed record CostTraceViewV1(
    int ContractVersion,
    string MethodCode,
    Guid OrderId,
    Guid PlanId,
    Guid WorkCenterId,
    string Process,
    string Status,
    long Version,
    IReadOnlyList<CostTraceLotViewV1> Lots,
    string TotalIssuedKg,
    string TotalReturnedKg,
    string TotalConsumedKg,
    IReadOnlyList<CostTraceOutputViewV1> Outputs,
    CostTraceTimelineViewV1 Timeline,
    CostTraceSalesOrderLinkViewV1? SalesOrderLink,
    CostTraceCostViewV1 Cost);

public static class ManufacturingCostTraceCodesV1
{
    public const string CostAvailable = "available";
    public const string CostPending = "pending";
    public const string CostUnreadable = "unreadable";
    public const string CostUnavailable = "unavailable";

    /// <summary>The port answered for another company or order, or with a status this read does not know.</summary>
    public const string CostAnswerInvalid = "cost_answer_invalid";
}

public sealed partial class ManufacturingCommandService
{
    /// <summary>
    /// URT-S19: one order's cost trace under its own grant (<see cref="ErpPermissions.ManufacturingReportsRead"/>,
    /// registered and granted to no role), measured again after the reads. The server-resolved company only: another
    /// company's order is not found, never forbidden. A read meeting concurrent commits is a retryable 503, as the order
    /// read. A missing plan behind a stored order is a broken store contract and fails closed (503), never a trace
    /// without its sales-order link. The cost port's own failure never fails the trace: the cost part says
    /// <c>unavailable</c> and why. Cancellation is not swallowed.
    /// </summary>
    public async Task<ManufacturingApiResult<CostTraceViewV1>> GetCostTraceAsync(Guid orderId,
        CancellationToken cancellationToken)
    {
        if (!HasContext() || ActorGuid() is not { } actorId)
        {
            return Denied<CostTraceViewV1>();
        }

        if (orderId == Guid.Empty)
        {
            return NotFoundResult<CostTraceViewV1>();
        }

        var companyId = companyContext.CompanyId;
        ManufacturingApiResult<CostTraceViewV1> answer;
        try
        {
            var order = await orders.FindOrderAsync(orderId, cancellationToken);
            if (order is null || order.CompanyId != companyId)
            {
                answer = NotFoundResult<CostTraceViewV1>();
            }
            else if (await plans.FindPlanAsync(order.PlanId, cancellationToken) is not { } plan
                     || plan.CompanyId != companyId)
            {
                answer = new ManufacturingApiResult<CostTraceViewV1>(ManufacturingApiStatus.Unavailable, null, false,
                    null);
            }
            else
            {
                var cost = await ReadCostAsync(companyId, order.Id, cancellationToken);
                var link = plan.SalesOrderLink is { } salesLink
                    ? new CostTraceSalesOrderLinkViewV1(salesLink.SalesOrderId, salesLink.SalesOrderLineId,
                        salesLink.CustomerPartyId, salesLink.ProductId, salesLink.LineRevision, salesLink.LinkedAtUtc)
                    : null;
                answer = new ManufacturingApiResult<CostTraceViewV1>(ManufacturingApiStatus.Ok,
                    CostTraceView(ProductionCostTraceBuilderV1.Build(order), link, cost), false, null);
            }
        }
        catch (ManufacturingStoreException exception) when (exception.Code == ManufacturingStoreCodes.ConcurrentChange)
        {
            answer = new ManufacturingApiResult<CostTraceViewV1>(ManufacturingApiStatus.Unavailable, null, false, null);
        }

        // A grant withdrawn while the trace was read answers nothing of it.
        var scope = await authority.RevalidatePermissionScopeAsync(
            ErpPermissions.ManufacturingReportsRead, companyId, cancellationToken);
        return scope.IsGranted && HasCapturedContext(companyId, actorId)
            ? answer
            : Denied<CostTraceViewV1>();
    }

    /// <summary>
    /// The production cost port's answer as the trace shows it. No port (no issue-cost source yet) or a port that throws
    /// is <c>unavailable</c> with the port's <see cref="ManufacturingScrapCostDistributionCodesV1.SourceUnavailable"/>; an
    /// answer for another company or order, or an unknown status, is <c>unavailable</c> with
    /// <see cref="ManufacturingCostTraceCodesV1.CostAnswerInvalid"/>. Figures pass only with <c>available</c>.
    /// </summary>
    private async Task<CostTraceCostViewV1> ReadCostAsync(Guid companyId, Guid orderId,
        CancellationToken cancellationToken)
    {
        if (costDistribution is null)
        {
            return CostUnavailable(ManufacturingScrapCostDistributionCodesV1.SourceUnavailable);
        }

        ManufacturingScrapCostDistributionResultV1 result;
        try
        {
            result = await costDistribution.DistributeAsync(companyId, orderId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return CostUnavailable(ManufacturingScrapCostDistributionCodesV1.SourceUnavailable);
        }

        return CostView(companyId, orderId, result);
    }

    internal static CostTraceCostViewV1 CostView(Guid companyId, Guid orderId,
        ManufacturingScrapCostDistributionResultV1? result)
    {
        if (result is null || result.CompanyId != companyId || result.OrderId != orderId)
        {
            return CostUnavailable(ManufacturingCostTraceCodesV1.CostAnswerInvalid);
        }

        return result.Status switch
        {
            ManufacturingScrapCostDistributionStatusV1.Available
                when result.CostPerKgTl is { } costPerKg && result.RawMaterialCostPerKgTl is { } rawMaterial =>
                new CostTraceCostViewV1(ManufacturingCostTraceCodesV1.CostAvailable, result.CostBasis, Text(costPerKg),
                    Text(rawMaterial),
                    result.WeighedScrapExpenseTl is { } scrapExpense ? Text(scrapExpense) : null,
                    result.NoteCodes.ToArray(), null),
            ManufacturingScrapCostDistributionStatusV1.Pending =>
                new CostTraceCostViewV1(ManufacturingCostTraceCodesV1.CostPending, null, null, null, null,
                    result.NoteCodes.ToArray(), result.ReasonCode),
            ManufacturingScrapCostDistributionStatusV1.Unreadable =>
                new CostTraceCostViewV1(ManufacturingCostTraceCodesV1.CostUnreadable, null, null, null, null,
                    result.NoteCodes.ToArray(), result.ReasonCode),
            ManufacturingScrapCostDistributionStatusV1.Unavailable =>
                CostUnavailable(result.ReasonCode ?? ManufacturingScrapCostDistributionCodesV1.SourceUnavailable),
            _ => CostUnavailable(ManufacturingCostTraceCodesV1.CostAnswerInvalid),
        };
    }

    private static CostTraceCostViewV1 CostUnavailable(string reasonCode) =>
        new(ManufacturingCostTraceCodesV1.CostUnavailable, null, null, null, null, [], reasonCode);

    internal static CostTraceViewV1 CostTraceView(ProductionCostTraceV1 trace, CostTraceSalesOrderLinkViewV1? link,
        CostTraceCostViewV1 cost) =>
        new(trace.ContractVersion, trace.MethodCode, trace.OrderId, trace.PlanId, trace.WorkCenterId,
            trace.Process.ToString(), trace.Status.ToString(), trace.Version,
            trace.Lots.Select(lot => new CostTraceLotViewV1(lot.ParentRollId, Text(lot.PlannedKg.Value),
                Text(lot.IssuedKg.Value), Text(lot.ReturnedKg.Value), Text(lot.ConsumedKg.Value), lot.IssueCount,
                lot.ReturnCount, lot.FirstIssuedAtUtc, lot.LastIssuedAtUtc, lot.CommercialPartyId,
                lot.CommercialPartyAccountId)).ToArray(),
            Text(trace.TotalIssuedKg.Value), Text(trace.TotalReturnedKg.Value), Text(trace.TotalConsumedKg.Value),
            trace.Outputs.Select(output => new CostTraceOutputViewV1(output.Class.ToString(), Text(output.NetKg.Value),
                output.DeclarationCount, output.ReversalCount)).ToArray(),
            new CostTraceTimelineViewV1(trace.Timeline.FirstFactAtUtc, trace.Timeline.LastFactAtUtc,
                trace.Timeline.ClosedAtUtc, trace.Timeline.StoppageCode, trace.Timeline.DurationCode),
            link, cost);
}
