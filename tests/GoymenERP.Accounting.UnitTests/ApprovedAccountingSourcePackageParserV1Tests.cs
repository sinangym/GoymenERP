using System.Security.Cryptography;
using System.Text;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Infrastructure;

namespace GoymenERP.Accounting.UnitTests;

public sealed class ApprovedAccountingSourcePackageParserV1Tests
{
    private static readonly Guid Company = Guid.Parse("a1000000-0000-4000-8000-000000000001");
    private static readonly Guid Approver = Guid.Parse("a2000000-0000-4000-8000-000000000001");
    private static readonly Guid Source = Guid.Parse("a3000000-0000-4000-8000-000000000001");
    private static readonly Guid Currency = Guid.Parse("a4000000-0000-4000-8000-000000000001");
    private static readonly Guid Debit = Guid.Parse("a5000000-0000-4000-8000-000000000001");
    private static readonly Guid Credit = Guid.Parse("a5000000-0000-4000-8000-000000000002");
    private static readonly DateTimeOffset ApprovedAt =
        new(2026, 10, 2, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Exact_package_uses_server_provenance_and_builds_existing_domain_contracts()
    {
        var accounts = Bytes(Accounts());
        var mappings = Bytes(Mappings());

        var result = Parser().Preview(Trusted(), Request(accounts, mappings));

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
        Assert.Equal(Sha(accounts), result.AccountsSourceSha256);
        Assert.Equal(Sha(mappings), result.PostingMappingsSourceSha256);
        var package = Assert.IsType<ApprovedAccountingSourcePackageV1>(result.Package);
        Assert.Equal(Company, package.Mirror.CompanyId);
        Assert.Equal("mikro-approved-2026-10", package.Mirror.SourceVersion);
        Assert.Equal(Approver, package.Mirror.ApprovedByUserId);
        Assert.Equal(ApprovedAt, package.Mirror.ApprovedAtUtc);
        Assert.Equal(2, package.Mirror.Rows.Count);
        var binding = Assert.Single(package.Bindings);
        Assert.Equal(Source, binding.SourceId);
        Assert.Equal(NormalJournalSourceKindsV1.RecordedManualReceipt, binding.SourceKind);
        Assert.Equal("approved-normal-receipt", binding.Rule.PostingPurpose);
        Assert.Equal("posting-rule-v1", binding.Rule.Version);
        Assert.Equal(2, binding.Rule.Mappings.Count);
    }

    [Fact]
    public void Uploaded_approval_or_digest_columns_are_not_an_accepted_header()
    {
        var accounts = Accounts().Replace(
            ApprovedAccountingSourcePackageTemplateV1.AccountsHeader,
            ApprovedAccountingSourcePackageTemplateV1.AccountsHeader + ",approvedByUserId,sourceSha256",
            StringComparison.Ordinal);

        var result = Parser().Preview(Trusted(), Request(Bytes(accounts), Bytes(Mappings())));

        Assert.False(result.IsValid);
        Assert.Null(result.Package);
        Assert.Contains(result.Issues, issue => issue.File == "accounts.csv"
            && issue.Code == ApprovedAccountingSourcePackageCodesV1.HeaderInvalid);
    }

    [Fact]
    public void Invalid_trusted_context_cannot_be_replaced_by_file_content()
    {
        var result = Parser().Preview(
            new TrustedAccountingSourceApprovalContextV1(Company, Guid.Empty, ApprovedAt),
            Request(Bytes(Accounts()), Bytes(Mappings())));

        Assert.False(result.IsValid);
        Assert.Null(result.Package);
        Assert.Contains(result.Issues, issue =>
            issue.Code == ApprovedAccountingSourcePackageCodesV1.TrustedContextInvalid);
    }

    [Fact]
    public void Duplicate_account_code_is_refused()
    {
        var duplicate = Accounts() + $"{Guid.Parse("a5000000-0000-4000-8000-000000000003"):D},100,Duplicate\n";

        var result = Parser().Preview(Trusted(), Request(Bytes(duplicate), Bytes(Mappings())));

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue =>
            issue.Code == ApprovedAccountingSourcePackageCodesV1.DuplicateAccount);
    }

