using System.Security.Cryptography;
using System.Text;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Domain.Quantities;
using GoymenERP.Modules.Logistics.Infrastructure.Fixtures;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Real SQL Server evidence for the gated Logistics line-block fixture store (browser suite l04, decision
/// W1/C): on an instruction the REAL store created and issued, the fixture blocks one line once — the
/// line's flag and one version step, no attempt, no audit; the real store restores it as blocked and refuses
/// a load on that line (D2); a re-run is a no-op; a Draft instruction, an unknown line, an unlabelled request
/// and another company's context are refused with nothing written.
/// </summary>
public sealed class LogisticsLineBlockFixtureStoreSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly Guid KgUnit = Guid.Parse("c5ef643e-a6e8-51f3-89f2-8e68b00cb983");
    private static readonly DateOnly Day = new DateOnly(2026, 9, 19);
    private static readonly DateTimeOffset T0 = new DateTimeOffset(2026, 9, 19, 9, 0, 0, TimeSpan.Zero);
    private const string Label = "SENTETIK browser-suite:l04";
    private static readonly LogisticsMutationAudit Issued = new LogisticsMutationAudit("logistics.loading-instruction.issued.v1", "Issue", "SENTETIK yayımlandı");
    private static readonly LogisticsMutationAudit Loaded = new LogisticsMutationAudit("logistics.loading-instruction.load-recorded.v1", "RecordLoad", "SENTETIK yüklendi");

    [SqlServerFact]
    public async Task A_fixture_block_marks_one_line_of_an_issued_instruction_once_without_attempt_or_audit_and_the_real_store_refuses_a_load_on_it()
    {
        await using Run run = await Run.StartAsync();
        Owners owners = Owners.For(CompanyA);
        Guid issuedId;
        Guid blockedLine;
        Guid openLine;
        Guid draftId;
        await using (Scope a = run.Open(CompanyA))
        {
            LoadingInstruction built = Instruction(owners, lines: 2);
            LoadingInstruction stored = (await a.Store.StoreInstructionAsync(run.Envelope("k-create"), Hash("create-1"), Day, () => built)).Instruction.Instruction;
            issuedId = stored.Id;
            blockedLine = stored.Lines[1].Id;
            openLine = stored.Lines[0].Id;
            LogisticsStoreResult? issued = await a.Store.MutateInstructionAsync(run.Envelope("k-issue"), Hash("issue-3"), LogisticsCommands.IssueV1, issuedId,
                (instruction, now) => instruction.Issue(3, now), Issued);
            Assert.Equal((LoadingInstructionStatus.Issued, 4L), (issued!.Instruction.Instruction.Status, issued.Instruction.Instruction.Version));

            LoadingInstruction draft = Instruction(owners, lines: 1);
            draftId = (await a.Store.StoreInstructionAsync(run.Envelope("k-create-draft"), Hash("create-2"), Day, () => draft)).Instruction.Instruction.Id;
        }

        Assert.Equal((1, 2, 0, 3, 2, 4L), await run.CountsAsync(issuedId));

        // The block: the flag and one version step; attempts and audit events stay as the real store left them.
        LogisticsLineBlockFixtureOutcome first;
        await using (Scope a = run.Open(CompanyA, resolved: false))
        {
            first = await a.Fixture.BlockAsync(Request(issuedId, blockedLine, CompanyA), CancellationToken.None);
            Assert.True(a.CompanyContext.IsResolved && a.CompanyContext.CompanyId == CompanyA);
        }

        Assert.Equal((issuedId, blockedLine, 5L, true), (first.InstructionId, first.LineId, first.Version, first.Blocked));
        Assert.Equal((1, 2, 0, 3, 2, 5L), await run.CountsAsync(issuedId));
        Assert.True(await run.ScalarAsync<bool>("SELECT IsBlocked FROM logistics.LoadingInstructionLines WHERE LineId = @id", ("@id", blockedLine)));
        Assert.False(await run.ScalarAsync<bool>("SELECT IsBlocked FROM logistics.LoadingInstructionLines WHERE LineId = @id", ("@id", openLine)));

        // The real store rebuilds it through the domain (the block last, one version step) and refuses a load on the blocked line (D2).
        await using (Scope a = run.Open(CompanyA))
        {
            StoredLoadingInstruction? restored = await a.Store.FindInstructionAsync(issuedId);
            Assert.NotNull(restored);
            Assert.Equal((LoadingInstructionStatus.Issued, 5L, true, false),
                (restored.Instruction.Status, restored.Instruction.Version, restored.Instruction.Lines[1].IsBlocked, restored.Instruction.Lines[0].IsBlocked));

            LogisticsRuleException refused = await Assert.ThrowsAsync<LogisticsRuleException>(() => a.Store.MutateInstructionAsync(
                run.Envelope("k-load-blocked"), Hash("load-blocked"), LogisticsCommands.RecordLoadV1, issuedId,
                (instruction, now) => instruction.RecordLoad(5, Guid.NewGuid(), blockedLine, Kg(1m), MeasurementKind.Measured, now), Loaded));
            Assert.Equal(LogisticsRuleCodes.LineBlocked, refused.Code);

            LogisticsStoreResult? loaded = await a.Store.MutateInstructionAsync(run.Envelope("k-load-open"), Hash("load-open"), LogisticsCommands.RecordLoadV1, issuedId,
                (instruction, now) => instruction.RecordLoad(5, Guid.NewGuid(), openLine, Kg(1m), MeasurementKind.Measured, now), Loaded);
            Assert.Equal((6L, 1m), (loaded!.Instruction.Instruction.Version, loaded.Instruction.Instruction.Lines[0].LoadedQuantity.Value));
        }

        Assert.Equal((1, 2, 1, 4, 3, 6L), await run.CountsAsync(issuedId));

        // A re-run is a no-op: nothing rewritten, no version step.
        await using (Scope a = run.Open(CompanyA))
        {
            LogisticsLineBlockFixtureOutcome again = await a.Fixture.BlockAsync(Request(issuedId, blockedLine, CompanyA), CancellationToken.None);
            Assert.Equal((6L, false), (again.Version, again.Blocked));
        }

        Assert.Equal((1, 2, 1, 4, 3, 6L), await run.CountsAsync(issuedId));

        // Refusals write nothing: a Draft instruction, an unknown line, an unlabelled request, another company's context.
        await using (Scope a = run.Open(CompanyA))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => a.Fixture.BlockAsync(Request(draftId, openLine, CompanyA), CancellationToken.None));
            await Assert.ThrowsAsync<InvalidOperationException>(() => a.Fixture.BlockAsync(Request(issuedId, Guid.NewGuid(), CompanyA), CancellationToken.None));
            await Assert.ThrowsAsync<InvalidOperationException>(() => a.Fixture.BlockAsync(
                new LogisticsLineBlockFixtureRequest(issuedId, openLine, CompanyA, "blok", T0), CancellationToken.None));
        }

        await using (Scope b = run.Open(CompanyB))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => b.Fixture.BlockAsync(Request(issuedId, openLine, CompanyA), CancellationToken.None));
            await Assert.ThrowsAsync<InvalidOperationException>(() => b.Fixture.BlockAsync(Request(issuedId, openLine, CompanyB), CancellationToken.None));
        }

        Assert.Equal((1, 2, 1, 4, 3, 6L), await run.CountsAsync(issuedId));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingInstructionLines WHERE IsBlocked = 1"));
        Assert.Equal(2L, await run.ScalarAsync<long>("SELECT Version FROM logistics.LoadingInstructions WHERE Id = @id", ("@id", draftId)));
    }

    private static LogisticsLineBlockFixtureRequest Request(Guid instructionId, Guid lineId, Guid company) =>
        new LogisticsLineBlockFixtureRequest(instructionId, lineId, company, $"{Label} blok", T0);

    private static Quantity Kg(decimal value) => Quantity.Of(value, QuantityUnit.Kilogram);

    private static string Hash(string seed) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));

    /// <summary>A Draft instruction of the company towards SYNTHETIC owner identities, its lines 6 kg each.</summary>
    private static LoadingInstruction Instruction(Owners owners, int lines)
    {
        LoadingInstruction instruction = LoadingInstruction.Create(Guid.NewGuid(), owners.Company, owners.Party, owners.Account, T0);
        for (int i = 0; i < lines; i++)
        {
            instruction.AddLine(instruction.Version, Guid.NewGuid(), new SourceOrderLineRef(owners.Order, Guid.NewGuid(), 0), Guid.NewGuid(), KgUnit, null, Kg(6m), T0);
        }

        return instruction;
    }

    private sealed record Owners(Guid Company, Guid Party, Guid Account, Guid Order)
    {
        public static Owners For(Guid company) => new Owners(company, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
    }

    private sealed class Scope(LogisticsDbContext dbContext, CompanyContext companyContext, LogisticsLineBlockFixtureStore fixture, LogisticsStore store) : IAsyncDisposable
    {
        public CompanyContext CompanyContext => companyContext;

        public ILogisticsLineBlockFixtureStore Fixture => fixture;

        public ILogisticsInstructionStore Store => store;

        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }

    private sealed class Run : IAsyncDisposable
    {
        private readonly string baseConnectionString;

        private Run(string baseConnectionString, SqlServerRelationalApiFactory factory)
        {
            this.baseConnectionString = baseConnectionString;
            Factory = factory;
        }

        public SqlServerRelationalApiFactory Factory { get; }

        public string Actor { get; } = Guid.NewGuid().ToString("D");

        public string Correlation { get; } = $"l04-{Guid.NewGuid():N}";

        public static async Task<Run> StartAsync()
        {
            string baseConnectionString = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            SqlServerRelationalApiFactory factory = new SqlServerRelationalApiFactory(baseConnectionString);
            Run run = new Run(baseConnectionString, factory);
            using HttpClient client = factory.CreateClient();
            using HttpResponseMessage health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            CompanyContext companyContext = new CompanyContext();
            companyContext.Set(CompanyA);
            await using LogisticsDbContext context = new LogisticsDbContext(Options(factory), companyContext);
            await context.Database.MigrateAsync();
            return run;
        }

        public LogisticsWriteEnvelope Envelope(string key) =>
            new LogisticsWriteEnvelope(Actor, "SENTETIK-logistics-permission", ["SENTETIK-LOGISTICS"], Correlation, null, key, "Web", "127.0.0.1", "SENTETIK-agent");

        public Scope Open(Guid company, bool resolved = true)
        {
            CompanyContext companyContext = new CompanyContext();
            if (resolved)
            {
                companyContext.Set(company);
            }

            AuditEventCollector collector = new AuditEventCollector();
            DbContextOptions<LogisticsDbContext> options = new DbContextOptionsBuilder<LogisticsDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", LogisticsDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext))
                .Options;
            LogisticsDbContext dbContext = new LogisticsDbContext(options, companyContext);
            return new Scope(dbContext, companyContext,
                new LogisticsLineBlockFixtureStore(dbContext, companyContext),
                new LogisticsStore(dbContext, companyContext, collector, TimeProvider.System));
        }

        /// <summary>(instructions, lines, shares, attempts (all), audit events of the instruction, version) as the real SQL holds them.</summary>
        public async Task<(int Instructions, int Lines, int Shares, int Attempts, int AuditEvents, long Version)> CountsAsync(Guid id) =>
            (await ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingInstructions WHERE Id = @id", ("@id", id)),
                await ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingInstructionLines WHERE InstructionId = @id", ("@id", id)),
                await ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingShares WHERE InstructionId = @id", ("@id", id)),
                await ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingAttempts"),
                await ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId = @id", ("@id", id.ToString("D"))),
                await ScalarAsync<long>("SELECT Version FROM logistics.LoadingInstructions WHERE Id = @id", ("@id", id)));

        public async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
        {
            await using SqlConnection connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using SqlCommand command = new SqlCommand(sql, connection);
            foreach ((string name, object value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            return (T)(await command.ExecuteScalarAsync())!;
        }

        public async ValueTask DisposeAsync()
        {
            await Factory.DisposeAsync();
            SqlConnection.ClearAllPools();
            await using SqlConnection connection = new SqlConnection(baseConnectionString);
            await connection.OpenAsync();
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = $"""
                IF DB_ID(N'{Factory.DatabaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{Factory.DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{Factory.DatabaseName}];
                END;
                """;
            await command.ExecuteNonQueryAsync();
        }

        private static DbContextOptions<LogisticsDbContext> Options(SqlServerRelationalApiFactory factory) =>
            new DbContextOptionsBuilder<LogisticsDbContext>()
                .UseSqlServer(factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", LogisticsDbContext.Schema))
                .Options;
    }
}
