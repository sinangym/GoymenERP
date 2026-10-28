using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>
/// API-30 (S-07 "Sevkiyat / İrsaliyeler") and API-31 (S-08 "Faturaya Hazır") — Satış hazırlık planı v1 §3.3, MOD-06; SAT-D7
/// (manual first version: ERP prepares, the official document is issued elsewhere), DEC-SAL-006 (grouping key), AT-025 (sales
/// and purchase documents never mix), AT-055 (a shipment is on one note). Read-only in every module.
/// <list type="bullet">
/// <item><b>Fences</b>: the route's own name (API-30 <c>sales.drafts.read</c>, API-31 <c>sales.invoices.read</c>) is measured
/// fresh — granted, company-wide, this company and actor — before anything is read, after every awaited owner read and before the
/// answer is built. The owner ports carry no authority.</item>
/// <item><b>Owners</b>: the Logistics dispatch owner (<see cref="ISalesDispatchNoteReadPortV1"/>,
/// <see cref="ISalesInvoiceableShipmentReadPortV1"/>) answers the notes and shares from its own tables; Sales composes each share's
/// order reference from its OWN store (<see cref="ISalesOrderDispatchReferenceReader"/>); the Cari owner presents the customer
/// (<see cref="ISalesPartyDisplaySourceV1"/>). No module reads another's tables.</item>
/// <item><b>Fail-closed</b>: an owner that is not composed, refuses, throws or answers for another company is 503 (never an empty
/// list); a share whose order, line, customer or unit does not match the Sales order is a malformed owner answer (503); a foreign
/// or broken cursor is 400.</item>
/// <item><b>No money</b>: no price, amount, tax or total is read or computed here. The order totals calculator (API-03) is not on
/// main; when it is, amounts are added from it under <c>sales.orders.price.read</c> — never computed by the screen.</item>
/// </list>
/// </summary>
public sealed class SalesDispatchReadService(
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority,
    SalesDispatchReadBindings bindings,
    ISalesOrderDispatchReferenceReader orders,
    ISalesPartyDisplaySourceV1? partyDisplays = null,
    ISalesDispatchNoteReadPortV1? dispatchNotes = null,
    ISalesInvoiceableShipmentReadPortV1? invoiceableShipments = null) : ISalesDispatchReadService
{
    private const int MaximumCursorLength = 8192;

    /// <summary>The Cari owner's batch bound (its own maximum).</summary>
    private const int MaximumPartyBatch = 100;

    public async Task<SalesDispatchListResponse> GetDispatchesAsync(SalesDispatchListQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var permission = bindings.DispatchReadPermission;
        var scope = await RequireScopeAsync(permission, cancellationToken);
        if (query.PageSize is < 1 or > SalesDispatchApiV1.MaximumPageSize || query.Cursor is { Length: 0 or > MaximumCursorLength })
        {
            throw new SalesDraftServiceException(400, "validation_failed");
        }

        SalesDispatchNoteStateV1? state = null;
        if (query.State is not null)
        {
            if (!Enum.TryParse<SalesDispatchNoteStateView>(query.State, ignoreCase: false, out var parsed)
                || !Enum.IsDefined(parsed) || parsed.ToString() != query.State)
            {
                throw new SalesDraftServiceException(400, "validation_failed");
            }

            state = (SalesDispatchNoteStateV1)(int)parsed;
        }

        var owner = dispatchNotes ?? throw DispatchUnavailable();
        SalesDispatchNotePageAnswerV1? answer;
        try
        {
            answer = await owner.ListSalesDispatchNotesAsync(new SalesDispatchNotePageQueryV1(query.PageSize, query.Cursor, state),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RequireStillAsync(scope, permission, cancellationToken);
            throw DispatchUnavailable();
        }

        await RequireStillAsync(scope, permission, cancellationToken);
        if (answer is null)
        {
            throw DispatchUnavailable();
        }

        switch (answer.Outcome)
        {
            case SalesDispatchReadOutcomeV1.Found:
                break;
            case SalesDispatchReadOutcomeV1.InvalidCursor:
                throw new SalesDraftServiceException(400, SalesDraftProblemCodesV1.InvalidCursor);
            case SalesDispatchReadOutcomeV1.Denied:
                throw new SalesDraftServiceException(403, "forbidden");
            default:
                throw DispatchUnavailable();
        }

        if (answer.CompanyId != scope.CompanyId || answer.Notes is null || answer.Notes.Count > query.PageSize
            || answer.NextCursor is { Length: 0 }
            || answer.Notes.Any(note => note is null || note.Lines is null || note.Lines.Count == 0 || note.Lines.Any(line => line is null)))
        {
            throw DispatchUnavailable();
        }

        if (answer.Notes.Count == 0)
        {
            return new SalesDispatchListResponse([], answer.NextCursor);
        }

        var references = await ReadOrderReferencesAsync(scope, permission,
            answer.Notes.SelectMany(note => note.Lines).Select(line => line.SalesOrderId), cancellationToken);
        var customers = await ReadCustomersAsync(scope, permission, answer.Notes.Select(note => note.CustomerPartyId), cancellationToken);
        await RequireStillAsync(scope, permission, cancellationToken);

        var items = new List<SalesDispatchSummaryView>(answer.Notes.Count);
        foreach (var note in answer.Notes)
        {
            var noteState = (SalesDispatchNoteStateView)(int)note.State;
            if (!Enum.IsDefined(noteState) || note.DispatchNoteId == Guid.Empty || note.InstructionId == Guid.Empty
                || noteState == SalesDispatchNoteStateView.Approved && (string.IsNullOrWhiteSpace(note.SystemNumber) || note.ApprovedAtUtc is null)
                || noteState == SalesDispatchNoteStateView.Cancelled && note.CancelledAtUtc is null)
            {
                throw ReferenceInvalid();
            }

            var lines = note.Lines.Select(line =>
            {
                var (order, orderLine, quantity) = MatchShare(references, note.CustomerPartyId, note.CompanyAccountId, line.SalesOrderId,
                    line.SalesOrderLineId, line.ProductId, line.UnitId, line.Quantity);
                return new SalesDispatchLineView(line.LineNo, order.OrderId, order.DocumentNo, orderLine.LineId, orderLine.LineNo,
                    line.ShipmentId, line.ShippedAtUtc, orderLine.ProductId, orderLine.ProductCode, orderLine.ProductName, orderLine.OrderUnitId,
                    orderLine.OrderUnitCode, quantity);
            }).ToArray();
            items.Add(new SalesDispatchSummaryView(note.DispatchNoteId, scope.CompanyId, note.InstructionId, noteState, note.SystemNumber,
                note.EDispatchNumber, Customer(customers, note.CustomerPartyId, note.CompanyAccountId), note.CreatedAtUtc,
                note.ApprovedAtUtc, note.CancelledAtUtc, SalesInvoicedStateView.NoSource, lines));
        }

        return new SalesDispatchListResponse(items, answer.NextCursor);
    }

    public async Task<SalesInvoiceCandidatesResponse> GetInvoiceCandidatesAsync(SalesInvoiceCandidateQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var permission = bindings.InvoiceCandidateReadPermission;
        var scope = await RequireScopeAsync(permission, cancellationToken);
        if (query.CompanyAccountId == Guid.Empty)
        {
            throw new SalesDraftServiceException(400, "validation_failed");
        }

        var owner = invoiceableShipments ?? throw DispatchUnavailable();
        SalesInvoiceableShipmentAnswerV1? answer;
        try
        {
            answer = await owner.ReadInvoiceableShipmentsAsync(
                new SalesInvoiceableShipmentQueryV1(query.CompanyAccountId, SalesDispatchApiV1.MaximumCandidateShares), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RequireStillAsync(scope, permission, cancellationToken);
            throw DispatchUnavailable();
        }

        await RequireStillAsync(scope, permission, cancellationToken);
        if (answer is null)
        {
            throw DispatchUnavailable();
        }

        if (answer.Outcome == SalesDispatchReadOutcomeV1.Denied)
        {
            throw new SalesDraftServiceException(403, "forbidden");
        }

        if (answer.Outcome != SalesDispatchReadOutcomeV1.Found || answer.CompanyId != scope.CompanyId || answer.Shares is null
            || answer.Shares.Count > SalesDispatchApiV1.MaximumCandidateShares || answer.InvoicedState != SalesInvoicedStateSourceV1.NoSource
            || answer.Shares.Any(share => share is null
                || query.CompanyAccountId is { } account && share.CompanyAccountId != account))
        {
            throw DispatchUnavailable();
        }

        if (answer.Shares.Count == 0)
        {
            return new SalesInvoiceCandidatesResponse([], answer.HasMore, SalesInvoicedStateView.NoSource);
        }

        // AT-055: one shipment is one share; the owner's single-link rule is checked again rather than trusted blindly.
        if (answer.Shares.Select(share => share.ShipmentId).Distinct().Count() != answer.Shares.Count
            || answer.Shares.Any(share => share.DispatchNoteId == Guid.Empty || share.ShipmentId == Guid.Empty
                || string.IsNullOrWhiteSpace(share.SystemNumber) || share.NoteLineNo < 1))
        {
            throw ReferenceInvalid();
        }

        var references = await ReadOrderReferencesAsync(scope, permission, answer.Shares.Select(share => share.SalesOrderId),
            cancellationToken);
        var customers = await ReadCustomersAsync(scope, permission, answer.Shares.Select(share => share.CustomerPartyId), cancellationToken);
        await RequireStillAsync(scope, permission, cancellationToken);

        // DEC-SAL-006: the decided key parts this version can read — company (the scope), customer (party + account) and the
        // order's currency, plus the order's payment-terms reference when it holds one. Shares meet in a group only when all of
        // them are equal; an unread part never merges anything that a read part separates.
        var groups = new List<(SalesInvoiceGroupKeyView Key, List<(SalesInvoiceableShipmentV1 Share, SalesInvoiceCandidateShareView View, bool Kilogram)> Shares)>();
        var index = new Dictionary<SalesInvoiceGroupKeyView, int>();
        foreach (var share in answer.Shares)
        {
            var (order, orderLine, quantity) = MatchShare(references, share.CustomerPartyId, share.CompanyAccountId, share.SalesOrderId,
                share.SalesOrderLineId, share.ProductId, share.UnitId, share.Quantity);
            var key = new SalesInvoiceGroupKeyView(scope.CompanyId, order.PartyId, order.CompanyAccountId, order.CurrencyId,
                order.PaymentTermsId is { } termsId && order.PaymentTermsVersion is { } termsVersion
                    ? new SalesInvoiceGroupReferenceView(termsId, termsVersion)
                    : null);
            var view = new SalesInvoiceCandidateShareView(share.DispatchNoteId, share.SystemNumber, share.EDispatchNumber,
                share.ApprovedAtUtc, share.NoteLineNo, share.ShipmentId, share.ShippedAtUtc, order.OrderId, order.DocumentNo,
                orderLine.LineId, orderLine.LineNo, orderLine.ProductId, orderLine.ProductCode, orderLine.ProductName, orderLine.OrderUnitId,
                orderLine.OrderUnitCode, quantity, SalesInvoicedStateView.NoSource);
            if (!index.TryGetValue(key, out var position))
            {
                position = groups.Count;
                index.Add(key, position);
                groups.Add((key, []));
            }

            groups[position].Shares.Add((share, view, orderLine.OrderUnitIsKilogram));
        }

        return new SalesInvoiceCandidatesResponse(
            [.. groups.Select(group => Group(group.Key, group.Shares, customers))],
            answer.HasMore,
            SalesInvoicedStateView.NoSource);
    }

    private static SalesInvoiceCandidateGroupView Group(SalesInvoiceGroupKeyView key,
        List<(SalesInvoiceableShipmentV1 Share, SalesInvoiceCandidateShareView View, bool Kilogram)> shares,
        IReadOnlyDictionary<Guid, SalesPartyDisplayV1> customers)
    {
        // Tax (SAT-D73/D74: product VAT class + Settings table, buyer withholding), the single invoice rate (No 26, fixed on the
        // invoice draft, API-32) and e-document compatibility have no owner on main: listed as unresolved, never assumed equal.
        var unresolved = new List<SalesInvoiceGroupKeyPart>(4);
        if (key.PaymentTerms is null)
        {
            unresolved.Add(SalesInvoiceGroupKeyPart.PaymentTerms);
        }

        unresolved.AddRange([SalesInvoiceGroupKeyPart.Tax, SalesInvoiceGroupKeyPart.ExchangeRate, SalesInvoiceGroupKeyPart.EDocument]);

        var totals = shares
            .GroupBy(item => item.Share.UnitId)
            .OrderBy(unit => unit.Min(item => item.Share.ApprovedAtUtc))
            .ThenBy(unit => unit.Key)
            .Select(unit =>
            {
                decimal sum;
                try
                {
                    sum = unit.Sum(item => item.Share.Quantity);
                }
                catch (OverflowException)
                {
                    throw ReferenceInvalid();
                }

                // One unit id is one Settings unit of one dimension (checked per share against its order line).
                return new SalesInvoiceQuantityTotalView(unit.Key, unit.First().View.UnitCode, QuantityText(sum, unit.First().Kilogram));
            })
            .ToArray();

        return new SalesInvoiceCandidateGroupView(key, Customer(customers, key.PartyId, key.CompanyAccountId), unresolved,
            Complete: unresolved.Count == 0,
            shares.Select(item => item.Share.DispatchNoteId).Distinct().Count(),
            totals,
            [.. shares.Select(item => item.View)]);
    }

    /// <summary>
    /// Matches one owner share to the Sales order it names: the order must be this company's, the note's customer must be the
    /// order's customer, the line must be the order's, the product the line's and the unit the line's order unit; the quantity must
    /// be a positive Ö2 quantity of that unit. Anything else is a malformed owner answer (503) — the screen shows no number rather
    /// than a wrong one.
    /// </summary>
    private static (SalesOrderDispatchReference Order, SalesOrderDispatchLineReference Line, string Quantity) MatchShare(
        IReadOnlyDictionary<Guid, SalesOrderDispatchReference> references, Guid partyId, Guid companyAccountId, Guid orderId,
        Guid orderLineId, Guid productId, Guid unitId, decimal quantity)
    {
        if (!references.TryGetValue(orderId, out var order) || order.PartyId != partyId || order.CompanyAccountId != companyAccountId)
        {
            throw ReferenceInvalid();
        }

        var line = order.Lines.SingleOrDefault(candidate => candidate.LineId == orderLineId);
        if (line is null || line.ProductId != productId || line.OrderUnitId != unitId || quantity <= 0m)
        {
            throw ReferenceInvalid();
        }

        return (order, line, QuantityText(quantity, line.OrderUnitIsKilogram));
    }

    private static string QuantityText(decimal value, bool kilogram)
    {
        try
        {
            // Ö2 in the line's own dimension: kilograms ≤ 3 decimals, pieces whole — never rounded to fit.
            return Quantity.Of(value, kilogram ? QuantityUnit.Kilogram : QuantityUnit.Piece).Value.ToString(CultureInfo.InvariantCulture);
        }
        catch (ArgumentException)
        {
            throw ReferenceInvalid();
        }
    }

    private async Task<IReadOnlyDictionary<Guid, SalesOrderDispatchReference>> ReadOrderReferencesAsync(ReadScope scope,
        string? permission, IEnumerable<Guid> orderIds, CancellationToken cancellationToken)
    {
        var ids = orderIds.Distinct().ToArray();
        if (ids.Any(id => id == Guid.Empty))
        {
            throw ReferenceInvalid();
        }

        IReadOnlyList<SalesOrderDispatchReference> answer;
        try
        {
            answer = await orders.ReadOrderReferencesAsync(ids, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RequireStillAsync(scope, permission, cancellationToken);
            throw new SalesDraftServiceException(503, "service_unavailable");
        }

        await RequireStillAsync(scope, permission, cancellationToken);
        var byId = new Dictionary<Guid, SalesOrderDispatchReference>();
        foreach (var reference in answer ?? [])
        {
            if (reference is null || reference.CompanyId != scope.CompanyId || reference.Lines is null || !byId.TryAdd(reference.OrderId, reference))
            {
                throw ReferenceInvalid();
            }
        }

        return byId;
    }

    private async Task<IReadOnlyDictionary<Guid, SalesPartyDisplayV1>> ReadCustomersAsync(ReadScope scope, string? permission,
        IEnumerable<Guid> partyIds, CancellationToken cancellationToken)
    {
        var ids = partyIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, SalesPartyDisplayV1>();
        }

        if (ids.Any(id => id == Guid.Empty))
        {
            throw ReferenceInvalid();
        }

        var source = partyDisplays ?? throw CounterpartyUnavailable();
        var byId = new Dictionary<Guid, SalesPartyDisplayV1>();
        foreach (var batch in ids.Chunk(MaximumPartyBatch))
        {
            SalesPartyDisplayBatchAnswerV1? answer;
            try
            {
                answer = await source.GetAuthorizedBatchAsync(new SalesPartyDisplayBatchRequestV1(scope.CompanyId, scope.ActorId, batch),
                    cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await RequireStillAsync(scope, permission, cancellationToken);
                throw CounterpartyUnavailable();
            }

            await RequireStillAsync(scope, permission, cancellationToken);
            if (answer?.Outcome == SalesPartyDisplayBatchOutcomeV1.Denied)
            {
                throw new SalesDraftServiceException(403, "forbidden");
            }

            if (answer is null || answer.Outcome != SalesPartyDisplayBatchOutcomeV1.Success || answer.Items is null
                || answer.Items.Count != batch.Length)
            {
                throw CounterpartyUnavailable();
            }

            var requested = batch.ToHashSet();
            foreach (var item in answer.Items)
            {
                if (item is null || !requested.Contains(item.PartyId) || string.IsNullOrWhiteSpace(item.Code)
                    || string.IsNullOrWhiteSpace(item.LegalName) || item.TradeName is not null && string.IsNullOrWhiteSpace(item.TradeName)
                    || !byId.TryAdd(item.PartyId, item))
                {
                    throw CounterpartyUnavailable();
                }
            }
        }

        return byId;
    }

    private static SalesDispatchCustomerView Customer(IReadOnlyDictionary<Guid, SalesPartyDisplayV1> customers, Guid partyId,
        Guid companyAccountId)
    {
        if (!customers.TryGetValue(partyId, out var display))
        {
            throw CounterpartyUnavailable();
        }

        return new SalesDispatchCustomerView(partyId, companyAccountId, display.Code, display.LegalName, display.TradeName);
    }

    private async Task<ReadScope> RequireScopeAsync(string? permission, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            throw new SalesDraftServiceException(401, "unauthenticated");
        }

        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty
            || !currentUser.CanAccessCompany(companyContext.CompanyId))
        {
            throw new SalesDraftServiceException(403, "forbidden");
        }

        if (string.IsNullOrWhiteSpace(permission) || !ErpPermissions.IsKnown(permission))
        {
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.PermissionUnregistered);
        }

        var companyId = companyContext.CompanyId;
        var actorId = currentUser.UserId;
        var measured = await authority.RevalidatePermissionScopeAsync(permission, companyId, cancellationToken);
        if (!measured.IsGranted || !measured.IsCompanyWide || !Matches(companyId, actorId))
        {
            throw new SalesDraftServiceException(403, "forbidden");
        }

        return new ReadScope(companyId, actorId);
    }

    private async Task RequireStillAsync(ReadScope expected, string? permission, CancellationToken cancellationToken)
    {
        var current = await RequireScopeAsync(permission, cancellationToken);
        if (current != expected)
        {
            throw new SalesDraftServiceException(403, "forbidden");
        }
    }

    private bool Matches(Guid companyId, string actorId) =>
        currentUser.IsAuthenticated
        && string.Equals(currentUser.UserId, actorId, StringComparison.Ordinal)
        && companyContext.IsResolved
        && companyContext.CompanyId == companyId
        && currentUser.CanAccessCompany(companyId);

    private static SalesDraftServiceException DispatchUnavailable() =>
        new(503, SalesDispatchProblemCodesV1.DispatchProviderUnavailable);

    private static SalesDraftServiceException ReferenceInvalid() =>
        new(503, SalesDraftProblemCodesV1.ReferenceProviderInvalid);

    private static SalesDraftServiceException CounterpartyUnavailable() =>
        new(503, SalesDraftProblemCodesV1.CounterpartyProviderUnavailable);

    private sealed record ReadScope(Guid CompanyId, string ActorId);
}
