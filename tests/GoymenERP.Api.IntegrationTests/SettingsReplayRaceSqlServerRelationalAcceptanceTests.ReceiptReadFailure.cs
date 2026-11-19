using System.Data.Common;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SettingsReplayRaceSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Bank_csv_receipt_read_SQL_failure_is_unavailable_without_consuming_the_retry_key()
    {
        await using var run = await Run.StartAsync();
        const string key = "SENTETIK-bank-receipt-read-timeout";
        const string failedCorrelation = key + "-failed";
        const string retryCorrelation = key + "-retry";
        var request = new RecordBankCsvMappingRequestV1("0", "SENTETIK-RECEIPT-BANK", "SENTETIK Banka",
            BankCsvMappingStatesV1.Active, BankCsvDelimitersV1.Semicolon, 1, true,
            BankCsvDecimalSeparatorsV1.Comma, BankCsvThousandsSeparatorsV1.Period, "dd.MM.yyyy",
            BankCsvAmountLayoutsV1.SignedAmount, BankCsvSignedAmountDirectionsV1.Inflow,
            1, 2, 3, 4, null, null, 5, null, null, "SENTETIK receipt read recovery");
        await using (var fault = await ReceiptReadSchemaLock.StartAsync(run.Connection))
        {
            await using var failed = run.Request(readFault: fault);
            var error = await Assert.ThrowsAsync<SettingsPrerequisiteException>(() =>
                failed.BankCsvMappings.RecordAsync(request, Metadata(key, failedCorrelation)));
            Assert.Equal(BankCsvMappingProblemCodesV1.ProviderUnavailable, error.Code);
            Assert.Equal(1, fault.ReceiptReadAttempts);
            Assert.Equal(1222, fault.SqlErrorNumber);
        }
        Assert.Equal(0, await run.CountAsync("SELECT COUNT(*) FROM [settings].[BankCsvMappingVersions] WHERE [CompanyId]=@company", failedCorrelation));
        Assert.Equal(0, await run.CountAsync("SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [OriginalCorrelationId]=@correlation", failedCorrelation));
        Assert.Equal(0, await run.CountAsync("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [CorrelationId]=@correlation", failedCorrelation));
        await using (var retry = run.Request())
        {
            var answer = await retry.BankCsvMappings.RecordAsync(request, Metadata(key, retryCorrelation));
            Assert.Equal(204, answer.Answer.StatusCode);
            Assert.False(answer.IsReplay);
            Assert.Equal(retryCorrelation, answer.OriginalCorrelationId);
        }
        await using (var replay = run.Request())
        {
            var answer = await replay.BankCsvMappings.RecordAsync(request, Metadata(key, key + "-replay"));
            Assert.Equal(204, answer.Answer.StatusCode);
            Assert.True(answer.IsReplay);
            Assert.Equal(retryCorrelation, answer.OriginalCorrelationId);
        }
        Assert.Equal(1, await run.CountAsync("SELECT COUNT(*) FROM [settings].[BankCsvMappingVersions] WHERE [CompanyId]=@company", retryCorrelation));
        Assert.Equal(1, await run.CountAsync("SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [OriginalCorrelationId]=@correlation", retryCorrelation));
        Assert.Equal(1, await run.CountAsync("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [CorrelationId]=@correlation", retryCorrelation));
    }

    // RCSI does not bypass schema locks. The disposable CHECK is rolled back with the test-owned transaction;
    // unlike TABLOCKX alone, its Sch-M lock deterministically blocks the receipt SELECT's Sch-S lock.
    private sealed class ReceiptReadSchemaLock : DbCommandInterceptor, IAsyncDisposable
    {
        private readonly SqlConnection blocker;
        private SqlTransaction? transaction;
        private bool armed = true;
        public int ReceiptReadAttempts { get; private set; }
        public int? SqlErrorNumber { get; private set; }

        private ReceiptReadSchemaLock(SqlConnection blocker, SqlTransaction transaction)
        { this.blocker = blocker; this.transaction = transaction; }

        public static async Task<ReceiptReadSchemaLock> StartAsync(string connectionString)
        {
            var blocker = new SqlConnection(connectionString);
            await blocker.OpenAsync();
            var transaction = (SqlTransaction)await blocker.BeginTransactionAsync();
            try
            {
                await using var command = new SqlCommand(
                    "ALTER TABLE [idempotency].[CommandExecutions] ADD CONSTRAINT [CK_SENTETIK_ReceiptReadLock] CHECK (1=1);",
                    blocker, transaction);
                await command.ExecuteNonQueryAsync();
                return new ReceiptReadSchemaLock(blocker, transaction);
            }
            catch
            {
                await transaction.RollbackAsync();
                await transaction.DisposeAsync();
                await blocker.DisposeAsync();
                throw;
            }
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (armed && command.CommandText.Contains("FROM [idempotency].[CommandExecutions]", StringComparison.Ordinal))
            {
                armed = false;
                ReceiptReadAttempts++;
                await using var timeout = command.Connection!.CreateCommand();
                timeout.Transaction = command.Transaction;
                timeout.CommandText = "SET LOCK_TIMEOUT 200;";
                await timeout.ExecuteNonQueryAsync(cancellationToken);
            }
            return result;
        }

        public override async Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (ReceiptReadAttempts == 1 && command.CommandText.Contains("FROM [idempotency].[CommandExecutions]", StringComparison.Ordinal))
            {
                SqlErrorNumber = (eventData.Exception.GetBaseException() as SqlException)?.Number;
                await ReleaseAsync();
                await using var timeout = command.Connection!.CreateCommand();
                timeout.Transaction = command.Transaction;
                timeout.CommandText = "SET LOCK_TIMEOUT -1;";
                await timeout.ExecuteNonQueryAsync(CancellationToken.None);
            }
        }

        private async Task ReleaseAsync()
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                await transaction.DisposeAsync();
                transaction = null;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await ReleaseAsync();
            await blocker.DisposeAsync();
        }
    }
}
