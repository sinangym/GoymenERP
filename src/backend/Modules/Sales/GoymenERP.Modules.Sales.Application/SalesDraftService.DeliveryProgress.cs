using System.Globalization;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.Quantities;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>
/// AT-024 ("Kısmi teslimat sipariş kalanını doğru tutar"): per order line the delivered and the remaining quantity, from
/// the delivery owner's RECORDED deliveries and approved physical returns (Logistics, through its typed owner ports) — never from
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

        // KLN-D100: an approved physical customer return corrects the delivered counter. A missing owner is
        // not proof of zero returns; use the existing return reader and its exact company/line/unit validation.
        var returnsOwner = customerReturns
            ?? throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.CustomerReturnProviderUnavailable);
        SalesOrderCustomerReturnReadAnswerV1? returned;
        try
        {
            returned = await GuardedAsync(scope, bindings.ReadPermission,
                () => returnsOwner.ReadApprovedReturnsAsync(draft.Id, cancellationToken), cancellationToken);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or SalesDraftServiceException))
        {
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.CustomerReturnProviderUnavailable);
        }

        var invoices = await ReadInvoicedQuantitiesAsync(scope, draft, cancellationToken);
        return DeliveryProgress(scope, draft, answer, CustomerReturns(scope, draft, returned), invoices);
    }

    private static SalesOrderDraftDeliveryProgressResponse DeliveryProgress(Scope scope, SalesOrderDraft draft,
        SalesOrderDeliveryReadAnswerV1? answer, SalesOrderDraftCustomerReturnsResponse approvedReturns,
        (IReadOnlyDictionary<Guid, decimal> Quantities, string Version)? invoices)
    {
        var recorded = RecordedDeliveries(scope, draft, answer);
        var returned = approvedReturns.Lines.ToDictionary(line => line.LineId);
        return new SalesOrderDraftDeliveryProgressResponse(draft.Version.ToString(CultureInfo.InvariantCulture),
            [.. draft.Lines.OrderBy(line => line.LineNo).Select(line =>
            {
                var totals = recorded.GetValueOrDefault(line.Id);
                return LineProgress(line, totals, ShippedByOf(answer!, line.Id, totals.Shipped), returned[line.Id].ApprovedReturned,
                    invoices?.Quantities.GetValueOrDefault(line.Id), invoices?.Version);
            })]);
    }

    /// <summary>
    /// F-006 (B-33; SAT-D216/SAT-D222): the shippers of the line's counted definite shipments, read with the over-delivery
    /// approval's own rule (<see cref="Shippers"/>) so the screen and the approval never disagree on who shipped — ordinal and
    /// distinct; empty when nothing is shipped; <c>null</c> when the owner does not name every shipper (shown as unknown, never
    /// as a partial list, and never a malformed answer: a share recorded before the owner kept its actor is a valid order).
    /// </summary>
    private static IReadOnlyList<string>? ShippedByOf(SalesOrderDeliveryReadAnswerV1 answer, Guid lineId, decimal shipped)
    {
        if (shipped == 0m)
        {
            return [];
        }

        var named = Shippers(answer, lineId);
        return named is null ? null : [.. named.Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// The owner's answer validated and summed per Sales line in the line's order unit (shared by AT-024 and the API-08
    /// realization axis): an answer of another company or order, an unknown line, a negative total, delivered above shipped,
    /// a definite total in another unit or a sum that overflows is a malformed owner answer (503 reference_provider_invalid).
    /// </summary>
    private static Dictionary<Guid, (decimal Shipped, decimal Delivered)> RecordedDeliveries(Scope scope, SalesOrderDraft draft,
        SalesOrderDeliveryReadAnswerV1? answer)
    {
        if (answer is null || answer.Lines is null || answer.CompanyId != scope.CompanyId || answer.OrderId != draft.Id)
        {
            throw DeliveryAnswerInvalid();
        }

        // SAT-D167 / SAT-D211 (K3 review A P1-2): an owner may answer a passive line of the order (it shipped under the accepted
        // revision, or was answered empty); it is the order's line, never a malformed answer. Only the active lines are reported.
        var lines = draft.AllLines.ToDictionary(line => line.Id);
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

            try
            {
                recorded[line.Id] = recorded.TryGetValue(line.Id, out var sum)
                    ? (sum.Shipped + entry.ShippedQuantity, sum.Delivered + entry.DeliveredQuantity)
                    : (entry.ShippedQuantity, entry.DeliveredQuantity);
            }
            catch (OverflowException)
            {
                // A malformed owner answer must not escape as a server error or be shown as a plausible quantity.
                throw DeliveryAnswerInvalid();
            }
        }

        return recorded;
    }

    private static SalesDraftLineDeliveryProgressV1 LineProgress(SalesOrderDraftLine line, (decimal Shipped, decimal Delivered) recorded,
        IReadOnlyList<string>? shippedBy, string approvedReturned, decimal? invoiced, string? invoiceSourceVersion)
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

        // KLN-D100 + KLN-D106: the one open-quantity rule (ordered − delivered, invoiced never deciding), where "delivered"
        // counts at the shipment's stock-out approval — shipment and delivery are one counter and no customer POD is awaited. The
        // owner's definite shipped total is therefore the delivered figure of the rule; its separate delivery record stays a
        // displayed fact. ApprovedReturned is already the return owner's exact, validated quantity in this line's own unit.
        SalesOrderLineOpenQuantity open;
        try
        {
            var returned = Quantity.Of(decimal.Parse(approvedReturned, CultureInfo.InvariantCulture), unit);
            open = SalesOrderLineOpenQuantity.Calculate(line.Quantity, shipped, returned,
                invoiced is { } quantity ? Quantity.Of(quantity, unit) : null);
        }
        catch (SalesRuleException)
        {
            // Inconsistent owner snapshots, such as returns exceeding shipped kg, cannot yield a plausible remainder.
            throw CustomerReturnAnswerInvalid();
        }
        return new SalesDraftLineDeliveryProgressV1(line.Id, line.LineNo, line.OrderUnit.UnitId, line.QuantityText,
            QuantityText(shipped), QuantityText(delivered), QuantityText(open.Open), QuantityText(open.Excess), shippedBy,
            open.Invoiced is { } billed && open.DeliveredNotInvoiced is { } unbilled && invoiceSourceVersion is not null
                ? new(QuantityText(billed), QuantityText(unbilled), invoiceSourceVersion) : null);
    }

    private async Task<(IReadOnlyDictionary<Guid, decimal> Quantities, string Version)?> ReadInvoicedQuantitiesAsync(
        Scope scope, SalesOrderDraft draft, CancellationToken token)
    {
        if (invoicedQuantities is null) return null;
        SalesOrderInvoicedQuantityReadV1? source;
        try
        {
            source = await GuardedAsync(scope, bindings.ReadPermission,
                () => invoicedQuantities.ReadAsync(scope.CompanyId, draft.Id,
                    async ct => { await RequireStillAsync(scope, bindings.ReadPermission, ct); }, token), token);
        }
        catch (OperationCanceledException) { throw; }
        catch (SalesDraftServiceException error) when (error.Status is 401 or 403) { throw; }
        catch { await RequireStillAsync(scope, bindings.ReadPermission, token); return null; }
        var prefix = ISalesOrderInvoicedQuantityReadPortV1.SourceVersionPrefix;
        if (source is null || source.CompanyId != scope.CompanyId || source.SalesOrderId != draft.Id
            || source.Outcome != SalesOrderInvoicedQuantityReadOutcomeV1.Available || source.Lines is null
            || source.SourceVersion is not { } version || !version.StartsWith(prefix, StringComparison.Ordinal)
            || version.Length != prefix.Length + 64 || !version[prefix.Length..].All(character =>
                character is >= '0' and <= '9' or >= 'a' and <= 'f')) return null;
        var actualLines = draft.AllLines.ToDictionary(line => line.Id);
        var claims = new HashSet<(string, Guid, Guid)>();
        var quantities = new Dictionary<Guid, decimal>();
        try
        {
            foreach (var item in source.Lines)
            {
                if (item is null || item.InvoiceId == Guid.Empty || item.InvoiceLineId == Guid.Empty
                    || item.InvoiceKind is not ("Normal" or "Subcontract") || item.SourceVersion is not { Length: 64 }
                    || !item.SourceVersion.All(Uri.IsHexDigit) || !claims.Add((item.InvoiceKind, item.InvoiceId, item.InvoiceLineId))
                    || !actualLines.TryGetValue(item.SalesOrderLineId, out var line) || item.UnitId != line.OrderUnit.UnitId
                    || item.Quantity <= 0m) return null;
                _ = Quantity.Of(item.Quantity, line.Quantity.Unit);
                quantities[line.Id] = quantities.GetValueOrDefault(line.Id) + item.Quantity;
                _ = Quantity.Of(quantities[line.Id], line.Quantity.Unit);
            }
        }
        catch (Exception error) when (error is ArgumentException or OverflowException) { return null; }
        return (quantities, version);
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
