using System.Data.Common;
using System.Net;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class IdentityAccessAssignmentSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Known_SQL_read_and_save_failures_roll_back_access_changes_and_allow_the_same_key_to_retry()
    {
        foreach (var mode in new[] { AccessFailureMode.InitialRead, AccessFailureMode.PrimaryRead, AccessFailureMode.Save })
        {
            await using var fault = new AccessSqlFault();
            var admin = SyntheticCode("ADMIN");
            await WithAccessFaultDatabaseAsync(admin, fault, async factory =>
            {
                fault.ConnectionString = factory.DatabaseConnectionString;
                var managerRole = await CreateRoleAsync(factory, admin, IdentityPermissionManifestV1.RolesManage);
                var manager = await EnsureUserAsync(factory, TestUsers.NoAssignment);
                await AssignDirectlyAsync(factory, manager, managerRole, TestCompanyIds.CompanyA);
                var target = await EnsureUserAsync(factory, TestUsers.CompanyA);
                var role = await CreateRoleAsync(factory, SyntheticCode("OPERATOR"), ErpPermissions.OrganizationLocationsStatusUpdate);
                using var client = CreateClient(factory, TestUsers.NoAssignment, TestCompanyIds.CompanyA);
                var request = new AssignIdentityRoleRequestV1(target, role, null, null, "Sentetik SQL hatası");
                var key = NewKey(); var failedCorrelation = NewKey(); var retryCorrelation = NewKey();
                fault.Arm(mode);
                using (var failed = await PostAsync(client, AssignmentsRoute, request, key, failedCorrelation))
                    Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
                Assert.Equal(1222, fault.SqlErrorNumber);
                Assert.Equal(1, fault.Injections);
                await AssertNoAccessFailureEffectsAsync(factory, target, role, failedCorrelation);
                using (var retry = await PostAsync(client, AssignmentsRoute, request, key, retryCorrelation))
                {
                    Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
                    Assert.False(retry.Headers.Contains(ReplayedHeader));
                }
                using (var replay = await PostAsync(client, AssignmentsRoute, request, key, NewKey()))
                {
                    Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
                    Assert.Equal("true", Assert.Single(replay.Headers.GetValues(ReplayedHeader)));
                    Assert.Equal(retryCorrelation, Assert.Single(replay.Headers.GetValues(OriginalCorrelationHeader)));
                }
                Assert.Equal(1, await CountAssignmentsAsync(factory, target, role));
            }, services => AddAccessSqlFault(services, fault));
        }
    }

    [SqlServerFact]
    public async Task Permission_revoked_after_a_primary_SQL_failure_is_denied_after_rollback_without_consuming_the_key()
    {
        await using var fault = new AccessSqlFault();
        var admin = SyntheticCode("ADMIN");
        await WithAccessFaultDatabaseAsync(admin, fault, async factory =>
        {
            fault.ConnectionString = factory.DatabaseConnectionString;
            var managerRole = await CreateRoleAsync(factory, admin, IdentityPermissionManifestV1.RolesManage);
            var manager = await EnsureUserAsync(factory, TestUsers.NoAssignment);
            var managerGrant = await AssignDirectlyAsync(factory, manager, managerRole, TestCompanyIds.CompanyA);
            var target = await EnsureUserAsync(factory, TestUsers.CompanyA);
            var role = await CreateRoleAsync(factory, SyntheticCode("OPERATOR"), ErpPermissions.OrganizationLocationsStatusUpdate);
            using var client = CreateClient(factory, TestUsers.NoAssignment, TestCompanyIds.CompanyA);
            var request = new AssignIdentityRoleRequestV1(target, role, null, null, "Sentetik son yetki kontrolü");
            var key = NewKey(); var correlation = NewKey();
            fault.AfterRollback = async () =>
            {
                Assert.NotNull(fault.FailedContext);
                Assert.Null(fault.FailedContext.Database.CurrentTransaction);
                Assert.False(fault.FailedContext.ChangeTracker.HasChanges());
                await using var scope = factory.Services.CreateAsyncScope();
                var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                // Only the synthetic test database's grant; the final authority reader sees a real missing grant.
                await identity.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [identity].[RoleAssignments] WHERE [Id]={managerGrant}");
            };
            fault.Arm(AccessFailureMode.Save);
            using (var failed = await PostAsync(client, AssignmentsRoute, request, key, correlation))
                Assert.Equal(HttpStatusCode.Forbidden, failed.StatusCode);
            Assert.Equal(1222, fault.SqlErrorNumber);
            Assert.Equal(1, fault.FinalAuthorityCallbacks);
            await AssertNoAccessFailureEffectsAsync(factory, target, role, correlation);
            await AssignDirectlyAsync(factory, manager, managerRole, TestCompanyIds.CompanyA);
            using var retry = await PostAsync(client, AssignmentsRoute, request, key, NewKey());
            Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
            Assert.False(retry.Headers.Contains(ReplayedHeader));
        }, services =>
        {
            AddAccessSqlFault(services, fault);
            var registration = services.Last(value => value.ServiceType == typeof(IErpAuthorityRevalidationService));
            services.Remove(registration);
            services.AddScoped<IErpAuthorityRevalidationService>(provider => new AccessFailureAuthority(
                (IErpAuthorityRevalidationService)(registration.ImplementationFactory?.Invoke(provider)
                    ?? ActivatorUtilities.CreateInstance(provider, registration.ImplementationType!)), fault));
        });
    }

    [SqlServerFact]
    public async Task A_SQL_failure_after_commit_or_during_a_replay_audit_is_not_reported_as_an_unstored_unavailable_answer()
    {
        await using var fault = new AccessSqlFault();
        var admin = SyntheticCode("ADMIN");
        await WithAccessFaultDatabaseAsync(admin, fault, async factory =>
        {
            fault.ConnectionString = factory.DatabaseConnectionString;
            var managerRole = await CreateRoleAsync(factory, admin, IdentityPermissionManifestV1.RolesManage);
            var manager = await EnsureUserAsync(factory, TestUsers.NoAssignment);
            await AssignDirectlyAsync(factory, manager, managerRole, TestCompanyIds.CompanyA);
            var target = await EnsureUserAsync(factory, TestUsers.CompanyA);
            var role = await CreateRoleAsync(factory, SyntheticCode("OPERATOR"), ErpPermissions.OrganizationLocationsStatusUpdate);
            using var client = CreateClient(factory, TestUsers.NoAssignment, TestCompanyIds.CompanyA);
            var request = new AssignIdentityRoleRequestV1(target, role, null, null, "Sentetik commit sınırı");
            var key = NewKey(); var correlation = NewKey();
            fault.Arm(AccessFailureMode.AfterCommit);
            using (var failed = await PostAsync(client, AssignmentsRoute, request, key, correlation))
                Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
            Assert.Equal(1222, fault.SqlErrorNumber);
            Assert.Equal(1, await CountAssignmentsAsync(factory, target, role));
            using (var replay = await PostAsync(client, AssignmentsRoute, request, key, NewKey()))
            {
                Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
                Assert.Equal(correlation, Assert.Single(replay.Headers.GetValues(OriginalCorrelationHeader)));
            }
            fault.FailReplayAudit = true;
            // This explicit fault decorator violates the normal recorder's never-throw provider contract.
            // It proves that a pre-existing receipt is never encompassed by the primary failure conversion.
            using (var failed = await PostAsync(client, AssignmentsRoute, request with { Reason = "Sentetik farklı gövde" }, key, NewKey()))
                Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
            fault.FailReplayAudit = false;
            using var unchanged = await PostAsync(client, AssignmentsRoute, request, key, NewKey());
            Assert.Equal(HttpStatusCode.Created, unchanged.StatusCode);
            Assert.Equal(correlation, Assert.Single(unchanged.Headers.GetValues(OriginalCorrelationHeader)));
            Assert.Equal(1, await CountAssignmentsAsync(factory, target, role));
        }, services =>
        {
            AddAccessSqlFault(services, fault);
            var registration = services.Single(value => value.ServiceType == typeof(IRefusedAttemptRecorderV1));
            services.Remove(registration);
            services.AddScoped<IRefusedAttemptRecorderV1>(provider => new AccessFailureRefusedAttempts(
                (IRefusedAttemptRecorderV1)ActivatorUtilities.CreateInstance(provider, registration.ImplementationType!), fault));
        });
    }

    private static Task WithAccessFaultDatabaseAsync(string admin, AccessSqlFault fault,
        Func<SqlServerRelationalApiFactory, Task> test, Action<IServiceCollection> configure) =>
        WithDatabaseAsync(admin, async factory =>
        {
            try { await test(factory); }
            finally { await fault.ReleaseAsync(); } // Release even an unexpected interceptor/assertion failure before DB cleanup.
        }, configure);

    private static void AddAccessSqlFault(IServiceCollection services, AccessSqlFault fault) =>
        services.AddDbContext<IdentityDbContext>((_, options) => options.AddInterceptors(
            new AccessFailureReader(fault), new AccessFailureSave(fault), new AccessFailureCommit(fault)));

    private static async Task AssertNoAccessFailureEffectsAsync(SqlServerRelationalApiFactory factory, Guid target, Guid role, string correlation)
    {
        Assert.Equal(0, await CountAssignmentsAsync(factory, target, role));
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyA);
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.Equal(0, await identity.CommandExecutions.CountAsync(row => row.OriginalCorrelationId == correlation));
        Assert.Equal(0, await identity.OutboxMessages.CountAsync(row => row.CorrelationId == correlation));
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<AuditDbContext>().AuditEvents.CountAsync(row => row.CorrelationId == correlation));
    }

    private enum AccessFailureMode { None, InitialRead, PrimaryRead, Save, AfterCommit }

    private sealed class AccessSqlFault : IAsyncDisposable
    {
        // Captured from the factory before SqlClient opens any connection or strips SQL-auth credentials.
        public string ConnectionString { private get; set; } = string.Empty;
        public AccessFailureMode Mode { get; private set; }
        private SqlConnection? blocker;
        private SqlTransaction? blockerTransaction;
        public int Injections { get; private set; }
        public int? SqlErrorNumber { get; private set; }
        public SqlException? CapturedSqlError { get; private set; }
        public DbContext? FailedContext { get; set; }
        public Func<Task>? AfterRollback { get; set; }
        public int FinalAuthorityCallbacks { get; set; }
        public bool FailReplayAudit { get; set; }
        public void Arm(AccessFailureMode mode) => Mode = mode;

        public async Task BlockAsync(DbConnection connection, DbTransaction? transaction, CancellationToken ct)
        {
            Mode = AccessFailureMode.None;
            Injections++;
            // AssignAsync has ManagedUserId=null and owns READ COMMITTED: its completed SELECTs
            // retain no receipt data locks when Sch-M is acquired. This fixture does not cover the
            // managed-user SERIALIZABLE path, whose retained locks require a different fault surface.
            if (transaction is not null) Assert.Equal(System.Data.IsolationLevel.ReadCommitted, transaction.IsolationLevel);
            try
            {
                blocker = new SqlConnection(new SqlConnectionStringBuilder(ConnectionString) { Pooling = false }.ConnectionString);
                await blocker.OpenAsync(ct);
                blockerTransaction = (SqlTransaction)await blocker.BeginTransactionAsync(ct);
                await using (var schema = new SqlCommand(
                    "ALTER TABLE [idempotency].[CommandExecutions] ADD CONSTRAINT [CK_SENTETIK_IdentityReceiptLock] CHECK (1=1);",
                    blocker, blockerTransaction) { CommandTimeout = 5 })
                    await schema.ExecuteNonQueryAsync(ct);
                await using var timeout = connection.CreateCommand();
                timeout.Transaction = transaction;
                timeout.CommandText = "SET LOCK_TIMEOUT 200;";
                timeout.CommandTimeout = 5;
                await timeout.ExecuteNonQueryAsync(ct);
            }
            catch
            {
                await ReleaseAsync();
                throw;
            }
        }

        public async Task ObserveFailureAsync(DbConnection connection, DbTransaction? transaction, Exception error)
        {
            CapturedSqlError = error.GetBaseException() as SqlException;
            SqlErrorNumber = CapturedSqlError?.Number;
            await ReleaseAsync();
            await using var timeout = connection.CreateCommand();
            timeout.Transaction = transaction;
            timeout.CommandText = "SET LOCK_TIMEOUT -1;";
            await timeout.ExecuteNonQueryAsync(CancellationToken.None);
        }

        public async Task ReleaseAsync()
        {
            try
            {
                if (blockerTransaction is not null)
                {
                    try { await blockerTransaction.RollbackAsync(CancellationToken.None); }
                    finally { await blockerTransaction.DisposeAsync(); blockerTransaction = null; }
                }
            }
            finally { if (blocker is not null) { await blocker.DisposeAsync(); blocker = null; } }
        }
        public ValueTask DisposeAsync() => new(ReleaseAsync());
    }

    private sealed class AccessFailureReader(AccessSqlFault fault) : DbCommandInterceptor
    {
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM [idempotency].[CommandExecutions]", StringComparison.Ordinal)
                && (fault.Mode == AccessFailureMode.InitialRead
                    || fault.Mode == AccessFailureMode.PrimaryRead && eventData.Context!.Database.CurrentTransaction is not null))
            {
                fault.FailedContext = eventData.Context;
                await fault.BlockAsync(command.Connection!, command.Transaction, cancellationToken);
            }
            return result;
        }
        public override async Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            if (fault.Injections > 0 && fault.SqlErrorNumber is null)
                await fault.ObserveFailureAsync(command.Connection!, command.Transaction, eventData.Exception);
        }
    }

    private sealed class AccessFailureSave(AccessSqlFault fault) : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (fault.Mode == AccessFailureMode.Save)
            {
                fault.FailedContext = eventData.Context;
                await fault.BlockAsync(eventData.Context!.Database.GetDbConnection(),
                    eventData.Context.Database.CurrentTransaction!.GetDbTransaction(), cancellationToken);
            }
            return result;
        }
        public override async Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            if (fault.Injections > 0 && fault.SqlErrorNumber is null)
                await fault.ObserveFailureAsync(eventData.Context!.Database.GetDbConnection(),
                    eventData.Context.Database.CurrentTransaction?.GetDbTransaction(), eventData.Exception);
        }
    }

    private sealed class AccessFailureCommit(AccessSqlFault fault) : DbTransactionInterceptor
    {
        public override async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (fault.Mode != AccessFailureMode.AfterCommit) return;
            var connection = eventData.Context!.Database.GetDbConnection();
            await fault.BlockAsync(connection, null, cancellationToken);
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM [idempotency].[CommandExecutions];";
                await command.ExecuteScalarAsync(cancellationToken);
            }
            catch (SqlException error)
            {
                await fault.ObserveFailureAsync(connection, null, error);
                throw;
            }
            finally { await fault.ReleaseAsync(); }
        }
    }

    private sealed class AccessFailureAuthority(IErpAuthorityRevalidationService inner, AccessSqlFault fault) : IErpAuthorityRevalidationService
    {
        public async Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default)
        {
            if (fault.SqlErrorNumber is not null && fault.AfterRollback is { } revoke)
            {
                fault.AfterRollback = null;
                fault.FinalAuthorityCallbacks++;
                await revoke();
            }
            return await inner.RevalidatePermissionScopeAsync(permission, companyId, cancellationToken);
        }
    }

    private sealed class AccessFailureRefusedAttempts(IRefusedAttemptRecorderV1 inner, AccessSqlFault fault) : IRefusedAttemptRecorderV1
    {
        public void Record(RefusedAttemptV1 attempt) => inner.Record(attempt);
        public void NoteReplayedRefusal(string refusalClass) => inner.NoteReplayedRefusal(refusalClass);
        public Task<RefusedAttemptRecordResultV1> RecordStandaloneAsync(RefusedAttemptV1 attempt, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (fault.FailReplayAudit) throw (Exception?)fault.CapturedSqlError ?? new InvalidOperationException("SENTETIK SQL failure was not observed.");
            return inner.RecordStandaloneAsync(attempt, cancellationToken);
        }
    }
}
