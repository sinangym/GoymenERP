using System.Data;
using System.Net;
using System.Net.Http.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Settings.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Logging.Abstractions;

namespace GoymenERP.Api.IntegrationTests;

// The shared actual-SQL host fixture remains the same; these are API-97 facts, never in-memory acceptance.
public sealed partial class SettingsCompanyPolicySqlServerRelationalAcceptanceTests
{
    private const string InvoicePolicyPath = SettingsPurchaseInvoiceVariancePolicyEndpoints.Path;
    private const string InvoiceManage = SettingsPurchaseInvoiceVariancePolicyPermissionsV1.Manage;

    [SqlServerFact]
    public async Task Invoice_policy_Down_passes_with_empty_history_and_refuses_while_a_published_version_exists_on_real_sql()
    {
        await using var run = await HostRun.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddPurchaseInvoiceVariancePolicies().DownOperations[0]).Sql;
        await run.ExecuteAsync(guard);
        var actor = await run.GrantAsync(TestUsers.CompanyA, CompanyA, InvoiceManage);
        using var editor = run.Client(actor, CompanyA);
        await run.CommandAsync(editor, InvoicePolicyPath + "/publish", "invoice-guard", new PublishPurchaseInvoiceVariancePolicyRequestV1("0", "0", "0", "SENTETIK guard"));
        Assert.Equal(51985, (await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard))).Number);
        Assert.Equal(1, await run.CountAsync("SELECT COUNT(*) FROM [settings].[PurchaseInvoiceVariancePolicies]"));
    }

    [SqlServerFact]
    public async Task Invoice_variance_policy_requires_explicit_publication_and_preserves_versions_company_replay_and_audit_on_real_sql()
    {
        await using var run = await HostRun.StartAsync();
        var actor = await run.GrantAsync(TestUsers.CompanyA, CompanyA, InvoiceManage);
        var viewer = await run.GrantAsync(TestUsers.CompanyAViewer, CompanyA, ErpPermissions.SalesDraftsRead);
        using var editor = run.Client(actor, CompanyA);
        using var denied = run.Client(viewer, CompanyA);
        using (var response = await denied.GetAsync(InvoicePolicyPath)) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var empty = await editor.GetFromJsonAsync<PurchaseInvoiceVariancePolicyViewV1>(InvoicePolicyPath);
        Assert.Equal("0", empty!.Version); Assert.Null(empty.Current); Assert.Empty(empty.Versions);
        await using (var port = run.Numbers(CompanyA))
        {
            var provider = InvoicePolicyService(port.Database, CompanyA);
            var missing = await Assert.ThrowsAsync<SettingsPrerequisiteException>(() => provider.GetAsync(CompanyA));
            Assert.Equal(PurchaseInvoiceVariancePolicyCodesV1.NotPublished, missing.Code);
            var foreign = await Assert.ThrowsAsync<SettingsPrerequisiteException>(() => provider.GetAsync(CompanyB));
            Assert.Equal(PurchaseInvoiceVariancePolicyCodesV1.AccessDenied, foreign.Code);
        }
        var publish = new PublishPurchaseInvoiceVariancePolicyRequestV1("0", "0", "0", "SENTETIK ilk açık yayın");
        using (var response = await editor.SendAsync(Json(InvoicePolicyPath + "/publish", "invoice-foreign-body",
                   new { expectedVersion = "0", amountTolerance = "0", rateTolerance = "0", reason = "SENTETIK", companyId = CompanyB })))
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using (var response = await denied.SendAsync(Json(InvoicePolicyPath + "/publish", "invoice-denied", publish)))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await run.CommandAsync(editor, InvoicePolicyPath + "/publish", "invoice-publish", publish);
        await run.CommandAsync(editor, InvoicePolicyPath + "/publish", "invoice-publish", publish, replay: true);
        using (var response = await editor.SendAsync(Json(InvoicePolicyPath + "/publish", "invoice-publish", publish with { RateTolerance = "1" })))
            await ProblemAsync(response, HttpStatusCode.Conflict, PurchaseInvoiceVariancePolicyCodesV1.IdempotencyConflict);
        using (var response = await editor.SendAsync(Json(InvoicePolicyPath + "/publish", "invoice-stale", publish)))
            await ProblemAsync(response, HttpStatusCode.Conflict, PurchaseInvoiceVariancePolicyCodesV1.VersionConflict);
        await run.CommandAsync(editor, InvoicePolicyPath + "/publish", "invoice-update",
            publish with { ExpectedVersion = "1", AmountTolerance = "0.01", RateTolerance = "0.000001" });
        var history = await editor.GetFromJsonAsync<PurchaseInvoiceVariancePolicyViewV1>(InvoicePolicyPath);
        Assert.Equal("2", history!.Version); Assert.Equal(2, history.Versions.Count);
        Assert.Equal(("0", "0"), (history.Versions[1].AmountTolerance, history.Versions[1].RateTolerance));
        await using (var port = run.Numbers(CompanyA))
        {
            var source = await InvoicePolicyService(port.Database, CompanyA).GetAsync(CompanyA);
            Assert.Equal((history.Current!.PolicyId, 2L, .01m, .000001m),
                (source.PolicyId, source.Version, source.AmountTolerance, source.RateTolerance));
        }
        await using (var port = run.Numbers(CompanyB))
            Assert.Equal(PurchaseInvoiceVariancePolicyCodesV1.NotPublished,
                (await Assert.ThrowsAsync<SettingsPrerequisiteException>(() => InvoicePolicyService(port.Database, CompanyB).GetAsync(CompanyB))).Code);
        Assert.Equal(2, await run.CountAsync("SELECT COUNT(*) FROM [settings].[PurchaseInvoiceVariancePolicies]"));
        Assert.Equal(2, await run.CountAsync("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EntityType] = N'Settings.PurchaseInvoiceVariancePolicy'"));
    }

    [SqlServerFact]
    public async Task Invoice_policy_publication_refuses_borrowed_transaction_without_clearing_the_callers_pending_state_on_real_sql()
    {
        await using var run = await HostRun.StartAsync();
        await using var port = run.Numbers(CompanyA);
        var missingContextAuthority = new InvoiceAuthority(); var missingContextAudit = new InvoiceAuditSpy();
        var missingContext = new SettingsPurchaseInvoiceVariancePolicy(port.Database, new CompanyContext(), new FixedActor(CompanyA),
            missingContextAuthority, missingContextAuthority, missingContextAudit, missingContextAudit, TimeProvider.System,
            NullLogger<SettingsPurchaseInvoiceVariancePolicy>.Instance);
        Assert.Equal(PurchaseInvoiceVariancePolicyCodesV1.AccessDenied,
            (await Assert.ThrowsAsync<SettingsPrerequisiteException>(() => missingContext.ReadAsync())).Code);
        await using var transaction = await port.Database.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var writer = new InvoiceAuditSpy { Pending = true };
        var service = InvoicePolicyService(port.Database, CompanyA, writer);
        var foreignDraft = new PurchaseInvoiceVariancePolicyRow { CompanyId = CompanyA, PolicyId = Guid.NewGuid(), Version = 1,
            AmountTolerance = 0, RateTolerance = 0, PublishedBy = "SENTETIK", PublishedAtUtc = DateTimeOffset.UtcNow, Reason = "SENTETIK caller" };
        port.Database.Add(foreignDraft);
        var error = await Assert.ThrowsAsync<SettingsPrerequisiteException>(() => service.PublishAsync(
            new("0", "0", "0", "SENTETIK"), NumberMetadata("invoice-borrowed")));
        Assert.Equal(PurchaseInvoiceVariancePolicyCodesV1.ProviderUnavailable, error.Code);
        Assert.Equal(EntityState.Added, port.Database.Entry(foreignDraft).State);
        Assert.True(writer.Pending); Assert.Equal(0, writer.DiscardCount);
        await port.Database.SaveChangesAsync();
        await transaction.CommitAsync();
        Assert.Equal(1, await run.CountAsync("SELECT COUNT(*) FROM [settings].[PurchaseInvoiceVariancePolicies]"));
    }

    [SqlServerFact]
    public async Task Failed_invoice_policy_audit_aborts_publication_and_leaves_the_key_reusable_on_real_sql()
    {
        await using var run = await HostRun.StartAsync();
        await using (var port = run.Numbers(CompanyA))
        {
            var writer = new InvoiceAuditSpy { FailWrite = true };
            var error = await Assert.ThrowsAsync<SettingsPrerequisiteException>(() => InvoicePolicyService(port.Database, CompanyA, writer)
                .PublishAsync(new("0", "0", "0", "SENTETIK audit failure"), NumberMetadata("invoice-failed")));
            Assert.Equal(PurchaseInvoiceVariancePolicyCodesV1.ProviderUnavailable, error.Code);
            Assert.False(port.Database.ChangeTracker.HasChanges()); Assert.False(writer.Pending);
        }
        Assert.Equal(0, await run.CountAsync("SELECT COUNT(*) FROM [settings].[PurchaseInvoiceVariancePolicies]"));
        Assert.Equal(0, await run.CountAsync("SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CommandName] = 'settings.purchase-invoice-variance-policy.publish.v1'"));
        var actor = await run.GrantAsync(TestUsers.CompanyA, CompanyA, InvoiceManage);
        using var editor = run.Client(actor, CompanyA);
        await run.CommandAsync(editor, InvoicePolicyPath + "/publish", "invoice-failed", new PublishPurchaseInvoiceVariancePolicyRequestV1("0", "0", "0", "SENTETIK audit failure"));
        Assert.Equal(1, await run.CountAsync("SELECT COUNT(*) FROM [settings].[PurchaseInvoiceVariancePolicies]"));
    }

    [SqlServerFact]
    public async Task Settings_save_failure_after_the_real_Audit_owner_append_rolls_back_policy_audit_and_key_on_real_sql()
    {
        await using var run = await HostRun.StartAsync();
        await using (var port = run.Numbers(CompanyA))
        {
            var context = new CompanyContext(); context.Set(CompanyA); var authority = new InvoiceAuthority();
            var service = new SettingsPurchaseInvoiceVariancePolicy(port.Database, context, new FixedActor(CompanyA),
                authority, authority, port.Audit, port.AuditWriter, TimeProvider.System, NullLogger<SettingsPurchaseInvoiceVariancePolicy>.Instance);
            // Production owner has already appended Audit in the same actual SQL transaction before this Settings save.
            port.Database.SavingChanges += (_, _) => throw new InvalidOperationException("SENTETIK after-Audit failure");
            var error = await Assert.ThrowsAsync<SettingsPrerequisiteException>(() => service.PublishAsync(
                new("0", "0", "0", "SENTETIK rollback"), NumberMetadata("invoice-after-audit")));
            Assert.Equal(PurchaseInvoiceVariancePolicyCodesV1.ProviderUnavailable, error.Code);
            Assert.False(port.Database.ChangeTracker.HasChanges()); Assert.False(port.AuditWriter.HasPendingDrafts);
        }
        Assert.Equal(0, await run.CountAsync("SELECT COUNT(*) FROM [settings].[PurchaseInvoiceVariancePolicies]"));
        Assert.Equal(0, await run.CountAsync("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EntityType] = N'Settings.PurchaseInvoiceVariancePolicy'"));
        Assert.Equal(0, await run.CountAsync("SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CommandName] = 'settings.purchase-invoice-variance-policy.publish.v1'"));
        var actor = await run.GrantAsync(TestUsers.CompanyA, CompanyA, InvoiceManage);
        using var editor = run.Client(actor, CompanyA);
        await run.CommandAsync(editor, InvoicePolicyPath + "/publish", "invoice-after-audit", new PublishPurchaseInvoiceVariancePolicyRequestV1("0", "0", "0", "SENTETIK rollback"));
    }

    [SqlServerFact]
    public async Task Revoked_invoice_policy_authority_aborts_the_new_version_and_terminal_key_on_real_sql()
    {
        await using var run = await HostRun.StartAsync();
        await using var port = run.Numbers(CompanyA);
        var writer = new InvoiceAuditSpy();
        var service = InvoicePolicyService(port.Database, CompanyA, writer, new InvoiceAuthority { RevokeOnFresh = 1 });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.PublishAsync(new("0", "0", "0", "SENTETIK revocation"),
            NumberMetadata("invoice-revoked")));
        Assert.False(port.Database.ChangeTracker.HasChanges()); Assert.False(writer.Pending);
        Assert.Equal(0, await run.CountAsync("SELECT COUNT(*) FROM [settings].[PurchaseInvoiceVariancePolicies]"));
        Assert.Equal(0, await run.CountAsync("SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [CommandName] = 'settings.purchase-invoice-variance-policy.publish.v1'"));
    }

    [SqlServerFact]
    public async Task Racing_invoice_policy_publications_have_one_immutable_version_and_a_retryable_loser_on_real_sql()
    {
        await using var run = await HostRun.StartAsync();
        var actor = await run.GrantAsync(TestUsers.CompanyA, CompanyA, InvoiceManage);
        using var first = run.Client(actor, CompanyA); using var second = run.Client(actor, CompanyA);
        var bodyA = new PublishPurchaseInvoiceVariancePolicyRequestV1("0", "0.01", "0", "SENTETIK race A");
        var bodyB = new PublishPurchaseInvoiceVariancePolicyRequestV1("0", "0.02", "0", "SENTETIK race B");
        var results = await Task.WhenAll(first.SendAsync(Json(InvoicePolicyPath + "/publish", "invoice-race-a", bodyA)),
            second.SendAsync(Json(InvoicePolicyPath + "/publish", "invoice-race-b", bodyB)));
        try
        {
            Assert.Single(results, response => response.StatusCode == HttpStatusCode.NoContent);
            var loser = results[0].StatusCode == HttpStatusCode.NoContent ? 1 : 0;
            Assert.Contains(results[loser].StatusCode, new[] { HttpStatusCode.Conflict, HttpStatusCode.ServiceUnavailable });
            if (results[loser].StatusCode == HttpStatusCode.ServiceUnavailable)
            {
                using var retry = await (loser == 0 ? first : second).SendAsync(Json(InvoicePolicyPath + "/publish",
                    loser == 0 ? "invoice-race-a" : "invoice-race-b", loser == 0 ? bodyA : bodyB));
                await ProblemAsync(retry, HttpStatusCode.Conflict, PurchaseInvoiceVariancePolicyCodesV1.VersionConflict);
            }
            Assert.Equal(1, await run.CountAsync("SELECT COUNT(*) FROM [settings].[PurchaseInvoiceVariancePolicies]"));
            Assert.Equal(1, await run.CountAsync("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EntityType] = N'Settings.PurchaseInvoiceVariancePolicy'"));
        }
        finally { foreach (var result in results) result.Dispose(); }
    }

    private static SettingsPurchaseInvoiceVariancePolicy InvoicePolicyService(SettingsDbContext database, Guid company,
        InvoiceAuditSpy? writer = null, InvoiceAuthority? authorization = null)
    {
        var context = new CompanyContext(); context.Set(company);
        var authority = authorization ?? new InvoiceAuthority(); var audit = writer ?? new InvoiceAuditSpy();
        return new(database, context, new FixedActor(company), authority, authority, audit, audit, TimeProvider.System,
            NullLogger<SettingsPurchaseInvoiceVariancePolicy>.Instance);
    }

    private sealed class InvoiceAuthority : IErpAuthorizationService, IErpAuthorityRevalidationService
    {
        private int freshCalls;
        public int RevokeOnFresh { get; init; } = int.MaxValue;
        private static readonly PermissionScope Granted = new(true, true, new HashSet<string> { "SENTETIK" },
            new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());
        public Task<AuthorizationSnapshot> GetAuthorizationSnapshotAsync(Guid companyId, IReadOnlyCollection<string> permissions,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> HasPermissionAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<PermissionScope> GetPermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) => Task.FromResult(Granted);
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(++freshCalls >= RevokeOnFresh ? PermissionScope.Denied : Granted);
    }

    private sealed class InvoiceAuditSpy : IAuditTransactionWriterV1, IAuditEventCollector
    {
        public bool Pending { get; set; }
        public bool FailWrite { get; init; }
        public int DiscardCount { get; private set; }
        public bool HasPendingDrafts => Pending;
        public void Enqueue(AuditEventDraft draft) => Pending = true;
        public Task WritePendingAsync(IDbTransaction transaction, CancellationToken cancellationToken = default)
        { if (FailWrite) throw new InvalidOperationException("SENTETIK audit failure"); Pending = false; return Task.CompletedTask; }
        public void DiscardPending() { DiscardCount++; Pending = false; }
    }
}
