using System.Security.Cryptography;
using System.Text;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Finance.Infrastructure.Persistence.Migrations;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// WP12 (Finance K3): real SQL storage, the per-account line uniqueness, check constraints, the serializable save with
/// audit and the forward-fix Down guard (THROW 52250). Permission, Settings mapping and vault inputs are SENTETIK / NOT_ENV.
/// </summary>
public sealed class FinanceBankStatementSqlServerRelationalAcceptanceTests
{
    private static readonly Guid Company = TestCompanyIds.CompanyA;
    private static readonly Guid OtherCompany = TestCompanyIds.CompanyB;
    private static readonly Guid BankAccount = Guid.Parse("f3850000-0000-4000-8000-000000000001");
    private const string Actor = "SENTETIK-bank-statement";
    private const string File = "SENTETIK\nTarih;Valor;Aciklama;Tutar;Bakiye;Referans\n"
                                + "17.11.2026;;Virman TR13 0006 2000 0000 0000 0000 02;100,00;;V-1\n"
                                + "18.11.2026;;Masraf;-3,50;;\n";

    [SqlServerFact]
    public async Task Parsed_lines_survive_reopen_replay_dedup_other_company_reads_and_the_down_guard_refuses_after_rows()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddBankStatementImports().DownOperations[0]).Sql;
        await run.ExecuteAsync(guard);
        await run.SeedBankAccountAsync();
        var first = Guid.NewGuid();
        await using (var scope = run.Open())
        {
            Assert.Equal(CommandOutcome.Succeeded, (await scope.ReserveAsync("SENTETIK-r1", first)).Outcome);
            var parsed = await scope.ParseAsync("SENTETIK-p1", first, File);
            Assert.Equal((CommandOutcome.Succeeded, 2, 0), (parsed.Outcome, parsed.Resource!.LineCount, parsed.Resource.DuplicateLineCount));
        }

        await using (var reopened = run.Open())
        {
            var replay = await reopened.ParseAsync("SENTETIK-p1", first, File);
            Assert.True(replay.Replayed);
            var detail = (await reopened.Service.GetAuthorizedAsync(reopened.Context, first, default)).Detail!;
            Assert.Equal(2, detail.Lines.Count);
            Assert.Equal("TR130006200000000000000002", detail.Lines[0].CounterpartyIban);
            var second = Guid.NewGuid();
            await reopened.ReserveAsync("SENTETIK-r2", second);
            var overlap = await reopened.ParseAsync("SENTETIK-p2", second, File + "19.11.2026;;Yeni;1,00;;\n");
            Assert.Equal((1, 2), (overlap.Resource!.LineCount, overlap.Resource.DuplicateLineCount));
        }

        await using (var foreign = run.Open(OtherCompany))
        {
            Assert.Equal(ReadOutcome.NotFound, (await foreign.Service.GetAuthorizedAsync(foreign.Context, first, default)).Outcome);
            Assert.Empty(await foreign.Db.BankStatementLines.ToListAsync());
        }

        Assert.Equal(3, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.BankStatementLines"));
        Assert.Equal(4, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.BankStatementCommands"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OutgoingMoneyEvents"));
        Assert.Equal(4, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType LIKE N'finance.bank-statement-import.%'"));

        // The unique index refuses a second copy of a stored line for the same account, whatever the writer.
        var duplicate = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "INSERT INTO finance.BankStatementLines (CompanyId, Id, ImportId, BankAccountId, LineNumber, Fingerprint, Occurrence, "
            + "TransactionOn, Direction, AmountText, Description) SELECT TOP 1 CompanyId, NEWID(), ImportId, BankAccountId, LineNumber, "
            + "Fingerprint, Occurrence, TransactionOn, Direction, AmountText, Description FROM finance.BankStatementLines"));
        Assert.Contains(duplicate.Number, new[] { 2601, 2627 });
        // A parsed import without its document evidence is refused by the state check.
        var state = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE finance.BankStatementImports SET DocumentSha256 = NULL WHERE State = 2"));
        Assert.Equal(547, state.Number);

        Assert.Equal(52250, (await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard))).Number);
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.BankStatementImports"));
    }

    [SqlServerFact]
    public async Task Concurrent_parses_of_overlapping_files_store_each_line_once()
    {
        await using var run = await Run.StartAsync();
        await run.SeedBankAccountAsync();
        var left = Guid.NewGuid();
        var right = Guid.NewGuid();
        await using (var scope = run.Open())
        {
            await scope.ReserveAsync("SENTETIK-left", left);
            await scope.ReserveAsync("SENTETIK-right", right);
        }

        await using var a = run.Open();
        await using var b = run.Open();
        var results = await Task.WhenAll(a.ParseAsync("SENTETIK-pl", left, File), b.ParseAsync("SENTETIK-pr", right, File + "20.11.2026;;Ek;2,00;;\n"));
        Assert.Contains(results, result => result.Outcome == CommandOutcome.Succeeded);
        Assert.All(results, result => Assert.Contains(result.Outcome, new[] { CommandOutcome.Succeeded, CommandOutcome.Unavailable }));
        var lines = await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.BankStatementLines");
        var distinct = await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM (SELECT DISTINCT BankAccountId, Fingerprint, Occurrence FROM finance.BankStatementLines) AS d");
        Assert.Equal(distinct, lines);
    }

    [SqlServerFact]
    public async Task A_confirmed_match_is_one_per_line_and_record_survives_reversal_as_a_record_and_guards_its_down()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddBankStatementMatches().DownOperations[0]).Sql;
        await run.ExecuteAsync(guard);
        await run.SeedBankAccountAsync();
        var receipt = await run.SeedReceiptAsync("100.00", new DateOnly(2026, 11, 17));
        var import = Guid.NewGuid();
        FinanceBankStatementMatchViewV1 match;
        await using (var scope = run.Open())
        {
            await scope.ReserveAsync("SENTETIK-r", import);
            await scope.ParseAsync("SENTETIK-p", import, File);
            var line = (await scope.Db.BankStatementLines.AsNoTracking().SingleAsync(x => x.Reference == "V-1")).Id;
            match = (await scope.Service.ConfirmMatchAsync(scope.Context, new("SENTETIK-c"), import,
                new(Guid.NewGuid(), line, FinanceBankStatementCandidateKindV1.Receipt, receipt), default)).Resource!;
        }

        // The filtered unique indexes refuse a second active match of the same line or record, whatever the writer.
        var second = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "INSERT INTO finance.BankStatementMatches (CompanyId, Id, Version, ImportId, LineId, BankAccountId, CandidateKind, RecordId, "
            + "CounterpartyCheck, ConfirmedBy, ConfirmedAtUtc) SELECT CompanyId, NEWID(), 1, ImportId, LineId, BankAccountId, CandidateKind, "
            + "RecordId, CounterpartyCheck, ConfirmedBy, ConfirmedAtUtc FROM finance.BankStatementMatches"));
        Assert.Contains(second.Number, new[] { 2601, 2627 });

        await using (var scope = run.Open())
        {
            var reversed = await scope.Service.ReverseMatchAsync(scope.Context, new("SENTETIK-rev"), match.Id,
                new("1", Guid.NewGuid(), "SENTETIK yanlis eslesme"), default);
            Assert.Equal(FinanceBankStatementMatchStateV1.Reversed, reversed.Resource!.State);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.BankStatementMatches"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.BankStatementMatchReversals"));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType LIKE N'finance.bank-statement-match.%'"));
        // A reversed match without its reversal link is refused by the check.
        Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(
            "UPDATE finance.BankStatementMatches SET ReversalId = NULL"))).Number);
        Assert.Equal(52251, (await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard))).Number);
    }

    private sealed class Run(string baseConnection, SqlServerRelationalApiFactory factory) : IAsyncDisposable
    {
        public static async Task<Run> StartAsync()
        {
            var connection = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(connection);
            var run = new Run(connection, factory);
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            await using var scope = run.Open();
            await scope.Db.Database.MigrateAsync();
            return run;
        }

        public Scope Open(Guid? companyId = null)
        {
            var company = new CompanyContext();
            company.Set(companyId ?? Company);
            var collector = new AuditEventCollector();
            var user = new User(company.CompanyId);
            var options = new DbContextOptionsBuilder<FinanceDbContext>().UseSqlServer(factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, company)).Options;
            var db = new FinanceDbContext(options, company);
            var context = new FinanceContext(company.CompanyId, Actor, "SENTETIK-statement-correlation", null);
            var documents = new Documents();
            var service = new FinanceBankStatementImportServiceV1(db, company, user, new Permissions(),
                new Mapping(company.CompanyId), documents, new UnavailableFinancePartyIbanReadPort(), collector,
                new(FinanceBankStatementPermissionsV1.Import, ["SENTETIK"], "Web"), TimeProvider.System);
            return new Scope(db, context, service, documents);
        }

        public async Task SeedBankAccountAsync()
        {
            await using var scope = Open();
            var now = DateTimeOffset.UtcNow;
            scope.Db.BankAccounts.Add(new BankAccountRow
            {
                Id = BankAccount, CompanyId = Company, CurrencyId = Guid.NewGuid(), DisplayName = "SENTETIK hesap", Status = 2,
                Version = 1, ProposedBy = "SENTETIK-proposer", ProposedAtUtc = now, ApprovedBy = "SENTETIK-approver",
                ApprovedAtUtc = now, UpdatedAtUtc = now, Iban = "TR420001000000000000000001",
            });
            await scope.Db.SaveChangesAsync();
        }

        public async Task<Guid> SeedReceiptAsync(string amount, DateOnly on)
        {
            await using var scope = Open();
            var source = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            var party = Guid.NewGuid();
            var account = Guid.NewGuid();
            var currency = Guid.NewGuid();
            scope.Db.ReceiptDrafts.Add(new() { Id = source, CompanyId = Company, Version = 2, PartyId = party, CompanyAccountId = account,
                TreasuryAccountId = BankAccount, CurrencyId = currency, AmountText = amount, ProfileId = "SENTETIK", ProfileVersion = "1",
                OccurredOn = on, CreatedAtUtc = now, UpdatedAtUtc = now, CreatedBy = Actor, UpdatedBy = Actor,
                Stage = ReceiptStages.Recorded, RecordedAtUtc = now, RecordedBy = Actor });
            scope.Db.MoneyEvents.Add(new() { Id = Guid.NewGuid(), CompanyId = Company, SourceId = source,
                Kind = MoneyEventKinds.ManualBankReceiptRecorded, PartyId = party, CompanyAccountId = account, TreasuryAccountId = BankAccount,
                CurrencyId = currency, AmountText = amount, ProfileId = "SENTETIK", ProfileVersion = "1", OccurredOn = on,
                RecordedAtUtc = now, RecordedBy = Actor, CorrelationId = "SENTETIK seed" });
            await scope.Db.SaveChangesAsync();
            return source;
        }

        public async Task<T> ScalarAsync<T>(string sql)
        {
            await using var connection = new SqlConnection(factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            return (T)(await command.ExecuteScalarAsync())!;
        }

        public async Task ExecuteAsync(string sql)
        {
            await using var connection = new SqlConnection(factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await factory.DisposeAsync();
            SqlConnection.ClearAllPools();
            await using var connection = new SqlConnection(baseConnection);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"IF DB_ID(N'{factory.DatabaseName}') IS NOT NULL BEGIN ALTER DATABASE [{factory.DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{factory.DatabaseName}]; END;";
            await command.ExecuteNonQueryAsync();
        }
    }

    private sealed record Scope(FinanceDbContext Db, FinanceContext Context, FinanceBankStatementImportServiceV1 Service,
        Documents Documents) : IAsyncDisposable
    {
        public Task<FinanceBankStatementCommandResultV1> ReserveAsync(string key, Guid importId) =>
            Service.ReserveAsync(Context, new(key), new(importId, BankAccount, FinanceBankStatementFormatV1.Csv, "SENTETIK-BANKA"), default);

        public Task<FinanceBankStatementCommandResultV1> ParseAsync(string key, Guid importId, string content)
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            var documentId = new Guid(SHA256.HashData(bytes).AsSpan(0, 16));
            Documents.Content[documentId] = bytes;
            return Service.ParseAsync(Context, new(key), importId, new("1", documentId, 1), default);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class User(Guid company) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string UserId => Actor;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid> { company };
        public bool CanAccessCompany(Guid value) => value == company;
    }

    private sealed class Permissions : IFinancePermissionPort
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceAction action, Guid? id, CancellationToken token) =>
            Task.FromResult(PermissionDecision.Allowed);
    }

    private sealed class Mapping(Guid company) : ISettingsBankCsvMappingProviderV1
    {
        public Task<SettingsBankCsvMappingV1> GetBankCsvMappingAsync(string bankKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SettingsBankCsvMappingV1(company, new ProfileVersionV1(Guid.Parse("f3850000-0000-4000-8000-000000000009"), 1),
                "SENTETIK-BANKA", "Sentetik Banka", ';', 1, true, ',', '.', "dd.MM.yyyy", SettingsBankCsvAmountLayoutV1.SignedAmount,
                SettingsBankCsvSignedAmountDirectionV1.Inflow, new SettingsBankCsvColumnsV1(1, 2, 3, 4, null, null, 5, 6, null)));

        public Task<IReadOnlyList<SettingsBankCsvMappingSummaryV1>> ListActiveBankCsvMappingsAsync(
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SettingsBankCsvMappingSummaryV1>>([]);
    }

    private sealed class Documents : IFinanceBankStatementDocumentReadV1
    {
        public Dictionary<Guid, byte[]> Content { get; } = [];

        public Task<FinanceBankStatementDocumentAnswerV1> ReadAsync(FinanceContext context, Guid importId, Guid documentId,
            int version, CancellationToken cancellationToken) =>
            Task.FromResult(Content.TryGetValue(documentId, out var bytes)
                ? new FinanceBankStatementDocumentAnswerV1(FinanceBankStatementDocumentOutcomeV1.Found,
                    new FinanceBankStatementDocumentV1(context.CompanyId, documentId, version,
                        Convert.ToHexString(SHA256.HashData(bytes)), FinanceBankStatementFormatV1.Csv, bytes))
                : new FinanceBankStatementDocumentAnswerV1(FinanceBankStatementDocumentOutcomeV1.NotFound, null));
    }
}
