using System.Globalization;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Invoices;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>
/// F2 — the subcontract service invoice DRAFT (#66 m.4, URT-D16, SAT-D195, SAT-D309/D312, LOJ-D4/D9/D12). The same K3 skeleton
/// as the dispatch-share invoice: the write name (<c>sales.invoices.write</c>) and the price read are measured before anything is
/// read, after every awaited owner read and right before the single save; the replay comes before any owner is asked; one save
/// writes the invoice, its line, the attempt and the audit event.
/// <list type="bullet">
/// <item><b>Key</b> — the subcontract order + the loading instruction + its delivery weighing (the F2 "separate key"); a second
/// invoice of the same key is refused (the store's unique index decides a race).</item>
/// <item><b>Quantity</b> — the Logistics owner's delivery weighing net kg (SAT-D309, LOJ-D9), billed whole; scrap is not
/// deducted. No weighing, no invoice (LOJ-D12).</item>
/// <item><b>Price</b> — the order's invoicing gate must be <c>Open</c>; the kg price and tax snapshot are the basis revision's.</item>
/// <item><b>Waits, never guesses</b> — a weighed instruction serving another order or more than one line (the SAT-D312 split's
/// denominator and residue are an open F2 point), a foreign-currency order (its rate day is open), a missing price or tax: 409
/// <c>invoice_not_ready</c> with every reason, nothing recorded under the key.</item>
/// </list>
/// Amounts are <see cref="SalesInvoiceCalculator"/>'s only. The due plan, approval and finalisation of a service invoice join the
/// invoice flow in a later slice; this draft carries no instalments.
/// </summary>
public sealed partial class SalesInvoiceService : ISalesSubcontractServiceInvoiceService
{
    public async Task<SalesSubcontractServiceInvoiceCommandResult> CreateSubcontractServiceInvoiceAsync(
        CreateSalesSubcontractServiceInvoiceRequest request, SalesCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        var scope = await RequireAsync(WritePermission, cancellationToken);
        await RequirePriceReadAsync(scope, cancellationToken);
        Validate(request);
        var store = subcontractInvoices ?? throw new SalesDraftServiceException(503, "service_unavailable");
        var requestHash = SalesSubcontractServiceInvoiceRequestHash.Compute(request);
        var envelope = new SalesDraftWriteEnvelope(scope.ActorId, WritePermission, scope.RoleCodes, metadata.CorrelationId,
            metadata.CausationId, metadata.IdempotencyKey, correlation.SourceChannel, correlation.ClientIpAddress, correlation.UserAgent);

        var replay = await StoreAsync(scope, () => store.FindSubcontractServiceInvoiceReplayAsync(envelope, requestHash, cancellationToken),
            cancellationToken);
        if (replay is not null)
        {
            return SubcontractResult(replay, isReplay: true);
        }

        // Another company's order is null under the tenant filter: 404, and no weighing is read for it.
        var order = await StoreAsync(scope, () => orders.FindDraftAsync(request.SalesOrderId, cancellationToken), cancellationToken);
        if (order is null || order.CompanyId != scope.CompanyId)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        var reasons = new List<SalesDraftFieldError>();
        if (!order.IsSubcontractService)
        {
            throw NotReady([new("salesOrderId", SalesSubcontractServiceInvoiceReasonCodesV1.NotSubcontractOrder)]);
        }

        var weighing = await ReadDeliveryWeighingAsync(scope, request, cancellationToken);
        if (weighing is null)
        {
            throw NotReady([new("weighingId", SalesSubcontractServiceInvoiceReasonCodesV1.WeighingMissing)]);
        }

        var netKg = ParseKilograms(weighing.NetKg);
        var served = weighing.Lines.Where(line => line.SourceOrderId == order.Id).ToList();
        if (served.Count == 0)
        {
            throw NotReady([new("loadingInstructionId", SalesSubcontractServiceInvoiceReasonCodesV1.WeighingOrderMismatch)]);
        }

        // SAT-D312: the share by declared kilograms is decided, but which line quantity is the denominator and where the residue
        // goes is an open F2 point with a money effect. Only the unsplit case (one order, one line) is billed now.
        if (served.Count != weighing.Lines.Count || served.Select(line => line.SourceOrderLineId).Distinct().Count() != 1)
        {
            throw NotReady([new("loadingInstructionId", SalesSubcontractServiceInvoiceReasonCodesV1.SharePolicyPending)]);
        }

        var existing = await StoreAsync(scope, () => store.FindSubcontractServiceInvoiceByWeighingAsync(request.SalesOrderId,
            request.LoadingInstructionId, request.WeighingId, cancellationToken), cancellationToken);
        if (existing is not null)
        {
            throw NotReady([new("weighingId", SalesSubcontractServiceInvoiceReasonCodesV1.WeighingAlreadyInvoiced)]);
        }

        var (gate, _) = SalesDraftService.InvoicingGate(order);
        if (gate != SalesDraftInvoicingGatesV1.Open)
        {
            throw NotReady([new("salesOrderId", gate switch
            {
                SalesDraftInvoicingGatesV1.AwaitingReapproval => SalesInvoiceReasonCodesV1.OrderAwaitingReapproval,
                SalesDraftInvoicingGatesV1.AwaitingCustomerAcceptance => SalesInvoiceReasonCodesV1.OrderAwaitingCustomerAcceptance,
                _ => SalesInvoiceReasonCodesV1.OrderNotApproved,
            })]);
        }

        var basis = Basis(order);
        var servedLine = served[0];
        if (!basis.Lines.TryGetValue(servedLine.SourceOrderLineId, out var line))
        {
            throw NotReady([new("loadingInstructionId", SalesInvoiceReasonCodesV1.OrderLineNotInBasis)]);
        }

        if (line.OrderUnitId != servedLine.UnitId)
        {
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.ReferenceProviderInvalid);
        }

