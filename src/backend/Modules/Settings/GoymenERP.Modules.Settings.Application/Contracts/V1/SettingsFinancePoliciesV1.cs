using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Settings.Application.Contracts.V1;

/// <summary>No 133: names only. URN-D3 has no separate approval action. No grant or Critical classification is made here.</summary>
public static class SettingsFinancePolicyPermissionsV1
{
    public const string Prepare = "settings.policies.prepare";
    /// <summary>No 133 retains the name; URN-D3 retires a separate approval command. Never used as a substitute for Publish.</summary>
    public const string Approve = "settings.policies.approve";
    public const string Publish = "settings.policies.publish";
    public const string PublishCritical = "settings.policies.publish-critical";
}

public static class SettingsFinancePolicyCodesV1
{
    public const string Invalid = "settings_finance_policy_request_invalid";
    public const string NotFound = "settings_finance_policy_not_found";
    public const string VersionConflict = "settings_finance_policy_version_conflict";
    public const string IdempotencyConflict = "settings_finance_policy_idempotency_conflict";
    public const string WindowConflict = "settings_finance_policy_window_conflict";
    public const string ReferenceChanged = "settings_finance_policy_reference_changed";
    public const string Unavailable = "settings_provider_unavailable";
    public const string AccessDenied = "settings_access_denied";
    public const string NotConfigured = "settings_finance_policy_not_configured";
}

[JsonConverter(typeof(JsonStringEnumConverter<SettingsFinancePolicyStateV1>))]
public enum SettingsFinancePolicyStateV1 { Draft = 1, Published = 2, Retired = 3 }

[JsonConverter(typeof(JsonStringEnumConverter<SettingsFinancePolicyReadOutcomeV1>))]
public enum SettingsFinancePolicyReadOutcomeV1 { Available = 1, Unavailable = 2 }

/// <summary>Only existing source identities leave their owners. This contract never assigns a role or grants a permission.</summary>
public sealed record SettingsFinanceOperationReferenceV1(string Permission, string Name);
public sealed record SettingsFinanceRoleReferenceV1(Guid RoleId, string Code, string Name, string SourceVersion);
public sealed record SettingsFinanceCashReferenceV1(Guid CashAccountId, Guid CurrencyId, string Name, string Version);
public sealed record SettingsFinanceCurrencyReferenceV1(Guid CurrencyId, string Code);
public sealed record SettingsFinancePolicyReferencesV1(Guid CompanyId,
    IReadOnlyList<SettingsFinanceOperationReferenceV1> Operations,
    IReadOnlyList<SettingsFinanceRoleReferenceV1> Roles,
    IReadOnlyList<SettingsFinanceCashReferenceV1> CashAccounts,
    IReadOnlyList<SettingsFinanceCurrencyReferenceV1> Currencies);

