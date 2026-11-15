using System.Text;
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

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// FIN-S5 / FNS-D19 (D5) over disposable real SQL: a v2 official invoice file is uploaded, approved by another actor
/// and admitted through <see cref="FinanceOpenItemSourceAdmissionService"/>; its documents claim their official
/// number and ETTN, a second acceptance of an identity whose receivable is not cancelled is refused, and the claim
/// migration refuses its rollback while claims exist. Every row, actor and permission is SENTETIK test data.
/// </summary>
public sealed class FinanceOfficialInvoiceIdentityClaimSqlServerRelationalAcceptanceTests
{
    private const string PreviousMigration = "20261115121907_RequirePartyCreditLimitSecondPerson";
    private static readonly Guid Company = TestCompanyIds.CompanyA;
    private static readonly Guid Party = Guid.Parse("8a000000-0000-4000-8000-000000000001");
    private static readonly Guid Account = Guid.Parse("8a000000-0000-4000-8000-000000000002");
    private static readonly Guid Currency = Guid.Parse("8a000000-0000-4000-8000-000000000003");
    private const string EttnA = "8e000000-0000-4000-8000-00000000000a";
    private const string EttnB = "8e000000-0000-4000-8000-00000000000b";

    [SqlServerFact]
    public async Task Installments_share_one_claim_and_a_second_acceptance_of_an_open_identity_admits_nothing()
    {
        await using var run = await Run.StartAsync();
        var firstBatch = Guid.Parse("81100000-0000-0000-0000-000000000001");
        var firstDocument = Guid.Parse("82100000-0000-0000-0000-000000000001");
        var first = await run.ApproveAsync(firstBatch, Csv(
            Row(Guid.Parse("83100000-0000-0000-0000-000000000001"), firstDocument, 1, "SNT2026000000001", EttnA),
            Row(Guid.Parse("83100000-0000-0000-0000-000000000002"), firstDocument, 2, "SNT2026000000001", EttnA)));

        var admitted = await run.AdmitAsync(first, "admit-first");
        Assert.Equal(CommandOutcome.Succeeded, admitted.Outcome);
        Assert.Equal(2, admitted.OpenItemIds.Count);
        Assert.Equal("ettn:1|official-no:1", await run.ScalarAsync<string>(
            "SELECT STRING_AGG(CONCAT(IdentityKind, ':', Generation), '|') WITHIN GROUP (ORDER BY IdentityKind) "
            + "FROM finance.OfficialInvoiceIdentityClaims WHERE HolderBatchId = @batch", ("@batch", firstBatch)));

        // The same document number on another document of a later file: refused before any open item is written.
        var secondBatch = Guid.Parse("81100000-0000-0000-0000-000000000002");
        var second = await run.ApproveAsync(secondBatch, Csv(
            Row(Guid.Parse("83100000-0000-0000-0000-000000000003"), Guid.Parse("82100000-0000-0000-0000-000000000002"),
                1, "SNT2026000000002", EttnA)));
        var refused = await run.AdmitAsync(second, "admit-second");

        Assert.Equal(CommandOutcome.ValidationFailed, refused.Outcome);
        Assert.Contains(refused.Issues, issue => issue.Field == "rows[0].ettn"
            && issue.Code == FinanceOfficialSalesInvoiceImportCodesV2.EttnAlreadyOpen);
        Assert.DoesNotContain(refused.Issues,
            issue => issue.Code == FinanceOfficialSalesInvoiceImportCodesV2.OfficialNoAlreadyOpen);
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OpenItems WHERE SourceBatchId = @batch", ("@batch", secondBatch)));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OfficialInvoiceIdentityClaims WHERE HolderBatchId = @batch",
            ("@batch", secondBatch)));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialInvoiceIdentityClaims"));

        // A replay of the first admission writes nothing new.
        var replay = await run.AdmitAsync(first, "admit-first");
        Assert.Equal(CommandOutcome.Succeeded, replay.Outcome);
        Assert.True(replay.IsReplay);
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialInvoiceIdentityClaims"));
    }

    [SqlServerFact]
    public async Task A_v1_file_claims_no_identity()
    {
        await using var run = await Run.StartAsync();
        var batch = Guid.Parse("81200000-0000-0000-0000-000000000001");
        var v1 = string.Join(',', Guid.Parse("83200000-0000-0000-0000-000000000001"),
            Guid.Parse("82200000-0000-0000-0000-000000000001"), Guid.Parse("84200000-0000-0000-0000-000000000001"),
            "invoice-v1", Party, Account, Currency, "1200.00", "2026-11-30");
        var approved = await run.ApproveAsync(batch,
            Encoding.UTF8.GetBytes(FinanceOfficialInvoiceImportTemplateV1.Header + "\n" + v1 + "\n"));

        Assert.Equal(CommandOutcome.Succeeded, (await run.AdmitAsync(approved, "admit-v1")).Outcome);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialInvoiceIdentityClaims"));
    }

    [SqlServerFact]
    public async Task The_store_refuses_a_duplicate_generation_and_a_non_canonical_ettn()
    {
        await using var run = await Run.StartAsync();
        var batch = Guid.Parse("81300000-0000-0000-0000-000000000001");
        var approved = await run.ApproveAsync(batch, Csv(
            Row(Guid.Parse("83300000-0000-0000-0000-000000000001"), Guid.Parse("82300000-0000-0000-0000-000000000001"),
                1, "SNT2026000000003", EttnB)));
        Assert.Equal(CommandOutcome.Succeeded, (await run.AdmitAsync(approved, "admit-store")).Outcome);

        var duplicate = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "INSERT INTO finance.OfficialInvoiceIdentityClaims (Id, CompanyId, IdentityKind, IdentityValue, Generation, "
            + "HolderBatchId, HolderSourceDocumentId, ClaimedBy, ClaimedAtUtc, CorrelationId) "
            + "SELECT NEWID(), CompanyId, IdentityKind, IdentityValue, Generation, HolderBatchId, "
            + "HolderSourceDocumentId, ClaimedBy, ClaimedAtUtc, CorrelationId "
            + "FROM finance.OfficialInvoiceIdentityClaims WHERE IdentityKind = N'ettn'"));
        Assert.True(duplicate.Number is 2601 or 2627);

        var uppercase = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "INSERT INTO finance.OfficialInvoiceIdentityClaims (Id, CompanyId, IdentityKind, IdentityValue, Generation, "
            + "HolderBatchId, HolderSourceDocumentId, ClaimedBy, ClaimedAtUtc, CorrelationId) "
            + "SELECT NEWID(), CompanyId, IdentityKind, UPPER(IdentityValue), Generation + 1, HolderBatchId, "
            + "HolderSourceDocumentId, ClaimedBy, ClaimedAtUtc, CorrelationId "
            + "FROM finance.OfficialInvoiceIdentityClaims WHERE IdentityKind = N'ettn'"));
        Assert.Equal(547, uppercase.Number);
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialInvoiceIdentityClaims"));
    }

    [SqlServerFact]
    public async Task The_identity_claim_rollback_refuses_while_claims_exist_and_stays_applied()
    {
        await using var run = await Run.StartAsync();
        var batch = Guid.Parse("81400000-0000-0000-0000-000000000001");
        var approved = await run.ApproveAsync(batch, Csv(
            Row(Guid.Parse("83400000-0000-0000-0000-000000000001"), Guid.Parse("82400000-0000-0000-0000-000000000001"),
                1, "SNT2026000000004", EttnA)));
        Assert.Equal(CommandOutcome.Succeeded, (await run.AdmitAsync(approved, "admit-rollback")).Outcome);

        await using var context = run.Context();
        var error = await Assert.ThrowsAsync<SqlException>(() =>
            context.GetService<IMigrator>().MigrateAsync(PreviousMigration));

        Assert.Equal(51372, error.Number);
        Assert.Contains("forward fix", error.Message, StringComparison.Ordinal);
        Assert.EndsWith("_AddOfficialInvoiceIdentityClaims",
            (await context.Database.GetAppliedMigrationsAsync()).Last(), StringComparison.Ordinal);
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialInvoiceIdentityClaims"));
    }

    private static byte[] Csv(params string[] rows) => Encoding.UTF8.GetBytes(
        FinanceOfficialSalesInvoiceImportTemplateV2.Header + "\n" + string.Join("\n", rows) + "\n");

    private static string Row(Guid openItem, Guid document, int installment, string officialNo, string ettn) =>
        string.Join(',', openItem, document, Guid.Parse($"84000000-0000-0000-{installment:D4}-{document.ToString("N")[^12..]}"),
            "invoice-v2", Party, Account, Currency, "600.00", "2026-11-30",
            officialNo, ettn, "1000.00", "200.00", "0");

    private sealed record Approved(Guid BatchId, ApprovedFinanceOpenItemSourceBatch Batch);

    private sealed class Run : IAsyncDisposable
    {
        private readonly string baseConnectionString;

        private Run(string baseConnectionString, SqlServerRelationalApiFactory factory)
        {
            this.baseConnectionString = baseConnectionString;
            Factory = factory;
        }

        private SqlServerRelationalApiFactory Factory { get; }

        public static async Task<Run> StartAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(
                SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(baseConnectionString);
            var run = new Run(baseConnectionString, factory);
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            await using var context = run.Context();
            await context.Database.MigrateAsync();
            return run;
        }

        public FinanceDbContext Context() => Context(CompanyScope(), null);

        public async Task<Approved> ApproveAsync(Guid batchId, byte[] bytes)
        {
            var request = new FinanceOfficialInvoiceImportPreviewRequestV1(batchId, $"official-file-{batchId:N}", bytes);
            await using (var maker = ImportService("SENTETIK-maker"))
            {
                var upload = await maker.Service.UploadAsync(maker.Context,
                    new CommandMetadata($"upload-{batchId:N}"), request, default);
                Assert.Equal(CommandOutcome.Succeeded, upload.Outcome);
            }

            await using var checker = ImportService("SENTETIK-checker");
            var approval = await checker.Service.ApproveAsync(checker.Context,
                new CommandMetadata($"approve-{batchId:N}"),
                new FinanceOfficialInvoiceImportApproveRequestV1(batchId, "1"), default);
            Assert.Equal(CommandOutcome.Succeeded, approval.Outcome);
            var source = await checker.Service.GetApprovedAsync(checker.Context,
                new FinanceOpenItemSourceReference(batchId, approval.Resource!.BatchVersion,
                    approval.Resource.BatchChecksum), default);
            Assert.Equal(ReadOutcome.Found, source.Outcome);
            return new Approved(batchId, source.Resource!);
        }

        public async Task<FinanceOpenItemAdmissionResult> AdmitAsync(Approved approved, string key)
        {
            var company = CompanyScope();
            var collector = new AuditEventCollector();
            await using var db = Context(company, collector);
            const string actor = "SENTETIK-admitter";
            var service = new FinanceOpenItemSourceAdmissionService(db, company, new User(actor),
                new AllowSourceAuthority(), new FixedApprovedSource(approved.Batch), new ActiveParty(), collector,
                new FinanceAuditEnvelope("synthetic.finance.open-item-source.admit", ["SENTETIK-FINANCE"], "Integration"),
                TimeProvider.System);
            return await service.AdmitAsync(new FinanceContext(Company, actor, $"fin-s5-{Guid.NewGuid():N}", null),
                new CommandMetadata(key),
                new FinanceOpenItemSourceReference(approved.BatchId, approved.Batch.BatchVersion,
                    approved.Batch.BatchChecksum),
                default);
        }

        public async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
            return (T)(await command.ExecuteScalarAsync())!;
        }

        public async Task ExecuteAsync(string sql)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Factory.DisposeAsync();
            SqlConnection.ClearAllPools();
            await using var connection = new SqlConnection(baseConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                IF DB_ID(N'{Factory.DatabaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{Factory.DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{Factory.DatabaseName}];
                END;
                """;
            await command.ExecuteNonQueryAsync();
        }

        private ImportScope ImportService(string actor)
        {
            var company = CompanyScope();
            var collector = new AuditEventCollector();
            var db = Context(company, collector);
            var service = new FinanceOfficialInvoiceImportApprovalServiceV1(db, company, new User(actor),
                new FinanceOfficialInvoiceImportParserV1(), new AllowImportAuthority(), new AllowSourceAuthority(),
                collector,
                new FinanceOfficialInvoiceImportAuditEnvelopesV1(
                    new FinanceAuditEnvelope(FinanceOfficialInvoiceImportPermissionNamesV1.Upload,
                        ["SENTETIK-FINANCE"], "Integration"),
                    new FinanceAuditEnvelope(FinanceOfficialInvoiceImportPermissionNamesV1.Approve,
                        ["SENTETIK-FINANCE"], "Integration")),
                TimeProvider.System);
            return new ImportScope(db, service, new FinanceContext(Company, actor, $"fin-s5-{Guid.NewGuid():N}", null));
        }

        private static CompanyContext CompanyScope()
        {
            var company = new CompanyContext();
            company.Set(FinanceOfficialInvoiceIdentityClaimSqlServerRelationalAcceptanceTests.Company);
            return company;
        }

        private FinanceDbContext Context(CompanyContext company, AuditEventCollector? collector)
        {
            var options = new DbContextOptionsBuilder<FinanceDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema));
            if (collector is not null)
            {
                options.AddInterceptors(new AuditSaveChangesInterceptor(collector, company));
            }
            return new FinanceDbContext(options.Options, company);
        }
    }

    private sealed class ImportScope(
        FinanceDbContext db,
        FinanceOfficialInvoiceImportApprovalServiceV1 service,
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

    private sealed class AllowImportAuthority : IFinanceOfficialInvoiceImportApprovalAuthorityV1
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context,
            FinanceOfficialInvoiceImportAuthorityActionV1 action, Guid batchId, CancellationToken cancellationToken) =>
            Task.FromResult(PermissionDecision.Allowed);
    }

    private sealed class AllowSourceAuthority : IFinanceOpenItemSourceAuthority
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, Guid batchId,
            CancellationToken cancellationToken) => Task.FromResult(PermissionDecision.Allowed);
    }

    private sealed class FixedApprovedSource(ApprovedFinanceOpenItemSourceBatch batch) : IApprovedFinanceOpenItemSourcePort
    {
        public Task<ReadResult<ApprovedFinanceOpenItemSourceBatch>> GetApprovedAsync(FinanceContext context,
            FinanceOpenItemSourceReference source, CancellationToken cancellationToken) =>
            Task.FromResult(new ReadResult<ApprovedFinanceOpenItemSourceBatch>(ReadOutcome.Found, batch));
    }

    private sealed class ActiveParty : IFinancePartyAccountPort
    {
        public Task<ReadResult<PartyAccountEligibility>> GetAuthorizedAsync(FinanceContext context, Guid partyId,
            Guid companyAccountId, CancellationToken cancellationToken) =>
            Task.FromResult(context.CompanyId == Company && partyId == Party && companyAccountId == Account
                ? new ReadResult<PartyAccountEligibility>(ReadOutcome.Found,
                    new PartyAccountEligibility(Party, Account, Company, true, IsPartyActive: true))
                : new ReadResult<PartyAccountEligibility>(ReadOutcome.NotFound, null));
    }
}