        const string lineField = "lines[0]";
        if (basis.CurrencyId != SettingsCurrencyIdentitiesV1.TurkishLira)
        {
            reasons.Add(new("salesOrderId", SalesSubcontractServiceInvoiceReasonCodesV1.CurrencyPending));
        }

        if (line.OrderUnitId != SettingsUnitIdentitiesV1.Kilogram)
        {
            reasons.Add(new(lineField, SalesSubcontractServiceInvoiceReasonCodesV1.LineNotKilogram));
        }

        if (line.Commercial.UnitPrice is null || line.Commercial.PriceUnitId != line.OrderUnitId)
        {
            reasons.Add(new(lineField, SalesInvoiceReasonCodesV1.LineUnpriced));
        }

        if (line.Tax is not { } tax)
        {
            reasons.Add(new(lineField, SalesInvoiceReasonCodesV1.TaxUnavailable));
        }
        else
        {
            reasons.AddRange(tax.InvoiceBlockers(basis.BuyerTax).Select(blocker => new SalesDraftFieldError(lineField, blocker)));
        }

        if (reasons.Count > 0)
        {
            throw NotReady(reasons);
        }

        var now = timeProvider.GetUtcNow();
        var header = new SalesSubcontractServiceInvoiceHeader(basis.PartyId, basis.CompanyAccountId, basis.CurrencyId,
            basis.MoneyProfileId, basis.MoneyProfileVersion, basis.MoneyScale, Day(now));
        var netKgText = weighing.NetKg.Trim();
        var source = new SalesSubcontractServiceInvoiceSource(order.Id, request.LoadingInstructionId, request.WeighingId,
            weighing.WeighingRevision, weighing.InstructionVersion, netKgText);
        SalesSubcontractServiceLineInput[] inputs =
        [
            new(Guid.NewGuid(), servedLine.SourceOrderLineId, basis.Revision, servedLine.ProductId, line.OrderUnitId, netKg, netKgText,
                line.Commercial, line.Tax!),
        ];
        var invoiceId = Guid.NewGuid();

        await RequireStillAsync(scope, WritePermission, cancellationToken);
        await RequirePriceReadAsync(scope, cancellationToken);
        SalesSubcontractServiceInvoiceStoreResult stored;
        try
        {
            stored = await store.StoreSubcontractServiceInvoiceAsync(envelope, requestHash, documentNo =>
                SalesSubcontractServiceInvoice.Create(invoiceId, scope.CompanyId, documentNo, header, source, inputs, netKg,
                    scope.ActorId, now), cancellationToken);
        }
        catch (SalesRuleException exception)
        {
            throw NotReady([new(lineField, exception.Code switch
            {
                SalesRuleCodes.InvoiceTaxUnavailable => SalesInvoiceReasonCodesV1.TaxUnavailable,
                SalesRuleCodes.InvoiceAmountOutOfRange => SalesInvoiceReasonCodesV1.AmountOutOfRange,
                _ => SalesInvoiceReasonCodesV1.LineUnpriced,
            })]);
        }
        catch (SalesDraftStoreException exception) when (exception.Code == SalesDraftStoreCodes.ConcurrentChange)
        {
            // ADR-011 §8: a racing caller of the same key answers the winner's invoice; another key that invoiced the weighing
            // first leaves nothing under this key — 409 concurrent_change, and the retry names the weighing as already invoiced.
            var winner = await StoreAsync(scope, () => store.FindSubcontractServiceInvoiceReplayAsync(envelope, requestHash,
                cancellationToken), cancellationToken);
            return winner is not null ? SubcontractResult(winner, isReplay: true) : throw Store(exception);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        // No fence after the commit (S03): the stored invoice is answered; the next request of the key meets the fence first.
        return SubcontractResult(stored, stored.Outcome == SalesDraftWriteOutcome.Replayed);
    }

    public async Task<SalesSubcontractServiceInvoiceDetailView> GetSubcontractServiceInvoiceAsync(Guid invoiceId,
        CancellationToken cancellationToken = default)
    {
        var scope = await RequireAsync(ReadPermission, cancellationToken);
        await RequirePriceReadAsync(scope, cancellationToken);
        if (invoiceId == Guid.Empty)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        var store = subcontractInvoices ?? throw new SalesDraftServiceException(503, "service_unavailable");
        var invoice = await StoreAsync(scope, () => store.FindSubcontractServiceInvoiceAsync(invoiceId, cancellationToken),
            cancellationToken, ReadPermission) ?? throw new SalesDraftServiceException(404, "not_found");
        await RequirePriceReadAsync(scope, cancellationToken);
        return SubcontractView(invoice, isReplay: false);
    }

