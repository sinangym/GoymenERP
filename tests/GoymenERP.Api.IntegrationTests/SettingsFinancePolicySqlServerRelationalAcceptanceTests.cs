using System.Net;
using System.Net.Http.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

// Reuses the actual SQL host, Identity role/grant fixtures and Audit owner fault injector. No in-memory acceptance.
public sealed partial class SettingsCollateralTypeSqlServerRelationalAcceptanceTests
{
    private const string FinancePolicyRoot = SettingsFinancePolicyEndpoints.Path;

    [SqlServerFact]
    public async Task Financial_policy_matrix_is_explicit_company_scoped_forward_versioned_and_replayed_without_assigning_a_role()
    {
        await using var run = await HostRun.StartAsync();
        await GrantFinancePolicyAuthorAsync(run);
        using var editor = run.Client(TestUsers.CompanyA, CompanyA);
        using var denied = run.Client(TestUsers.CompanyAViewer, CompanyA);
        using (var response = await denied.GetAsync(FinancePolicyRoot)) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var empty = (await editor.GetFromJsonAsync<SettingsFinancePoliciesWorkspaceV1>(FinancePolicyRoot))!;
        Assert.Empty(empty.ApprovalPolicies); Assert.Empty(empty.CashCountDifferencePolicies);
        Assert.NotEmpty(empty.References.Roles); Assert.NotEmpty(empty.References.Currencies); Assert.NotEmpty(empty.References.Operations);
        var role = empty.References.Roles.First();
        var currency = empty.References.Currencies.First();
        var operation = empty.References.Operations.First();
        var request = new PrepareSettingsFinanceApprovalPolicyV1("0", empty.BusinessDate, empty.BusinessDate,
            [new(operation.Permission, currency.CurrencyId, role.RoleId, role.SourceVersion, 2, "100000")], "Synthetic explicit matrix");
        var assignmentsBefore = await run.CountAsync("SELECT COUNT(*) FROM [identity].[RoleAssignments]");
        var permissionsBefore = await run.CountAsync("SELECT COUNT(*) FROM [identity].[RolePermissions]");
        await run.CommandAsync(editor, FinancePolicyRoot + "/approval-matrices", "matrix-prepare", request);
        await run.CommandAsync(editor, FinancePolicyRoot + "/approval-matrices", "matrix-prepare", request, replay: true);
        var draft = Assert.Single((await editor.GetFromJsonAsync<SettingsFinancePoliciesWorkspaceV1>(FinancePolicyRoot))!.ApprovalPolicies);
        Assert.Equal((1L, 1L, SettingsFinancePolicyStateV1.Draft), (draft.Version, draft.StateVersion, draft.State));
        using (var response = await editor.PostAsJsonAsync(FinancePolicyRoot + "/approval-matrices/preview",
                   new PreviewSettingsFinanceApprovalPolicyV1(operation.Permission, currency.CurrencyId, empty.BusinessDate, "100000")))
            Assert.Equal(SettingsFinancePolicyReadOutcomeV1.Unavailable, (await response.Content.ReadFromJsonAsync<SettingsFinanceApprovalPreviewV1>())!.Outcome);
        await run.CommandAsync(editor, $"{FinancePolicyRoot}/approval-matrices/{draft.PolicyId}/publish", "matrix-publish", new PublishSettingsFinancePolicyV1("1", "Synthetic publish"));
        using (var response = await editor.PostAsJsonAsync(FinancePolicyRoot + "/approval-matrices/preview",
                   new PreviewSettingsFinanceApprovalPolicyV1(operation.Permission, currency.CurrencyId, empty.BusinessDate, "100000")))
        {
            var preview = (await response.Content.ReadFromJsonAsync<SettingsFinanceApprovalPreviewV1>())!;
            Assert.Equal(SettingsFinancePolicyReadOutcomeV1.Available, preview.Outcome);
            Assert.Equal((role.RoleId, 2, "100000"), (Assert.Single(preview.MatchingRules).ApproverRoleId, preview.MatchingRules[0].ApprovalStep, preview.MatchingRules[0].AmountThreshold));
        }
        await run.CommandAsync(editor, FinancePolicyRoot + "/approval-matrices", "matrix-overlap-draft", request with { ExpectedVersion = "1" });
        var overlap = (await editor.GetFromJsonAsync<SettingsFinancePoliciesWorkspaceV1>(FinancePolicyRoot))!.ApprovalPolicies.Single(x => x.Version == 2);
        using (var response = await editor.SendAsync(Json($"{FinancePolicyRoot}/approval-matrices/{overlap.PolicyId}/publish", "matrix-overlap-publish", new PublishSettingsFinancePolicyV1("1", "Synthetic overlap"))))
            await ProblemAsync(response, HttpStatusCode.Conflict, SettingsFinancePolicyCodesV1.WindowConflict);
        var history = (await editor.GetFromJsonAsync<SettingsFinancePoliciesWorkspaceV1>(FinancePolicyRoot))!.ApprovalPolicies.Single(x => x.Version == 1);
        Assert.Equal("100000", Assert.Single(history.Rules).AmountThreshold);
        Assert.Equal(assignmentsBefore, await run.CountAsync("SELECT COUNT(*) FROM [identity].[RoleAssignments]"));
        Assert.Equal(permissionsBefore, await run.CountAsync("SELECT COUNT(*) FROM [identity].[RolePermissions]"));
        using (var response = await editor.SendAsync(Json(FinancePolicyRoot + "/approval-matrices", "matrix-past", request with { ExpectedVersion = "2", ValidFrom = empty.BusinessDate.AddDays(-1) })))
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using (var response = await editor.SendAsync(Json(FinancePolicyRoot + "/approval-matrices", "matrix-inject-company", new { companyId = CompanyB, request.ExpectedVersion, request.ValidFrom, request.ValidThrough, request.Rules, request.Reason })))
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [SqlServerFact]
    public async Task Cash_difference_policy_uses_the_real_cash_currency_version_and_explicit_threshold_without_reading_balances()
    {
        await using var run = await HostRun.StartAsync();
        await GrantFinancePolicyAuthorAsync(run);
        using var editor = run.Client(TestUsers.CompanyA, CompanyA);
        var workspace = (await editor.GetFromJsonAsync<SettingsFinancePoliciesWorkspaceV1>(FinancePolicyRoot))!;
        var currency = workspace.References.Currencies.First().CurrencyId;
        var cashId = Guid.NewGuid(); var foreignCash = Guid.NewGuid();
        await run.ExecuteAsync($"""
            INSERT INTO [finance].[CashAccounts] (Id,CompanyId,CurrencyId,DisplayName,Status,Version,ProposedBy,ProposedAtUtc,DecidedBy,DecidedAtUtc,UpdatedAtUtc)
            VALUES ('{cashId:D}','{CompanyA:D}','{currency:D}',N'Synthetic matrix cash',2,2,N'a',SYSUTCDATETIME(),N'b',SYSUTCDATETIME(),SYSUTCDATETIME()),
                   ('{foreignCash:D}','{CompanyB:D}','{currency:D}',N'Synthetic foreign cash',2,2,N'a',SYSUTCDATETIME(),N'b',SYSUTCDATETIME(),SYSUTCDATETIME());
            """);
        workspace = (await editor.GetFromJsonAsync<SettingsFinancePoliciesWorkspaceV1>(FinancePolicyRoot))!;
        Assert.DoesNotContain(workspace.References.CashAccounts, x => x.CashAccountId == foreignCash);
        var cash = workspace.References.CashAccounts.Single(x => x.CashAccountId == cashId);
        var request = new PrepareSettingsCashCountDifferencePolicyV1("0", cashId, cash.Version, currency, "5", workspace.BusinessDate, null, "Synthetic cash threshold");
        using (var response = await editor.SendAsync(Json(FinancePolicyRoot + "/cash-count-differences", "cash-foreign", request with { CashAccountId = foreignCash })))
            await ProblemAsync(response, HttpStatusCode.Conflict, SettingsFinancePolicyCodesV1.ReferenceChanged);
        await run.CommandAsync(editor, FinancePolicyRoot + "/cash-count-differences", "cash-prepare", request);
        var draft = Assert.Single((await editor.GetFromJsonAsync<SettingsFinancePoliciesWorkspaceV1>(FinancePolicyRoot))!.CashCountDifferencePolicies);
        await run.CommandAsync(editor, $"{FinancePolicyRoot}/cash-count-differences/{draft.PolicyId}/publish", "cash-publish", new PublishSettingsFinancePolicyV1("1", "Synthetic publish"));
        foreach (var (difference, exceeds) in new[] { ("-5", false), ("5.000000000000000000000000000001", true) })
        {
            using var response = await editor.PostAsJsonAsync(FinancePolicyRoot + "/cash-count-differences/preview", new PreviewSettingsCashCountDifferencePolicyV1(cashId, currency, workspace.BusinessDate, difference));
            var answer = (await response.Content.ReadFromJsonAsync<SettingsCashCountDifferencePreviewV1>())!;
            Assert.Equal(SettingsFinancePolicyReadOutcomeV1.Available, answer.Outcome); Assert.Equal(exceeds, answer.ExceedsThreshold);
        }
        Assert.Equal(0, await run.CountAsync("SELECT COUNT(*) FROM [finance].[MoneyEvents]"));
    }

    [SqlServerFact]
    public async Task Financial_policy_audit_failure_rolls_back_draft_rules_and_attempt_before_same_key_retry_and_replay()
    {
        var fault = new OwnerFault();
        await using var run = await HostRun.StartAsync(services =>
        {
            var registration = services.Single(x => x.ServiceType == typeof(IAuditTransactionWriterV1)); services.Remove(registration);
            services.AddScoped<IAuditTransactionWriterV1>(sp => new FaultingOwner((IAuditTransactionWriterV1)ActivatorUtilities.CreateInstance(sp, registration.ImplementationType!), fault));
        });
        await GrantFinancePolicyAuthorAsync(run); using var editor = run.Client(TestUsers.CompanyA, CompanyA);
        var workspace = (await editor.GetFromJsonAsync<SettingsFinancePoliciesWorkspaceV1>(FinancePolicyRoot))!;
        var role = workspace.References.Roles.First(); var operation = workspace.References.Operations.First(); var currency = workspace.References.Currencies.First();
        var request = new PrepareSettingsFinanceApprovalPolicyV1("0", workspace.BusinessDate, null,
            [new(operation.Permission, currency.CurrencyId, role.RoleId, role.SourceVersion, 1, "0")], "Synthetic explicit zero");
        using (var response = await editor.SendAsync(Json(FinancePolicyRoot + "/approval-matrices", "matrix-atomic", request))) Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(0, await run.CountAsync("SELECT COUNT(*) FROM [settings].[FinanceApprovalPolicies]"));
        Assert.Equal(0, await run.CountAsync("SELECT COUNT(*) FROM [settings].[FinanceApprovalPolicyRules]"));
        Assert.Equal(0, await run.CountAsync("SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CommandName]=N'settings.finance-approval-policy.prepare.v1'"));
        Assert.Equal(0, await run.CountAsync("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EntityType]=N'Settings.FinanceApprovalPolicy'"));
        fault.Fail = false;
        await run.CommandAsync(editor, FinancePolicyRoot + "/approval-matrices", "matrix-atomic", request);
        await run.CommandAsync(editor, FinancePolicyRoot + "/approval-matrices", "matrix-atomic", request, replay: true);
        Assert.Equal(1, await run.CountAsync("SELECT COUNT(*) FROM [settings].[FinanceApprovalPolicyRules]"));
        Assert.Equal(1, await run.CountAsync("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EntityType]=N'Settings.FinanceApprovalPolicy'"));
    }

    private static async Task GrantFinancePolicyAuthorAsync(HostRun run)
    {
        await run.GrantAsync(TestUsers.CompanyA, CompanyA, SettingsFinancePolicyPermissionsV1.Prepare);
        await run.GrantAsync(TestUsers.CompanyA, CompanyA, SettingsFinancePolicyPermissionsV1.Publish);
        await run.GrantAsync(TestUsers.CompanyA, CompanyA, SettingsFinancePolicyPermissionsV1.PublishCritical);
    }
}
