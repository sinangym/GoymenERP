using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Modules.Organization.Application.Contracts;

namespace GoymenERP.Organization.UnitTests;

public sealed class CompanyTaxIdDecisionFactsV1Tests
{
    [Theory]
    [InlineData(null, "pending")] [InlineData("9000000017", "approved")] [InlineData("9000000017", "rejected")]
    public void Extracted_hashes_equal_the_original_native_anonymous_JSON(string? oldVkn, string status)
    {
        var companyId = Guid.Parse("69000000-0000-4000-8000-000000000001");
        var view = new CompanyTaxIdChangeView(Guid.Parse("69000000-0000-4000-8000-000000000002"), oldVkn,
            "9000000033", "SENTETIK gerekçe", status, "maker", DateTimeOffset.MinValue,
            status == "pending" ? null : "checker", status == "pending" ? null : DateTimeOffset.UnixEpoch, null);
        var detail = new OrganizationCompanyTaxIdDecisionDetailV1(companyId, long.MaxValue, "9000000033", view, 7, false, "", "");
        var oldSource = Hash(new { CompanyId = companyId, view.Id, view.CurrentVkn, view.ProposedVkn,
            CompanyVersionAtRequest = 7L, view.Reason, view.RequestedBy, view.RequestedAtUtc });
        var oldPreview = Hash(new { Id = companyId, Version = long.MaxValue, Vkn = "9000000033", Request = view, CompanyVersionAtRequest = 7L });
        Assert.Equal(oldSource, OrganizationCompanyTaxIdDecisionFactsV1.SourceFactsDigest(companyId, view, 7));
        Assert.Equal(oldPreview, OrganizationCompanyTaxIdDecisionFactsV1.PreviewDigest(detail));
        Assert.NotEqual(oldPreview, OrganizationCompanyTaxIdDecisionFactsV1.PreviewDigest(detail with { CompanyVersion = 8 }));
    }
    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
}