    private static void Validate(CreateSalesSubcontractServiceInvoiceRequest request)
    {
        var errors = new List<SalesDraftFieldError>();
        if (request.SalesOrderId == Guid.Empty)
        {
            errors.Add(new("salesOrderId", "required"));
        }

        if (request.LoadingInstructionId == Guid.Empty)
        {
            errors.Add(new("loadingInstructionId", "required"));
        }

        if (request.WeighingId == Guid.Empty)
        {
            errors.Add(new("weighingId", "required"));
        }

        if (errors.Count > 0)
        {
            throw new SalesDraftServiceException(400, "validation_failed", errors);
        }
    }

    /// <summary>
    /// The Logistics owner's delivery weighing under the fence: <c>NoSource</c> is null (no weighing, no invoice — LOJ-D12);
    /// <c>Unavailable</c>, a throw, another company's or another key's answer, or a malformed value is a 503, never a guess.
    /// </summary>
    private async Task<SalesSubcontractDeliveryWeighingV1?> ReadDeliveryWeighingAsync(Scope scope,
        CreateSalesSubcontractServiceInvoiceRequest request, CancellationToken cancellationToken)
    {
        var owner = subcontractWeighings ?? throw new SalesDraftServiceException(503, "service_unavailable");
        SalesSubcontractDeliveryWeighingReadV1? answer;
        try
        {
            answer = await owner.ReadAsync(scope.CompanyId, request.LoadingInstructionId, request.WeighingId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RequireStillAsync(scope, WritePermission, cancellationToken);
            throw new SalesDraftServiceException(503, "service_unavailable");
        }

        await RequireStillAsync(scope, WritePermission, cancellationToken);
        if (answer is null || answer.CompanyId != scope.CompanyId || answer.LoadingInstructionId != request.LoadingInstructionId
            || answer.WeighingId != request.WeighingId)
        {
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.ReferenceProviderInvalid);
        }

        switch (answer.Status)
        {
            case SalesSubcontractDeliveryWeighingStatusV1.NoSource:
                return null;
            case SalesSubcontractDeliveryWeighingStatusV1.Available when answer.Value is { } value
                && value.Lines is { Count: > 0 } lines && lines.All(line => line is not null && line.SourceOrderId != Guid.Empty
                    && line.SourceOrderLineId != Guid.Empty && line.ProductId != Guid.Empty && line.UnitId != Guid.Empty)
                && value.WeighingRevision >= 1 && value.InstructionVersion >= 1 && !string.IsNullOrWhiteSpace(value.NetKg)
                && value.NetKg.Trim().Length <= SalesSubcontractServiceInvoice.MaximumQuantityTextLength:
                return value;
            case SalesSubcontractDeliveryWeighingStatusV1.Available:
                throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.ReferenceProviderInvalid);
            default:
                throw new SalesDraftServiceException(503, "service_unavailable");
        }
    }

    /// <summary>The owner's exact invariant kilogram text; a non-positive or unreadable value breaks the owner's contract (503).</summary>
    private static decimal ParseKilograms(string text) =>
        decimal.TryParse(text.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) && value > 0m
            ? value
            : throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.ReferenceProviderInvalid);

    private static SalesSubcontractServiceInvoiceDetailView SubcontractView(SalesSubcontractServiceInvoice invoice, bool isReplay)
    {
        var header = invoice.Header;
        var scale = header.AmountScale;
        return new SalesSubcontractServiceInvoiceDetailView(invoice.Id, invoice.DocumentNo, invoice.State.ToString(),
            invoice.Version.ToString(CultureInfo.InvariantCulture), header.BusinessDate, header.PartyId, header.CompanyAccountId,
            header.CurrencyId, scale, invoice.Source.SalesOrderId, invoice.Source.LoadingInstructionId, invoice.Source.WeighingId,
            invoice.Source.WeighingRevision, invoice.Source.NetKgText, Amounts(invoice.Totals, scale),
            [.. invoice.Lines.Select(line => new SalesSubcontractServiceInvoiceLineView(line.Id, line.LineNo, line.Input.SalesOrderLineId,
                SalesDraftApiV1.RevisionText(line.Input.PriceBasisRevision), line.Input.ProductId, line.Input.UnitId, line.Input.QuantityText,
                Decimal(line.NetUnitPrice), Decimal(line.VatPercent), line.WithholdingShare is { } share ? Decimal(share) : null,
                Amounts(line.Amounts, scale)))],
            invoice.CreatedBy, invoice.CreatedAtUtc, isReplay);
    }

    private static SalesSubcontractServiceInvoiceCommandResult SubcontractResult(SalesSubcontractServiceInvoiceStoreResult stored,
        bool isReplay) => new(SubcontractView(stored.Invoice, isReplay), isReplay, stored.OriginalCorrelationId);
}
