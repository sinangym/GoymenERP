namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

internal interface IFinancePolicyVersionRow
{
    Guid CompanyId { get; set; }
    Guid PolicyId { get; set; }
    long Version { get; set; }
    long StateVersion { get; set; }
    string State { get; set; }
    DateOnly ValidFrom { get; set; }
    DateOnly? ValidThrough { get; set; }
    DateOnly? RetiredFrom { get; set; }
    string PreparedBy { get; set; }
    DateTimeOffset PreparedAtUtc { get; set; }
    string? PublishedBy { get; set; }
    DateTimeOffset? PublishedAtUtc { get; set; }
    string Reason { get; set; }
    string LastReason { get; set; }
}

internal sealed class FinanceApprovalPolicyRow : IFinancePolicyVersionRow
{
    public Guid CompanyId { get; set; }
    public Guid PolicyId { get; set; }
    public long Version { get; set; }
    public long StateVersion { get; set; }
    public string State { get; set; } = string.Empty;
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidThrough { get; set; }
    public DateOnly? RetiredFrom { get; set; }
    public string PreparedBy { get; set; } = string.Empty;
    public DateTimeOffset PreparedAtUtc { get; set; }
    public string? PublishedBy { get; set; }
    public DateTimeOffset? PublishedAtUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string LastReason { get; set; } = string.Empty;
}

internal sealed class FinanceApprovalPolicyRuleRow
{
    public Guid CompanyId { get; set; }
    public Guid PolicyId { get; set; }
    public Guid RuleId { get; set; }
    public string OperationPermission { get; set; } = string.Empty;
    public Guid CurrencyId { get; set; }
    public Guid ApproverRoleId { get; set; }
    public string ApproverRoleCode { get; set; } = string.Empty;
    public string RoleSourceVersion { get; set; } = string.Empty;
    public int ApprovalStep { get; set; }
    public string AmountThreshold { get; set; } = string.Empty;
}

internal sealed class CashCountDifferencePolicyRow : IFinancePolicyVersionRow
{
    public Guid CompanyId { get; set; }
    public Guid PolicyId { get; set; }
    public long Version { get; set; }
    public long StateVersion { get; set; }
    public string State { get; set; } = string.Empty;
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidThrough { get; set; }
    public DateOnly? RetiredFrom { get; set; }
    public string PreparedBy { get; set; } = string.Empty;
    public DateTimeOffset PreparedAtUtc { get; set; }
    public string? PublishedBy { get; set; }
    public DateTimeOffset? PublishedAtUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string LastReason { get; set; } = string.Empty;
    public Guid CashAccountId { get; set; }
    public string CashAccountVersion { get; set; } = string.Empty;
    public Guid CurrencyId { get; set; }
    public string AmountThreshold { get; set; } = string.Empty;
}

internal sealed class BankInstitutionPolicyRow : IFinancePolicyVersionRow
{
    public Guid CompanyId { get; set; }
    public Guid PolicyId { get; set; }
    public long Version { get; set; }
    public long StateVersion { get; set; }
    public string State { get; set; } = string.Empty;
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidThrough { get; set; }
    public DateOnly? RetiredFrom { get; set; }
    public string PreparedBy { get; set; } = string.Empty;
    public DateTimeOffset PreparedAtUtc { get; set; }
    public string? PublishedBy { get; set; }
    public DateTimeOffset? PublishedAtUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string LastReason { get; set; } = string.Empty;
}

internal sealed class CollateralRatePolicyRow : IFinancePolicyVersionRow
{
    public Guid CompanyId { get; set; }
    public Guid PolicyId { get; set; }
    public long Version { get; set; }
    public long StateVersion { get; set; }
    public string State { get; set; } = string.Empty;
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidThrough { get; set; }
    public DateOnly? RetiredFrom { get; set; }
    public string PreparedBy { get; set; } = string.Empty;
    public DateTimeOffset PreparedAtUtc { get; set; }
    public string? PublishedBy { get; set; }
    public DateTimeOffset? PublishedAtUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string LastReason { get; set; } = string.Empty;
    public string ScopeKey { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public Guid? CollateralTypeId { get; set; }
    public string? CollateralTypeVersion { get; set; }
    public Guid? BankInstitutionId { get; set; }
    public string? BankSourceVersion { get; set; }
    public int? MaximumRemainingDays { get; set; }
    public string RatePercent { get; set; } = string.Empty;
}

internal sealed class BankInstitutionEntryRow
{
    public Guid CompanyId { get; set; }
    public Guid PolicyId { get; set; }
    public Guid BankInstitutionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string EftCode { get; set; } = string.Empty;
    public bool Active { get; set; }
}
