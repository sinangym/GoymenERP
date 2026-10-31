using System.Globalization;
using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.Quotes;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>
/// API-41 / API-42 (Satış hazırlık planı v1 §3.4, UI-80): the three Sales reports under their own read name
/// (<c>sales.reports.read</c>). Read-only in every module: no attempt, audit event, version or cache effect.
/// <list type="bullet">
/// <item><b>Fences</b>: the report read is measured fresh — granted, company-wide, this company and actor — before anything is read
/// and after every awaited store and owner read (the Sales read fence). Every query is the Sales store's under the tenant filter;
/// another company's orders and quotes never appear.</item>
/// <item><b>API-41 open orders (KPI No 124)</b>: an order is open while its lifecycle is <c>Draft</c> (neither cancelled nor
/// closed) and it is approved — its current revision, or an earlier approved revision archived by one awaiting re-approval. A line's
/// approved quantity is the API-08 measurement basis (the current lines, or the last approved revision's while one awaits
/// re-approval; <see cref="Basis"/>), its shipped total the Logistics owner's recorded total through the AT-024 port
/// (<see cref="RecordedShipmentsAsync"/>, the same validation), and its open quantity approved − shipped, never below zero. Lines are
/// grouped by customer account, product and order unit: quantities are added only within one unit (Ö2, exact, never converted or
/// rounded). A line whose basis cannot be taken exactly is listed as unmeasured and its group is not <c>complete</c>; an unreadable
/// delivery owner is 503 — never a zero.</item>
/// <item><b>API-42 lost opportunities (SAT-D25)</b>: lost quotes, latest loss first, with the Settings list reason snapshot and the
/// explanation as recorded; the net amount is <see cref="SalesOrderTotalsCalculator"/>'s answer over the quote's lines and money
/// profile snapshot, in the quote's currency (never added across quotes or currencies), disclosed only under
/// <c>sales.orders.price.read</c>.</item>
/// <item><b>API-42 price variance (No 25)</b>: there is no Sales invoice owner yet (S5 API-32), so the answer is <c>NoSource</c> —
/// never an empty list that would read as "no variance". It is prices: the price read is required (403 otherwise).</item>
/// </list>
/// </summary>
public sealed partial class SalesDraftService
{
    public async Task<SalesOpenOrderReportResponse> GetOpenOrderReportAsync(SalesOpenOrderReportQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var permission = bindings.ReportReadPermission;
        var scope = await RequireAsync(permission, cancellationToken);
        RequireReportPage(query.PageSize, query.Cursor);
        RequireRange(query.From, query.To);
        RequireAccount(query.CompanyAccountId);
        var store = reports ?? throw new SalesDraftServiceException(503, "service_unavailable");

        SalesOpenOrderGroupPage page;
        try
        {
            page = await GuardedAsync(scope, permission, () => store.ListOpenOrderGroupsAsync(
                new SalesOpenOrderReportFilter(query.From, query.To, query.CompanyAccountId), query.PageSize, query.Cursor,
                SalesReportApiV1.MaximumOrdersPerPage, cancellationToken), cancellationToken);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        if (page is null || page.Groups is null || page.Orders is null || page.Groups.Count > query.PageSize
            || page.NextCursor is { Length: 0 })
        {
            throw new SalesDraftServiceException(503, "service_unavailable");
        }

        if (page.OrdersExceeded)
        {
            // A technical read bound (each order is one owner read): refused, never a cut group.
            throw new SalesDraftServiceException(422, SalesReportProblemCodesV1.ReportScopeTooLarge,
                [new("pageSize", SalesReportProblemCodesV1.ReportScopeTooLarge)]);
        }

        if (page.Groups.Count == 0)
        {
            return new SalesOpenOrderReportResponse(SalesOpenOrderMeasureView.ApprovedMinusShipped, [], page.NextCursor);
        }

        var keys = page.Groups.ToHashSet();
        var measured = new Dictionary<SalesOpenOrderGroupKey, List<OpenLine>>();
        var unmeasured = new Dictionary<SalesOpenOrderGroupKey, List<SalesOpenOrderUnmeasuredLineView>>();
        foreach (var draft in page.Orders.OrderBy(order => order.Header.BusinessDate).ThenBy(order => order.DocumentNo, StringComparer.Ordinal)
            .ThenBy(order => order.Id))
        {
            if (draft is null || draft.CompanyId != scope.CompanyId)
            {
                throw new SalesDraftServiceException(503, "service_unavailable");
            }

            var lines = draft.Lines.OrderBy(line => line.LineNo)
                .Select(line => (Line: line, Key: new SalesOpenOrderGroupKey(draft.Header.CompanyAccountId, draft.Header.PartyId,
                    line.Product.ProductId, line.OrderUnit.UnitId)))
                .Where(entry => keys.Contains(entry.Key))
                .ToArray();
            if (lines.Length == 0)
            {
                continue;
            }

            if (Basis(draft) is not { } basis)
            {
                // A revision awaits re-approval and the approved quantities cannot be taken exactly: nothing is measured and no
                // owner is asked (the API-08 rule), but the line is named so the group's sum is never read as complete.
                foreach (var (line, key) in lines)
                {
                    Add(unmeasured, key, new SalesOpenOrderUnmeasuredLineView(draft.Id, draft.DocumentNo, line.Id, line.LineNo,
                        SalesOpenOrderUnmeasuredReasonView.PendingReapproval));
                }

                continue;
            }

            if (deliveries is null)
            {
                // Not composed (the owner registers the port in its own module): 503, never an assumed "nothing shipped".
                throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.DeliveryProviderUnavailable);
            }

            var recorded = await RecordedShipmentsAsync(scope, permission, draft, cancellationToken)
                ?? throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.DeliveryProviderUnavailable);
            foreach (var (line, key) in lines)
            {
                var approved = basis.Lines[line.Id].Quantity;
                var shipped = recorded[line.Id].Shipped;
                Quantity remaining;
                try
                {
                    remaining = shipped.CompareTo(approved) >= 0 ? Quantity.Zero(approved.Unit) : approved.Subtract(shipped);
                }
                catch (Exception exception) when (exception is ArgumentException or OverflowException or SalesRuleException)
                {
                    throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.ReferenceProviderInvalid);
                }

                Add(measured, key, new OpenLine(draft, line, basis.Revision, approved, shipped, remaining));
            }
        }

        var customers = await ReportCustomersAsync(scope, permission, page.Groups.Select(group => group.PartyId), cancellationToken);
        var groups = new List<SalesOpenOrderGroupView>(page.Groups.Count);
        foreach (var key in page.Groups)
        {
            var lines = measured.GetValueOrDefault(key) ?? [];
            var skipped = unmeasured.GetValueOrDefault(key) ?? [];
            groups.Add(OpenOrderGroup(key, ReportCustomer(customers, key.PartyId, key.CompanyAccountId), lines, skipped, page.Orders));
        }

        return new SalesOpenOrderReportResponse(SalesOpenOrderMeasureView.ApprovedMinusShipped, groups, page.NextCursor);
    }

    public async Task<SalesLostOpportunityReportResponse> GetLostOpportunityReportAsync(SalesLostOpportunityReportQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var permission = bindings.ReportReadPermission;
        var scope = await RequireAsync(permission, cancellationToken);
        RequireReportPage(query.PageSize, query.Cursor);
        RequireRange(query.From, query.To);
        RequireAccount(query.CompanyAccountId);
        if (query.ReasonId == Guid.Empty)
        {
            throw new SalesDraftServiceException(400, "validation_failed", [new("reasonId", "validation_failed")]);
        }

        var store = reports ?? throw new SalesDraftServiceException(503, "service_unavailable");
        var filter = new SalesLostQuoteReportFilter(
            query.From is { } from ? DayStartUtc(from) : null,
            query.To is { } to ? DayStartUtc(NextDay(to)) : null,
            query.CompanyAccountId,
            query.ReasonId);

        SalesQuotePage page;
        try
        {
            page = await GuardedAsync(scope, permission, () => store.ListLostQuotesAsync(filter, query.PageSize, query.Cursor, cancellationToken),
                cancellationToken);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        if (page is null || page.Items is null || page.Items.Count > query.PageSize || page.NextCursor is { Length: 0 }
            || page.Items.Any(quote => quote is null || quote.CompanyId != scope.CompanyId || quote.State != SalesQuoteState.Lost
                || quote.Loss is null))
        {
            throw new SalesDraftServiceException(503, "service_unavailable");
        }

        var customers = await ReportCustomersAsync(scope, permission, page.Items.Select(quote => quote.Header.PartyId), cancellationToken);
        // The price read is asked last, after every other fence: the disclosure is the authority's freshest word.
        var disclosure = await DiscloseAsync(scope, cancellationToken);
        return new SalesLostOpportunityReportResponse(disclosure,
            [.. page.Items.Select(quote => LostOpportunity(quote, ReportCustomer(customers, quote.Header.PartyId, quote.Header.CompanyAccountId),
                disclosure))],
            page.NextCursor);
    }

    public async Task<SalesPriceVarianceReportResponse> GetPriceVarianceReportAsync(SalesPriceVarianceReportQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequireAsync(bindings.ReportReadPermission, cancellationToken);
        RequireReportPage(query.PageSize, query.Cursor);
        RequireRange(query.From, query.To);
        RequireAccount(query.CompanyAccountId);
        // The variance is prices: the narrow price read is required, not merely asked (nothing is disclosed without it).
        if (await DiscloseAsync(scope, cancellationToken) != SalesPriceDisclosure.Visible)
        {
            throw new SalesDraftServiceException(403, "forbidden");
        }

        // No 25 / SAT-D92: the variance is recorded by the invoice owner (S5 API-32), which does not exist yet. No source is
        // "kaynak yok" — never an empty page, which would claim that no invoice was priced differently.
        return new SalesPriceVarianceReportResponse(SalesPriceVarianceSourceStateView.NoSource, null, null);
    }

    // ---- open orders ----------------------------------------------------------------------------------------------------------

    private sealed record OpenLine(SalesOrderDraft Order, SalesOrderDraftLine Line, string BasisRevision, Quantity Approved, Quantity Shipped,
        Quantity Remaining);

    private static void Add<T>(Dictionary<SalesOpenOrderGroupKey, List<T>> map, SalesOpenOrderGroupKey key, T value)
    {
        if (!map.TryGetValue(key, out var list))
        {
            list = [];
            map.Add(key, list);
        }

        list.Add(value);
    }

    /// <summary>
    /// One group's view: the sums are exact additions in the group's single unit (one unit id is one Settings unit of one dimension,
    /// checked again per line); the product and unit texts are the order line's snapshots. A group with no measured line (only
    /// unmeasured ones) shows the product of its first open line and no sums of zero are claimed complete.
    /// </summary>
    private static SalesOpenOrderGroupView OpenOrderGroup(SalesOpenOrderGroupKey key, SalesReportCustomerView customer, List<OpenLine> lines,
        List<SalesOpenOrderUnmeasuredLineView> unmeasured, IReadOnlyList<SalesOrderDraft> orders)
    {
        var sample = lines.Count > 0
            ? lines[0].Line
            : orders.Where(order => unmeasured.Any(line => line.OrderId == order.Id))
                .SelectMany(order => order.Lines)
                .FirstOrDefault(line => unmeasured.Any(entry => entry.LineId == line.Id))
            ?? throw new SalesDraftServiceException(503, "service_unavailable");
        var unit = sample.Quantity.Unit;
        var approved = Quantity.Zero(unit);
        var shipped = Quantity.Zero(unit);
        var remaining = Quantity.Zero(unit);
        try
        {
            foreach (var line in lines)
            {
                if (line.Line.Product.ProductId != key.ProductId || line.Line.OrderUnit.UnitId != key.UnitId)
                {
                    throw new SalesDraftServiceException(503, "service_unavailable");
                }

                approved = approved.Add(line.Approved);
                shipped = shipped.Add(line.Shipped);
                remaining = remaining.Add(line.Remaining);
            }
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException or SalesRuleException)
        {
            // Another dimension under the same unit id, or a sum beyond the exact range: no number rather than a wrong one.
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.ReferenceProviderInvalid);
        }

        return new SalesOpenOrderGroupView(
            customer,
            key.ProductId,
            sample.Product.Code,
            sample.Product.Name,
            key.UnitId,
            sample.OrderUnit.UnitCode,
            lines.Select(line => line.Order.Id).Distinct().Count(),
            QuantityText(approved),
            QuantityText(shipped),
            QuantityText(remaining),
            Complete: unmeasured.Count == 0,
            [.. lines.Select(line => new SalesOpenOrderLineView(line.Order.Id, line.Order.DocumentNo, line.Order.Header.BusinessDate,
                line.Line.RequestedDeliveryDate ?? line.Order.Header.RequestedDeliveryDate, line.BasisRevision, line.Line.Id, line.Line.LineNo,
                QuantityText(line.Approved), QuantityText(line.Shipped), QuantityText(line.Remaining)))],
            unmeasured);
    }

    // ---- lost opportunities ---------------------------------------------------------------------------------------------------

    private static SalesLostOpportunityView LostOpportunity(SalesQuote quote, SalesReportCustomerView customer, SalesPriceDisclosure disclosure)
    {
        var loss = quote.Loss!;
        var reason = loss.ListReason is { } entry
            ? new SalesLostReasonView(entry.ReasonId, entry.Code, entry.Label, entry.ProfileId, entry.ProfileVersion)
            : null;
        return new SalesLostOpportunityView(
            quote.Id,
            quote.DocumentNo,
            SalesDraftApiV1.RevisionText(quote.Revision),
            quote.Header.BusinessDate,
            quote.Terms.ValidUntil,
            customer,
            quote.Header.CurrencyId,
            loss.LostAtUtc,
            BusinessDayOf(loss.LostAtUtc),
            loss.LostBy,
            reason,
            loss.Reason,
            quote.Lines.Count,
            disclosure == SalesPriceDisclosure.Visible ? QuoteAmount(quote) : null);
    }

    /// <summary>
    /// The quote's net amount from the ONE totals calculator (API-03 / SAT-D11): the quote's current lines with their quoted
    /// unit price, price unit and discount, in the quote currency at its money profile snapshot's amount scale. No VAT master exists
    /// (MOD-03), so only the net is shown.
    /// </summary>
    private static SalesLostAmountView QuoteAmount(SalesQuote quote)
    {
        var totals = SalesOrderTotalsCalculator.Calculate(
            [.. quote.Lines.Select(line => new SalesTotalsLineInput(line.Id, line.LineNo, line.OrderUnit.UnitId, line.Quantity.Value,
                new CommercialDraftFields(line.UnitPrice, line.PriceUnitId, line.DiscountPercent)))],
            quote.Header.CurrencyId,
            Math.Min(quote.Money.Scale, Money.MaximumScale));
        var document = totals.Document;
        return new SalesLostAmountView(DocumentStateText(document.State), totals.CurrencyId, totals.AmountScale, Amount(document.NetAmount),
            document.UnpricedLineCount, document.PriceUnitNotOrderUnitLineCount);
    }

    // ---- shared ---------------------------------------------------------------------------------------------------------------

    private static void RequireReportPage(int pageSize, string? cursor)
    {
        if (pageSize is < 1 or > SalesReportApiV1.MaximumPageSize || cursor is { Length: 0 or > MaximumCursorLength }
            || cursor is not null && string.IsNullOrWhiteSpace(cursor))
        {
            throw new SalesDraftServiceException(400, "validation_failed");
        }
    }

    private static void RequireRange(DateOnly? from, DateOnly? to)
    {
        if (from is { } start && to is { } end && start > end)
        {
            throw new SalesDraftServiceException(400, "validation_failed", [new("to", "validation_failed")]);
        }
    }

    private static void RequireAccount(Guid? companyAccountId)
    {
        if (companyAccountId == Guid.Empty)
        {
            throw new SalesDraftServiceException(400, "validation_failed", [new("companyAccountId", "validation_failed")]);
        }
    }

    private static DateOnly NextDay(DateOnly day) => day == DateOnly.MaxValue
        ? throw new SalesDraftServiceException(400, "validation_failed", [new("to", "validation_failed")])
        : day.AddDays(1);

    /// <summary>SAT-D10: the Europe/Istanbul day's first instant; without the zone nothing is guessed (503, never UTC).</summary>
    private static DateTimeOffset DayStartUtc(DateOnly day)
    {
        try
        {
            return SalesBusinessDay.StartOfDayUtc(day);
        }
        catch (SalesBusinessDayUnavailableException)
        {
            throw new SalesDraftServiceException(503, SalesQuoteProblemCodesV1.BusinessDayUnavailable);
        }
    }

    private static DateOnly BusinessDayOf(DateTimeOffset instant)
    {
        try
        {
            return SalesBusinessDay.DateOf(instant);
        }
        catch (SalesBusinessDayUnavailableException)
        {
            throw new SalesDraftServiceException(503, SalesQuoteProblemCodesV1.BusinessDayUnavailable);
        }
    }

    /// <summary>The Cari owner's batch bound (its own maximum).</summary>
    private const int MaximumPartyBatch = 100;

    /// <summary>
    /// The customers as the Cari owner presents them now, under the report fence after each batch. Not composed, refused for a
    /// reason other than access, or malformed is 503 (never a guessed name); an access refusal is 403.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, SalesPartyDisplayV1>> ReportCustomersAsync(Scope scope, string? permission,
        IEnumerable<Guid> partyIds, CancellationToken cancellationToken)
    {
        var ids = partyIds.Distinct().ToArray();
        var byId = new Dictionary<Guid, SalesPartyDisplayV1>();
        if (ids.Length == 0)
        {
            return byId;
        }

        if (ids.Any(id => id == Guid.Empty))
        {
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.ReferenceProviderInvalid);
        }

        var source = partyDisplays ?? throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.CounterpartyProviderUnavailable);
        foreach (var batch in ids.Chunk(MaximumPartyBatch))
        {
            SalesPartyDisplayBatchAnswerV1? answer;
            try
            {
                answer = await GuardedAsync(scope, permission, () => source.GetAuthorizedBatchAsync(
                    new SalesPartyDisplayBatchRequestV1(scope.CompanyId, scope.ActorId, batch), cancellationToken), cancellationToken);
            }
            catch (Exception exception) when (exception is not (OperationCanceledException or SalesDraftServiceException))
            {
                throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.CounterpartyProviderUnavailable);
            }

            if (answer?.Outcome == SalesPartyDisplayBatchOutcomeV1.Denied)
            {
                throw new SalesDraftServiceException(403, "forbidden");
            }

            if (answer is null || answer.Outcome != SalesPartyDisplayBatchOutcomeV1.Success || answer.Items is null
                || answer.Items.Count != batch.Length)
            {
                throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.CounterpartyProviderUnavailable);
            }

            var requested = batch.ToHashSet();
            foreach (var item in answer.Items)
            {
                if (item is null || !requested.Contains(item.PartyId) || string.IsNullOrWhiteSpace(item.Code)
                    || string.IsNullOrWhiteSpace(item.LegalName) || item.TradeName is not null && string.IsNullOrWhiteSpace(item.TradeName)
                    || !byId.TryAdd(item.PartyId, item))
                {
                    throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.CounterpartyProviderUnavailable);
                }
            }
        }

        return byId;
    }

    private static SalesReportCustomerView ReportCustomer(IReadOnlyDictionary<Guid, SalesPartyDisplayV1> customers, Guid partyId,
        Guid companyAccountId) => customers.TryGetValue(partyId, out var display)
        ? new SalesReportCustomerView(partyId, companyAccountId, display.Code, display.LegalName, display.TradeName)
        : throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.CounterpartyProviderUnavailable);
}
