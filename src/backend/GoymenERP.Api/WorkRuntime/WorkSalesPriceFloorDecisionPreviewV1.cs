using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Api.WorkRuntime;

/// <summary>Current selected human facts only. Native private source, digests and original receipt never travel.</summary>
public sealed record WorkSalesPriceFloorDecisionPreviewV1(Guid CompanyId, Guid DraftId, string DocumentNo, string Version,
    int Revision, string State, string ApprovalState, Guid PartyId, Guid CompanyAccountId, Guid CurrencyId, DateOnly BusinessDate,
    IReadOnlyList<SalesPriceFloorDecisionLineV1> Lines, SalesPriceDisclosure PriceDisclosure, bool ApprovalSupported)
{
    internal static WorkSalesPriceFloorDecisionPreviewV1 FromNative(SalesPriceFloorDecisionDetailV1 detail)
    {
        if (!SalesDraftService.MatchesPriceFloorDecisionDetail(detail)) throw new WorkDecisionException(503);
        return new(detail.CompanyId, detail.DraftId, detail.DocumentNo, detail.Version, detail.Revision, detail.State,
            detail.ApprovalState, detail.PartyId, detail.CompanyAccountId, detail.CurrencyId, detail.BusinessDate,
            Array.AsReadOnly(detail.Lines.ToArray()), detail.PriceDisclosure, detail.ApprovalSupported);
    }
}

/// <summary>The human's ordered native line/reason input. The host never sorts, trims or substitutes it.</summary>
public sealed record WorkSalesPriceFloorSelectionV1(IReadOnlyList<SalesDraftPriceFloorExceptionApprovalLineV1> Lines);
