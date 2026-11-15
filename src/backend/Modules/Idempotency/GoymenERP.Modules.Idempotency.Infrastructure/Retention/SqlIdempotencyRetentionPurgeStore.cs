using System.Data;
using GoymenERP.Modules.Idempotency.Domain.Retention;
using Microsoft.Data.SqlClient;

namespace GoymenERP.Modules.Idempotency.Infrastructure.Retention;

/// <summary>
/// The only delete path of <c>idempotency.CommandExecutions</c>. Each batch runs in one transaction: it publishes the
/// cutoff in <c>SESSION_CONTEXT</c> (the table trigger refuses every delete without it, and every delete of a record
/// that completed at or after it), deletes one company's expired records through the existing
/// (CompanyId, CompletedAtUtc, Id) index, writes the append-only evidence row with the real count and clears the
/// context before commit. All values are parameters; no caller text reaches the SQL.
/// </summary>
public sealed class SqlIdempotencyRetentionPurgeStore(string connectionString, TimeSpan commandTimeout)
    : IIdempotencyRetentionPurgeStore
{
    public const string CutoffSessionKey = "goymen.idempotency.retention_cutoff_utc";

    private const string ListCompaniesSql = """
        SELECT DISTINCT [CompanyId]
        FROM [idempotency].[CommandExecutions]
        WHERE [CompletedAtUtc] < @cutoff
        ORDER BY [CompanyId];
        """;

    private const string PurgeBatchSql = """
        SET NOCOUNT ON;
        EXEC sys.sp_set_session_context @key = @sessionKey, @value = @cutoff;
        DELETE TOP (@batchSize) FROM [idempotency].[CommandExecutions]
        WHERE [CompanyId] = @companyId AND [CompletedAtUtc] < @cutoff;
        DECLARE @deleted int = @@ROWCOUNT;
        EXEC sys.sp_set_session_context @key = @sessionKey, @value = NULL;
        IF @deleted > 0
        BEGIN
            INSERT INTO [idempotency].[RetentionPurgeRecords]
                ([Id], [RunId], [CompanyId], [CutoffUtc], [RetentionPeriod], [PolicyRef], [DeletedCount], [RecordedAtUtc])
            VALUES
                (@id, @runId, @companyId, @cutoff, @retentionPeriod, @policyRef, @deleted, @recordedAtUtc);
        END;
        SELECT @deleted;
        """;

    private readonly int commandTimeoutSeconds = ToSeconds(commandTimeout);

    public async Task<IReadOnlyList<Guid>> ListCompaniesWithExpiredRecordsAsync(
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = ListCompaniesSql;
        command.CommandTimeout = commandTimeoutSeconds;
        command.Parameters.Add(new SqlParameter("@cutoff", SqlDbType.DateTimeOffset) { Value = cutoffUtc.ToUniversalTime() });

        var companies = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            companies.Add(reader.GetGuid(0));
        }

        return companies;
    }

    public async Task<int> PurgeBatchAsync(
        IdempotencyRetentionPurgeBatch evidence,
        int batchSize,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        var cutoff = evidence.CutoffUtc.ToUniversalTime();
        var recordedAt = evidence.RecordedAtUtc.ToUniversalTime();
        // Validates the evidence shape before anything is deleted; the real count is decided below by the database.
        var probe = IdempotencyRetentionPurgeRecord.Record(
            Guid.NewGuid(), evidence.RunId, evidence.CompanyId, cutoff, evidence.Period, evidence.PolicyRef, 1, recordedAt);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = PurgeBatchSql;
        command.CommandTimeout = commandTimeoutSeconds;
        command.Parameters.Add(new SqlParameter("@sessionKey", SqlDbType.NVarChar, 128) { Value = CutoffSessionKey });
        command.Parameters.Add(new SqlParameter("@cutoff", SqlDbType.DateTimeOffset) { Value = probe.CutoffUtc });
        command.Parameters.Add(new SqlParameter("@batchSize", SqlDbType.Int) { Value = batchSize });
        command.Parameters.Add(new SqlParameter("@companyId", SqlDbType.UniqueIdentifier) { Value = probe.CompanyId });
        command.Parameters.Add(new SqlParameter("@id", SqlDbType.UniqueIdentifier) { Value = probe.Id });
        command.Parameters.Add(new SqlParameter("@runId", SqlDbType.UniqueIdentifier) { Value = probe.RunId });
        command.Parameters.Add(new SqlParameter("@retentionPeriod", SqlDbType.VarChar, IdempotencyRetentionPeriod.MaxTextLength)
        {
            Value = probe.RetentionPeriod,
        });
        command.Parameters.Add(new SqlParameter("@policyRef", SqlDbType.NVarChar, IdempotencyRetentionPurgeRecord.PolicyRefMaxLength)
        {
            Value = probe.PolicyRef,
        });
        command.Parameters.Add(new SqlParameter("@recordedAtUtc", SqlDbType.DateTimeOffset) { Value = probe.RecordedAtUtc });

        // A failure (trigger refusal, timeout, cancellation) leaves the transaction uncommitted; disposing it rolls the
        // whole batch back, so a delete never survives without its evidence row.
        var deleted = Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);
        await transaction.CommitAsync(cancellationToken);
        return deleted;
    }

    private static int ToSeconds(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Komut zaman aşımı pozitif olmalıdır.");
        }

        var seconds = Math.Ceiling(timeout.TotalSeconds);
        return seconds >= int.MaxValue ? int.MaxValue : (int)seconds;
    }
}
