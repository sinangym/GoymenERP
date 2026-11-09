namespace GoymenERP.Modules.Accounting.Application.Contracts;

public enum AccountingPostingAccountMappingResolutionKindV1
{
    /// <summary>The answer cannot be given now (no company, no active mapping or plan, a bad question). Fail closed.</summary>
    Unavailable = 0,

    /// <summary>Exactly one most specific row of the active version maps the question to an account.</summary>
    Mapped = 1,

    /// <summary>The active version holds no row for the question. Never a fallback account.</summary>
    Unmapped = 2,

    /// <summary>Two or more rows match equally specifically. Nothing is chosen between them.</summary>
    Ambiguous = 3,
}

/// <summary>Why a resolution is <see cref="AccountingPostingAccountMappingResolutionKindV1.Unavailable"/>.</summary>
public static class AccountingPostingAccountMappingUnavailableReasonsV1
{
    public const string CompanyContextInvalid = "company_not_resolved";
    public const string QueryInvalid = "query_invalid";
    public const string NoActiveMapping = "no_active_mapping";
    public const string NotEffectiveForDate = "not_effective_for_date";
    public const string AccountPlanUnavailable = "account_plan_unavailable";
    public const string AccountNotInActivePlan = "account_not_in_active_plan";
}

/// <param name="Kind">The outcome.</param>
/// <param name="AccountId">The Accounting account id, only when <see cref="AccountingPostingAccountMappingResolutionKindV1.Mapped"/>.</param>
/// <param name="MappingVersion">The active version that answered, when one did.</param>
/// <param name="UnavailableReason">One of <see cref="AccountingPostingAccountMappingUnavailableReasonsV1"/>, when unavailable.</param>
public sealed record AccountingPostingAccountMappingResolutionV1(
    AccountingPostingAccountMappingResolutionKindV1 Kind,
    Guid? AccountId,
    long? MappingVersion,
    string? UnavailableReason)
{
    public static AccountingPostingAccountMappingResolutionV1 Mapped(Guid accountId, long mappingVersion) =>
        new(AccountingPostingAccountMappingResolutionKindV1.Mapped, accountId, mappingVersion, null);

    public static AccountingPostingAccountMappingResolutionV1 Unmapped(long mappingVersion) =>
        new(AccountingPostingAccountMappingResolutionKindV1.Unmapped, null, mappingVersion, null);

    public static AccountingPostingAccountMappingResolutionV1 Ambiguous(long mappingVersion) =>
        new(AccountingPostingAccountMappingResolutionKindV1.Ambiguous, null, mappingVersion, null);

    public static AccountingPostingAccountMappingResolutionV1 Unavailable(string reason, long? mappingVersion = null) =>
        new(AccountingPostingAccountMappingResolutionKindV1.Unavailable, null, mappingVersion, reason);
}

/// <summary>
/// B-04a read port for the invoice voucher draft (B-04b): which account of the ACTIVE posting account mapping
/// answers one voucher role of one invoice line.
/// <list type="bullet">
/// <item><b>No authority of its own.</b> The caller fences its own command before and after; this port only reads
/// the server-resolved company and answers <see cref="AccountingPostingAccountMappingResolutionKindV1.Unavailable"/>
/// when <paramref name="companyId"/> is not that company.</item>
/// <item><b>Active version only.</b> A business date before the active version's effective date is unavailable,
/// not answered from a superseded version (whether history may answer back-dated invoices is open).</item>
/// <item><b>Never a guess.</b> Most specific match wins; ambiguity and no match are explicit answers. A mapped
/// account that is no longer in the active approved account plan is unavailable.</item>
/// </list>
/// </summary>
public interface IAccountingPostingAccountMappingResolverV1
{
    Task<AccountingPostingAccountMappingResolutionV1> ResolveAsync(
        Guid companyId,
        string purpose,
        DateOnly businessDate,
        string mappingKey,
        string? itemType = null,
        string? linePurpose = null,
        bool? isExport = null,
        string? vatRateCode = null,
        CancellationToken cancellationToken = default);
}
