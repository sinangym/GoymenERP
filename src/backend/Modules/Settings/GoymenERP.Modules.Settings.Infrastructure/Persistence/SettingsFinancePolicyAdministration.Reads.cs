using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

public sealed partial class SettingsFinancePolicyAdministration
{
    async Task<SettingsFinanceApprovalPolicyReadV1> ISettingsFinanceApprovalPolicyProviderV1.GetAsync(Guid companyId,
        string operationPermission, Guid currencyId, DateOnly businessDate, CancellationToken cancellationToken)
    {
        var scope = Capture(cancellationToken);
        if (companyId != scope.CompanyId) throw new SettingsPrerequisiteException(SettingsFinancePolicyCodesV1.AccessDenied);
        if (currencyId == Guid.Empty || string.IsNullOrWhiteSpace(operationPermission) || businessDate == DateOnly.MinValue)
            return new(SettingsFinancePolicyReadOutcomeV1.Unavailable, null, [], SettingsFinancePolicyCodesV1.NotConfigured);
        var rows = await ReadAsync(() => database.FinanceApprovalPolicies.AsNoTracking()
            .Where(x => x.State != "Draft" && x.ValidFrom <= businessDate && (x.ValidThrough == null || businessDate <= x.ValidThrough)
                && (x.RetiredFrom == null || businessDate < x.RetiredFrom)).ToListAsync(cancellationToken));
        Same(scope, cancellationToken);
        if (rows.Count != 1) return new(SettingsFinancePolicyReadOutcomeV1.Unavailable, null, [], SettingsFinancePolicyCodesV1.NotConfigured);
        var rules = await ReadAsync(() => database.FinanceApprovalPolicyRules.AsNoTracking()
            .Where(x => x.PolicyId == rows[0].PolicyId).ToArrayAsync(cancellationToken));
        Same(scope, cancellationToken);
        var policy = ApprovalView(rows[0], rules);
        if (!ValidHeader(rows[0]) || rules.Length == 0 || rules.Any(rule => rule.CompanyId != scope.CompanyId
            || rule.RuleId == Guid.Empty || rule.ApproverRoleId == Guid.Empty || rule.CurrencyId == Guid.Empty
            || rule.ApprovalStep is not (1 or 2) || string.IsNullOrWhiteSpace(rule.OperationPermission)
            || string.IsNullOrWhiteSpace(rule.RoleSourceVersion)
            || !FinancePolicyRulesV1.TryAmount(rule.AmountThreshold, out var canonical) || canonical != rule.AmountThreshold))
            return new(SettingsFinancePolicyReadOutcomeV1.Unavailable, null, [], SettingsFinancePolicyCodesV1.Unavailable);
        var selected = policy.Rules.Where(x => x.OperationPermission == operationPermission && x.CurrencyId == currencyId).ToArray();
        return selected.Length == 0
            ? new(SettingsFinancePolicyReadOutcomeV1.Unavailable, null, [], SettingsFinancePolicyCodesV1.NotConfigured)
            : new(SettingsFinancePolicyReadOutcomeV1.Available, policy, selected, null);
    }

    async Task<SettingsCashCountDifferencePolicyReadV1> ISettingsCashCountDifferencePolicyProviderV1.GetAsync(Guid companyId,
        Guid cashAccountId, Guid currencyId, DateOnly businessDate, CancellationToken cancellationToken)
    {
        var scope = Capture(cancellationToken);
        if (companyId != scope.CompanyId) throw new SettingsPrerequisiteException(SettingsFinancePolicyCodesV1.AccessDenied);
        if (cashAccountId == Guid.Empty || currencyId == Guid.Empty || businessDate == DateOnly.MinValue)
            return new(SettingsFinancePolicyReadOutcomeV1.Unavailable, null, SettingsFinancePolicyCodesV1.NotConfigured);
        var rows = await ReadAsync(() => database.CashCountDifferencePolicies.AsNoTracking()
            .Where(x => x.CashAccountId == cashAccountId && x.CurrencyId == currencyId && x.State != "Draft"
                && x.ValidFrom <= businessDate && (x.ValidThrough == null || businessDate <= x.ValidThrough)
                && (x.RetiredFrom == null || businessDate < x.RetiredFrom)).ToListAsync(cancellationToken));
        Same(scope, cancellationToken);
        if (rows.Count != 1) return new(SettingsFinancePolicyReadOutcomeV1.Unavailable, null, SettingsFinancePolicyCodesV1.NotConfigured);
        var row = rows[0];
        if (!ValidHeader(row) || !FinancePolicyRulesV1.TryAmount(row.AmountThreshold, out var canonical) || canonical != row.AmountThreshold
            || string.IsNullOrWhiteSpace(row.CashAccountVersion))
            return new(SettingsFinancePolicyReadOutcomeV1.Unavailable, null, SettingsFinancePolicyCodesV1.Unavailable);
        return new(SettingsFinancePolicyReadOutcomeV1.Available, CashView(row), null);
    }

