using System.Globalization;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

namespace GoymenERP.Api.WorkRuntime;

/// <summary>Current quantity-only native human facts. Private receipt and source digests stay server-side.</summary>
public sealed record WorkSalesOverDeliveryRequestDecisionPreviewV1(Guid RequestId, SalesApprovalRequestView Request,
    WorkSalesOverDeliveryRequestOrderV1 Order, bool ApprovalSupported, bool RejectionSupported)
{
    internal static WorkSalesOverDeliveryRequestDecisionPreviewV1 FromNative(SalesOverDeliveryRequestDecisionDetailV1 detail)
    {
        if (SalesOverDeliveryRequestDecisionBindingV1.Item(detail) is null) throw new WorkDecisionException(503);
        var o = detail.Order;
        return new(detail.RequestId, detail.Request, new(o.Id, o.DocumentNo, o.Version.ToString(CultureInfo.InvariantCulture),
            o.Revision, o.State, o.ApprovalState, o.PartyId, o.CompanyAccountId, o.CurrencyId, o.BusinessDate, o.Line),
            detail.ApprovalSupported, detail.RejectionSupported);
    }
}

public sealed record WorkSalesOverDeliveryRequestOrderV1(Guid Id, string DocumentNo, string Version, int Revision,
    string State, string ApprovalState, Guid PartyId, Guid CompanyAccountId, Guid CurrencyId, DateOnly BusinessDate,
    SalesOverDeliveryRequestLineV1? Line);
