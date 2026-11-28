using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

namespace GoymenERP.Api.WorkRuntime;

/// <summary>Current authorized native human facts, without the private original receipt or digests.</summary>
public sealed record WorkSalesQuoteConversionDecisionPreviewV1(Guid RequestId, SalesApprovalRequestView Request,
    SalesQuoteDetailView Quote, bool ApprovalSupported, bool RejectionSupported)
{
    internal static WorkSalesQuoteConversionDecisionPreviewV1 FromNative(SalesQuoteConversionDecisionDetailV1 detail)
    {
        // Reject malformed owner collections before the binding computes their native digest.
        if (detail.Quote.DueTerms is { Instalments: null } || SalesQuoteConversionDecisionBindingV1.Item(detail) is null) throw new WorkDecisionException(503);
        var q = detail.Quote;
        return new(detail.RequestId, detail.Request, q with
        {
            RequestIds = Array.AsReadOnly(q.RequestIds.ToArray()), Lines = Array.AsReadOnly(q.Lines.ToArray()),
            Revisions = Array.AsReadOnly(q.Revisions.ToArray()), ExpiryApprovals = Array.AsReadOnly(q.ExpiryApprovals.ToArray()),
            DueTerms = q.DueTerms is { } terms ? terms with { Instalments = Array.AsReadOnly(terms.Instalments.ToArray()) } : null,
        }, detail.ApprovalSupported, detail.RejectionSupported);
    }
}

/// <summary>Client-frozen target. The request id is the route id; the two native version axes are never interchanged.</summary>
public sealed record WorkSalesQuoteConversionSelectionV1(Guid QuoteId, int RequestNo, int SubjectRevision, string RequestVersion);
