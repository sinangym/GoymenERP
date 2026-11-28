using System.Text.Json;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Api.WorkRuntime;

/// <summary>Current selected human facts only. Native private source, digests and original receipt never travel.</summary>
public sealed record WorkSalesDiscountDecisionPreviewV1(Guid CompanyId, Guid DraftId, string DocumentNo, string Version,
    int Revision, string State, string ApprovalState, Guid PartyId, Guid CompanyAccountId, Guid CurrencyId, DateOnly BusinessDate, bool IsSubcontractService,
    IReadOnlyList<SalesDiscountDecisionLineV1> Lines, SalesPriceDisclosure PriceDisclosure, bool ApprovalSupported)
{
    internal static WorkSalesDiscountDecisionPreviewV1 FromNative(SalesDiscountDecisionDetailV1 detail)
    {
        try
        {
            if (!SalesDraftService.MatchesDiscountDecisionDetail(detail)) throw new WorkDecisionException(503);
        }
        catch (JsonException) { throw new WorkDecisionException(503); }
        return new(detail.CompanyId, detail.DraftId, detail.DocumentNo, detail.Version, detail.Revision, detail.State,
            detail.ApprovalState, detail.PartyId, detail.CompanyAccountId, detail.CurrencyId, detail.BusinessDate, detail.IsSubcontractService,
            Array.AsReadOnly(detail.Lines.ToArray()), detail.PriceDisclosure, detail.ApprovalSupported);
    }
}

/// <summary>The human's ordered native line/reason input. The host never sorts, trims or substitutes it.</summary>
public sealed record WorkSalesDiscountSelectionV1(IReadOnlyList<SalesDraftDiscountExceptionApprovalLineV1> Lines);
