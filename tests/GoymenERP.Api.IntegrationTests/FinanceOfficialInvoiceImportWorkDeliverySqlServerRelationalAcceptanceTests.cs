using System.Text;
using System.Security.Cryptography;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit.Abstractions;

namespace GoymenERP.Api.IntegrationTests;

public sealed class FinanceOfficialInvoiceImportWorkDeliverySqlServerRelationalAcceptanceTests(
    ITestOutputHelper output)
{
    private const string LegacyStartPhase = "finance_work_legacy_start";
    private const string LegacyPreviewPhase = "finance_work_legacy_preview_validation";
    private const string LegacySerializationPhase = "finance_work_legacy_serialization";
    private const string LegacyBatchInsertPhase = "finance_work_legacy_batch_insert";
    private const string LegacyBatchIdentityPhase = "finance_work_legacy_batch_identity";
    private const string LegacyBatchContentPhase = "finance_work_legacy_batch_content";
    private const string LegacyBatchCompanyForeignKeyPhase = "finance_work_legacy_batch_company_fk";
    private const string LegacyBatchDuplicatePhase = "finance_work_legacy_batch_duplicate";
    private const string LegacyBatchNullPhase = "finance_work_legacy_batch_null";
    private const string LegacyBatchTruncatedPhase = "finance_work_legacy_batch_truncated";
    private const string LegacyBatchTablePhase = "finance_work_legacy_batch_table";
    private const string LegacyBatchColumnPhase = "finance_work_legacy_batch_column";
    private const string LegacyBatchOtherSqlPhase = "finance_work_legacy_batch_other_sql";
    private const string LegacyCommandInsertPhase = "finance_work_legacy_command_insert";
    private const string LegacyUpPhase = "finance_work_legacy_up";
    private const string LegacyReplayPhase = "finance_work_legacy_replay";
    private const string LegacyCountsPhase = "finance_work_legacy_counts";
    private const string LegacyDownUpPhase = "finance_work_legacy_down_up";
    private static readonly Guid Company = TestCompanyIds.CompanyA;
    private static readonly Guid Batch = Guid.Parse("91000000-0000-0000-0000-000000000001");

    [SqlServerFact]
    public async Task Upload_and_approval_deliveries_are_atomic_coherent_and_replay_stable()
    {
        await using var run = await Run.StartAsync();
        Guid uploadDelivery;
        await using (var maker = run.Service("SENTETIK-maker"))
        {
            var first = await maker.Service.UploadAsync(maker.Context, new CommandMetadata("upload-key"), Request(), default);
            var replay = await maker.Service.UploadAsync(maker.Context, new CommandMetadata("upload-key"), Request(), default);
            Assert.Equal(CommandOutcome.Succeeded, first.Outcome);
            Assert.True(replay.IsReplay);
            uploadDelivery = await run.ScalarAsync<Guid>(
                "SELECT DeliveryId FROM finance.OfficialInvoiceImportWorkDeliveries WHERE BatchId = @id AND ObservationOrdinal = 1",
                ("@id", Batch));
            var observed = await maker.Service.GetAsync(maker.Context, Batch, 1, uploadDelivery, default);
            Assert.Equal(ReadOutcome.Found, observed.Outcome);
            Assert.Equal(FinanceOfficialInvoiceImportWorkStateV1.NeedsAction, observed.Resource!.DesiredState);
            Assert.Equal(Batch, observed.Resource.EvidenceId);
        }

        await using (var checker = run.Service("SENTETIK-checker"))
        {
            var request = new FinanceOfficialInvoiceImportApproveRequestV1(Batch, "1");
            var first = await checker.Service.ApproveAsync(checker.Context, new CommandMetadata("approve-key"), request, default);
            var replay = await checker.Service.ApproveAsync(checker.Context, new CommandMetadata("approve-key"), request, default);
            Assert.Equal(CommandOutcome.Succeeded, first.Outcome);
            Assert.True(replay.IsReplay);
            var delivery = await run.ScalarAsync<Guid>(
                "SELECT DeliveryId FROM finance.OfficialInvoiceImportWorkDeliveries WHERE BatchId = @id AND ObservationOrdinal = 2",
                ("@id", Batch));
            var observed = await checker.Service.GetAsync(checker.Context, Batch, 2, delivery, default);
            Assert.Equal(ReadOutcome.Found, observed.Outcome);
            Assert.Equal(FinanceOfficialInvoiceImportWorkStateV1.Satisfied, observed.Resource!.DesiredState);
            Assert.NotEqual(Batch, observed.Resource.EvidenceId);
        }

        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialInvoiceImportCommands"));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialInvoiceImportWorkDeliveries"));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OfficialInvoiceImportCommands WHERE ProducerSchemaVersion = 1"));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Finance.OfficialInvoiceImportBatch'"));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OfficialInvoiceImportWorkDeliveries WHERE DATALENGTH(PayloadSha256) <> 64 OR PayloadSha256 COLLATE Latin1_General_100_BIN2 LIKE '%[^0-9a-f]%'"));
    }

    [SqlServerFact]
    public async Task Work_delivery_migration_refuses_occupied_down_and_preserves_the_fact()
    {
        await using var run = await Run.StartAsync();
        await using (var maker = run.Service("SENTETIK-maker"))
        {
            Assert.Equal(CommandOutcome.Succeeded,
                (await maker.Service.UploadAsync(maker.Context, new CommandMetadata("guard-key"), Request(), default)).Outcome);
        }

        await using var context = run.Context();
        var error = await Assert.ThrowsAsync<SqlException>(() => context.GetService<IMigrator>()
            .MigrateAsync("20260930185113_AddOfficialInvoiceImportApprovalV1"));

        Assert.Equal(51051, error.Number);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialInvoiceImportWorkDeliveries"));
        Assert.EndsWith("_AddOfficialInvoiceImportWorkDeliveryV1",
            (await context.Database.GetAppliedMigrationsAsync()).Last(), StringComparison.Ordinal);
    }

    [SqlServerFact]
    public async Task Work_delivery_migration_rolls_back_and_reapplies_when_empty()
    {
        await using var run = await Run.StartAsync();
        await using var context = run.Context();
        var migrator = context.GetService<IMigrator>();

        await migrator.MigrateAsync("20260930185113_AddOfficialInvoiceImportApprovalV1");
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'finance') AND name = N'OfficialInvoiceImportWorkDeliveries'"));
        await context.Database.MigrateAsync();
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'finance') AND name = N'OfficialInvoiceImportWorkDeliveries'"));
    }

    [SqlServerFact]
    public async Task Legacy_previous_migration_receipt_replays_after_up_without_fabricating_work_history()
    {
        var failurePhase = LegacyStartPhase;
        var previewIssueCount = -1;
        var sourceRowCount = -1;
        try
        {
            await using var run = await Run.StartAsync("20260930185113_AddOfficialInvoiceImportApprovalV1");
            failurePhase = LegacyPreviewPhase;
            var request = Request();
            var preview = new FinanceOfficialInvoiceImportParserV1().Preview(
                new FinanceContext(Company, "SENTETIK-maker", "legacy-correlation", null), request);
            previewIssueCount = preview.Issues.Count;
            Assert.True(preview.IsValid);
            var batch = Assert.IsType<ApprovedFinanceOpenItemSourceBatch>(preview.Batch);
            sourceRowCount = batch.Rows.Count;

            failurePhase = LegacySerializationPhase;
            var uploadedAt = DateTimeOffset.Parse("2026-10-05T18:00:00+00:00");
            var resource = new FinanceOfficialInvoiceImportBatchViewV1(Batch, Company, batch.BatchVersion,
                batch.BatchChecksum, preview.TemplateVersion, FinanceOfficialInvoiceImportApprovalStatusV1.Pending,
                "1", batch.Rows.Count, "SENTETIK-maker", uploadedAt, null, null);
            var result = new FinanceOfficialInvoiceImportApprovalResultV1(
                CommandOutcome.Succeeded, resource, false, "legacy-correlation", []);
            var sourceRowsJson = JsonSerializer.Serialize(batch.Rows, FinanceJson.CreateOptions());
            var resultJson = JsonSerializer.Serialize(result, FinanceJson.CreateOptions());

            await using var connection = new SqlConnection(run.DatabaseConnectionString);
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            failurePhase = LegacyBatchInsertPhase;
            await using (var batchCommand = new SqlCommand(
                """
                INSERT INTO finance.OfficialInvoiceImportBatches
                    (Id, CompanyId, BatchVersion, BatchChecksum, TemplateVersion, FileBytes, SourceRowsJson, RowCount,
                     UploadedBy, UploadedAtUtc, CorrelationId, CausationId)
                VALUES (@batch, @company, @batchVersion, @checksum, @templateVersion, @bytes, @rows, @rowCount,
                        @actor, @at, @correlation, NULL);
                """, connection, transaction))
            {
                batchCommand.Parameters.AddWithValue("@batch", Batch);
                batchCommand.Parameters.AddWithValue("@company", Company);
                batchCommand.Parameters.AddWithValue("@batchVersion", batch.BatchVersion);
                batchCommand.Parameters.AddWithValue("@checksum", batch.BatchChecksum);
                batchCommand.Parameters.AddWithValue("@templateVersion", preview.TemplateVersion);
                batchCommand.Parameters.AddWithValue("@bytes", request.Utf8Csv.ToArray());
                batchCommand.Parameters.AddWithValue("@rows", sourceRowsJson);
                batchCommand.Parameters.AddWithValue("@rowCount", batch.Rows.Count);
                batchCommand.Parameters.AddWithValue("@actor", "SENTETIK-maker");
                batchCommand.Parameters.AddWithValue("@at", uploadedAt);
                batchCommand.Parameters.AddWithValue("@correlation", "legacy-correlation");
                try
                {
                    await batchCommand.ExecuteNonQueryAsync();
                }
                catch (SqlException exception)
                {
                    failurePhase = LegacyBatchInsertDiagnostic(exception);
                    throw;
                }
            }

            failurePhase = LegacyCommandInsertPhase;
            await using (var commandReceipt = new SqlCommand(
                """
                INSERT INTO finance.OfficialInvoiceImportCommands
                    (Id, CompanyId, CommandName, IdempotencyKeyHash, ActorId, PayloadHash, BatchId, ResultJson,
                     CorrelationId, RecordedAtUtc)
                VALUES (@command, @company, @commandName, @keyHash, @actor, @payloadHash, @batch, @result,
                        @correlation, @at);
                """, connection, transaction))
            {
                commandReceipt.Parameters.AddWithValue("@command", Guid.NewGuid());
                commandReceipt.Parameters.AddWithValue("@company", Company);
                commandReceipt.Parameters.AddWithValue("@commandName", FinanceOfficialInvoiceImportApprovalServiceV1.UploadCommand);
                commandReceipt.Parameters.AddWithValue("@keyHash", FinanceCommandKeys.Hash("legacy-key"));
                commandReceipt.Parameters.AddWithValue("@actor", "SENTETIK-maker");
                commandReceipt.Parameters.AddWithValue("@payloadHash", UploadHash(request));
                commandReceipt.Parameters.AddWithValue("@batch", Batch);
                commandReceipt.Parameters.AddWithValue("@result", resultJson);
                commandReceipt.Parameters.AddWithValue("@correlation", "legacy-correlation");
                commandReceipt.Parameters.AddWithValue("@at", uploadedAt);
                await commandReceipt.ExecuteNonQueryAsync();
            }
            await transaction.CommitAsync();

            failurePhase = LegacyUpPhase;
            await using (var context = run.Context()) await context.Database.MigrateAsync();
            failurePhase = LegacyReplayPhase;
            await using (var maker = run.Service("SENTETIK-maker"))
            {
                var replay = await maker.Service.UploadAsync(maker.Context,
                    new CommandMetadata("legacy-key"), request, default);
                Assert.Equal(CommandOutcome.Succeeded, replay.Outcome);
                Assert.True(replay.IsReplay);
            }
            failurePhase = LegacyCountsPhase;
            Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialInvoiceImportWorkDeliveries"));
            Assert.Equal(1, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM finance.OfficialInvoiceImportCommands WHERE ProducerSchemaVersion IS NULL"));

            failurePhase = LegacyDownUpPhase;
            await using var migrationContext = run.Context();
            await migrationContext.GetService<IMigrator>().MigrateAsync("20260930185113_AddOfficialInvoiceImportApprovalV1");
            await migrationContext.Database.MigrateAsync();
        }
        catch
        {
            output.WriteLine(
                $"CI_SAFE_DIAGNOSTIC:{failurePhase};preview_issues={SafeCount(previewIssueCount)};source_rows={SafeCount(sourceRowCount)}");
            throw;
        }
    }

    [SqlServerFact]
    public async Task New_marker_without_delivery_refuses_replay_and_blocks_destructive_down()
    {
        await using var run = await Run.StartAsync();
        await using (var maker = run.Service("SENTETIK-maker"))
        {
            Assert.Equal(CommandOutcome.Succeeded,
                (await maker.Service.UploadAsync(maker.Context, new CommandMetadata("missing-key"), Request(), default)).Outcome);
        }
        await run.ExecuteAsync("DELETE FROM finance.OfficialInvoiceImportWorkDeliveries WHERE BatchId = @batch",
            ("@batch", Batch));

        await using (var maker = run.Service("SENTETIK-maker"))
        {
            var replay = await maker.Service.UploadAsync(maker.Context,
                new CommandMetadata("missing-key"), Request(), default);
            Assert.Equal(CommandOutcome.Unavailable, replay.Outcome);
        }
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialInvoiceImportCommands"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialInvoiceImportWorkDeliveries"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Finance.OfficialInvoiceImportBatch'"));
        await using var context = run.Context();
        var error = await Assert.ThrowsAsync<SqlException>(() => context.GetService<IMigrator>()
            .MigrateAsync("20260930185113_AddOfficialInvoiceImportApprovalV1"));
        Assert.Equal(51051, error.Number);
    }

    [SqlServerFact]
    public async Task Corrupt_new_delivery_hash_refuses_replay_without_another_effect()
    {
        await using var run = await Run.StartAsync();
        await using (var maker = run.Service("SENTETIK-maker"))
        {
            Assert.Equal(CommandOutcome.Succeeded,
                (await maker.Service.UploadAsync(maker.Context, new CommandMetadata("corrupt-key"), Request(), default)).Outcome);
        }
        await run.ExecuteAsync(
            "UPDATE finance.OfficialInvoiceImportWorkDeliveries SET PayloadSha256 = @hash WHERE BatchId = @batch",
            ("@hash", new string('0', 64)), ("@batch", Batch));

        await using (var maker = run.Service("SENTETIK-maker"))
        {
            var replay = await maker.Service.UploadAsync(maker.Context,
                new CommandMetadata("corrupt-key"), Request(), default);
            Assert.Equal(CommandOutcome.Unavailable, replay.Outcome);
        }
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialInvoiceImportCommands"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialInvoiceImportWorkDeliveries"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Finance.OfficialInvoiceImportBatch'"));
    }

    private static FinanceOfficialInvoiceImportPreviewRequestV1 Request()
    {
        var csv = string.Join('\n', FinanceOfficialInvoiceImportTemplateV1.Header,
            $"{Guid.Parse("92000000-0000-0000-0000-000000000002"):D},{Guid.Parse("93000000-0000-0000-0000-000000000003"):D},{Guid.Parse("94000000-0000-0000-0000-000000000004"):D},invoice-work,{Guid.Parse("95000000-0000-0000-0000-000000000005"):D},{Guid.Parse("96000000-0000-0000-0000-000000000006"):D},{Guid.Parse("97000000-0000-0000-0000-000000000007"):D},1250.50,2026-10-31");
        return new(Batch, "official-file-v1", Encoding.UTF8.GetBytes(csv));
    }

    private sealed class Run : IAsyncDisposable
    {
        private readonly string baseConnectionString;
        private Run(string baseConnectionString, SqlServerRelationalApiFactory factory)
        {
            this.baseConnectionString = baseConnectionString;
            Factory = factory;
        }

        private SqlServerRelationalApiFactory Factory { get; }
        public string DatabaseConnectionString => Factory.DatabaseConnectionString;

        public static async Task<Run> StartAsync(string? targetMigration = null)
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(
                SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(baseConnectionString);
            var run = new Run(baseConnectionString, factory);
            try
            {
                using var client = factory.CreateClient();
                using var health = await client.GetAsync("/health/live");
                health.EnsureSuccessStatusCode();
                await using var context = run.Context();
                if (targetMigration is null) await context.Database.MigrateAsync();
                else await context.GetService<IMigrator>().MigrateAsync(targetMigration);
                return run;
            }
            catch
            {
                try { await run.DisposeAsync(); } catch { }
                throw;
            }
        }

        public FinanceDbContext Context()
        {
            var company = new CompanyContext();
            company.Set(Company);
            return new FinanceDbContext(Options(), company);
        }

        public Scope Service(string actor)
        {
            var company = new CompanyContext();
            company.Set(Company);
            var collector = new AuditEventCollector();
            var options = new DbContextOptionsBuilder<FinanceDbContext>(Options())
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, company)).Options;
            var db = new FinanceDbContext(options, company);
            var service = new FinanceOfficialInvoiceImportApprovalServiceV1(db, company, new User(actor),
                new FinanceOfficialInvoiceImportParserV1(), new Authority(), new ReadAuthority(), collector,
                new FinanceOfficialInvoiceImportAuditEnvelopesV1(
                    new FinanceAuditEnvelope(FinanceOfficialInvoiceImportPermissionNamesV1.Upload, [], "Integration"),
                    new FinanceAuditEnvelope(FinanceOfficialInvoiceImportPermissionNamesV1.Approve, [], "Integration")),
                TimeProvider.System);
            return new Scope(db, service, new FinanceContext(Company, actor, $"work-{Guid.NewGuid():N}", null));
        }

        public async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
            return (T)(await command.ExecuteScalarAsync())!;
        }

        public async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
            await command.ExecuteNonQueryAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Factory.DisposeAsync();
            SqlConnection.ClearAllPools();
            await using var connection = new SqlConnection(baseConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"IF DB_ID(N'{Factory.DatabaseName}') IS NOT NULL BEGIN ALTER DATABASE [{Factory.DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{Factory.DatabaseName}]; END;";
            await command.ExecuteNonQueryAsync();
        }

        private DbContextOptions<FinanceDbContext> Options() => new DbContextOptionsBuilder<FinanceDbContext>()
            .UseSqlServer(Factory.DatabaseConnectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema)).Options;
    }

    private sealed class Scope(FinanceDbContext db, FinanceOfficialInvoiceImportApprovalServiceV1 service,
        FinanceContext context) : IAsyncDisposable
    {
        public FinanceOfficialInvoiceImportApprovalServiceV1 Service => service;
        public FinanceContext Context => context;
        public ValueTask DisposeAsync() => db.DisposeAsync();
    }

    private sealed class User(string actor) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => actor;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { Company };
        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }

    private sealed class Authority : IFinanceOfficialInvoiceImportApprovalAuthorityV1
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context,
            FinanceOfficialInvoiceImportAuthorityActionV1 action, Guid batchId, CancellationToken cancellationToken) =>
            Task.FromResult(PermissionDecision.Allowed);
    }

    private sealed class ReadAuthority : IFinanceOpenItemSourceAuthority
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, Guid batchId,
            CancellationToken cancellationToken) => Task.FromResult(PermissionDecision.Allowed);
    }

    private static string UploadHash(FinanceOfficialInvoiceImportPreviewRequestV1 request)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(request.BatchId.ToByteArray());
        hash.AppendData(Encoding.UTF8.GetBytes(request.BatchVersion));
        hash.AppendData(request.Utf8Csv.Span);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static string LegacyBatchInsertDiagnostic(SqlException exception)
    {
        if (exception.Number == 547)
        {
            if (ContainsExactSqlObject(exception, "CK_OfficialInvoiceImportBatches_Identity"))
            {
                return LegacyBatchIdentityPhase;
            }
            if (ContainsExactSqlObject(exception, "CK_OfficialInvoiceImportBatches_Content"))
            {
                return LegacyBatchContentPhase;
            }
            if (ContainsExactSqlObject(exception, "FK_OfficialInvoiceImportBatches_Companies_CompanyId"))
            {
                return LegacyBatchCompanyForeignKeyPhase;
            }
        }

        return exception.Number switch
        {
            2601 or 2627 => LegacyBatchDuplicatePhase,
            515 => LegacyBatchNullPhase,
            8152 or 2628 => LegacyBatchTruncatedPhase,
            208 => LegacyBatchTablePhase,
            207 => LegacyBatchColumnPhase,
            _ => LegacyBatchOtherSqlPhase,
        };
    }

    private static bool ContainsExactSqlObject(SqlException exception, string objectName) =>
        exception.Errors.Cast<SqlError>().Any(error => error.Message.Contains(objectName, StringComparison.Ordinal));

    private static string SafeCount(int value) => value < 0 ? "not_available" : value.ToString();
}
