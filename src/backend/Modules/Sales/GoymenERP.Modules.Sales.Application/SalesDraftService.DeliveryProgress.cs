using System.Globalization;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.Quantities;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>
/// AT-024 ("Kısmi teslimat sipariş kalanını doğru tutar"): per order line the delivered and the remaining quantity, from
/// the delivery owner's RECORDED deliveries (Logistics, through <see cref="ISalesOrderDeliveryReadPortV1"/>) — never from
/// another module's tables and never a stored Sales copy. Read-only: no attempt, audit or version effect in either module.
/// <list type="bullet">
/// <item><b>Fences</b> (the draft read's): the draft read name before anything is read, after the draft read and after
/// the owner's answer; the owner port carries no authority (LOG-4 D6).</item>
/// <item><b>Company</b>: the draft is read under the tenant filter (another company's is 404, never 403) and the owner is
/// asked only for that draft; an answer of another company or order is a malformed owner answer (503).</item>
/// <item><b>Exactness</b> (Ö2): every total is taken in the line's own order unit, never converted and never rounded; a
/// total recorded in another unit, above the unit's scale, negative, or delivered above shipped is malformed (503) —
/// the screen then shows no number rather than a wrong one.</item>
/// <item><b>No tolerance</b> (OPEN-004 open): remaining = ordered − delivered, not below zero; excess = what was delivered
/// above the order. Neither is judged: no completion, warning or approval state is derived.</item>
/// </list>
/// </summary>
public sealed partial class SalesDraftService
{
    public async Task<SalesOrderDraftDeliveryProgressResponse> GetDraftDeliveryProgressAsync(Guid draftId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequireAsync(bindings.ReadPermission, cancellationToken);
        if (draftId == Guid.Empty)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        SalesOrderDraft? draft;
        try
        {
            draft = await GuardedAsync(scope, bindings.ReadPermission,
                () => store.FindDraftAsync(draftId, cancellationToken), cancellationToken);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        if (draft is null)
        {
            // Another company's draft is null under the tenant filter: 404, and its owner is never asked.
            throw new SalesDraftServiceException(404, "not_found");
        }

        // Not composed (the owner registers the port in its own module): 503, never an assumed "nothing delivered".
        var owner = deliveries ?? throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.DeliveryProviderUnavailable);
        SalesOrderDeliveryReadAnswerV1? answer;
        try
        {
            answer = await GuardedAsync(scope, bindings.ReadPermission,
                () => owner.ReadOrderDeliveriesAsync(draft.Id, cancellationToken), cancellationToken);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or SalesDraftServiceException))
        {
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.DeliveryProviderUnavailable);
        }

        return DeliveryProgress(scope, draft, answer);
    }

    private static SalesOrderDraftDeliveryProgressResponse DeliveryProgress(Scope scope, SalesOrderDraft draft,
        SalesOrderDeliveryReadAnswerV1? answer)
    {
        if (answer is null || answer.Lines is null || answer.CompanyId != scope.CompanyId || answer.OrderId != draft.Id)
        {
            throw DeliveryAnswerInvalid();
        }

        var lines = draft.Lines.ToDictionary(line => line.Id);
        var recorded = new Dictionary<Guid, (decimal Shipped, decimal Delivered)>();
        foreach (var entry in answer.Lines)
        {
            if (entry is null || !lines.TryGetValue(entry.OrderLineId, out var line)
                || entry.ShippedQuantity < 0m || entry.DeliveredQuantity < 0m || entry.DeliveredQuantity > entry.ShippedQuantity)
            {
                throw DeliveryAnswerInvalid();
            }

            if (entry.ShippedQuantity == 0m)
            {
                // Nothing definite in this unit yet (an instructed or loaded line): it adds nothing, whatever its unit.
                continue;
            }

            if (entry.UnitId != line.OrderUnit.UnitId)
            {
                // No conversion: a total recorded in another unit cannot be added to the order unit's quantity.
                throw DeliveryAnswerInvalid();
            }

            recorded[line.Id] = recorded.TryGetValue(line.Id, out var sum)
                ? (sum.Shipped + entry.ShippedQuantity, sum.Delivered + entry.DeliveredQuantity)
                : (entry.ShippedQuantity, entry.DeliveredQuantity);
        }

        return new SalesOrderDraftDeliveryProgressResponse(draft.Version.ToString(CultureInfo.InvariantCulture),
            [.. draft.Lines.OrderBy(line => line.LineNo).Select(line => LineProgress(line, recorded.GetValueOrDefault(line.Id)))]);
    }

    private static SalesDraftLineDeliveryProgressV1 LineProgress(SalesOrderDraftLine line, (decimal Shipped, decimal Delivered) recorded)
    {
        var unit = line.Quantity.Unit;
        Quantity shipped;
        Quantity delivered;
        try
        {
            // Ö2 in the line's own dimension: kilograms ≤ 3 decimals, pieces whole — never rounded to fit.
            shipped = Quantity.Of(recorded.Shipped, unit);
            delivered = Quantity.Of(recorded.Delivered, unit);
        }
        catch (ArgumentException)
        {
            throw DeliveryAnswerInvalid();
        }

        var over = delivered.IsGreaterThan(line.Quantity);
        var remaining = over ? Quantity.Zero(unit) : line.Quantity.Subtract(delivered);
        var excess = over ? delivered.Subtract(line.Quantity) : Quantity.Zero(unit);
        return new SalesDraftLineDeliveryProgressV1(line.Id, line.LineNo, line.OrderUnit.UnitId, line.QuantityText,
            QuantityText(shipped), QuantityText(delivered), QuantityText(remaining), QuantityText(excess));
    }

    /// <summary>
    /// Plain invariant text of an exact quantity with trailing zeros dropped (Ö2: equal quantities print equally) — a
    /// difference such as 1.25 − 0.75 keeps its operands' scale, so it is re-taken through the rule before it is printed.
    /// </summary>
    private static string QuantityText(Quantity quantity) =>
        Quantity.Of(quantity.Value, quantity.Unit).Value.ToString(CultureInfo.InvariantCulture);

    private static SalesDraftServiceException DeliveryAnswerInvalid() =>
        new(503, SalesDraftProblemCodesV1.ReferenceProviderInvalid);
}