    public async Task<SettingsFinanceApprovalPreviewV1> PreviewApprovalAsync(PreviewSettingsFinanceApprovalPolicyV1 request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scope = Capture(cancellationToken);
        await RequireAsync(scope, [SettingsFinancePolicyPermissionsV1.Prepare], false, cancellationToken);
        if (!FinancePolicyRulesV1.TryAmount(request.Amount, out var amount))
            return new(SettingsFinancePolicyReadOutcomeV1.Unavailable, null, null, [], SettingsFinancePolicyCodesV1.Invalid);
        var answer = await ((ISettingsFinanceApprovalPolicyProviderV1)this).GetAsync(scope.CompanyId,
            request.OperationPermission ?? string.Empty, request.CurrencyId, request.BusinessDate, cancellationToken);
        await RequireAsync(scope, [SettingsFinancePolicyPermissionsV1.Prepare], true, cancellationToken);
        return answer.Outcome != SettingsFinancePolicyReadOutcomeV1.Available || answer.Policy is null
            ? new(SettingsFinancePolicyReadOutcomeV1.Unavailable, null, null, [], answer.Reason)
            : new(SettingsFinancePolicyReadOutcomeV1.Available, answer.Policy.PolicyId, answer.Policy.Version,
                answer.Rules.Where(rule => FinancePolicyRulesV1.MatrixThresholdReached(amount, rule.AmountThreshold)).ToArray(), null);
    }

    public async Task<SettingsCashCountDifferencePreviewV1> PreviewCashDifferenceAsync(PreviewSettingsCashCountDifferencePolicyV1 request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scope = Capture(cancellationToken);
        await RequireAsync(scope, [SettingsFinancePolicyPermissionsV1.Prepare], false, cancellationToken);
        if (!FinancePolicyRulesV1.TryAbsoluteDifference(request.DifferenceAmount, out var difference))
            return new(SettingsFinancePolicyReadOutcomeV1.Unavailable, null, null, null, SettingsFinancePolicyCodesV1.Invalid);
        var answer = await ((ISettingsCashCountDifferencePolicyProviderV1)this).GetAsync(scope.CompanyId,
            request.CashAccountId, request.CurrencyId, request.BusinessDate, cancellationToken);
        await RequireAsync(scope, [SettingsFinancePolicyPermissionsV1.Prepare], true, cancellationToken);
        return answer.Outcome != SettingsFinancePolicyReadOutcomeV1.Available || answer.Policy is null
            ? new(SettingsFinancePolicyReadOutcomeV1.Unavailable, null, null, null, answer.Reason)
            : new(SettingsFinancePolicyReadOutcomeV1.Available, answer.Policy.PolicyId, answer.Policy.Version,
                FinancePolicyRulesV1.CashDifferenceThresholdExceeded(difference, answer.Policy.AmountThreshold), null);
    }

    private static bool ValidHeader(IFinancePolicyVersionRow row) => row.CompanyId != Guid.Empty && row.PolicyId != Guid.Empty
        && row.Version > 0 && row.StateVersion == (row.State == "Published" ? 2 : row.State == "Retired" ? 3 : 1)
        && row.ValidFrom != DateOnly.MinValue && (row.ValidThrough is null || row.ValidThrough.Value >= row.ValidFrom)
        && !string.IsNullOrWhiteSpace(row.PreparedBy) && row.PreparedAtUtc != default && row.PreparedAtUtc.Offset == TimeSpan.Zero
        && !string.IsNullOrWhiteSpace(row.PublishedBy) && row.PublishedAtUtc is { Offset: var offset } && offset == TimeSpan.Zero
        && row.PublishedAtUtc.Value != default && !string.IsNullOrWhiteSpace(row.Reason)
        && (row.State == "Published" && row.RetiredFrom is null || row.State == "Retired" && row.RetiredFrom is not null);
}
