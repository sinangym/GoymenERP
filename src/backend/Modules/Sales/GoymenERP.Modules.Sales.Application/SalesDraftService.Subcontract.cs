using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>
/// SAT-D175…D178 (EK25; handoff S-3): the incoming subcontract service ("gelen fason hizmet") order — a NORMAL sales order flagged
/// "fason" at create (the same approval, risk, customer acceptance and revision rules, SAT-D175).
/// <list type="bullet">
/// <item><b>Lines (SAT-D176):</b> each line's item type is the Product owner's shared card (SAT-D149) through
/// <see cref="IProductItemTypeReaderV1"/>; only <c>Service</c> is admitted (the domain refuses anything else). Unreadable → 503
/// <see cref="SalesDraftProblemCodesV1.ProductItemTypeUnavailable"/>, never assumed a service. A normal order asks nothing new.
/// SAT-D167 line add / product change does not exist yet; when it does, it must call the same check.</item>
/// <item><b>Price (SAT-D177/D178):</b> the price comes from the list or is typed. A typed price on a service line whose list chain
/// answers "no price" (neither a price nor a floor) is recorded with a <see cref="PriceListAbsenceSnapshot"/> (who wrote which value,
/// when) and needs no discount-limit approval — SAT-D36/D43's fail-closed routing is lifted for that line only. Writing it still
/// needs the price-write name (SAT-D151; the create/revision fences, unchanged). A list with a price or floor keeps every existing
/// rule; an owner that is not composed or answers out of contract leaves the line on the existing approval routing (fail-closed).
/// The price unit must be the line's order unit (the owner converts nothing).</item>
/// <item><b>Customer (SAT-D175):</b> while a production plan or customer-goods allocation is linked to the order — or that cannot
/// be read, as today when no Production port is composed — the customer does not change.</item>
/// <item><b>Tax:</b> the service line resolves through the existing line tax snapshot (Settings, by item type); the subcontract
/// withholding stays whatever the Settings owner answers, fail-closed until the advisor's codes exist (SAT-D110).</item>
/// </list>
/// </summary>
public sealed partial class SalesDraftService
{
    /// <summary>
    /// SAT-D176: the Product owner's item type of every line of a subcontract service order, under <paramref name="permission"/>
    /// (fenced after each read). A product that is not a service — or whose item type is not set, not found — is 422 on
    /// <c>lines[i].productId</c> (<see cref="SalesRuleCodes.SubcontractLineNotService"/>); a denied read is 403; an unreadable owner 503.
    /// </summary>
    private async Task<List<SalesDraftLineInput>> ConfirmServiceLinesAsync(Scope scope, string? permission,
        IReadOnlyList<SalesDraftLineInput> lineInputs, CancellationToken cancellationToken)
    {
        var reader = itemTypes ?? throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.ProductItemTypeUnavailable);
        var confirmed = new List<SalesDraftLineInput>(lineInputs.Count);
        for (var index = 0; index < lineInputs.Count; index++)
        {
            var input = lineInputs[index];
            var answer = await GuardedAsync(scope, permission, () => reader.GetAsync(input.Product.ProductId, cancellationToken),
                cancellationToken);
            switch (answer?.Outcome)
            {
                case ProductItemTypeOutcomeV1.Found when string.Equals(answer.ItemType, SalesOrderDraft.ServiceItemType, StringComparison.Ordinal):
                    confirmed.Add(input with { ItemType = SalesOrderDraft.ServiceItemType });
                    break;
                case ProductItemTypeOutcomeV1.Found or ProductItemTypeOutcomeV1.NotSet or ProductItemTypeOutcomeV1.NotFound:
                    throw new SalesDraftServiceException(422, "validation_failed",
                        [new($"lines[{index}].productId", SalesRuleCodes.SubcontractLineNotService)]);
                case ProductItemTypeOutcomeV1.Denied:
                    throw new SalesDraftServiceException(403, "forbidden");
                default:
                    throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.ProductItemTypeUnavailable);
            }
        }

        return confirmed;
    }

    /// <summary>
    /// SAT-D178 at create: the lines of a subcontract service order whose typed price (in the order unit) meets a list chain that
    /// holds neither a price nor a floor — each such line's id; and the currency code the absence is recorded in. Nothing is read
    /// when no line is priced; without the price owner, or on an owner answer out of contract, no line is free (fail-closed).
    /// </summary>
    private async Task<(IReadOnlySet<Guid> Lines, string? CurrencyCode)> FindFreeSubcontractPricesAsync(Scope scope, string? permission,
        Guid partyId, Guid companyAccountId, Guid currencyId, DateOnly on, IReadOnlyList<SalesDraftLineInput> lineInputs,
        CancellationToken cancellationToken)
    {
        var priced = lineInputs.Where(input => input.Commercial is { UnitPrice: not null } commercial
            && commercial.PriceUnitId == input.OrderUnit.UnitId).ToArray();
        if (priced.Length == 0 || priceReferences is not { } reader)
        {
            return (new HashSet<Guid>(), null);
        }

        var currencyCode = await CurrencyCodeAsync(scope, permission, currencyId, on, cancellationToken);
        var free = new HashSet<Guid>();
        foreach (var input in priced)
        {
            if (await HasNoListPriceAsync(scope, permission, reader, partyId, companyAccountId, input.Product.ProductId,
                    input.OrderUnit.UnitId, input.Quantity, currencyCode, on, cancellationToken))
            {
                free.Add(input.LineId);
            }
        }

        return (free, currencyCode);
    }

    /// <summary>
    /// SAT-D178 at a commercial revision: for a subcontract service order, every request line that types a price in its order unit
    /// WITHOUT naming a list version, whose list chain holds neither a price nor a floor — keyed by line id. Nothing is read when no
    /// such line is in the request; a normal order answers none.
    /// </summary>
    private async Task<(IReadOnlyDictionary<Guid, decimal> Lines, string? CurrencyCode, DateOnly On)> FindRevisionFreeSubcontractPricesAsync(Scope scope,
        string permission, Guid draftId, SalesDraftCommercialRevisionRequest request, CancellationToken cancellationToken)
    {
        var typed = request.Lines.Where(line => line.UnitPrice is not null && line.PriceUnitId is not null && line.PriceReference is null)
            .ToArray();
        if (typed.Length == 0 || priceReferences is not { } reader)
        {
            return (new Dictionary<Guid, decimal>(), null, default);
        }

        var draft = await LoadDraftAsync(scope, permission, draftId, cancellationToken);
        if (!draft.IsSubcontractService)
        {
            return (new Dictionary<Guid, decimal>(), null, default);
        }

        var currencyCode = await CurrencyCodeAsync(scope, permission, draft, cancellationToken);
        var free = new Dictionary<Guid, decimal>();
        foreach (var line in typed)
        {
            var draftLine = draft.Lines.SingleOrDefault(candidate => candidate.Id == line.LineId);
            if (draftLine is null || line.PriceUnitId != draftLine.OrderUnit.UnitId)
            {
                continue;
            }

            if (await HasNoListPriceAsync(scope, permission, reader, draft.Header.PartyId, draft.Header.CompanyAccountId,
                    draftLine.Product.ProductId, draftLine.OrderUnit.UnitId, draftLine.Quantity.Value, currencyCode, draft.Money.EffectiveOn,
                    cancellationToken))
            {
                // The quantity the owner was asked at; the domain refuses it if the line moved meanwhile (a stale measurement).
                free.Add(line.LineId, draftLine.Quantity.Value);
            }
        }

        return (free, currencyCode, draft.Money.EffectiveOn);
    }

    /// <summary>Whether the owner's tier chain answers "no price" (no list price, hence no floor); an out-of-contract answer is not "no price".</summary>
    private async Task<bool> HasNoListPriceAsync(Scope scope, string? permission, IProductQuantityPriceReferenceReaderV2 reader,
        Guid partyId, Guid companyAccountId, Guid productId, Guid priceUnitId, decimal quantity, string currencyCode, DateOnly on,
        CancellationToken cancellationToken)
    {
        try
        {
            var resolution = await ResolvePriceAsync(scope, permission, reader, partyId, companyAccountId, productId, priceUnitId, quantity,
                currencyCode, on, strict: true, cancellationToken);
            return resolution.Outcome == ProductPriceResolutionOutcomeV2.NoPrice;
        }
        catch (SalesDraftServiceException exception) when (exception.Status == 503)
        {
            // Fail-closed: an owner that answers out of contract frees nothing; the line stays on the approval routing.
            return false;
        }
    }

    /// <summary>
    /// The line's commercial fields with the SAT-D178 absence bound to its exact typed price, the line quantity the owner was asked at
    /// (K3 review B P2-1: the lists are quantity-banded), the writer and the time.
    /// </summary>
    private static CommercialDraftFields WithListAbsence(CommercialDraftFields commercial, string currencyCode, DateOnly on,
        decimal measuredQuantity, string actorId, DateTimeOffset now) =>
        commercial with
        {
            ListAbsence = new PriceListAbsenceSnapshot(commercial.PriceUnitId!.Value, currencyCode, on, commercial.UnitPrice!, actorId,
                now.ToUniversalTime(), SalesLinePriceCalculator.Format(measuredQuantity)),
        };

    /// <summary>
    /// SAT-D175: whether the customer of a subcontract service order may change — the binding hook's "not bound". Not composed or
    /// unreadable is 503 <see cref="SalesDraftProblemCodesV1.SubcontractBindingUnavailable"/> (fail-closed); "bound" answers false
    /// and the domain refuses the change (<see cref="SalesRuleCodes.SubcontractCustomerLocked"/>).
    /// </summary>
    private async Task<bool> IsSubcontractCustomerUnboundAsync(Scope scope, string? permission, SalesOrderDraft draft,
        CancellationToken cancellationToken)
    {
        var port = subcontractBindings ?? throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.SubcontractBindingUnavailable);
        var answer = await GuardedAsync(scope, permission, () => port.GetAsync(scope.CompanyId, draft.Id, cancellationToken), cancellationToken);
        return answer?.Outcome switch
        {
            SalesSubcontractOrderBindingOutcomeV1.NotBound => true,
            SalesSubcontractOrderBindingOutcomeV1.Bound => false,
            _ => throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.SubcontractBindingUnavailable),
        };
    }
}