/// <summary>Host composes existing Finance/Identity owner reads. Denial is not an available empty list.</summary>
public interface ISettingsFinancePolicyReferencesV1
{
    Task<SettingsFinancePolicyReferencesV1> GetAsync(Guid companyId, CancellationToken cancellationToken = default);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SettingsFinanceApprovalRuleInputV1(string? OperationPermission, Guid CurrencyId,
    Guid ApproverRoleId, string? ExpectedRoleSourceVersion, int ApprovalStep, string? AmountThreshold);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PrepareSettingsFinanceApprovalPolicyV1(string? ExpectedVersion, DateOnly ValidFrom,
    DateOnly? ValidThrough, IReadOnlyList<SettingsFinanceApprovalRuleInputV1>? Rules, string? Reason);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PrepareSettingsCashCountDifferencePolicyV1(string? ExpectedVersion, Guid CashAccountId,
    string? ExpectedCashAccountVersion, Guid CurrencyId, string? AmountThreshold, DateOnly ValidFrom,
    DateOnly? ValidThrough, string? Reason);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PublishSettingsFinancePolicyV1(string? ExpectedStateVersion, string? Reason);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RetireSettingsFinancePolicyV1(string? ExpectedStateVersion, DateOnly RetiredFrom, string? Reason);

/// <summary>DEC-FIN-012 and 6960531086: threshold comparison is inclusive. No role permission is conferred by a row.</summary>
public sealed record SettingsFinanceApprovalRuleV1(Guid RuleId, string OperationPermission, Guid CurrencyId,
    Guid ApproverRoleId, string ApproverRoleCode, string RoleSourceVersion, int ApprovalStep, string AmountThreshold);

public sealed record SettingsFinanceApprovalPolicyV1(Guid CompanyId, Guid PolicyId, long Version, long StateVersion,
    SettingsFinancePolicyStateV1 State, DateOnly ValidFrom, DateOnly? ValidThrough, DateOnly? RetiredFrom,
    string PreparedBy, DateTimeOffset PreparedAtUtc, string? PublishedBy, DateTimeOffset? PublishedAtUtc,
    string Reason, IReadOnlyList<SettingsFinanceApprovalRuleV1> Rules);

public sealed record SettingsCashCountDifferencePolicyV1(Guid CompanyId, Guid PolicyId, long Version, long StateVersion,
    SettingsFinancePolicyStateV1 State, DateOnly ValidFrom, DateOnly? ValidThrough, DateOnly? RetiredFrom,
    string PreparedBy, DateTimeOffset PreparedAtUtc, string? PublishedBy, DateTimeOffset? PublishedAtUtc,
    string Reason, Guid CashAccountId, string CashAccountVersion, Guid CurrencyId, string AmountThreshold);

public sealed record SettingsFinancePoliciesWorkspaceV1(Guid CompanyId, DateOnly BusinessDate,
    SettingsFinancePolicyReferencesV1 References, IReadOnlyList<SettingsFinanceApprovalPolicyV1> ApprovalPolicies,
    IReadOnlyList<SettingsCashCountDifferencePolicyV1> CashCountDifferencePolicies);

public sealed record SettingsFinancePolicyCommandAnswerV1(int StatusCode, string? Code, string Title, string Detail,
    Guid? PolicyId = null, long? Version = null, long? StateVersion = null);
public sealed record SettingsFinancePolicyCommandResultV1(SettingsFinancePolicyCommandAnswerV1 Answer,
    bool IsReplay, string OriginalCorrelationId);

public interface ISettingsFinancePolicyAdministrationV1
{
    Task<SettingsFinancePoliciesWorkspaceV1> GetAsync(CancellationToken cancellationToken = default);
    Task<SettingsFinancePolicyCommandResultV1> PrepareApprovalAsync(PrepareSettingsFinanceApprovalPolicyV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
    Task<SettingsFinancePolicyCommandResultV1> PrepareCashDifferenceAsync(PrepareSettingsCashCountDifferencePolicyV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
    Task<SettingsFinancePolicyCommandResultV1> PublishApprovalAsync(Guid policyId, PublishSettingsFinancePolicyV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
    Task<SettingsFinancePolicyCommandResultV1> PublishCashDifferenceAsync(Guid policyId, PublishSettingsFinancePolicyV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
    Task<SettingsFinancePolicyCommandResultV1> RetireApprovalAsync(Guid policyId, RetireSettingsFinancePolicyV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
    Task<SettingsFinancePolicyCommandResultV1> RetireCashDifferenceAsync(Guid policyId, RetireSettingsFinancePolicyV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default);
}

public sealed record SettingsFinanceApprovalPolicyReadV1(SettingsFinancePolicyReadOutcomeV1 Outcome,
    SettingsFinanceApprovalPolicyV1? Policy, IReadOnlyList<SettingsFinanceApprovalRuleV1> Rules, string? Reason);
public sealed record SettingsCashCountDifferencePolicyReadV1(SettingsFinancePolicyReadOutcomeV1 Outcome,
    SettingsCashCountDifferencePolicyV1? Policy, string? Reason);

/// <summary>
/// Exact company, operation/currency and caller-supplied business date. Missing/ambiguous data is unavailable, never
/// an implicit zero threshold. Reads do not activate OPEN-009, replace existing approvals or confer any authority.
/// </summary>
public interface ISettingsFinanceApprovalPolicyProviderV1
{
    Task<SettingsFinanceApprovalPolicyReadV1> GetAsync(Guid companyId, string operationPermission, Guid currencyId,
        DateOnly businessDate, CancellationToken cancellationToken = default);
}

public interface ISettingsCashCountDifferencePolicyProviderV1
{
    Task<SettingsCashCountDifferencePolicyReadV1> GetAsync(Guid companyId, Guid cashAccountId, Guid currencyId,
        DateOnly businessDate, CancellationToken cancellationToken = default);
}

/// <summary>Actual stored policy preview, not an approval or a money event.</summary>
public sealed record SettingsFinanceApprovalPreviewV1(SettingsFinancePolicyReadOutcomeV1 Outcome, Guid? PolicyId,
    long? Version, IReadOnlyList<SettingsFinanceApprovalRuleV1> MatchingRules, string? Reason);
public sealed record SettingsCashCountDifferencePreviewV1(SettingsFinancePolicyReadOutcomeV1 Outcome, Guid? PolicyId,
    long? Version, bool? ExceedsThreshold, string? Reason);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PreviewSettingsFinanceApprovalPolicyV1(string? OperationPermission, Guid CurrencyId,
    DateOnly BusinessDate, string? Amount);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PreviewSettingsCashCountDifferencePolicyV1(Guid CashAccountId, Guid CurrencyId,
    DateOnly BusinessDate, string? DifferenceAmount);

public interface ISettingsFinancePolicyPreviewV1
{
    Task<SettingsFinanceApprovalPreviewV1> PreviewApprovalAsync(PreviewSettingsFinanceApprovalPolicyV1 request,
        CancellationToken cancellationToken = default);
    Task<SettingsCashCountDifferencePreviewV1> PreviewCashDifferenceAsync(PreviewSettingsCashCountDifferencePolicyV1 request,
        CancellationToken cancellationToken = default);
}
