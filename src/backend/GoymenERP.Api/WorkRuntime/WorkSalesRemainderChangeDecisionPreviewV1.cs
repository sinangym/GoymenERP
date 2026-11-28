using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

namespace GoymenERP.Api.WorkRuntime;

/// <summary>The current quantity-only owner view. Original-key provenance, creators/persona flags and digests stay server-side.</summary>
public sealed record WorkSalesRemainderChangeDecisionPreviewV1(Guid DraftId, string DocumentNo, string Version, int Revision,
    string State, string ApprovalState, SalesDraftRemainderChangeV1 Change, SalesRemainderDecisionLineV1? Line,
    bool ApprovalSupported, bool RejectionSupported)
{
    internal static WorkSalesRemainderChangeDecisionPreviewV1 FromNative(SalesRemainderDecisionDetailV1 detail)
    {
        if (!SalesRemainderChangeDecisionBindingV1.ValidDetail(detail)) throw new WorkDecisionException(503);
        return new(detail.DraftId, detail.DocumentNo, detail.Version, detail.Revision, detail.State, detail.ApprovalState,
            detail.Change, detail.Line, detail.ApprovalSupported, detail.RejectionSupported);
    }
}
