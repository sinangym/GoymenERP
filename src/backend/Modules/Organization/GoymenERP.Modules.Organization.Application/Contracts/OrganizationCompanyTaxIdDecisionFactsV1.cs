using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GoymenERP.Modules.Organization.Application.Contracts;

/// <summary>Existing owner canonical JSON, with the original property order/names and default JSON options.</summary>
public static class OrganizationCompanyTaxIdDecisionFactsV1
{
    public static string SourceFactsDigest(Guid companyId, CompanyTaxIdChangeView request, long companyVersionAtRequest) => Hash(new
    {
        CompanyId = companyId, request.Id, request.CurrentVkn, request.ProposedVkn,
        CompanyVersionAtRequest = companyVersionAtRequest, request.Reason, request.RequestedBy, request.RequestedAtUtc,
    });
    public static string PreviewDigest(OrganizationCompanyTaxIdDecisionDetailV1 detail) => Hash(new
    {
        Id = detail.CompanyId, Version = detail.CompanyVersion, Vkn = detail.CompanyVkn,
        Request = detail.Request, detail.CompanyVersionAtRequest,
    });
    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
}
