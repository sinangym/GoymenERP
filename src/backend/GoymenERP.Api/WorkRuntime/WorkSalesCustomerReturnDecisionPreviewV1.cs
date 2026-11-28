using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

namespace GoymenERP.Api.WorkRuntime;

/// <summary>The full native customer return invoice view, never the separate physical Logistics return. Private provenance stays server-side.</summary>
public sealed record WorkSalesCustomerReturnDecisionPreviewV1(SalesCustomerReturnView CustomerReturn, bool ApprovalSupported, bool RejectionSupported)
{
    internal static WorkSalesCustomerReturnDecisionPreviewV1 FromNative(SalesCustomerReturnDecisionDetailV1 detail)
    {
        if (!SalesCustomerReturnDecisionBindingV1.ValidDetail(detail)) throw new WorkDecisionException(503);
        return new(detail.CustomerReturn with { Lines = Array.AsReadOnly(detail.CustomerReturn.Lines.ToArray()) }, detail.ApprovalSupported, detail.RejectionSupported);
    }
}
