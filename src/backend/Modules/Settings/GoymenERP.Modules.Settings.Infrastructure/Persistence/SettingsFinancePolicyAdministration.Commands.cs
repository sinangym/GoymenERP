using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

public sealed partial class SettingsFinancePolicyAdministration
{
    public Task<SettingsFinancePolicyCommandResultV1> PrepareApprovalAsync(PrepareSettingsFinanceApprovalPolicyV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default) =>
        ExecuteAsync("settings.finance-approval-policy.prepare.v1", null, request, metadata,
            [SettingsFinancePolicyPermissionsV1.Prepare], async (scope, today, token) =>
            {
                if (!FinancePolicyRulesV1.TryVersion(request.ExpectedVersion, out var expected)
                    || !FinancePolicyRulesV1.IsWindow(request.ValidFrom, request.ValidThrough, today)
                    || !Reason(request.Reason, out var reason)
                    || request.Rules is not { Count: > 0 and <= FinancePolicyRulesV1.MatrixRuleMaximumCount }) return new(Invalid());
                var source = await ReferenceAsync(scope, token);
                var rows = new List<FinanceApprovalPolicyRuleRow>(request.Rules.Count);
                foreach (var input in request.Rules)
                {
                    if (input is null || input.ApprovalStep is not (1 or 2)
                        || !FinancePolicyRulesV1.TryAmount(input.AmountThreshold, out var amount)
                        || !source.Operations.Any(x => x.Permission == input.OperationPermission)
                        || !source.Currencies.Any(x => x.CurrencyId == input.CurrencyId)) return new(Invalid());
                    var role = source.Roles.SingleOrDefault(x => x.RoleId == input.ApproverRoleId);
                    if (role is null || role.SourceVersion != input.ExpectedRoleSourceVersion)
                        return new(Problem(409, SettingsFinancePolicyCodesV1.ReferenceChanged, "Seçilen rolün güncel kaynağını yeniden okuyun."));
                    rows.Add(new() { CompanyId = scope.CompanyId, RuleId = Guid.NewGuid(), OperationPermission = input.OperationPermission!,
                        CurrencyId = input.CurrencyId, ApproverRoleId = role.RoleId, ApproverRoleCode = role.Code,
                        RoleSourceVersion = role.SourceVersion, ApprovalStep = input.ApprovalStep, AmountThreshold = amount });
                }
                if (rows.Select(x => (x.OperationPermission, x.CurrencyId, x.ApproverRoleId, x.ApprovalStep)).Distinct().Count() != rows.Count)
                    return new(Invalid());
                var latest = await database.FinanceApprovalPolicies.OrderByDescending(x => x.Version).Select(x => (long?)x.Version).FirstOrDefaultAsync(token) ?? 0;
                if (latest != expected) return new(Conflict());
                var row = new FinanceApprovalPolicyRow { CompanyId = scope.CompanyId, PolicyId = Guid.NewGuid(), Version = checked(latest + 1),
                    StateVersion = 1, State = "Draft", ValidFrom = request.ValidFrom, ValidThrough = request.ValidThrough,
                    PreparedBy = scope.ActorId, PreparedAtUtc = clock.GetUtcNow(), Reason = reason, LastReason = reason };
                foreach (var rule in rows) rule.PolicyId = row.PolicyId;
                database.FinanceApprovalPolicies.Add(row);
                database.FinanceApprovalPolicyRules.AddRange(rows);
                return new(Saved(row), ApprovalEntity, row.PolicyId, "settings.finance-approval-policy.prepared.v1",
                    null, row.Version, null, ApprovalView(row, rows), reason);
            }, cancellationToken);

