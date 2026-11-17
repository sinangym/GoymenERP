using GoymenERP.BuildingBlocks.Security;
using System.Text.Json.Serialization;
using GoymenERP.Modules.Manufacturing.Domain.Costing;

namespace GoymenERP.Modules.Manufacturing.Application;

// URT-S17 (SAT-D317): the order cost view, read only. Every figure comes from the production cost port version 2
// (IManufacturingProductionCostPortV2 over ProductionCostRoundingCalculatorV1): the recorded kg cost (4 decimals), the
// recorded TL amounts (2 decimals, a half away from zero, URT-D78), the remainder on the good product line with the most
// kg (URT-D80), the currency lines (URT-D55) and the issues that keep the cost from being computed (URT-D58). Nothing is
// computed or stored here. SAT-D317 (1): an order that is not closed has no receipt cost; the calculator answers
// pending with order_not_closed and this view passes that on. SAT-D317 (2): a return's cost is its linked issue's cost
// of that day, inside the calculator. Until Inventory gives the stamped issue cost the port has no source, so the view
// answers "unavailable" with cost_source_unavailable, never a zero.

/// <summary>One recorded output line; every figure is lossless invariant text, <see cref="RemainderTl"/> may be signed.</summary>
public sealed record ProductionCostLineViewV1(
    Guid OutputLabelId,
    string OutputClass,
    string Kilograms,
    string? UnitCostPerKgTl,
    string? AmountTl,
    string? SeparateScrapExpenseTl,
    string RemainderTl,
    string? AllocationBasis,
    string? NotAllocatedCode);

/// <summary>One issue that entered stock in a foreign currency, with the rate recorded then; display only.</summary>
public sealed record ProductionCostCurrencyLineViewV1(
    Guid IssueMovementId,
    Guid CurrencyId,
    string? ForeignUnitCostPerKg,
    string ExchangeRate,
    string TlUnitCostPerKg,
    string KeptKilograms,
    string? ForeignAmount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ProductionExactRatioV1? ForeignUnitCostRatio = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ProductionExactRatioV1? ForeignAmountRatio = null);

/// <summary>One issue that keeps the cost from being computed and why (<c>input_cost_zero</c>, <c>input_exchange_rate_missing</c>).</summary>
public sealed record ProductionCostIssueReasonViewV1(Guid IssueMovementId, string ReasonCode);

/// <summary>
/// The order's production cost, version 1 of this view. Only <c>available</c> carries figures, lines and currency
/// lines; any other status carries none (never zero) and <see cref="ReasonCode"/> says why. <see cref="IssueReasons"/>
/// name the inputs an unreadable answer is waiting for. The figures are the recorded ones the port gives; the screen
/// never computes them again.
/// </summary>
public sealed record ProductionCostViewV1(
    int ContractVersion,
    Guid OrderId,
    string OrderStatus,
    long OrderVersion,
    string Status,
    string? CostBasis,
    string? CostPerKgTl,
    string? RawMaterialCostPerKgTl,
    string? InputTotalTl,
    string? WeighedScrapExpenseTl,
    string? DistributedTotalTl,
    string? RemainderTl,
    Guid? RemainderLabelId,
    IReadOnlyList<ProductionCostLineViewV1> Lines,
    IReadOnlyList<ProductionCostCurrencyLineViewV1> CurrencyLines,
    IReadOnlyList<ProductionCostIssueReasonViewV1> IssueReasons,
    IReadOnlyList<string> NoteCodes,
    string? ReasonCode)
{
    public const int CurrentContractVersion = 1;
}

