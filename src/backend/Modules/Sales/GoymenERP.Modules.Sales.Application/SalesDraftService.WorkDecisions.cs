using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Application;

public sealed partial class SalesDraftService
{
    /// <summary>YOL-D23 (2026-11-22): only the immutable maker and current submitter are excluded; other contributors are not.</summary>
    private static bool IsMakerOrSubmitter(Scope scope,SalesOrderDraft draft) => IsOwn(scope,draft)
        || string.Equals(draft.Approval.SubmittedBy,scope.ActorId,StringComparison.Ordinal);
}
