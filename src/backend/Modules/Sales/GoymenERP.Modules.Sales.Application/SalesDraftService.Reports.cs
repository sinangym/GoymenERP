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
/// (<see cref="RecordedShipmentsAsync"/>, the same validation), and its open quantity approved − shipped, never below zero.
/// SAT-D130 (EK18): a line shipped short is judged by <see cref="SalesRealizationCalculator"/> with the Settings SAT-D5 tolerance of
/// the order's business date (<see cref="ToleranceAsync"/>, the API-08 read) — "Tamamlandı" within the tolerance has nothing open;
/// an unreadable tolerance makes the line unmeasured (never open, never closed). Lines are
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
        if (query.Cursor is not null)
        {
            // While there is no source no page was ever issued, so no cursor can be one of this report's (consistently refused).
            throw new SalesDraftServiceException(400, SalesDraftProblemCodesV1.InvalidCursor);
        }

        // The variance is prices: the narrow price read is required, not merely asked (nothing is disclosed without it).
        if (await DiscloseAsync(scope, cancellationToken) != SalesPriceDisclosure.Visible)
        {
            throw new SalesDraftServiceException(403, "forbidden");
        }

        // No 25 / SAT-D92: the variance is recorded by the invoice owner (S5 API-32), which does not exist yet. No source is
        // "kaynak yok" — never an empty page, which would claim that no invoice was priced differently.
        return new SalesPriceVarianceReportResponse(SalesPriceVarianceSourceStateView.NoSource, null, null);
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
        SalesOrderTotals totals;
        try
        {
            totals = SalesOrderTotalsCalculator.Calculate(
                [.. quote.Lines.Select(line => new SalesTotalsLineInput(line.Id, line.LineNo, line.OrderUnit.UnitId, line.Quantity.Value,
                    new CommercialDraftFields(line.UnitPrice, line.PriceUnitId, line.DiscountPercent)))],
                quote.Header.CurrencyId,
                Math.Min(quote.Money.Scale, Money.MaximumScale));
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException or SalesRuleException)
        {
            // A stored quote the calculator cannot read is no amount to show (never a 500, never a guessed amount).
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.ReferenceProviderInvalid);
        }

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
