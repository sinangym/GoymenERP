namespace GoymenERP.Modules.Accounting.Application;

/// <summary>
/// Application-owned interchange for the approved Mikro mirror prerequisite. It is not a native
/// Mikro export contract and does not activate a catalog, persist data or grant authority.
/// </summary>
public static class ApprovedAccountingSourcePackageTemplateV1
{
    public const string Version = "accounting-approved-source-package-csv.v1";
    public const string AccountsHeader = "accountId,accountCode,accountName";
    public const string MappingsHeader =
        "sourceId,sourceKind,sourceVersion,ownerProfileId,ownerProfileVersion,currencyId,postingPurpose,postingRuleVersion,mappingKey,side,accountId";
    public const int MaximumBytesPerFile = 1_048_576;
    public const int MaximumRowsPerFile = 10_000;
}

public static class ApprovedAccountingSourcePackageCodesV1
{
    public const string TrustedContextInvalid = "accounting_source_package_trusted_context_invalid";
    public const string SourceVersionInvalid = "accounting_source_package_source_version_invalid";
    public const string FileEmpty = "accounting_source_package_file_empty";
    public const string FileTooLarge = "accounting_source_package_file_too_large";
    public const string Utf8Invalid = "accounting_source_package_utf8_invalid";
    public const string HeaderInvalid = "accounting_source_package_header_invalid";
    public const string RowShapeInvalid = "accounting_source_package_row_shape_invalid";
    public const string RowLimitExceeded = "accounting_source_package_row_limit_exceeded";
    public const string IdentityRequired = "accounting_source_package_identity_required";
    public const string TextInvalid = "accounting_source_package_text_invalid";
    public const string DuplicateAccount = "accounting_source_package_duplicate_account";
    public const string SourceKindUnknown = "accounting_source_package_source_kind_unknown";
    public const string SideUnknown = "accounting_source_package_side_unknown";
    public const string MappedAccountUnknown = "accounting_source_package_mapped_account_unknown";
    public const string BindingAmbiguous = "accounting_source_package_binding_ambiguous";
    public const string MappingDuplicate = "accounting_source_package_mapping_duplicate";
    public const string MappingIncomplete = "accounting_source_package_mapping_incomplete";
    public const string DomainInvalid = "accounting_source_package_domain_invalid";
}

/// <summary>
/// Server-owned provenance supplied separately from uploaded bytes. A future endpoint must construct
/// this value only after fresh authority checks; it must never bind it from a CSV or public request.
/// </summary>
public sealed record TrustedAccountingSourceApprovalContextV1(
    Guid CompanyId,
    Guid ApprovedByUserId,
    DateTimeOffset ApprovedAtUtc);

public sealed record ApprovedAccountingSourcePackagePreviewRequestV1(
    string AccountPlanSourceVersion,
    ReadOnlyMemory<byte> AccountsUtf8Csv,
    ReadOnlyMemory<byte> PostingMappingsUtf8Csv);

public sealed record ApprovedAccountingSourcePackageIssueV1(
    string File,
    int RowNumber,
    string Field,
    string Code);

/// <summary>Preview-only values. Possession of this object does not activate a production catalog.</summary>
public sealed record ApprovedAccountingSourcePackageV1(
    AccountPlanMirrorV1 Mirror,
    IReadOnlyList<ApprovedNormalJournalSourceBindingV1> Bindings);

public sealed record ApprovedAccountingSourcePackagePreviewV1(
    string TemplateVersion,
    string AccountsSourceSha256,
    string PostingMappingsSourceSha256,
    ApprovedAccountingSourcePackageV1? Package,
    IReadOnlyList<ApprovedAccountingSourcePackageIssueV1> Issues)
{
    public bool IsValid => Package is not null && Issues.Count == 0;
}

public interface IApprovedAccountingSourcePackageParserV1
{
    ApprovedAccountingSourcePackagePreviewV1 Preview(
        TrustedAccountingSourceApprovalContextV1 trustedContext,
        ApprovedAccountingSourcePackagePreviewRequestV1 request);
}
