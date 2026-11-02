using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>
/// SAT-D175/D176 (EK25), SAT-D195 (EK29; handoff S-3): the incoming subcontract service ("gelen fason hizmet") order — a NORMAL sales order flagged
/// "fason" at create (the same approval, risk, customer acceptance and revision rules, SAT-D175).
/// <list type="bullet">
/// <item><b>Lines (SAT-D176):</b> each line's item type is the Product owner's shared card (SAT-D149) through
/// <see cref="IProductItemTypeReaderV1"/>; only <c>Service</c> is admitted (the domain refuses anything else). Unreadable → 503
/// <see cref="SalesDraftProblemCodesV1.ProductItemTypeUnavailable"/>, never assumed a service. A normal order asks nothing new.
/// SAT-D167 line add / product change does not exist yet; when it does, it must call the same check.</item>
/// <item><b>Price (SAT-D195, EK29 — replaces SAT-D177/D178's list-or-manual logic):</b> a subcontract service line never uses the
/// price list (no list reference or floor; <c>…/price-references</c> and a revision naming a list version are 422
/// <see cref="SalesRuleCodes.SubcontractPriceListNotUsed"/> before the price owner is asked). The price is typed PER KILOGRAM (the
/// line's order unit must be a kilogram unit and is the price unit, <see cref="SalesRuleCodes.SubcontractUnitNotKilogram"/>) and is
/// approved by the order's normal internal approval — no discount-limit or floor exception routing. Writing it still needs the
/// price-write name (SAT-D151; the create / revision fences, unchanged); who typed which value, when, is in the audit.</item>
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
