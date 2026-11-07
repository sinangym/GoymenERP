using System.Globalization;
using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.Invoices;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>
/// S5 API-32 — Fatura çekirdeği 1: a sales invoice DRAFT from approved shipment shares (SAT-D7: the ERP prepares and checks; the
/// official invoice is issued in the external program). The K3 skeleton: the write name (<c>sales.invoices.write</c>, SAT-D202)
/// and the price read are measured before anything is read, after every awaited owner read and right before the single save;
/// the replay comes before any owner is asked; one save writes the invoice, its lines, the attempt and the audit event.
/// <list type="bullet">
/// <item><b>Shares</b> — the Logistics owner's approved, not released dispatch-note lines of the customer
/// (<see cref="ISalesInvoiceableShipmentReadPortV1"/>), each WHOLE and at most once on an active invoice (AT-055, SAT-D94; the
/// store's unique active-share index decides a race).</item>
/// <item><b>Price basis</b> — the order's invoicing gate (SAT-D103/D114/D115/D121) must be <c>Open</c>; the line's commercial
/// fields and tax snapshot are the basis revision's (current, customer-accepted archive or last approved archive).</item>
/// <item><b>One invoice key</b> (SAT-D101) — one customer account, one currency (and money profile), one rate (SAT-D90/D170
/// resolved per shipment day, SAT-D172 written on the invoice), one due plan (SAT-D129: equal terms and equal dates).</item>
/// <item><b>Waits, never guesses</b> — a tax blocker (SAT-D135/D147), an exemption document dated after the shipment (SAT-D186),
/// a withholding line (the VUK 232 comparison is not decided), a missing price, rate or due date: 409 <c>invoice_not_ready</c>
/// with every reason; nothing is recorded under the key, so the same key asks again once the cause is gone.</item>
/// </list>
/// Amounts are <see cref="SalesInvoiceCalculator"/>'s only (SAT-D11/D75/D76, EK42 SAT-D223…D226).
/// </summary>
public sealed class SalesInvoiceService(
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority,
    ICorrelationContext correlation,
    ISalesInvoiceStore invoices,
    ISalesOrderDraftStore orders,
    ISettingsPrerequisiteProvider settings,
    TimeProvider timeProvider,
    ISalesInvoiceableShipmentReadPortV1? shipments = null,
    ISettingsExchangeRateResolverV1? exchangeRates = null) : ISalesInvoiceService
{
    private const string WritePermission = SalesPermissionManifestV1.InvoicesWrite;
    private const string ReadPermission = SalesPermissionManifestV1.InvoicesRead;
    private const string PriceReadPermission = SalesPermissionManifestV1.OrdersPriceRead;
    private const int MaximumCursorLength = 8192;

    public async Task<SalesInvoiceCommandResult> CreateInvoiceAsync(CreateSalesInvoiceRequest request, SalesCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        var scope = await RequireAsync(WritePermission, cancellationToken);
        await RequirePriceReadAsync(scope, cancellationToken);
        Validate(request);
        var requestHash = SalesInvoiceRequestHash.Compute(request);
        var envelope = new SalesDraftWriteEnvelope(scope.ActorId, WritePermission, scope.RoleCodes, metadata.CorrelationId,
            metadata.CausationId, metadata.IdempotencyKey, correlation.SourceChannel, correlation.ClientIpAddress, correlation.UserAgent);

        var replay = await StoreAsync(scope, () => invoices.FindInvoiceReplayAsync(envelope, requestHash, cancellationToken),
            cancellationToken);
        if (replay is not null)
        {
            return Result(replay, isReplay: true);
        }

        var shares = await ReadSharesAsync(scope, request, cancellationToken);
        var invoiced = await StoreAsync(scope,
            () => invoices.FindInvoicedShipmentsAsync([.. request.Shares.Select(share => share.ShipmentId)], cancellationToken),
            cancellationToken);
        var basisByOrder = await ReadOrdersAsync(scope, shares.Values.Select(share => share.SalesOrderId), cancellationToken);

        var reasons = new List<SalesDraftFieldError>();
        var prepared = new List<PreparedShare>(request.Shares.Count);
        for (var index = 0; index < request.Shares.Count; index++)
        {
            var field = string.Create(CultureInfo.InvariantCulture, $"shares[{index}]");
            if (!shares.TryGetValue(request.Shares[index].ShipmentId, out var share))
            {
                reasons.Add(new(field, SalesInvoiceReasonCodesV1.ShareNotInvoiceable));
                continue;
            }

            if (invoiced.Contains(share.ShipmentId))
            {
                reasons.Add(new(field, SalesInvoiceReasonCodesV1.ShareAlreadyInvoiced));
                continue;
            }

            if (Prepare(field, share, basisByOrder[share.SalesOrderId], reasons) is { } ready)
            {
                prepared.Add(ready);
            }
        }

        if (reasons.Count == 0)
        {
            RequireOneInvoiceKey(prepared, reasons);
        }

        if (reasons.Count > 0)
        {
            throw NotReady(reasons);
        }

        var first = prepared[0];
        var now = timeProvider.GetUtcNow();
        var businessDate = Day(now);

        var rate = first.Basis.CurrencyId == SettingsCurrencyIdentitiesV1.TurkishLira
            ? null
            : await ReadRateAsync(scope, first.Basis.CurrencyId, prepared, businessDate, cancellationToken);

        var header = new SalesInvoiceHeader(first.Share.CustomerPartyId, first.Share.CompanyAccountId, first.Basis.CurrencyId,
            first.Basis.MoneyProfileId, first.Basis.MoneyProfileVersion, first.Basis.MoneyScale, businessDate, rate);
        var inputs = prepared.Select(item => new SalesInvoiceShareInput(Guid.NewGuid(), item.Share.ShipmentId, item.Share.DispatchNoteId,
            item.Share.SystemNumber, item.Share.NoteLineNo, item.ShipmentDate, item.Share.SalesOrderId, item.Share.SalesOrderLineId,
            item.Basis.Revision, item.Share.ProductId, item.Share.UnitId, item.Share.Quantity,
            item.Share.Quantity.ToString(CultureInfo.InvariantCulture), item.Line.Commercial, item.Line.Tax!)).ToList();
        var invoiceId = Guid.NewGuid();

        await RequireStillAsync(scope, WritePermission, cancellationToken);
        await RequirePriceReadAsync(scope, cancellationToken);
        SalesInvoiceStoreResult stored;
        try
        {
            stored = await invoices.StoreInvoiceAsync(envelope, requestHash, documentNo => SalesInvoice.Create(invoiceId, scope.CompanyId,
                documentNo, header, inputs, first.DueDates, first.Basis.DueTerms!, scope.ActorId, now), cancellationToken);
        }
        catch (SalesRuleException exception)
        {
            throw NotReady([new("shares", exception.Code switch
            {
                SalesRuleCodes.InvoiceTaxUnavailable => SalesInvoiceReasonCodesV1.TaxUnavailable,
                SalesRuleCodes.InvoiceAmountOutOfRange => SalesInvoiceReasonCodesV1.AmountOutOfRange,
                SalesRuleCodes.InvoiceDueDateMissing => SalesInvoiceReasonCodesV1.DueDatePending,
                _ => SalesInvoiceReasonCodesV1.LineUnpriced,
            })]);
        }
        catch (SalesDueTermsException exception) when (exception.Code == SalesDueTermsCodes.AmountNotSplittable)
        {
            throw NotReady([new("shares", SalesInvoiceReasonCodesV1.DueAmountNotSplittable)]);
        }
        catch (SalesDraftStoreException exception) when (exception.Code == SalesDraftStoreCodes.ConcurrentChange)
        {
            // ADR-011 §8: a racing caller of the same key answers the winner's invoice; another invoice that took a share first
            // leaves nothing under this key — 409 concurrent_change, and the retry names the share as already invoiced.
            var winner = await StoreAsync(scope, () => invoices.FindInvoiceReplayAsync(envelope, requestHash, cancellationToken),
                cancellationToken);
            return winner is not null ? Result(winner, isReplay: true) : throw Store(exception);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        // No fence after the commit (S03): the stored invoice is answered; the next request of the key meets the fence first.
        return Result(stored, stored.Outcome == SalesDraftWriteOutcome.Replayed);
    }

    public async Task<SalesInvoiceListResponse> GetInvoicesAsync(SalesInvoiceListQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var scope = await RequireAsync(ReadPermission, cancellationToken);
        await RequirePriceReadAsync(scope, cancellationToken);
        if (query.PageSize is < 1 or > SalesInvoiceApiV1.MaximumPageSize || query.Cursor is { Length: 0 or > MaximumCursorLength })
        {
            throw new SalesDraftServiceException(400, "validation_failed");
        }

        var page = await StoreAsync(scope, () => invoices.ListInvoicesAsync(query.PageSize, query.Cursor, cancellationToken),
            cancellationToken, ReadPermission);
        await RequirePriceReadAsync(scope, cancellationToken);
        return new SalesInvoiceListResponse([.. page.Items.Select(Summary)], page.NextCursor);
    }

    public async Task<SalesInvoiceDetailView> GetInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken = default)
    {
        var scope = await RequireAsync(ReadPermission, cancellationToken);
        await RequirePriceReadAsync(scope, cancellationToken);
        if (invoiceId == Guid.Empty)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        var invoice = await StoreAsync(scope, () => invoices.FindInvoiceAsync(invoiceId, cancellationToken), cancellationToken,
            ReadPermission) ?? throw new SalesDraftServiceException(404, "not_found");
        await RequirePriceReadAsync(scope, cancellationToken);
        return View(invoice, isReplay: false);
    }

    // ---- validation and owners ------------------------------------------------------------------------------------------

    private static void Validate(CreateSalesInvoiceRequest request)
    {
        var errors = new List<SalesDraftFieldError>();
        if (request.CompanyAccountId == Guid.Empty)
        {
            errors.Add(new("companyAccountId", "required"));
        }

        if (request.Shares is null || request.Shares.Count is 0 or > SalesInvoiceApiV1.MaximumShares)
        {
            errors.Add(new("shares", "count_out_of_range"));
        }
        else
        {
            var seen = new HashSet<Guid>();
            for (var index = 0; index < request.Shares.Count; index++)
            {
                if (request.Shares[index] is not { ShipmentId: var id } || id == Guid.Empty || !seen.Add(id))
                {
                    errors.Add(new(string.Create(CultureInfo.InvariantCulture, $"shares[{index}].shipmentId"), "invalid"));
                }
            }
        }

        if (errors.Count > 0)
        {
            throw new SalesDraftServiceException(400, "validation_failed", errors);
        }
    }

    private async Task<IReadOnlyDictionary<Guid, SalesInvoiceableShipmentV1>> ReadSharesAsync(Scope scope,
        CreateSalesInvoiceRequest request, CancellationToken cancellationToken)
    {
        var owner = shipments ?? throw DispatchUnavailable();
        SalesInvoiceableShipmentAnswerV1? answer;
        try
        {
            answer = await owner.ReadInvoiceableShipmentsAsync(
                new SalesInvoiceableShipmentQueryV1(request.CompanyAccountId, SalesInvoiceApiV1.MaximumShares), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RequireStillAsync(scope, WritePermission, cancellationToken);
            throw DispatchUnavailable();
        }

        await RequireStillAsync(scope, WritePermission, cancellationToken);
        if (answer?.Outcome == SalesDispatchReadOutcomeV1.Denied)
        {
            throw new SalesDraftServiceException(403, "forbidden");
        }

        if (answer is null || answer.Outcome != SalesDispatchReadOutcomeV1.Found || answer.CompanyId != scope.CompanyId
            || answer.Shares is null || answer.Shares.Count > SalesInvoiceApiV1.MaximumShares)
        {
            throw DispatchUnavailable();
        }

        var byShipment = new Dictionary<Guid, SalesInvoiceableShipmentV1>();
        foreach (var share in answer.Shares)
        {
            // A share of another customer than asked, a duplicate shipment or an empty identity breaks the owner's contract.
            if (share is null || share.CompanyAccountId != request.CompanyAccountId || share.ShipmentId == Guid.Empty
                || share.SalesOrderId == Guid.Empty || share.SalesOrderLineId == Guid.Empty || share.Quantity <= 0m
                || string.IsNullOrWhiteSpace(share.SystemNumber) || share.SystemNumber.Length > SalesInvoice.MaximumDispatchNumberLength
                || !byShipment.TryAdd(share.ShipmentId, share))
            {
                throw DispatchUnavailable();
            }
        }

        return byShipment;
    }

    private async Task<IReadOnlyDictionary<Guid, SalesOrderDraft>> ReadOrdersAsync(Scope scope, IEnumerable<Guid> orderIds,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, SalesOrderDraft>();
        foreach (var orderId in orderIds.Distinct())
        {
            var order = await StoreAsync(scope, () => orders.FindDraftAsync(orderId, cancellationToken), cancellationToken);
            // The dispatch owner named an order this company's store does not hold: a malformed owner answer, never a guess.
            result[orderId] = order is not null && order.CompanyId == scope.CompanyId
                ? order
                : throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.ReferenceProviderInvalid);
        }

        return result;
    }

    /// <summary>One share against its order: lifecycle, invoicing gate, price basis line, tax waits, price and due plan.</summary>
    private static PreparedShare? Prepare(string field, SalesInvoiceableShipmentV1 share, SalesOrderDraft order,
        List<SalesDraftFieldError> reasons)
    {
        if (order.State == SalesDraftState.Cancelled)
        {
            reasons.Add(new(field, SalesInvoiceReasonCodesV1.OrderCancelled));
            return null;
        }

        var (gate, _) = SalesDraftService.InvoicingGate(order);
        if (gate != SalesDraftInvoicingGatesV1.Open)
        {
            reasons.Add(new(field, gate switch
            {
                SalesDraftInvoicingGatesV1.AwaitingReapproval => SalesInvoiceReasonCodesV1.OrderAwaitingReapproval,
                SalesDraftInvoicingGatesV1.AwaitingCustomerAcceptance => SalesInvoiceReasonCodesV1.OrderAwaitingCustomerAcceptance,
                _ => SalesInvoiceReasonCodesV1.OrderNotApproved,
            }));
            return null;
        }

        var basis = Basis(order);
        if (basis.PartyId != share.CustomerPartyId || basis.CompanyAccountId != share.CompanyAccountId)
        {
            reasons.Add(new(field, SalesInvoiceReasonCodesV1.CustomerMismatch));
            return null;
        }

        if (!basis.Lines.TryGetValue(share.SalesOrderLineId, out var line))
        {
            reasons.Add(new(field, SalesInvoiceReasonCodesV1.OrderLineNotInBasis));
            return null;
        }

        if (line.OrderUnitId != share.UnitId)
        {
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.ReferenceProviderInvalid);
        }

        var shipmentDate = Day(share.ShippedAtUtc);
        var before = reasons.Count;
        if (line.Commercial.UnitPrice is null || line.Commercial.PriceUnitId != line.OrderUnitId)
        {
            reasons.Add(new(field, SalesInvoiceReasonCodesV1.LineUnpriced));
        }

        if (line.Tax is not { } tax)
        {
            reasons.Add(new(field, SalesInvoiceReasonCodesV1.TaxUnavailable));
        }
        else
        {
            reasons.AddRange(tax.InvoiceBlockers(basis.BuyerTax).Select(blocker => new SalesDraftFieldError(field, blocker)));
            if (tax.Rate is { WithholdingShare: not null })
            {
                reasons.Add(new(field, SalesInvoiceReasonCodesV1.WithholdingThresholdUndecided));
            }

            if (tax.BuyerDocumentRequired && basis.BuyerTax?.DocumentDate is { } documentDate && documentDate > shipmentDate)
            {
                reasons.Add(new(field, SalesInvoiceReasonCodesV1.ExemptionDocumentAfterShipment));
            }
        }

        IReadOnlyList<SalesDueDate> dueDates = [];
        if (basis.DueTerms is null)
        {
            reasons.Add(new(field, SalesInvoiceReasonCodesV1.DueTermsMissing));
        }
        else
        {
            var due = SalesDueDateCalculator.Compute(basis.DueTerms, order.Header.BusinessDate, shipmentDate);
            if (!due.IsResolved)
            {
                reasons.Add(new(field, SalesInvoiceReasonCodesV1.DueDatePending));
            }

            dueDates = due.DueDates;
        }

        return reasons.Count == before ? new PreparedShare(field, share, shipmentDate, basis, line, dueDates) : null;
    }

    /// <summary>SAT-D101: one currency and money profile, one due plan (the same terms giving the same dates) for every share.</summary>
    private static void RequireOneInvoiceKey(IReadOnlyList<PreparedShare> prepared, List<SalesDraftFieldError> reasons)
    {
        var first = prepared[0];
        foreach (var item in prepared.Skip(1))
        {
            if (item.Basis.CurrencyId != first.Basis.CurrencyId)
            {
                reasons.Add(new(item.Field, SalesInvoiceReasonCodesV1.CurrencyMismatch));
            }
            else if (item.Basis.MoneyProfileId != first.Basis.MoneyProfileId || item.Basis.MoneyScale != first.Basis.MoneyScale
                || !string.Equals(item.Basis.MoneyProfileVersion, first.Basis.MoneyProfileVersion, StringComparison.Ordinal))
            {
                reasons.Add(new(item.Field, SalesInvoiceReasonCodesV1.MoneyProfileMismatch));
            }

            if (!SalesDueTerms.SameTerms(item.Basis.DueTerms, first.Basis.DueTerms)
                || !item.DueDates.Select(date => date.DueDate).SequenceEqual(first.DueDates.Select(date => date.DueDate)))
            {
                reasons.Add(new(item.Field, SalesInvoiceReasonCodesV1.DueDateMismatch));
            }
        }
    }

    /// <summary>
    /// SAT-D90 / SAT-D170: the rate of every shipment day, resolved by the Settings resolver; all must be resolved and name the
    /// same bulletin day and value (SAT-D101) — that one number is written on the invoice (SAT-D172). The functional scale is the
    /// Settings TRY money profile's (SAT-D223's "2 ondalık").
    /// </summary>
    private async Task<SalesInvoiceRateSnapshot> ReadRateAsync(Scope scope, Guid currencyId, IReadOnlyList<PreparedShare> prepared,
        DateOnly businessDate, CancellationToken cancellationToken)
    {
        var resolver = exchangeRates ?? throw NotReady([new("shares", SalesInvoiceReasonCodesV1.RateUnresolved)]);
        var resolved = new List<(DateOnly Day, DateOnly Bulletin, decimal Rate, string Field)>();
        var reasons = new List<SalesDraftFieldError>();
        foreach (var group in prepared.GroupBy(item => item.ShipmentDate))
        {
            ExchangeRateResolutionV1? answer;
            try
            {
                answer = await resolver.ResolveAsync(group.Key, currencyId, SettingsCurrencyIdentitiesV1.TurkishLira, cancellationToken);
            }
            catch (SettingsPrerequisiteException exception) when (exception.Code == ExchangeRateProblemCodesV1.AccessDenied)
            {
                await RequireStillAsync(scope, WritePermission, cancellationToken);
                throw new SalesDraftServiceException(403, "forbidden");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await RequireStillAsync(scope, WritePermission, cancellationToken);
                answer = null;
            }

            await RequireStillAsync(scope, WritePermission, cancellationToken);
            if (answer is { State: ExchangeRateResolutionStatesV1.Resolved, BulletinDate: { } bulletin, SellingRate: { } text }
                && answer.SourceCurrencyId == currencyId && answer.RateDay == group.Key
                && SalesLinePriceCalculator.TryParseExact(text, out var value) && value > 0m)
            {
                resolved.Add((group.Key, bulletin, value, group.First().Field));
            }
            else
            {
                reasons.AddRange(group.Select(item => new SalesDraftFieldError(item.Field, SalesInvoiceReasonCodesV1.RateUnresolved)));
            }
        }

        if (reasons.Count == 0)
        {
            var first = resolved[0];
            reasons.AddRange(resolved.Skip(1).Where(item => item.Bulletin != first.Bulletin || item.Rate != first.Rate)
                .Select(item => new SalesDraftFieldError(item.Field, SalesInvoiceReasonCodesV1.RateMismatch)));
        }

        if (reasons.Count > 0)
        {
            throw NotReady(reasons);
        }

        MoneyProfileV1 functional;
        try
        {
            functional = await settings.GetMoneyProfileAsync(SettingsCurrencyIdentitiesV1.TurkishLira, SalesMoneyOperations.OrderDraftV1,
                businessDate, cancellationToken);
        }
        catch (SettingsPrerequisiteException)
        {
            await RequireStillAsync(scope, WritePermission, cancellationToken);
            throw new SalesDraftServiceException(503, "service_unavailable");
        }

        await RequireStillAsync(scope, WritePermission, cancellationToken);
        if (functional is null || functional.CompanyId != scope.CompanyId || functional.CurrencyId != SettingsCurrencyIdentitiesV1.TurkishLira
            || functional.Scale is < 0 or > Money.MaximumScale || !functional.IsActive)
        {
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.ReferenceProviderInvalid);
        }

        return new SalesInvoiceRateSnapshot(resolved[0].Rate, resolved[0].Day, resolved[0].Bulletin,
            SettingsCurrencyIdentitiesV1.TurkishLira, functional.Scale);
    }

    /// <summary>
    /// The price basis the gate opened (SAT-D103/D114/D115): the customer-accepted archive when the customer rejected the last
    /// approved revision; else the current revision when it is approved; else (a newer revision rejected internally) the last
    /// approved archive. Only called when the gate is <c>Open</c>, so exactly one applies.
    /// </summary>
    private static InvoiceBasis Basis(SalesOrderDraft order)
    {
        var archived = order.CustomerAcceptedFallback
            ?? (order.ApprovalState == InternalApprovalState.Approved ? null : order.ArchivedRevisions[^1]);
        if (archived is null)
        {
            return new InvoiceBasis(order.Revision, order.Header.PartyId, order.Header.CompanyAccountId, order.Header.CurrencyId,
                order.Money.ProfileId, order.Money.ProfileVersion, order.Money.Scale, order.Header.DueTerms, order.Header.BuyerTax,
                order.Lines.ToDictionary(line => line.Id, line => new BasisLine(line.OrderUnit.UnitId, line.Commercial, line.Tax)));
        }

        var header = archived.Header
            ?? throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.ReferenceProviderInvalid);
        return new InvoiceBasis(archived.Revision, header.PartyId, header.CompanyAccountId, header.CurrencyId, archived.MoneyProfileId,
            archived.MoneyProfileVersion, archived.MoneyScale, header.DueTerms, header.BuyerTax,
            archived.Lines.ToDictionary(line => line.LineId, line => new BasisLine(line.OrderUnitId, line.Commercial, line.Tax)));
    }

    // ---- mapping ------------------------------------------------------------------------------------------------------------

    private static SalesInvoiceDetailView View(SalesInvoice invoice, bool isReplay)
    {
        var header = invoice.Header;
        var scale = header.AmountScale;
        var functionalScale = header.Rate?.FunctionalScale ?? 0;
        var groups = invoice.Lines.GroupBy(line => line.VatPercent).Select(group =>
        {
            var lines = group.ToList();
            var amounts = SalesInvoiceCalculator.Sum([.. lines.Select(line => line.Amounts)], header.CurrencyId, scale);
            var functional = header.Rate is { } rate
                ? SalesInvoiceCalculator.Sum([.. lines.Select(line => line.Functional!.Amounts)], rate.FunctionalCurrencyId,
                    rate.FunctionalScale)
                : null;
            return new SalesInvoiceVatGroupView(Decimal(group.Key), lines.Count, Amounts(amounts, scale),
                functional is null ? null : Amounts(functional, functionalScale));
        }).ToList();
        return new SalesInvoiceDetailView(invoice.Id, invoice.DocumentNo, invoice.State.ToString(),
            invoice.Version.ToString(CultureInfo.InvariantCulture), header.BusinessDate, header.PartyId, header.CompanyAccountId,
            header.CurrencyId, scale,
            header.Rate is { } r ? new SalesInvoiceRateView(Decimal(r.Rate), r.RateDay, r.BulletinDate, r.FunctionalCurrencyId, r.FunctionalScale) : null,
            Amounts(invoice.Totals, scale),
            invoice.FunctionalTotals is { } totals ? Amounts(totals, functionalScale) : null,
            groups,
            [.. invoice.Instalments.Select(item => new SalesInvoiceInstalmentView(item.SequenceNo, item.DueDate, item.SharePercent,
                Text(item.Amount.Amount, scale)))],
            [.. invoice.Lines.Select(line => new SalesInvoiceLineView(line.Id, line.LineNo, line.Share.ShipmentId,
                line.Share.DispatchNoteId, line.Share.DispatchNoteNumber, line.Share.NoteLineNo, line.Share.ShipmentDate,
                line.Share.SalesOrderId, line.Share.SalesOrderLineId, SalesDraftApiV1.RevisionText(line.Share.PriceBasisRevision),
                line.Share.ProductId, line.Share.UnitId, line.Share.QuantityText, Decimal(line.NetUnitPrice), Decimal(line.VatPercent),
                line.WithholdingShare is { } share ? Decimal(share) : null, Amounts(line.Amounts, scale),
                line.Functional is { } f
                    ? new SalesInvoiceLineFunctionalView(Amounts(f.Amounts, functionalScale), [.. f.Adjusted.Select(type => type.ToString())],
                        f.NeedsReview)
                    : null))],
            invoice.CreatedBy, invoice.CreatedAtUtc, isReplay);
    }

    private static SalesInvoiceCommandResult Result(SalesInvoiceStoreResult stored, bool isReplay) =>
        new(View(stored.Invoice, isReplay), isReplay, stored.OriginalCorrelationId);

    private static SalesInvoiceSummaryView Summary(SalesInvoice invoice) => new(invoice.Id, invoice.DocumentNo, invoice.State.ToString(),
        invoice.Version.ToString(CultureInfo.InvariantCulture), invoice.Header.BusinessDate, invoice.Header.PartyId,
        invoice.Header.CompanyAccountId, invoice.Header.CurrencyId, Text(invoice.Totals.Payable.Amount, invoice.Header.AmountScale),
        invoice.FunctionalTotals is { } functional ? Text(functional.Payable.Amount, invoice.Header.Rate!.FunctionalScale) : null,
        invoice.Lines.Count, invoice.CreatedAtUtc);

    private static SalesInvoiceAmountsView Amounts(SalesInvoiceAmountSet set, int scale) => new(Text(set.Net.Amount, scale),
        Text(set.Vat.Amount, scale), Text(set.Withholding.Amount, scale), Text(set.Gross.Amount, scale), Text(set.Payable.Amount, scale));

    private static string Text(decimal amount, int scale) =>
        amount.ToString("F" + scale.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    private static string Decimal(decimal value) => SalesLinePriceCalculator.Format(value);

    /// <summary>SAT-D10: the Europe/Istanbul day; without the zone nothing is guessed (503, never UTC).</summary>
    private static DateOnly Day(DateTimeOffset instant)
    {
        try
        {
            return SalesBusinessDay.DateOf(instant);
        }
        catch (SalesBusinessDayUnavailableException)
        {
            throw new SalesDraftServiceException(503, "service_unavailable");
        }
    }

    // ---- fences -------------------------------------------------------------------------------------------------------------

    private async Task<Scope> RequireAsync(string permission, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            throw new SalesDraftServiceException(401, "unauthenticated");
        }

        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty || !currentUser.CanAccessCompany(companyContext.CompanyId))
        {
            throw new SalesDraftServiceException(403, "forbidden");
        }

        if (!ErpPermissions.IsKnown(permission))
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

        return new Scope(companyId, actorId, [.. measured.RoleCodes.Order(StringComparer.Ordinal)]);
    }

    private async Task RequireStillAsync(Scope scope, string permission, CancellationToken cancellationToken)
    {
        var current = await RequireAsync(permission, cancellationToken);
        if (current.CompanyId != scope.CompanyId || !string.Equals(current.ActorId, scope.ActorId, StringComparison.Ordinal))
        {
            throw new SalesDraftServiceException(403, "forbidden");
        }
    }

    /// <summary>The invoice is prices: the price read is REQUIRED (403 otherwise), measured for the same company and actor.</summary>
    private Task RequirePriceReadAsync(Scope scope, CancellationToken cancellationToken) =>
        RequireStillAsync(scope, PriceReadPermission, cancellationToken);

    private bool Matches(Guid companyId, string actorId) =>
        currentUser.IsAuthenticated
        && string.Equals(currentUser.UserId, actorId, StringComparison.Ordinal)
        && companyContext.IsResolved
        && companyContext.CompanyId == companyId
        && currentUser.CanAccessCompany(companyId);

    /// <summary>A Sales store read under the fence: a store refusal is mapped; the fence is measured again after the await.</summary>
    private async Task<T> StoreAsync<T>(Scope scope, Func<Task<T>> read, CancellationToken cancellationToken,
        string permission = WritePermission)
    {
        T result;
        try
        {
            result = await read();
        }
        catch (SalesDraftStoreException exception)
        {
            await RequireStillAsync(scope, permission, cancellationToken);
            throw Store(exception);
        }

        await RequireStillAsync(scope, permission, cancellationToken);
        return result;
    }

    private static SalesDraftServiceException Store(SalesDraftStoreException exception) => exception.Code switch
    {
        SalesDraftStoreCodes.SameKeyPayloadMismatch => new SalesDraftServiceException(409, SalesDraftProblemCodesV1.SameKeyPayloadMismatch),
        SalesDraftStoreCodes.ConcurrentChange => new SalesDraftServiceException(409, SalesDraftProblemCodesV1.ConcurrentChange),
        SalesDraftStoreCodes.InvalidCursor => new SalesDraftServiceException(400, SalesDraftProblemCodesV1.InvalidCursor),
        SalesDraftStoreCodes.CompanyContextMismatch => new SalesDraftServiceException(403, "forbidden"),
        _ => new SalesDraftServiceException(503, "service_unavailable"),
    };

    private static SalesDraftServiceException NotReady(IReadOnlyList<SalesDraftFieldError> reasons) =>
        new(409, SalesInvoiceProblemCodesV1.InvoiceNotReady, reasons);

    private static SalesDraftServiceException DispatchUnavailable() =>
        new(503, SalesDispatchProblemCodesV1.DispatchProviderUnavailable);

    private sealed record Scope(Guid CompanyId, string ActorId, IReadOnlyCollection<string> RoleCodes);

    private sealed record BasisLine(Guid OrderUnitId, CommercialDraftFields Commercial, SalesLineTaxSnapshot? Tax);

    private sealed record InvoiceBasis(int Revision, Guid PartyId, Guid CompanyAccountId, Guid CurrencyId, Guid MoneyProfileId,
        string MoneyProfileVersion, int MoneyScale, SalesDueTerms? DueTerms, SalesBuyerTax? BuyerTax,
        IReadOnlyDictionary<Guid, BasisLine> Lines);

    private sealed record PreparedShare(string Field, SalesInvoiceableShipmentV1 Share, DateOnly ShipmentDate, InvoiceBasis Basis,
        BasisLine Line, IReadOnlyList<SalesDueDate> DueDates);
}
