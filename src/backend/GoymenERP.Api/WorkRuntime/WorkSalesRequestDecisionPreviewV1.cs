using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

namespace GoymenERP.Api.WorkRuntime;

/// <summary>Current price-free request facts only; no Audit, original receipt or private digest is part of the human wire.</summary>
public sealed record WorkSalesRequestDecisionPreviewV1(SalesRequestDetailView Detail, bool ApprovalSupported, bool RejectionSupported)
{
    internal static WorkSalesRequestDecisionPreviewV1 FromNative(SalesRequestDecisionDetailV1 detail)
    {
        if (SalesRequestDecisionBindingV1.Item(detail) is null) throw new WorkDecisionException(503);
        // The owner/native record fields stay exact; detach the one collection from a provider-owned mutable list.
        return new(detail.Detail with { Lines = Array.AsReadOnly(detail.Detail.Lines.ToArray()) }, detail.ApprovalSupported, detail.RejectionSupported);
    }
}