public sealed partial class ManufacturingCommandService
{
    /// <summary>
    /// URT-S17: one order's production cost under the reports grant
    /// (<see cref="ErpPermissions.ManufacturingReportsRead"/>, registered and granted to no role), measured again after
    /// the reads. The server-resolved company only: another company's order is not found, never forbidden. A read
    /// meeting concurrent commits is a retryable 503. The cost port's own failure never fails the read: the view says
    /// <c>unavailable</c> and why. Cancellation is not swallowed. Nothing is written.
    /// </summary>
    public async Task<ManufacturingApiResult<ProductionCostViewV1>> GetProductionCostAsync(Guid orderId,
        CancellationToken cancellationToken)
    {
        if (!HasContext() || ActorGuid() is not { } actorId)
        {
            return Denied<ProductionCostViewV1>();
        }

        if (orderId == Guid.Empty)
        {
            return NotFoundResult<ProductionCostViewV1>();
        }

        var companyId = companyContext.CompanyId;
        ManufacturingApiResult<ProductionCostViewV1> answer;
        try
        {
            var order = await orders.FindOrderAsync(orderId, cancellationToken);
            if (order is null || order.CompanyId != companyId)
            {
                answer = NotFoundResult<ProductionCostViewV1>();
            }
            else
            {
                var cost = await ReadProductionCostAsync(companyId, order.Id, cancellationToken);
                answer = new ManufacturingApiResult<ProductionCostViewV1>(ManufacturingApiStatus.Ok,
                    ProductionCostView(order.Id, order.Status.ToString(), order.Version, companyId, cost), false,
                    null);
            }
        }
        catch (ManufacturingStoreException exception) when (exception.Code == ManufacturingStoreCodes.ConcurrentChange)
        {
            answer = new ManufacturingApiResult<ProductionCostViewV1>(ManufacturingApiStatus.Unavailable, null, false,
                null);
        }

        // A grant withdrawn while the cost was read answers nothing of it.
        var scope = await authority.RevalidatePermissionScopeAsync(
            ErpPermissions.ManufacturingReportsRead, companyId, cancellationToken);
        return scope.IsGranted && HasCapturedContext(companyId, actorId)
            ? answer
            : Denied<ProductionCostViewV1>();
    }

