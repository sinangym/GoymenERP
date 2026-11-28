using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Api.WorkRuntime;

/// <summary>Complete price-free current native facts. Original decision receipts and private source digests are not human wire fields.</summary>
public sealed record WorkSalesRiskExceptionDecisionPreviewV1(Guid CompanyId, Guid ExceptionId, string OrderDocumentNo,
    string OrderVersion, string OrderState, string OrderApprovalState, bool HasApprovedRevision, SalesRiskExceptionView Exception,
    bool ApprovalSupported, bool RejectionSupported)
{
    internal static WorkSalesRiskExceptionDecisionPreviewV1 FromNative(SalesRiskExceptionDecisionDetailV1? detail)
    {
        if (!SalesRiskExceptionService.MatchesRiskDecisionDetail(detail)) throw new WorkDecisionException(503);
        return new(detail!.CompanyId, detail.ExceptionId, detail.OrderDocumentNo, detail.OrderVersion, detail.OrderState,
            detail.OrderApprovalState, detail.HasApprovedRevision, detail.Exception, detail.ApprovalSupported, detail.RejectionSupported);
    }
}
