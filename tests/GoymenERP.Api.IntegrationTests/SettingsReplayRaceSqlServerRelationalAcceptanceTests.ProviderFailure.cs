using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SettingsReplayRaceSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Reference_list_SQL_lock_timeout_is_unavailable_without_consuming_the_key_or_leaking_audit()
    {
        await using var run = await Run.StartAsync();
        await using (var seed = run.Request())
        {
            var result = await seed.ReferenceLists.CreateAsync(SettingsReferenceListKindV1.ExpenseType,
                new("TAKEN", "SENTETIK existing", "SENTETIK seed"), Metadata("SENTETIK-seed", "SENTETIK-seed"));
            Assert.Equal(204, result.Answer.StatusCode);
        }

        // Exercise both the change+audit save and the refusal-only save. The blocker is released before
        // the failed save returns: the old broad catch would therefore permanently store a false conflict.
        foreach (var refusal in new[] { false, true })
        {
            var key = "SENTETIK-timeout-" + refusal;
            var failedCorrelation = key + "-failed";
            var request = new CreateSettingsReferenceListEntryRequestV1(refusal ? "TAKEN" : "NEW",
                "SENTETIK lock timeout", "SENTETIK transient failure");
            await using var fault = new LockTimeoutOnSave(run.Connection);
            await using (var failed = run.Request(saveFault: fault))
            {
                var error = await Assert.ThrowsAsync<SettingsPrerequisiteException>(() =>
                    failed.ReferenceLists.CreateAsync(SettingsReferenceListKindV1.ExpenseType,
                        request, Metadata(key, failedCorrelation)));
                Assert.Equal(SettingsReferenceListProblemCodesV1.ProviderUnavailable, error.Code);
            }
            Assert.Equal(1, fault.SaveAttempts);
            Assert.Equal(1222, fault.SqlErrorNumber);
            Assert.Equal(0, await run.CountAsync("SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [OriginalCorrelationId]=@correlation", failedCorrelation));
            Assert.Equal(0, await run.CountAsync("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [CorrelationId]=@correlation", failedCorrelation));
            Assert.Equal(refusal ? 2 : 1, await run.CountAsync("SELECT COUNT(*) FROM [settings].[ReferenceListEntries] WHERE [CompanyId]=@company", failedCorrelation));

            var retryCorrelation = key + "-retry";
            await using (var retry = run.Request())
            {
                var result = await retry.ReferenceLists.CreateAsync(SettingsReferenceListKindV1.ExpenseType,
                    request, Metadata(key, retryCorrelation));
                Assert.False(result.IsReplay);
                Assert.Equal(refusal ? 409 : 204, result.Answer.StatusCode);
                Assert.Equal(refusal ? SettingsReferenceListProblemCodesV1.CodeConflict : null, result.Answer.Code);
            }
            await using (var replay = run.Request())
            {
                var result = await replay.ReferenceLists.CreateAsync(SettingsReferenceListKindV1.ExpenseType,
                    request, Metadata(key, key + "-replay"));
                Assert.True(result.IsReplay);
                Assert.Equal(retryCorrelation, result.OriginalCorrelationId);
            }
            Assert.Equal(1, await run.CountAsync("SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [OriginalCorrelationId]=@correlation", retryCorrelation));
            Assert.Equal(refusal ? 0 : 1, await run.CountAsync("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [CorrelationId]=@correlation", retryCorrelation));
        }
    }

    private sealed class LockTimeoutOnSave(string connectionString) : SaveChangesInterceptor, IAsyncDisposable
    {
        private SqlConnection? blocker;
        private SqlTransaction? transaction;
        public int SaveAttempts { get; private set; }
        public int? SqlErrorNumber { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Assert.Equal(1, ++SaveAttempts);
            var context = eventData.Context!;
            await context.Database.OpenConnectionAsync(cancellationToken);
            await context.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT 200;", cancellationToken);
            blocker = new SqlConnection(connectionString);
            await blocker.OpenAsync(cancellationToken);
            transaction = (SqlTransaction)await blocker.BeginTransactionAsync(cancellationToken);
            await using var command = new SqlCommand(
                "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WITH (TABLOCKX,HOLDLOCK);", blocker, transaction);
            await command.ExecuteScalarAsync(cancellationToken);
            return result;
        }

        public override async Task SaveChangesFailedAsync(DbContextErrorEventData eventData,
            CancellationToken cancellationToken = default)
        {
            SqlErrorNumber = (eventData.Exception.GetBaseException() as SqlException)?.Number;
            await ReleaseAsync();
            var context = eventData.Context!;
            Assert.Null(context.Database.CurrentTransaction);
            await using var transactionProbe = context.Database.GetDbConnection().CreateCommand();
            transactionProbe.CommandText = "SELECT @@TRANCOUNT;";
            Assert.Equal(0, (int)(await transactionProbe.ExecuteScalarAsync(CancellationToken.None))!);
            await context.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT -1;", CancellationToken.None);
        }

        private async Task ReleaseAsync()
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                await transaction.DisposeAsync();
                transaction = null;
            }
            if (blocker is not null) { await blocker.DisposeAsync(); blocker = null; }
        }

        public ValueTask DisposeAsync() => new(ReleaseAsync());
    }
}
