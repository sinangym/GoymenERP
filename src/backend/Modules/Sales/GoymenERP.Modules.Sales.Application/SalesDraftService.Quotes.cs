using System.Globalization;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.Quotes;
using GoymenERP.Modules.Sales.Domain.Requests;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>
/// API-22 / API-23 / API-24 (plan §3.2, S-01; delegation No 160, 162, 163; master §7.2): the sales quote use cases on the
/// draft service's fence and owner reads.
/// <list type="bullet">
/// <item><b>Create / revision:</b> <c>sales.quotes.write</c>; owners in the draft's order — Cari account, the Settings money
/// profile of the currency for the ORDER operation (the prices become the order's), per line Product eligibility and the
/// order-unit profile. No 163: the named requests must be this company's, of the SAME customer, not rejected and not
/// covered by another live quote.</item>
/// <item><b>Loss:</b> <c>sales.quotes.write</c>; a free-text reason (SAT-D25's closed Settings list is a follow-up).</item>
/// <item><b>Conversion (No 162):</b> <c>sales.quotes.write</c> AND <c>sales.drafts.create</c>; the quote's current revision
/// becomes the order through the draft's OWN creation path (the same owners, rules, price fence and idempotency) with the
/// quote's price/quantity snapshot; the one save records the source quote (one order per quote, enforced by the database)
/// and the quote's conversion. No 160: every request the quote covers must be approved.</item>
/// <item><b>Prices on the wire</b> are disclosed under the order's narrow price read, as on the order detail.</item>
/// </list>
/// </summary>
public sealed partial class SalesDraftService
{
    public async Task<SalesQuoteResponse> CreateQuoteAsync(CreateSalesQuoteRequest request, SalesCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();

        var permission = bindings.QuoteWritePermission;
        var scope = await RequireAsync(permission, cancellationToken);
        var store = QuoteStore();
        // A quote price written by an actor who cannot see quote prices is refused before anything is read or recorded.
        var priced = QuotePriceFields(request.Lines);
        if (priced.Count > 0)
        {
            await RequirePriceReadAsync(scope, priced, cancellationToken);
        }

        var requestHash = SalesQuoteHash.Compute(request);
        var envelope = Envelope(scope, metadata, permission!);
        var replay = await FindQuoteReplayAsync(scope, permission, envelope, requestHash, SalesQuoteCommands.CreateV1, cancellationToken);
        if (replay is not null)
        {
            return await QuoteReplayedAsync(scope, replay, cancellationToken);
        }

        var requestIds = request.RequestIds ?? [];
        MoneyProfileSnapshot money;
        List<SalesQuoteLineInput> lines;
        try
        {
            await RequireAccountAsync(scope, permission, request.PartyId, request.CompanyAccountId, cancellationToken);
            await RequireQuotableRequestsAsync(scope, permission, request.PartyId, request.CompanyAccountId, requestIds, cancellationToken);
            money = await ReadMoneyProfileAsync(scope, permission, request.CurrencyId, request.BusinessDate, cancellationToken);
            lines = await ReadQuoteLinesAsync(scope, permission, request.Lines, request.BusinessDate,
                refusal => QuoteTerminalAsync(scope, permission, envelope, requestHash, SalesQuoteCommands.CreateV1, null, refusal, cancellationToken),
                cancellationToken);
        }
        catch (QuoteRaceWon won)
        {
            return won.Response;
        }

        scope = await RequireStillAsync(scope, permission, cancellationToken);
        if (priced.Count > 0)
        {
            await RequirePriceReadAsync(scope, priced, cancellationToken);
        }

        var header = new SalesQuoteHeader(request.PartyId, request.CompanyAccountId, request.CurrencyId, request.BusinessDate);
        var terms = new SalesQuoteTerms(request.ValidUntil, request.RequestedDeliveryDate, request.Note);
        var now = timeProvider.GetUtcNow();
        SalesQuoteStoreResult stored;
        try
        {
            stored = await store.StoreQuoteAsync(envelope, requestHash,
                documentNo => SalesQuote.Create(Guid.NewGuid(), scope.CompanyId, documentNo, header, money, requestIds, terms, lines,
                    scope.ActorId, now),
                cancellationToken);
        }
        catch (SalesRuleException exception)
        {
            throw QuoteRule(exception, request.Lines);
        }
        catch (ArgumentException)
        {
            throw new SalesDraftServiceException(400, "validation_failed");
        }
        catch (SalesDraftStoreException exception) when (exception.Code == SalesDraftStoreCodes.ConcurrentChange)
        {
            // §8: a lost same-key race answers the winner; a request taken by another quote meanwhile (the filtered unique
            // index) or a lost counter race is the caller's concurrent_change.
            var won = await FindQuoteReplayAsync(scope, permission, envelope, requestHash, SalesQuoteCommands.CreateV1, cancellationToken);
            return won is null ? throw Store(exception) : await QuoteReplayedAsync(scope, won, cancellationToken);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        return await QuoteResponseAsync(scope, stored, cancellationToken);
    }

    public async Task<SalesQuoteListResponse> GetQuoteListAsync(SalesQuoteListQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var permission = bindings.QuoteReadPermission;
        var scope = await RequireAsync(permission, cancellationToken);
        var store = QuoteStore();
        var pageSize = query.PageSize == 0 ? DefaultPageSize : query.PageSize;
        if (pageSize is < 1 or > MaximumPageSize || query.Cursor?.Length > MaximumCursorLength
            || (query.Cursor is not null && string.IsNullOrWhiteSpace(query.Cursor)))
        {
            throw new SalesDraftServiceException(400, "validation_failed");
        }

        SalesQuoteState? state = null;
        if (query.State is not null)
        {
            if (!Enum.TryParse<SalesQuoteState>(query.State, ignoreCase: false, out var parsed) || !Enum.IsDefined(parsed)
                || !string.Equals(parsed.ToString(), query.State, StringComparison.Ordinal))
            {
                throw new SalesDraftServiceException(400, "validation_failed", [new("state", "validation_failed")]);
            }

            state = parsed;
        }

        SalesQuotePage page;
        try
        {
            page = await GuardedAsync(scope, permission, () => store.ListQuotesAsync(pageSize, query.Cursor, state, cancellationToken), cancellationToken);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        return new SalesQuoteListResponse([.. page.Items.Select(QuoteSummary)], page.NextCursor);
    }

    public async Task<SalesQuoteDetailView> GetQuoteDetailAsync(Guid quoteId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var permission = bindings.QuoteReadPermission;
        var scope = await RequireAsync(permission, cancellationToken);
        var store = QuoteStore();
        if (quoteId == Guid.Empty)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        SalesQuote? quote;
        try
        {
            quote = await GuardedAsync(scope, permission, () => store.FindQuoteAsync(quoteId, cancellationToken), cancellationToken);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        return quote is null
            ? throw new SalesDraftServiceException(404, "not_found")
            : QuoteDetail(quote, await DiscloseAsync(scope, cancellationToken));
    }

    public async Task<SalesQuoteResponse> ReviseQuoteAsync(Guid quoteId, SalesQuoteRevisionRequest request, SalesCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();
        var permission = bindings.QuoteWritePermission;
        var scope = await RequireAsync(permission, cancellationToken);
        var store = QuoteStore();
        // A revision REPLACES every line, prices included: an actor whose quote prices are withheld would send them back empty
        // and erase them. Such an actor does not revise (403), whatever the lines carry — fail-closed, before anything is read
        // or recorded (SAT-D26: this refusal stays; writing a quote price needs no more than sales.quotes.write).
        await RequirePriceReadAsync(scope, [new("lines", SalesQuoteProblemCodesV1.PriceReadRequired)], cancellationToken);
        var expectedVersion = ExpectedVersion(request.ExpectedVersion);
        var requestHash = SalesQuoteHash.Compute(quoteId, request);
        var envelope = Envelope(scope, metadata, permission!);
        var replay = await FindQuoteReplayAsync(scope, permission, envelope, requestHash, SalesQuoteCommands.ReviseV1, cancellationToken);
        if (replay is not null)
        {
            return await QuoteReplayedAsync(scope, replay, cancellationToken);
        }

        var current = await ReadQuoteAsync(scope, permission, quoteId, cancellationToken);
        List<SalesQuoteLineInput> lines;
        try
        {
            // The lines are read again from their owners on the quote's own business date (the snapshot date).
            lines = await ReadQuoteLinesAsync(scope, permission, request.Lines, current.Header.BusinessDate,
                refusal => QuoteTerminalAsync(scope, permission, envelope, requestHash, SalesQuoteCommands.ReviseV1, quoteId, refusal, cancellationToken),
                cancellationToken);
        }
        catch (QuoteRaceWon won)
        {
            return won.Response;
        }

        // The price visibility is measured again after the owner reads, right before the save.
        await RequirePriceReadAsync(scope, [new("lines", SalesQuoteProblemCodesV1.PriceReadRequired)], cancellationToken);
        var terms = new SalesQuoteTerms(request.ValidUntil, request.RequestedDeliveryDate, request.Note);
        return await UpdateQuoteAsync(scope, permission, envelope, requestHash, SalesQuoteCommands.ReviseV1, quoteId,
            (quote, now) => quote.Revise(expectedVersion, terms, lines, now),
            new SalesDraftStateAudit("sales.quote.revised.v1", "Revise", "Satış teklifi revize edildi"), request.Lines, cancellationToken);
    }

    public async Task<SalesQuoteResponse> RecordQuoteLossAsync(Guid quoteId, SalesQuoteLossRequest request, SalesCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();
        var permission = bindings.QuoteWritePermission;
        var scope = await RequireAsync(permission, cancellationToken);
        QuoteStore();
        var expectedVersion = ExpectedVersion(request.ExpectedVersion);
        var requestHash = SalesQuoteHash.Compute(quoteId, request);
        var envelope = Envelope(scope, metadata, permission!);
        var replay = await FindQuoteReplayAsync(scope, permission, envelope, requestHash, SalesQuoteCommands.LoseV1, cancellationToken);
        if (replay is not null)
        {
            return await QuoteReplayedAsync(scope, replay, cancellationToken);
        }

        return await UpdateQuoteAsync(scope, permission, envelope, requestHash, SalesQuoteCommands.LoseV1, quoteId,
            (quote, now) => quote.RecordLoss(expectedVersion, scope.ActorId, request.Reason, now),
            new SalesDraftStateAudit("sales.quote.lost.v1", "Lose", "Satış teklifi kaybedildi"), null, cancellationToken);
    }

    public async Task<SalesOrderDraftResponse> ConvertQuoteAsync(Guid quoteId, SalesQuoteConversionRequest request, SalesCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();

        // 1. Authority first, both names (ADR-011 §5): the quote's write name and the order's create name — neither confers the other.
        var quotePermission = bindings.QuoteWritePermission;
        var scope = await RequireAsync(quotePermission, cancellationToken);
        scope = await RequireStillAsync(scope, bindings.CreatePermission, cancellationToken);
        var store = QuoteStore();
        var expectedVersion = ExpectedVersion(request.ExpectedVersion);
        var requestHash = SalesQuoteHash.Compute(quoteId, request);
        // The attempt is the ORDER's (a draft attempt under the conversion's own command name): its replay answers the order.
        var envelope = Envelope(scope, metadata, bindings.CreatePermission!);
        var replay = await FindReplayAsync(scope, bindings.CreatePermission, envelope, requestHash, SalesQuoteCommands.ConvertV1, cancellationToken);
        if (replay is not null)
        {
            return await ReplayedAsync(scope, replay, cancellationToken);
        }

        // 2. The quote as it stands, read under the quote's write name; the conversion is checked before any owner is asked.
        var quote = await ReadQuoteAsync(scope, quotePermission, quoteId, cancellationToken);
        try
        {
            quote.RequireConvertible(expectedVersion);
        }
        catch (SalesVersionConflictException exception)
        {
            throw await TerminalAsync(scope, bindings.CreatePermission, envelope, requestHash,
                new SalesDraftTerminalRefusal(409, SalesDraftProblemCodesV1.VersionConflict, [new("expectedVersion", exception.Code)]),
                cancellationToken, SalesQuoteCommands.ConvertV1);
        }
        catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.StateConflict)
        {
            throw await TerminalAsync(scope, bindings.CreatePermission, envelope, requestHash,
                new SalesDraftTerminalRefusal(409, SalesDraftProblemCodesV1.StateConflict, [new("state", exception.Code)]),
                cancellationToken, SalesQuoteCommands.ConvertV1);
        }

        // SAT-D24 (interim, fail-closed): a quote whose validity date precedes the conversion's business date is refused (409
        // sales_quote_expired) until the manager-approval step SAT-D24 names exists. Not recorded, like No 160 below: the
        // refusal stands in for an approval that is meant to clear it for the SAME request, so the key must not be burned; nothing
        // is written either way. The quote's version pins its validity date and the store checks it again inside the save.
        try
        {
            quote.RequireValidOn(request.BusinessDate);
        }
        catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.QuoteExpired)
        {
            throw QuoteExpired();
        }

        // No 160 (default-deny): every request the quote covers is approved. Not recorded: the request may still be approved
        // and the same key retried; the store asks again inside the one save.
        await RequireApprovedRequestsAsync(scope, quotePermission, quote, cancellationToken);

        // 3. No 162: the quote's current revision as the order's create request — its price/quantity snapshot verbatim — through
        // the draft's OWN creation path (owners, rules, the separate price-write fence for a priced line, idempotency).
        var draftRequest = new CreateSalesOrderDraftRequest("Order", quote.Header.PartyId, quote.Header.CompanyAccountId, request.BusinessDate,
            quote.Header.CurrencyId,
            [.. quote.Lines.Select(line => new SalesOrderDraftLineRequest(line.Id, line.Product.ProductId, line.Product.ProductVersion,
                line.OrderUnit.UnitId, line.QuantityText, line.UnitPrice, line.PriceUnitId, line.RequestedDeliveryDate, line.Note, null,
                line.DiscountPercent))],
            quote.Terms.RequestedDeliveryDate, quote.Terms.Note);
        var priced = CommercialFields(draftRequest);
        if (priced.Count > 0)
        {
            await RequirePriceWriteAsync(scope, priced, cancellationToken);
        }

        scope = await RequireStillAsync(scope, quotePermission, cancellationToken);
        return await CreateDraftCoreAsync(scope, draftRequest, envelope, requestHash, SalesQuoteCommands.ConvertV1, priced,
            async build =>
            {
                // ADR-011 §5: the quote's write name is measured again right before the one save, like the order's create name
                // (CreateDraftCoreAsync measures that one just before calling here).
                await RequireStillAsync(scope, quotePermission, cancellationToken);
                try
                {
                    return await store.StoreConvertedDraftAsync(envelope, requestHash, quoteId, expectedVersion, build, cancellationToken);
                }
                catch (SalesVersionConflictException exception)
                {
                    // The quote moved after it was read: a revision or a loss won. Nothing was stored.
                    throw new SalesDraftServiceException(409, SalesDraftProblemCodesV1.VersionConflict, [new("expectedVersion", exception.Code)]);
                }
                catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.StateConflict)
                {
                    throw new SalesDraftServiceException(409, SalesDraftProblemCodesV1.StateConflict, [new("state", exception.Code)]);
                }
                catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.QuoteRequestNotApproved)
                {
                    // No 160 inside the save: a covered request left Approved after the service's read. Not recorded (as below).
                    throw new SalesDraftServiceException(409, SalesQuoteProblemCodesV1.RequestNotApproved,
                        [new("requestIds", SalesQuoteProblemCodesV1.RequestNotApproved)]);
                }
                catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.QuoteExpired)
                {
                    // SAT-D24 inside the save (the domain's Convert asks again on the order's business date). Not recorded.
                    throw QuoteExpired();
                }
            },
            cancellationToken);
    }

    private static SalesDraftServiceException QuoteExpired() =>
        new(409, SalesQuoteProblemCodesV1.Expired, [new("validUntil", SalesQuoteProblemCodesV1.Expired)]);

    private async Task<SalesQuoteResponse> UpdateQuoteAsync(Scope scope, string? permission, SalesDraftWriteEnvelope envelope, string requestHash,
        string commandName, Guid quoteId, Action<SalesQuote, DateTimeOffset> mutate, SalesDraftStateAudit audit,
        IReadOnlyList<SalesQuoteLineRequest>? lines, CancellationToken cancellationToken)
    {
        if (quoteId == Guid.Empty)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        scope = await RequireStillAsync(scope, permission, cancellationToken);
        SalesQuoteStoreResult? result;
        try
        {
            result = await QuoteStore().UpdateQuoteAsync(envelope, requestHash, commandName, quoteId, mutate, audit, cancellationToken);
        }
        catch (SalesVersionConflictException exception)
        {
            throw await QuoteTerminalAsync(scope, permission, envelope, requestHash, commandName, quoteId,
                new SalesDraftTerminalRefusal(409, SalesDraftProblemCodesV1.VersionConflict, [new("expectedVersion", exception.Code)]), cancellationToken);
        }
        catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.StateConflict)
        {
            throw await QuoteTerminalAsync(scope, permission, envelope, requestHash, commandName, quoteId,
                new SalesDraftTerminalRefusal(409, SalesDraftProblemCodesV1.StateConflict, [new("state", exception.Code)]), cancellationToken);
        }
        catch (SalesRuleException exception) when (exception.Code is SalesRuleCodes.QuoteLossReasonRequired or SalesRuleCodes.QuoteLossReasonTooLong)
        {
            throw new SalesDraftServiceException(422, exception.Code, [new("reason", exception.Code)]);
        }
        catch (SalesRuleException exception)
        {
            throw QuoteRule(exception, lines);
        }
        catch (ArgumentException)
        {
            throw new SalesDraftServiceException(400, "validation_failed");
        }
        catch (SalesDraftStoreException exception) when (exception.Code == SalesDraftStoreCodes.ConcurrentChange)
        {
            var won = await FindQuoteReplayAsync(scope, permission, envelope, requestHash, commandName, cancellationToken);
            return won is null ? throw Store(exception) : await QuoteReplayedAsync(scope, won, cancellationToken);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        return result is null ? throw new SalesDraftServiceException(404, "not_found") : await QuoteResponseAsync(scope, result, cancellationToken);
    }

    /// <summary>The lines' owner snapshots (Product eligibility, Settings order-unit profile) under the quote's name, with the client's texts.</summary>
    private async Task<List<SalesQuoteLineInput>> ReadQuoteLinesAsync(Scope scope, string? permission, IReadOnlyList<SalesQuoteLineRequest> lines,
        DateOnly businessDate, Func<SalesDraftTerminalRefusal, Task<SalesDraftServiceException>> terminal, CancellationToken cancellationToken)
    {
        var inputs = new List<SalesQuoteLineInput>(lines.Count);
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var (product, orderUnit) = await ReadProductUnitAsync(scope, permission, index, line.ProductId, line.ExpectedProductVersion,
                line.OrderUnitId, businessDate, terminal, cancellationToken);
            inputs.Add(new SalesQuoteLineInput(line.LineId, product, orderUnit, line.Quantity, Decimal(line.Quantity), line.UnitPrice,
                line.PriceUnitId, line.DiscountPercent, line.RequestedDeliveryDate, line.Note));
        }

        return inputs;
    }

    /// <summary>
    /// No 163: each named request is this company's (else 422 unknown), of the SAME customer (else 422 customer mismatch), not
    /// rejected and not covered by another live quote (else 422 unavailable). The database's filtered unique index is the
    /// last word on the last condition (a race is the caller's concurrent_change).
    /// </summary>
    private async Task RequireQuotableRequestsAsync(Scope scope, string? permission, Guid partyId, Guid companyAccountId,
        IReadOnlyList<Guid> requestIds, CancellationToken cancellationToken)
    {
        var requestStore = RequestStore();
        var store = QuoteStore();
        var errors = new List<SalesDraftFieldError>();
        for (var index = 0; index < requestIds.Count; index++)
        {
            var field = $"requestIds[{index}]";
            var id = requestIds[index];
            var request = await GuardedAsync(scope, permission, () => requestStore.FindRequestAsync(id, cancellationToken), cancellationToken);
            if (request is null)
            {
                errors.Add(new(field, SalesQuoteProblemCodesV1.RequestUnknown));
            }
            else if (request.Header.PartyId != partyId || request.Header.CompanyAccountId != companyAccountId)
            {
                errors.Add(new(field, SalesQuoteProblemCodesV1.RequestCustomerMismatch));
            }
            else if (request.State == SalesRequestState.Rejected
                     || await GuardedAsync(scope, permission, () => store.IsRequestQuotedAsync(id, cancellationToken), cancellationToken))
            {
                errors.Add(new(field, SalesQuoteProblemCodesV1.RequestUnavailable));
            }
        }

        if (errors.Count > 0)
        {
            throw new SalesDraftServiceException(422, errors[0].Code, errors);
        }
    }

    /// <summary>
    /// No 160: every request the quote covers is approved now (409 otherwise, not recorded: the request may still be approved
    /// and the same key retried; the store asks again inside the one save). One field, <c>requestIds</c>, on both paths.
    /// </summary>
    private async Task RequireApprovedRequestsAsync(Scope scope, string? permission, SalesQuote quote, CancellationToken cancellationToken)
    {
        var requestStore = RequestStore();
        foreach (var id in quote.RequestIds)
        {
            var request = await GuardedAsync(scope, permission, () => requestStore.FindRequestAsync(id, cancellationToken), cancellationToken);
            if (request?.State != SalesRequestState.Approved)
            {
                throw new SalesDraftServiceException(409, SalesQuoteProblemCodesV1.RequestNotApproved,
                    [new("requestIds", SalesQuoteProblemCodesV1.RequestNotApproved)]);
            }
        }
    }

    /// <summary>Every quote line price field the request carries, as the field errors a price-visibility refusal names.</summary>
    private static List<SalesDraftFieldError> QuotePriceFields(IReadOnlyList<SalesQuoteLineRequest>? lines)
    {
        var fields = new List<SalesDraftFieldError>();
        for (var index = 0; index < (lines?.Count ?? 0); index++)
        {
            var line = lines![index];
            if (line.UnitPrice is not null) fields.Add(new($"lines[{index}].unitPrice", SalesQuoteProblemCodesV1.PriceReadRequired));
            if (line.PriceUnitId is not null) fields.Add(new($"lines[{index}].priceUnitId", SalesQuoteProblemCodesV1.PriceReadRequired));
            if (line.DiscountPercent is not null) fields.Add(new($"lines[{index}].discountPercent", SalesQuoteProblemCodesV1.PriceReadRequired));
        }

        return fields;
    }

    /// <summary>
    /// Fail-closed price visibility for a quote write: the actor must SEE quote prices (the order's narrow price read, the same
    /// measurement as the disclosure) or the write is refused with 403 — a caller who cannot see the prices must not overwrite
    /// or erase them. Never recorded (an authority answer, like the price-write fence). Writing a quote price needs no more than
    /// <c>sales.quotes.write</c> (SAT-D26).
    /// </summary>
    private async Task RequirePriceReadAsync(Scope scope, IReadOnlyList<SalesDraftFieldError> fields, CancellationToken cancellationToken)
    {
        if (await DiscloseAsync(scope, cancellationToken) != SalesPriceDisclosure.Visible)
        {
            throw new SalesDraftServiceException(403, SalesQuoteProblemCodesV1.PriceReadRequired, fields);
        }
    }

    private async Task<SalesQuote> ReadQuoteAsync(Scope scope, string? permission, Guid quoteId, CancellationToken cancellationToken)
    {
        if (quoteId == Guid.Empty)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        var store = QuoteStore();
        SalesQuote? quote;
        try
        {
            quote = await GuardedAsync(scope, permission, () => store.FindQuoteAsync(quoteId, cancellationToken), cancellationToken);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        return quote ?? throw new SalesDraftServiceException(404, "not_found");
    }

    /// <summary>A domain refusal of a quote: per line when the rule names the line (the client's own index), else on the quote.</summary>
    private static SalesDraftServiceException QuoteRule(SalesRuleException exception, IReadOnlyList<SalesQuoteLineRequest>? lines)
    {
        var field = "quote";
        if (exception.LineId is { } lineId && lines is not null)
        {
            for (var index = 0; index < lines.Count; index++)
            {
                if (lines[index].LineId == lineId)
                {
                    field = $"lines[{index}]";
                    break;
                }
            }
        }

        return new SalesDraftServiceException(422, "validation_failed", [new(field, exception.Code)]);
    }

    /// <summary>A host without the quote store is a setup gap (503), never an empty answer.</summary>
    private ISalesQuoteStore QuoteStore() =>
        quotes ?? throw new SalesDraftServiceException(503, "service_unavailable");

    private async Task<SalesQuoteReplay?> FindQuoteReplayAsync(Scope scope, string? permission, SalesDraftWriteEnvelope envelope,
        string requestHash, string commandName, CancellationToken cancellationToken)
    {
        var store = QuoteStore();
        try
        {
            return await GuardedAsync(scope, permission, () => store.FindQuoteReplayAsync(envelope, requestHash, commandName, cancellationToken),
                cancellationToken);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }
    }

    private async Task<SalesQuoteResponse> QuoteReplayedAsync(Scope scope, SalesQuoteReplay replay, CancellationToken cancellationToken)
    {
        if (replay.Refusal is { } refusal)
        {
            throw new SalesDraftServiceException(refusal.Status, refusal.Code, refusal.Errors, isReplay: true,
                originalCorrelationId: replay.OriginalCorrelationId);
        }

        return new SalesQuoteResponse(QuoteDetail(replay.Quote!, await DiscloseAsync(scope, cancellationToken)), true, replay.OriginalCorrelationId);
    }

    private async Task<SalesQuoteResponse> QuoteResponseAsync(Scope scope, SalesQuoteStoreResult result, CancellationToken cancellationToken) =>
        new(QuoteDetail(result.Quote, await DiscloseAsync(scope, cancellationToken)), result.Outcome == SalesDraftWriteOutcome.Replayed,
            result.OriginalCorrelationId);

    private async Task<SalesDraftServiceException> QuoteTerminalAsync(Scope scope, string? permission, SalesDraftWriteEnvelope envelope,
        string requestHash, string commandName, Guid? quoteId, SalesDraftTerminalRefusal refusal, CancellationToken cancellationToken)
    {
        await RequireStillAsync(scope, permission, cancellationToken);
        try
        {
            await QuoteStore().RecordQuoteRefusalAsync(envelope, requestHash, commandName, quoteId, refusal, cancellationToken);
        }
        catch (SalesDraftStoreException exception) when (exception.Code == SalesDraftStoreCodes.ConcurrentChange)
        {
            var won = await FindQuoteReplayAsync(scope, permission, envelope, requestHash, commandName, cancellationToken);
            if (won?.Refusal is { } recorded)
            {
                return new SalesDraftServiceException(recorded.Status, recorded.Code, recorded.Errors, isReplay: true,
                    originalCorrelationId: won.OriginalCorrelationId);
            }

            if (won?.Quote is not null)
            {
                throw new QuoteRaceWon(await QuoteReplayedAsync(scope, won, cancellationToken));
            }
        }

        return new SalesDraftServiceException(refusal.Status, refusal.Code, refusal.Errors);
    }

    private sealed class QuoteRaceWon(SalesQuoteResponse response) : Exception
    {
        public SalesQuoteResponse Response { get; } = response;
    }

    private static SalesQuoteSummaryView QuoteSummary(SalesQuote quote) => new(
        quote.Id, quote.CompanyId, quote.DocumentNo, quote.State.ToString(), quote.Version.ToString(CultureInfo.InvariantCulture),
        SalesDraftApiV1.RevisionText(quote.Revision), quote.Header.PartyId, quote.Header.CompanyAccountId, quote.Header.CurrencyId,
        quote.Header.BusinessDate, quote.Terms.ValidUntil, quote.CreatedAtUtc, quote.Lines.Count, quote.Conversion?.DraftId);

    private static SalesQuoteDetailView QuoteDetail(SalesQuote quote, SalesPriceDisclosure disclosure) => new(
        QuoteSummary(quote), quote.RequestIds, quote.Terms.RequestedDeliveryDate, quote.Terms.Note,
        [.. quote.Lines.Select(line => new SalesQuoteLineView(line.Id, line.LineNo, line.Product.ProductId, line.Product.ProductVersion,
            line.Product.Code, line.Product.Name, line.OrderUnit.UnitId, line.OrderUnit.UnitCode, line.QuantityText,
            Disclosed(disclosure, line.UnitPrice), disclosure == SalesPriceDisclosure.Visible ? line.PriceUnitId : null,
            Disclosed(disclosure, line.DiscountPercent), disclosure, line.RequestedDeliveryDate, line.Note))],
        quote.Loss is { } loss ? new SalesQuoteLossV1(loss.Reason, loss.LostBy, loss.LostAtUtc) : null,
        quote.Conversion is { } conversion ? new SalesQuoteConversionV1(conversion.DraftId, conversion.ConvertedBy, conversion.ConvertedAtUtc) : null);
}
