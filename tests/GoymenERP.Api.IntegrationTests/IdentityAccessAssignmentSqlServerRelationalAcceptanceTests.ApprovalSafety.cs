using System.Data;
using System.Data.Common;
using System.Net;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Security.Events;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Approvals;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class IdentityAccessAssignmentSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Actual_access_approval_alias_preserves_borrowed_ambient_dirty_and_real_caller_audit()
    {
        var fault = new GrantApprovalFault();
        await WithDatabaseAsync(SyntheticCode("ADMIN"), async factory =>
        {
            foreach (var boundary in new[] { "borrowed", "ambient", "dirty", "audit", "after-keyread" })
            {
                var (client, pending, target, role) = await GrantApprovalRequestAsync(factory);
                using (client)
                {
                    fault.Arm("entry-" + boundary, true);
                    var correlation = NewKey();
                    using var refused = await GrantDecisionAsync(client, pending.Id, true, NewKey(), correlation);
                    Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
                    Assert.True(fault.CallerPreserved, boundary);
                    Assert.Equal(0, fault.Discards);
                    await AssertGrantApprovalEffectsAsync(factory, pending.Id, target, role, correlation, false, false);
                    fault.Disarm();
                }
            }
        }, services => ConfigureGrantApprovalFault(services, fault));
    }

    // Actual SQL + native HTTP + actual Audit owner. Authority/provider failures are controlled injections,
    // not a claim that a live grant was changed or a real two-session deadlock occurred.
    [SqlServerFact]
    public async Task Access_decisions_recheck_after_save_audit_security_and_before_commit_then_same_key_changes_once()
    {
        var fault = new GrantApprovalFault();
        await WithDatabaseAsync(SyntheticCode("ADMIN"), async factory =>
        {
            foreach (var approve in new[] { true, false })
            foreach (var phase in new[] { "before-save", "after-save", "after-audit", "precommit" }.Concat(approve ? ["after-security"] : Array.Empty<string>()))
            {
                var (client, pending, target, role) = await GrantApprovalRequestAsync(factory);
                using (client)
                {
                    var key = NewKey(); var correlation = NewKey();
                    fault.Arm(phase, approve);
                    using (var denied = await GrantDecisionAsync(client, pending.Id, approve, key, correlation))
                        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
                    Assert.True(fault.Fired, phase);
                    Assert.Equal(phase, fault.FiredPhase);
                    Assert.Equal(1, fault.Discards);
                    await AssertGrantApprovalEffectsAsync(factory, pending.Id, target, role, correlation, false, false);
                    fault.Disarm();
                    var retryCorrelation = NewKey();
                    Outcome native;
                    using (var retry = await GrantDecisionAsync(client, pending.Id, approve, key, retryCorrelation))
                    {
                        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
                        native = await ReadOutcomeAsync(retry);
                    }
                    Assert.Equal("2", native.Version);
                    Assert.Equal(approve ? "Approved" : "Rejected", native.Status);
                    using (var replay = await GrantDecisionAsync(client, pending.Id, approve, key, NewKey()))
                    {
                        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
                        Assert.Equal(native, await ReadOutcomeAsync(replay));
                        Assert.Equal("true", Assert.Single(replay.Headers.GetValues(ReplayedHeader)));
                        Assert.Equal(retryCorrelation, Assert.Single(replay.Headers.GetValues(OriginalCorrelationHeader)));
                    }
                    await AssertGrantApprovalEffectsAsync(factory, pending.Id, target, role, retryCorrelation, true, approve);
                }
            }
        }, services => ConfigureGrantApprovalFault(services, fault));
    }

    [SqlServerFact]
    public async Task Access_decision_postcommit_denial_hides_release_but_original_replay_does_not_repeat_the_assignment()
    {
        var fault = new GrantApprovalFault();
        await WithDatabaseAsync(SyntheticCode("ADMIN"), async factory =>
        {
            foreach (var approve in new[] { true, false })
            {
                var (client, pending, target, role) = await GrantApprovalRequestAsync(factory);
                using (client)
                {
                    var key = NewKey(); var correlation = NewKey();
                    fault.Arm("postcommit", approve);
                    using (var denied = await GrantDecisionAsync(client, pending.Id, approve, key, correlation))
                        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
                    Assert.True(fault.Fired);
                    await AssertGrantApprovalEffectsAsync(factory, pending.Id, target, role, correlation, true, approve);
                    fault.Disarm();
                    using var replay = await GrantDecisionAsync(client, pending.Id, approve, key, NewKey());
                    Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
                    Assert.Equal("true", Assert.Single(replay.Headers.GetValues(ReplayedHeader)));
                    Assert.Equal(correlation, Assert.Single(replay.Headers.GetValues(OriginalCorrelationHeader)));
                    await AssertGrantApprovalEffectsAsync(factory, pending.Id, target, role, correlation, true, approve);
                }
            }
        }, services => ConfigureGrantApprovalFault(services, fault));
    }

    [SqlServerFact]
    public async Task Access_approval_actual_fresh_target_read_fence_stops_the_next_role_read_after_authority_loss()
    {
        var fault = new GrantApprovalFault();
        await WithDatabaseAsync(SyntheticCode("ADMIN"), async factory =>
        {
            var (client, pending, target, role) = await GrantApprovalRequestAsync(factory);
            using (client)
            {
                var key = NewKey(); var correlation = NewKey();
                fault.Arm("target-read", true);
                fault.TargetId = target; fault.RoleId = role;
                using (var denied = await GrantDecisionAsync(client, pending.Id, true, key, correlation))
                    Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
                Assert.True(fault.Fired);
                Assert.Equal(1, fault.TargetReads);
                Assert.Equal(0, fault.RoleReadsAfterTarget);
                Assert.True(fault.ActualFreshReads > 0);
                await AssertGrantApprovalEffectsAsync(factory, pending.Id, target, role, correlation, false, false);
                fault.Disarm();
                using var retry = await GrantDecisionAsync(client, pending.Id, true, key, NewKey());
                Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
                Assert.Equal(1, await CountAssignmentsAsync(factory, target, role));
            }
        }, services => ConfigureGrantApprovalFault(services, fault));
    }

    [SqlServerFact]
    public async Task Controlled_access_1205_1222_abort_reconciles_only_one_actual_original_key_winner_without_mutation_retry()
    {
        var fault = new GrantApprovalFault();
        await WithDatabaseAsync(SyntheticCode("ADMIN"), async factory =>
        {
            foreach (var error in new[] { 1205, 1222 })
            foreach (var wrapped in new[] { false, true })
            foreach (var winner in new[] { false, true })
            foreach (var completedProviderTransaction in new[] { false, true })
            {
                if (completedProviderTransaction && error != 1205) continue;
                var (client, pending, target, role) = await GrantApprovalRequestAsync(factory);
                using (client)
                {
                    var key = NewKey(); var failedCorrelation = NewKey(); var winnerCorrelation = NewKey();
                    fault.Arm(completedProviderTransaction ? "sql-completed" : wrapped ? "sql-audit" : "sql-save", true);
                    fault.SqlNumber = error; fault.Wrapped = wrapped;
                    if (winner) fault.AfterAbort = async () =>
                    {
                        // This callback is reached only after the original owner's rollback, disposal and cleanup.
                        var aborted = Assert.IsAssignableFrom<DbContext>(fault.Context);
                        Assert.Null(aborted.Database.CurrentTransaction);
                        Assert.False(aborted.ChangeTracker.HasChanges());
                        Assert.False(fault.WriterPending);
                        fault.Phase = null;
                        using var original = await GrantDecisionAsync(client, pending.Id, true, key, winnerCorrelation);
                        Assert.Equal(HttpStatusCode.OK, original.StatusCode);
                        Assert.False(original.Headers.Contains(ReplayedHeader));
                    };
                    using (var result = await GrantDecisionAsync(client, pending.Id, true, key, failedCorrelation))
                    {
                        Assert.Equal(winner ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable, result.StatusCode);
                        if (winner)
                        {
                            Assert.Equal("true", Assert.Single(result.Headers.GetValues(ReplayedHeader)));
                            Assert.Equal(winnerCorrelation, Assert.Single(result.Headers.GetValues(OriginalCorrelationHeader)));
                        }
                        else Assert.False(result.Headers.Contains(ReplayedHeader));
                    }
                    Assert.Equal(1, fault.Injections);
                    Assert.Equal(1, fault.AfterAbortReads);
                    await AssertGrantApprovalEffectsAsync(factory, pending.Id, target, role, failedCorrelation, winner, winner,
                        expectedCommands: 0, expectedAudits: 0, expectedOutbox: 0);
                    if (winner) await AssertGrantApprovalEffectsAsync(factory, pending.Id, target, role, winnerCorrelation, true, true);
                    else
                    {
                        fault.Disarm();
                        using var retry = await GrantDecisionAsync(client, pending.Id, true, key, winnerCorrelation);
                        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
                        await AssertGrantApprovalEffectsAsync(factory, pending.Id, target, role, winnerCorrelation, true, true);
                    }
                    fault.Disarm();
                }
            }
        }, services => ConfigureGrantApprovalFault(services, fault));
    }

    [SqlServerFact]
    public async Task Access_approval_rollback_or_audit_cleanup_fault_is_not_an_unstored_safe_retry_answer()
    {
        var fault = new GrantApprovalFault();
        await WithDatabaseAsync(SyntheticCode("ADMIN"), async factory =>
        {
            foreach (var phase in new[] { "rollback-fault", "discard-fault" })
            {
                var (client, pending, target, role) = await GrantApprovalRequestAsync(factory);
                using (client)
                {
                    var key = NewKey(); var correlation = NewKey();
                    fault.Arm(phase, true);
                    using (var failed = await GrantDecisionAsync(client, pending.Id, true, key, correlation))
                        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
                    Assert.True(fault.Fired);
                    Assert.Equal(1, fault.CleanupFaults);
                    await AssertGrantApprovalEffectsAsync(factory, pending.Id, target, role, correlation, false, false);
                    fault.Disarm();
                    using var retry = await GrantDecisionAsync(client, pending.Id, true, key, NewKey());
                    Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
                    Assert.Equal(1, await CountAssignmentsAsync(factory, target, role));
                }
            }
        }, services => ConfigureGrantApprovalFault(services, fault));
    }

    private static async Task<(HttpClient Client, Outcome Pending, Guid Target, Guid Role)> GrantApprovalRequestAsync(SqlServerRelationalApiFactory factory)
    {
        var requester = await EnsureUserAsync(factory, TestUsers.NoAssignment);
        var approver = await EnsureUserAsync(factory, TestUsers.Unprovisioned);
        var target = await EnsureUserAsync(factory, TestUsers.CompanyA);
        var managerRole = await CreateRoleAsync(factory, SyntheticCode("MANAGER"), IdentityPermissionManifestV1.RolesManage);
        var approverRole = await CreateRoleAsync(factory, SyntheticCode("APPROVER"), IdentityPermissionManifestV1.AccessApprove);
        var criticalRole = await CreateRoleAsync(factory, SyntheticCode("CRITICAL"), IdentityPermissionManifestV1.RolesManage);
        await AssignDirectlyAsync(factory, requester, managerRole, TestCompanyIds.CompanyA);
        await AssignDirectlyAsync(factory, approver, approverRole, TestCompanyIds.CompanyA);
        using var requesterClient = CreateClient(factory, TestUsers.NoAssignment, TestCompanyIds.CompanyA);
        using var opened = await PostAsync(requesterClient, GrantRequestsRoute,
            new CreateAccessGrantRequestRequestV1(target, criticalRole, null, null, "SENTETIK approval boundary"), NewKey(), NewKey());
        Assert.Equal(HttpStatusCode.Created, opened.StatusCode);
        return (CreateClient(factory, TestUsers.Unprovisioned, TestCompanyIds.CompanyA), await ReadOutcomeAsync(opened), target, criticalRole);
    }

    private static Task<HttpResponseMessage> GrantDecisionAsync(HttpClient client, Guid id, bool approve, string key, string correlation) => approve
        ? PostAsync(client, $"{GrantRequestsRoute}/{id}/approve", new ApproveAccessGrantRequestV1("1"), key, correlation)
        : PostAsync(client, $"{GrantRequestsRoute}/{id}/reject", new RejectAccessGrantRequestV1("1", "reason_code"), key, correlation);

    private static async Task AssertGrantApprovalEffectsAsync(SqlServerRelationalApiFactory factory, Guid request, Guid target, Guid role,
        string correlation, bool terminal, bool assigned, int? expectedCommands = null, int? expectedAudits = null, int? expectedOutbox = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var row = await db.AccessGrantRequests.AsNoTracking().SingleAsync(x => x.Id == request && x.CompanyId == TestCompanyIds.CompanyA);
        Assert.Equal(terminal ? 2L : 1L, row.Version);
        Assert.Equal(terminal ? assigned ? AccessGrantRequestStatus.Approved : AccessGrantRequestStatus.Rejected : AccessGrantRequestStatus.Pending, row.Status);
        Assert.Equal(assigned ? 1 : 0, await CountAssignmentsAsync(factory, target, role));
        Assert.Equal(expectedCommands ?? (terminal ? 1 : 0), await db.CommandExecutions.CountAsync(x => x.OriginalCorrelationId == correlation));
        Assert.Equal(expectedOutbox ?? (assigned ? 1 : 0), await db.OutboxMessages.CountAsync(x => x.CorrelationId == correlation));
        scope.ServiceProvider.GetRequiredService<GoymenERP.BuildingBlocks.Multitenancy.ICompanyContext>().Set(TestCompanyIds.CompanyA);
        Assert.Equal(expectedAudits ?? (terminal ? assigned ? 2 : 1 : 0), await scope.ServiceProvider.GetRequiredService<AuditDbContext>()
            .AuditEvents.CountAsync(x => x.CorrelationId == correlation));
    }

    private static void ConfigureGrantApprovalFault(IServiceCollection services, GrantApprovalFault fault)
    {
        services.AddDbContext<IdentityDbContext>((provider, options) => options.AddInterceptors(new GrantApprovalSave(fault), new GrantApprovalTransaction(fault),
            new GrantApprovalRead(fault, provider.GetRequiredService<IAuditEventCollector>())));
        Decorate<IErpAuthorityRevalidationService>((inner, provider) => new GrantApprovalAuthority(inner, fault, provider.GetRequiredService<IdentityDbContext>()));
        Decorate<IAuditTransactionWriterV1>((inner, _) => new GrantApprovalAudit(inner, fault));
        Decorate<ISecurityEventWriterV1>((inner, _) => new GrantApprovalSecurity(inner, fault));
        Decorate<IIdentityAccessAssignmentService>((inner, provider) => new GrantApprovalEntryProbe(inner,
            provider.GetRequiredService<IdentityDbContext>(), provider.GetRequiredService<IAuditEventCollector>(),
            provider.GetRequiredService<IAuditTransactionWriterV1>(), fault));
        void Decorate<T>(Func<T, IServiceProvider, T> decorate) where T : class
        {
            var registration = services.Last(x => x.ServiceType == typeof(T));
            services.Remove(registration);
            services.AddScoped<T>(provider => decorate((T)(registration.ImplementationFactory?.Invoke(provider)
                ?? registration.ImplementationInstance ?? ActivatorUtilities.CreateInstance(provider, registration.ImplementationType!)), provider));
        }
    }

    private sealed class GrantApprovalFault
    {
        internal string? Phase; internal string Stage = "none"; internal string? FiredPhase;
        internal bool Approve; internal bool Fired; internal bool CallerPreserved; internal int Discards; internal int Injections;
        internal int SqlNumber; internal bool Wrapped; internal int AfterAbortReads; internal int CleanupFaults;
        internal int SecurityReads; internal int AuditReads; internal DbContext? Context; internal bool WriterPending;
        internal Guid TargetId; internal Guid RoleId; internal int TargetReads; internal int RoleReadsAfterTarget; internal int ActualFreshReads;
        internal GoymenERP.Modules.Identity.Domain.Users.UserAccount? CallerEntry;
        internal Func<Task>? AfterAbort;
        internal void Arm(string phase, bool approve)
        { Disarm(); Phase = phase; Approve = approve; }
        internal void Disarm()
        { Phase = null; Stage = "none"; FiredPhase = null; Fired = CallerPreserved = false; Discards = Injections = AfterAbortReads = CleanupFaults = SecurityReads = AuditReads = TargetReads = RoleReadsAfterTarget = ActualFreshReads = 0; AfterAbort = null; Context = null; CallerEntry = null; WriterPending = false; }
        internal void ThrowSql()
        {
            Injections++;
            var error = SqlConcurrencyWrapperPolicyTests.SqlFailure(SqlNumber);
            if (Wrapped) throw new DbUpdateException("SENTETIK wrapped provider abort", error);
            throw error;
        }
    }

    private sealed class GrantApprovalSave(GrantApprovalFault fault) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (fault.Phase is not null)
            {
                fault.Context = eventData.Context;
                if (fault.Phase == "sql-save" && fault.Injections == 0) fault.ThrowSql();
            }
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        { if (fault.Phase is not null) fault.Stage = "saved"; return ValueTask.FromResult(result); }
    }

    private sealed class GrantApprovalRead(GrantApprovalFault fault, IAuditEventCollector collector) : DbCommandInterceptor
    {
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (fault.Phase == "entry-after-keyread" && fault.CallerEntry is null
                && eventData.Context is IdentityDbContext db && db.Database.CurrentTransaction is null
                && command.CommandText.Contains("FROM [idempotency].[CommandExecutions]", StringComparison.Ordinal))
            {
                fault.CallerEntry = new(Guid.NewGuid(), "SENTETIK after-keyread caller", null);
                db.Users.Add(fault.CallerEntry);
                collector.Enqueue(AuditOwnerProbe(TestCompanyIds.CompanyA, "SENTETIK-after-keyread-caller"));
            }
            if (fault.Phase != "target-read") return ValueTask.FromResult(result);
            var parameters = command.Parameters.Cast<DbParameter>().Select(x => x.Value).ToArray();
            if (command.CommandText.Contains("FROM [identity].[Users]", StringComparison.Ordinal)
                && parameters.Contains(fault.TargetId))
            { fault.TargetReads++; fault.Stage = "target-read"; }
            if (fault.TargetReads > 0 && command.CommandText.Contains("FROM [identity].[Roles]", StringComparison.Ordinal)
                && parameters.Contains(fault.RoleId)) fault.RoleReadsAfterTarget++;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class GrantApprovalTransaction(GrantApprovalFault fault) : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        { if (fault.Phase is not null) fault.Stage = "committed"; return Task.CompletedTask; }
        public override ValueTask<InterceptionResult> TransactionRollingBackAsync(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (fault.Phase == "rollback-fault" && fault.CleanupFaults++ == 0)
                throw new InvalidOperationException("SENTETIK rollback failure; must not reconcile.");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class GrantApprovalAudit(IAuditTransactionWriterV1 inner, GrantApprovalFault fault) : IAuditTransactionWriterV1
    {
        public bool HasPendingDrafts => inner.HasPendingDrafts;
        public async Task WritePendingAsync(IDbTransaction transaction, CancellationToken cancellationToken = default)
        {
            if (fault.Phase == "sql-completed" && fault.Injections == 0)
            {
                // Real provider transaction is completed before controlled 1205; this is not a real deadlock reproduction.
                await ((DbTransaction)transaction).RollbackAsync(CancellationToken.None);
                fault.ThrowSql();
            }
            if (fault.Phase == "sql-audit" && fault.Injections == 0) fault.ThrowSql();
            await inner.WritePendingAsync(transaction, cancellationToken);
            if (fault.Phase is not null)
            {
                fault.Stage = "audit";
                if (fault.Phase == "discard-fault") { fault.Fired = true; throw new InvalidOperationException("SENTETIK after Audit append."); }
            }
        }
        public void DiscardPending()
        {
            inner.DiscardPending();
            if (fault.Phase is null) return;
            fault.Discards++; fault.WriterPending = inner.HasPendingDrafts;
            if (fault.Phase == "discard-fault") { fault.CleanupFaults++; throw new InvalidOperationException("SENTETIK discard failure."); }
        }
    }

    private sealed class GrantApprovalSecurity(ISecurityEventWriterV1 inner, GrantApprovalFault fault) : ISecurityEventWriterV1
    {
        public async Task<SecurityEventWriteResultV1> WriteAsync(SecurityEventV1 securityEvent, CancellationToken cancellationToken = default)
        {
            var result = await inner.WriteAsync(securityEvent, cancellationToken);
            if (fault.Phase is not null) fault.Stage = "security";
            return result;
        }
    }

    private sealed class GrantApprovalAuthority(IErpAuthorityRevalidationService inner, GrantApprovalFault fault, IdentityDbContext database) : IErpAuthorityRevalidationService
    {
        public async Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        {
            var scope = await inner.RevalidatePermissionScopeAsync(permission, companyId, cancellationToken);
            if (permission != IdentityPermissionManifestV1.AccessApprove || fault.Phase is null) return scope;
            fault.ActualFreshReads++;
            fault.Context = database;
            if (fault.Injections != 0 && fault.Context.Database.CurrentTransaction is null && fault.AfterAbortReads == 0)
            {
                fault.AfterAbortReads++;
                if (fault.AfterAbort is { } winner) { fault.AfterAbort = null; await winner(); }
                return scope;
            }
            if (fault.Stage == "security") fault.SecurityReads++;
            if (fault.Stage == "audit") fault.AuditReads++;
            var hit = fault.Phase switch
            {
                "before-save" => fault.Context?.ChangeTracker.HasChanges() == true && fault.Stage == "none",
                "after-save" or "rollback-fault" => fault.Stage == "saved",
                "after-audit" => fault.Stage == "audit",
                "after-security" => fault.Stage == "security",
                "precommit" => fault.Approve ? fault.SecurityReads == 2 : fault.AuditReads == 2,
                "postcommit" => fault.Stage == "committed",
                "target-read" => fault.Stage == "target-read",
                _ => false,
            };
            if (!hit) return scope;
            fault.Fired = true; fault.FiredPhase = fault.Phase;
            return PermissionScope.Denied;
        }
    }

    // Test-only outer observer. The inner service is the actual module's guarded alias; this probe never implements the decision.
    private sealed class GrantApprovalEntryProbe(IIdentityAccessAssignmentService inner, IdentityDbContext db,
        IAuditEventCollector collector, IAuditTransactionWriterV1 writer, GrantApprovalFault fault) : IIdentityAccessAssignmentService
    {
        public async Task<IdentityAccessCommandResultV1> ApproveAsync(Guid id, ApproveAccessGrantRequestV1 request,
            IdentityAccessCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
        {
            if (fault.Phase?.StartsWith("entry-", StringComparison.Ordinal) is not true)
                return await inner.ApproveAsync(id, request, metadata, cancellationToken);
            var caller = new GoymenERP.Modules.Identity.Domain.Users.UserAccount(Guid.NewGuid(), "SENTETIK caller WIP", null);
            await using var borrowed = fault.Phase == "entry-borrowed" ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
            using var ambient = fault.Phase == "entry-ambient" ? new System.Transactions.TransactionScope(
                System.Transactions.TransactionScopeAsyncFlowOption.Enabled) : null;
            if (fault.Phase == "entry-dirty") db.Users.Add(caller);
            if (fault.Phase == "entry-audit") collector.Enqueue(AuditOwnerProbe(metadata.CompanyId, "SENTETIK-caller-audit"));
            var result = await inner.ApproveAsync(id, request, metadata, cancellationToken);
            fault.CallerPreserved = fault.Phase switch
            {
                "entry-borrowed" => ReferenceEquals(borrowed, db.Database.CurrentTransaction),
                "entry-ambient" => System.Transactions.Transaction.Current is not null,
                "entry-dirty" => db.Entry(caller).State == EntityState.Added,
                "entry-audit" => writer.HasPendingDrafts,
                "entry-after-keyread" => fault.CallerEntry is { } entry && db.Entry(entry).State == EntityState.Added && writer.HasPendingDrafts,
                _ => false,
            };
            if (borrowed is not null) await borrowed.RollbackAsync(CancellationToken.None);
            return result;
        }
        public Task<IdentityAccessCommandResultV1> RejectAsync(Guid id, RejectAccessGrantRequestV1 request, IdentityAccessCommandMetadataV1 metadata, CancellationToken cancellationToken = default) => inner.RejectAsync(id, request, metadata, cancellationToken);
        public Task<IdentityAccessCommandResultV1> AssignAsync(AssignIdentityRoleRequestV1 request, IdentityAccessCommandMetadataV1 metadata, CancellationToken cancellationToken = default) => inner.AssignAsync(request, metadata, cancellationToken);
        public Task<IdentityAccessCommandResultV1> RevokeAsync(Guid id, RevokeIdentityRoleAssignmentRequestV1 request, IdentityAccessCommandMetadataV1 metadata, CancellationToken cancellationToken = default) => inner.RevokeAsync(id, request, metadata, cancellationToken);
        public Task<IdentityAccessCommandResultV1> CreateGrantRequestAsync(CreateAccessGrantRequestRequestV1 request, IdentityAccessCommandMetadataV1 metadata, CancellationToken cancellationToken = default) => inner.CreateGrantRequestAsync(request, metadata, cancellationToken);
        public Task<IdentityAccessCommandResultV1> WithdrawAsync(Guid id, WithdrawAccessGrantRequestV1 request, IdentityAccessCommandMetadataV1 metadata, CancellationToken cancellationToken = default) => inner.WithdrawAsync(id, request, metadata, cancellationToken);
        public Task<IReadOnlyList<IdentityAccessGrantRequestView>?> GetPendingGrantRequestsAsync(CancellationToken cancellationToken = default) => inner.GetPendingGrantRequestsAsync(cancellationToken);
    }
}
