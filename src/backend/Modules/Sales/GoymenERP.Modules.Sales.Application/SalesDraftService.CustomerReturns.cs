using System.Globalization;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.Quantities;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>
/// SAT-D8 / API-10 (delegation No 21, 38): the order's approved customer returns per Sales line. Logistics owns the return
/// record; Sales reads it only through <see cref="ISalesOrderCustomerReturnReadPortV1"/> and stores no copy. Read-only: no
/// attempt, audit or version effect in either module, and no credit note, stock or remainder effect is derived.
/// <list type="bullet">
/// <item><b>Fences</b> (the draft read's): the draft read name before anything is read, after the draft read and after the
/// owner's answer; the owner port carries no authority of its own.</item>
/// <item><b>Company</b>: the draft is read under the tenant filter (another company's is 404, never 403) and the owner is
/// asked only for that draft; an answer of another company or order is a malformed owner answer (503).</item>
/// <item><b>Exactness</b> (Ö2): every total is taken in the line's own order unit, never converted and never rounded; a
/// total of an unknown line, in another unit, not positive or above the unit's scale is malformed (503) — the screen then
/// shows no number rather than a wrong one.</item>
/// </list>
/// </summary>
public sealed partial class SalesDraftService
{
    public async Task<SalesOrderDraftCustomerReturnsResponse> GetDraftCustomerReturnsAsync(Guid draftId,
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
            // Another company's draft is null under the tenant filter: 404, and the return owner is never asked.
            throw new SalesDraftServiceException(404, "not_found");
        }

        // Not composed (the owner registers the port in its own module): 503, never an assumed "nothing returned".
        var owner = customerReturns
            ?? throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.CustomerReturnProviderUnavailable);
        SalesOrderCustomerReturnReadAnswerV1? answer;
        try
        {
            answer = await GuardedAsync(scope, bindings.ReadPermission,
                () => owner.ReadApprovedReturnsAsync(draft.Id, cancellationToken), cancellationToken);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or SalesDraftServiceException))
        {
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.CustomerReturnProviderUnavailable);
        }

        return CustomerReturns(scope, draft, answer);
    }

    private static SalesOrderDraftCustomerReturnsResponse CustomerReturns(Scope scope, SalesOrderDraft draft,
        SalesOrderCustomerReturnReadAnswerV1? answer)
    {
        if (answer is null || answer.Lines is null || answer.CompanyId != scope.CompanyId || answer.SalesOrderId != draft.Id)
        {
            throw CustomerReturnAnswerInvalid();
        }

        var lines = draft.Lines.ToDictionary(line => line.Id);
        var returned = new Dictionary<Guid, decimal>();
        foreach (var entry in answer.Lines)
        {
            // An approved return always carries a positive quantity in the loading line's unit, which is the order unit:
            // an unknown line, another unit (no conversion) or a non-positive total is a malformed owner answer.
            if (entry is null || !lines.TryGetValue(entry.SalesOrderLineId, out var line)
                || entry.UnitId != line.OrderUnit.UnitId || entry.ApprovedReturnedQuantity <= 0m)
            {
                throw CustomerReturnAnswerInvalid();
            }

            try
            {
                returned[line.Id] = returned.TryGetValue(line.Id, out var sum)
                    ? sum + entry.ApprovedReturnedQuantity
                    : entry.ApprovedReturnedQuantity;
            }
            catch (OverflowException)
            {
                // A malformed owner answer must not escape as a server error or be shown as a plausible quantity.
                throw CustomerReturnAnswerInvalid();
            }
        }

        return new SalesOrderDraftCustomerReturnsResponse(draft.Version.ToString(CultureInfo.InvariantCulture),
            [.. draft.Lines.OrderBy(line => line.LineNo).Select(line => LineCustomerReturn(line, returned.GetValueOrDefault(line.Id)))]);
    }

    private static SalesDraftLineCustomerReturnV1 LineCustomerReturn(SalesOrderDraftLine line, decimal approvedReturned)
    {
        Quantity quantity;
        try
        {
            // Ö2 in the line's own dimension: kilograms ≤ 3 decimals, pieces whole — never rounded to fit.
            quantity = Quantity.Of(approvedReturned, line.Quantity.Unit);
        }
        catch (ArgumentException)
        {
            throw CustomerReturnAnswerInvalid();
        }

        return new SalesDraftLineCustomerReturnV1(line.Id, line.LineNo, line.OrderUnit.UnitId, line.QuantityText,
            QuantityText(quantity));
    }

    private static SalesDraftServiceException CustomerReturnAnswerInvalid() =>
        new(503, SalesDraftProblemCodesV1.ReferenceProviderInvalid);
}