    /// <summary>
    /// The port's answer, or null when there is no port (no issue-cost source yet) or the port throws; the view answers
    /// both as <c>unavailable</c> with <see cref="ManufacturingScrapCostDistributionCodesV1.SourceUnavailable"/>.
    /// </summary>
    private async Task<ManufacturingProductionCostResultV2?> ReadProductionCostAsync(Guid companyId, Guid orderId,
        CancellationToken cancellationToken)
    {
        if (productionCost is null)
        {
            return null;
        }

        try
        {
            return await productionCost.ReadAsync(companyId, orderId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// The port's answer as the view shows it. No answer is <c>unavailable</c> with
    /// <see cref="ManufacturingScrapCostDistributionCodesV1.SourceUnavailable"/>; an answer for another company or order,
    /// of another contract version, of an unknown status, an available answer missing a figure or whose distributed
    /// total differs from the input total, is <c>unavailable</c> with
    /// <see cref="ManufacturingCostTraceCodesV1.CostAnswerInvalid"/>. Figures pass only with <c>available</c>.
    /// </summary>
    internal static ProductionCostViewV1 ProductionCostView(Guid orderId, string orderStatus, long orderVersion,
        Guid companyId, ManufacturingProductionCostResultV2? result)
    {
        if (result is null)
        {
            return ProductionCostUnavailable(orderId, orderStatus, orderVersion,
                ManufacturingScrapCostDistributionCodesV1.SourceUnavailable);
        }

        if (result.ContractVersion != ManufacturingProductionCostResultV2.CurrentContractVersion
            || result.CompanyId != companyId || result.OrderId != orderId)
        {
            return ProductionCostUnavailable(orderId, orderStatus, orderVersion,
                ManufacturingCostTraceCodesV1.CostAnswerInvalid);
        }

        switch (result.Status)
        {
            case ManufacturingScrapCostDistributionStatusV1.Available
                when result.RecordedCostPerKgTl is { } costPerKg
                     && result.RecordedRawMaterialCostPerKgTl is { } rawMaterial
                     && result.InputTotalTl is { } inputTotal
                     && result.DistributedTotalTl is { } distributedTotal
                     && result.RemainderTl is { } remainder
                     && inputTotal == distributedTotal:
                return new ProductionCostViewV1(ProductionCostViewV1.CurrentContractVersion, orderId, orderStatus,
                    orderVersion, ManufacturingCostTraceCodesV1.CostAvailable, result.CostBasis, Text(costPerKg),
                    Text(rawMaterial), Text(inputTotal),
                    result.RecordedWeighedScrapExpenseTl is { } scrapExpense ? Text(scrapExpense) : null,
                    Text(distributedTotal), Text(remainder), result.RemainderLabelId,
                    result.Lines.Select(static line => new ProductionCostLineViewV1(line.OutputLabelId,
                        line.OutputClass.ToString(), Text(line.Kilograms),
                        line.RecordedUnitCostPerKgTl is { } unitCost ? Text(unitCost) : null,
                        line.RecordedAmountTl is { } amount ? Text(amount) : null,
                        line.RecordedSeparateScrapExpenseTl is { } separate ? Text(separate) : null,
                        Text(line.RemainderTl), line.AllocationBasis, line.NotAllocatedCode)).ToArray(),
                    result.CurrencyLines.Select(static line => new ProductionCostCurrencyLineViewV1(
                        line.IssueMovementId, line.CurrencyId, line.ForeignUnitCostPerKg is { } foreign ? Text(foreign) : null,
                        Text(line.ExchangeRate), Text(line.TlUnitCostPerKg), Text(line.KeptKilograms),
                        line.ForeignAmount is { } foreignAmount ? Text(foreignAmount) : null, line.ForeignUnitCostRatio, line.ForeignAmountRatio)).ToArray(),
                    [], result.NoteCodes.ToArray(), null);
            case ManufacturingScrapCostDistributionStatusV1.Pending:
                return ProductionCostWithoutFigures(orderId, orderStatus, orderVersion,
                    ManufacturingCostTraceCodesV1.CostPending, result);
            case ManufacturingScrapCostDistributionStatusV1.Unreadable:
                return ProductionCostWithoutFigures(orderId, orderStatus, orderVersion,
                    ManufacturingCostTraceCodesV1.CostUnreadable, result);
            case ManufacturingScrapCostDistributionStatusV1.Unavailable:
                return ProductionCostUnavailable(orderId, orderStatus, orderVersion,
                    result.ReasonCode ?? ManufacturingScrapCostDistributionCodesV1.SourceUnavailable);
            default:
                return ProductionCostUnavailable(orderId, orderStatus, orderVersion,
                    ManufacturingCostTraceCodesV1.CostAnswerInvalid);
        }
    }

    private static ProductionCostViewV1 ProductionCostWithoutFigures(Guid orderId, string orderStatus,
        long orderVersion, string status, ManufacturingProductionCostResultV2 result) =>
        new(ProductionCostViewV1.CurrentContractVersion, orderId, orderStatus, orderVersion, status, null, null, null,
            null, null, null, null, null, [], [],
            result.IssueReasons.Select(static reason =>
                new ProductionCostIssueReasonViewV1(reason.IssueMovementId, reason.ReasonCode)).ToArray(),
            result.NoteCodes.ToArray(), result.ReasonCode ?? ManufacturingCostTraceCodesV1.CostAnswerInvalid);

    private static ProductionCostViewV1 ProductionCostUnavailable(Guid orderId, string orderStatus, long orderVersion,
        string reasonCode) =>
        new(ProductionCostViewV1.CurrentContractVersion, orderId, orderStatus, orderVersion,
            ManufacturingCostTraceCodesV1.CostUnavailable, null, null, null, null, null, null, null, null, [], [], [],
            [], reasonCode);
}