    public Task<SettingsFinancePolicyCommandResultV1> PrepareCashDifferenceAsync(PrepareSettingsCashCountDifferencePolicyV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default) =>
        ExecuteAsync("settings.cash-count-difference-policy.prepare.v1", null, request, metadata,
            [SettingsFinancePolicyPermissionsV1.Prepare], async (scope, today, token) =>
            {
                if (!FinancePolicyRulesV1.TryVersion(request.ExpectedVersion, out var expected)
                    || !FinancePolicyRulesV1.IsWindow(request.ValidFrom, request.ValidThrough, today)
                    || !Reason(request.Reason, out var reason)
                    || !FinancePolicyRulesV1.TryAmount(request.AmountThreshold, out var amount)) return new(Invalid());
                var source = await ReferenceAsync(scope, token);
                var cash = source.CashAccounts.SingleOrDefault(x => x.CashAccountId == request.CashAccountId && x.CurrencyId == request.CurrencyId);
                if (cash is null || cash.Version != request.ExpectedCashAccountVersion)
                    return new(Problem(409, SettingsFinancePolicyCodesV1.ReferenceChanged, "Kasanın güncel şirket, para birimi ve sürümünü yeniden okuyun."));
                var latest = await database.CashCountDifferencePolicies.Where(x => x.CashAccountId == cash.CashAccountId && x.CurrencyId == cash.CurrencyId)
                    .OrderByDescending(x => x.Version).Select(x => (long?)x.Version).FirstOrDefaultAsync(token) ?? 0;
                if (latest != expected) return new(Conflict());
                var row = new CashCountDifferencePolicyRow { CompanyId = scope.CompanyId, PolicyId = Guid.NewGuid(), Version = checked(latest + 1),
                    StateVersion = 1, State = "Draft", ValidFrom = request.ValidFrom, ValidThrough = request.ValidThrough,
                    PreparedBy = scope.ActorId, PreparedAtUtc = clock.GetUtcNow(), Reason = reason, LastReason = reason,
                    CashAccountId = cash.CashAccountId, CashAccountVersion = cash.Version, CurrencyId = cash.CurrencyId, AmountThreshold = amount };
                database.CashCountDifferencePolicies.Add(row);
                return new(Saved(row), CashEntity, row.PolicyId, "settings.cash-count-difference-policy.prepared.v1",
                    null, row.Version, null, CashView(row), reason);
            }, cancellationToken);

    public Task<SettingsFinancePolicyCommandResultV1> PublishApprovalAsync(Guid policyId, PublishSettingsFinancePolicyV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default) =>
        LifecycleAsync(true, false, policyId, request, request.ExpectedStateVersion, null, request.Reason, metadata, cancellationToken);
    public Task<SettingsFinancePolicyCommandResultV1> PublishCashDifferenceAsync(Guid policyId, PublishSettingsFinancePolicyV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default) =>
        LifecycleAsync(false, false, policyId, request, request.ExpectedStateVersion, null, request.Reason, metadata, cancellationToken);
    public Task<SettingsFinancePolicyCommandResultV1> RetireApprovalAsync(Guid policyId, RetireSettingsFinancePolicyV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default) =>
        LifecycleAsync(true, true, policyId, request, request.ExpectedStateVersion, request.RetiredFrom, request.Reason, metadata, cancellationToken);
    public Task<SettingsFinancePolicyCommandResultV1> RetireCashDifferenceAsync(Guid policyId, RetireSettingsFinancePolicyV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default) =>
        LifecycleAsync(false, true, policyId, request, request.ExpectedStateVersion, request.RetiredFrom, request.Reason, metadata, cancellationToken);

