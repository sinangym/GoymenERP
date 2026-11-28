using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Api.WorkRuntime;

/// <summary>Complete native human facts under the existing optional disclosure. No private source, receipt or digest is a wire field.</summary>
public sealed record WorkSalesContractDiscountDecisionPreviewV1(Guid CompanyId, SalesContractView Contract,
    bool ApprovalSupported, bool RejectionSupported)
{
    internal static WorkSalesContractDiscountDecisionPreviewV1 FromNative(SalesContractDiscountDecisionDetailV1? detail)
    {
        // Validate null collection boundaries before any native digest serializer is called.
        if (detail?.Contract is not { Installments: { } installments, DiscountRules: { } rules } contract
            || installments.Any(x => x is null) || rules.Any(x => x is null)
            || !SalesContractService.MatchesContractDiscountDecisionDetail(detail)) throw new WorkDecisionException(503);
        return new(detail.CompanyId, contract with { Installments = Array.AsReadOnly(installments.ToArray()), DiscountRules = Array.AsReadOnly(rules.ToArray()) },
            detail.ApprovalSupported, detail.RejectionSupported);
    }
}