    [Theory]
    [InlineData("Unknown", "Debit", ApprovedAccountingSourcePackageCodesV1.SourceKindUnknown)]
    [InlineData("finance.manual-receipt.recorded.v1", "debit", ApprovedAccountingSourcePackageCodesV1.SideUnknown)]
    public void Unknown_source_kind_or_side_is_refused(string sourceKind, string side, string code)
    {
        var mappings = Mappings().Replace(
            $"{NormalJournalSourceKindsV1.RecordedManualReceipt},source-v2",
            $"{sourceKind},source-v2", StringComparison.Ordinal).Replace(
            ",debit,Debit,", $",debit,{side},", StringComparison.Ordinal);

        var result = Parser().Preview(Trusted(), Request(Bytes(Accounts()), Bytes(mappings)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == code);
    }

    [Fact]
    public void Mapping_to_an_account_outside_the_same_mirror_is_refused()
    {
        var unknown = Guid.Parse("a5000000-0000-4000-8000-000000000099");
        var mappings = Mappings().Replace(Credit.ToString("D"), unknown.ToString("D"), StringComparison.Ordinal);

        var result = Parser().Preview(Trusted(), Request(Bytes(Accounts()), Bytes(mappings)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue =>
            issue.Code == ApprovedAccountingSourcePackageCodesV1.MappedAccountUnknown);
    }

    [Fact]
    public void Conflicting_rule_versions_for_the_same_binding_are_refused()
    {
        var mappings = Mappings().Replace(
            $"credit,Credit,{Credit:D}", $"credit,Credit,{Credit:D}".Replace("credit,", "credit,", StringComparison.Ordinal),
            StringComparison.Ordinal);
        var lines = mappings.Split('\n');
        lines[2] = lines[2].Replace("posting-rule-v1", "posting-rule-v2", StringComparison.Ordinal);

        var result = Parser().Preview(Trusted(), Request(Bytes(Accounts()), Bytes(string.Join('\n', lines))));

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue =>
            issue.Code == ApprovedAccountingSourcePackageCodesV1.BindingAmbiguous);
    }

    [Fact]
    public void Invalid_utf8_is_refused_without_constructing_a_package()
    {
        var result = Parser().Preview(Trusted(), Request(new byte[] { 0xC3, 0x28 }, Bytes(Mappings())));

        Assert.False(result.IsValid);
        Assert.Null(result.Package);
        Assert.Contains(result.Issues, issue => issue.Code == ApprovedAccountingSourcePackageCodesV1.Utf8Invalid);
    }

    private static ApprovedAccountingSourcePackageParserV1 Parser() => new();

    private static TrustedAccountingSourceApprovalContextV1 Trusted() =>
        new(Company, Approver, ApprovedAt);

    private static ApprovedAccountingSourcePackagePreviewRequestV1 Request(
        ReadOnlyMemory<byte> accounts,
        ReadOnlyMemory<byte> mappings) =>
        new("mikro-approved-2026-10", accounts, mappings);

    private static string Accounts() =>
        ApprovedAccountingSourcePackageTemplateV1.AccountsHeader + "\n"
        + $"{Debit:D},100,Kasa\n"
        + $"{Credit:D},120,Alicilar\n";

    private static string Mappings() =>
        ApprovedAccountingSourcePackageTemplateV1.MappingsHeader + "\n"
        + Mapping("debit", "Debit", Debit) + "\n"
        + Mapping("credit", "Credit", Credit) + "\n";

    private static string Mapping(string key, string side, Guid accountId) =>
        $"{Source:D},{NormalJournalSourceKindsV1.RecordedManualReceipt},source-v2,finance-profile,7,"
        + $"{Currency:D},approved-normal-receipt,posting-rule-v1,{key},{side},{accountId:D}";

    private static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);

    private static string Sha(ReadOnlyMemory<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes.Span)).ToLowerInvariant();
}
