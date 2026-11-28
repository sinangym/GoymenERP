using System.Globalization;
using GoymenERP.Modules.Organization.Application.Contracts;

namespace GoymenERP.Api.WorkRuntime;

// Purpose-only public facts. Both Int64 company versions are lossless text; no private receipt/digest is emitted.
public sealed record WorkOrganizationCompanyTaxIdDecisionPreviewV1(Guid CompanyId, string CompanyVersion,
    string? CompanyVkn, CompanyTaxIdChangeView Request, string CompanyVersionAtRequest, bool IsCurrent)
{
    internal static WorkOrganizationCompanyTaxIdDecisionPreviewV1 FromNative(OrganizationCompanyTaxIdDecisionDetailV1 detail) =>
        new(detail.CompanyId, detail.CompanyVersion.ToString(CultureInfo.InvariantCulture), detail.CompanyVkn,
            detail.Request, detail.CompanyVersionAtRequest.ToString(CultureInfo.InvariantCulture), detail.IsCurrent);
}
