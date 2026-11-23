using System.Globalization;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Application;

public sealed partial class SalesDraftService
{
    private sealed record ReducedRevisionLine(SalesOrderDraftLine Line, decimal NewQuantity);

    private static bool CompletesReducedRevision(SalesOrderDraft draft, long expectedVersion, string commandName)
    {
        if (draft.State != SalesDraftState.Draft || draft.Version != expectedVersion || draft.Revision == 0)
            return false;
        var completes = commandName == SalesDraftCommands.ApproveV1
            ? !draft.CustomerAcceptanceRequired && draft.ApprovalState == InternalApprovalState.PendingApproval
            : commandName == SalesDraftCommands.RecordCustomerAcceptanceV1 && draft.CustomerAcceptanceRequired
                && draft.ApprovalState == InternalApprovalState.Approved && !draft.IsCustomerAccepted
                && draft.CurrentRevisionCustomerRejection is null;
        return completes && ReducedRevisionLines(draft, draft.CustomerAcceptance?.Revision).Count > 0;
    }

    private static IReadOnlyList<ReducedRevisionLine> ReducedRevisionLines(SalesOrderDraft draft,
        int? previouslyAcceptedRevision)
    {
        ApprovedRevisionSnapshot? basis;
        if (draft.CustomerAcceptanceRequired)
        {
            if (previouslyAcceptedRevision is null) return [];
            basis = draft.ArchivedRevisions.SingleOrDefault(revision => revision.Revision == previouslyAcceptedRevision);
            if (basis is null)
                throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.RevisionImpactUnavailable);
        }
        else
            basis = draft.ArchivedRevisions.LastOrDefault();
        if (basis is null) return [];

        var all = draft.AllLines.ToDictionary(line => line.Id);
        var active = draft.Lines.Select(line => line.Id).ToHashSet();
        var changed = new List<ReducedRevisionLine>();
        foreach (var previous in basis.Lines.OrderBy(line => line.LineId))
        {
            if (!all.TryGetValue(previous.LineId, out var line) || line.OrderUnit.UnitId != previous.OrderUnitId
                || line.Product.ProductId != previous.ProductId || line.Product.ProductVersion != previous.ProductVersion
                || !decimal.TryParse(previous.QuantityText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture,
                    out var previousQuantity) || previousQuantity <= 0m)
                throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.RevisionImpactUnavailable);
            var quantity = active.Contains(line.Id) ? line.Quantity.Value : 0m;
            if (quantity < previousQuantity) changed.Add(new(line, quantity));
        }
        return changed;
    }
}