    private Task<SettingsFinancePolicyCommandResultV1> LifecycleAsync<T>(bool approval, bool retiring, Guid policyId, T request,
        string? expectedVersion, DateOnly? retiredFrom, string? inputReason, SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken)
    {
        var family = approval ? "finance-approval-policy" : "cash-count-difference-policy";
        var action = retiring ? "retire" : "publish";
        string[] permissions = approval
            ? [SettingsFinancePolicyPermissionsV1.Prepare, SettingsFinancePolicyPermissionsV1.Publish, SettingsFinancePolicyPermissionsV1.PublishCritical]
            : [SettingsFinancePolicyPermissionsV1.Prepare, SettingsFinancePolicyPermissionsV1.Publish];
        return ExecuteAsync($"settings.{family}.{action}.v1", policyId, request, metadata, permissions,
            async (scope, today, token) =>
            {
                if (!FinancePolicyRulesV1.TryVersion(expectedVersion, out var expected) || !Reason(inputReason, out var reason)) return new(Invalid());
                var approvalRow = approval ? await database.FinanceApprovalPolicies.SingleOrDefaultAsync(x => x.PolicyId == policyId, token) : null;
                var cashRow = approval ? null : await database.CashCountDifferencePolicies.SingleOrDefaultAsync(x => x.PolicyId == policyId, token);
                IFinancePolicyVersionRow? row = approval ? approvalRow : cashRow;
                if (row is null) return new(Problem(404, SettingsFinancePolicyCodesV1.NotFound, "Politika bulunamadı."));
                var rules = approval ? await database.FinanceApprovalPolicyRules.Where(x => x.PolicyId == policyId).ToArrayAsync(token) : [];
                object before = approval ? ApprovalView(approvalRow!, rules) : CashView(cashRow!);
                if (row.StateVersion != expected || row.State != (retiring ? "Published" : "Draft")) return new(Conflict());
                if (retiring)
                {
                    if (!retiredFrom.HasValue || retiredFrom.Value < today) return new(Invalid());
                }
                else
                {
                    if (!FinancePolicyRulesV1.IsWindow(row.ValidFrom, row.ValidThrough, today)) return new(Invalid());
                    var source = await ReferenceAsync(scope, token);
                    if (approval)
                    {
                        if (rules.Length == 0 || rules.Any(rule => !source.Operations.Any(x => x.Permission == rule.OperationPermission)
                            || !source.Currencies.Any(x => x.CurrencyId == rule.CurrencyId)
                            || !source.Roles.Any(x => x.RoleId == rule.ApproverRoleId && x.Code == rule.ApproverRoleCode && x.SourceVersion == rule.RoleSourceVersion)))
                            return new(Problem(409, SettingsFinancePolicyCodesV1.ReferenceChanged, "Matris kaynakları değişmiş; yeni taslak hazırlayın."));
                        var others = await database.FinanceApprovalPolicies.AsNoTracking().Where(x => x.PolicyId != policyId && x.State != "Draft").ToListAsync(token);
                        if (others.Any(x => FinancePolicyRulesV1.Overlaps(x.ValidFrom, x.ValidThrough, x.RetiredFrom, row.ValidFrom, row.ValidThrough)))
                            return new(Problem(409, SettingsFinancePolicyCodesV1.WindowConflict, "Aynı iş tarihinde birden çok matris yayımlanamaz; mevcut pencereyi açıkça sonlandırın."));
                    }
                    else
                    {
                        if (!source.CashAccounts.Any(x => x.CashAccountId == cashRow!.CashAccountId && x.CurrencyId == cashRow.CurrencyId && x.Version == cashRow.CashAccountVersion))
                            return new(Problem(409, SettingsFinancePolicyCodesV1.ReferenceChanged, "Kasa kaynağı değişmiş; yeni taslak hazırlayın."));
                        var others = await database.CashCountDifferencePolicies.AsNoTracking()
                            .Where(x => x.PolicyId != policyId && x.CashAccountId == cashRow!.CashAccountId && x.CurrencyId == cashRow.CurrencyId && x.State != "Draft").ToListAsync(token);
                        if (others.Any(x => FinancePolicyRulesV1.Overlaps(x.ValidFrom, x.ValidThrough, x.RetiredFrom, row.ValidFrom, row.ValidThrough)))
                            return new(Problem(409, SettingsFinancePolicyCodesV1.WindowConflict, "Bu kasa/para kapsamının tarih penceresi çakışıyor; mevcut pencereyi açıkça sonlandırın."));
                    }
                }
                row.State = retiring ? "Retired" : "Published";
                row.StateVersion = checked(row.StateVersion + 1);
                row.LastReason = reason;
                if (retiring) row.RetiredFrom = retiredFrom;
                else { row.PublishedBy = scope.ActorId; row.PublishedAtUtc = clock.GetUtcNow(); }
                object after = approval ? ApprovalView(approvalRow!, rules) : CashView(cashRow!);
                return new(Saved(row), approval ? ApprovalEntity : CashEntity, row.PolicyId,
                    $"settings.{family}.{(retiring ? "retired" : "published")}.v1", expected, row.StateVersion, before, after, reason);
            }, cancellationToken);
    }
}
