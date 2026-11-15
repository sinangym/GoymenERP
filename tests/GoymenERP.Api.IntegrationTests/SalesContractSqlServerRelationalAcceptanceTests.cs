using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.SalesContracts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// S-11 (SAT-D28, NS-13, SAT-D346) against a REAL SQL Server and the REAL host composition: a sales contract with a discount and no
/// configured limit is recorded pending through the real Sales store in one save (row, attempt, audit) and approved once by another
/// actor (version 2). The database itself refuses a changed fact, a skipped version, a second decision and a delete (TR 51471), the
/// creator deciding their own discount (CK maker-checker) and any change of an attempt (TR 51472). Another company never sees the
/// contract. The migration refuses to roll back over a recorded contract (THROW 51473, forward fix). No other module's table is
/// written. SENTETIK / NOT_ENV.
/// </summary>
public sealed class SalesContractSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly DateOnly Day = new(2026, 11, 15);
    private static readonly Guid Currency = Guid.Parse("7e3f4051-6c7b-4d8e-9fa0-1b2c3d4e5f01");
    private static readonly Guid Profile = Guid.Parse("7e3f4051-6c7b-4d8e-9fa0-1b2c3d4e5f02");
    private const string Seller = "SENTETIK-contract-seller";
    private const string Manager = "SENTETIK-contract-manager";

    private static readonly SalesDraftStateAudit Created = new("sales.contract.created.v1", "CreateContract", "SENTETIK kaydedildi");
    private static readonly SalesDraftStateAudit Approved = new("sales.contract.discount-approved.v1", "ApproveContractDiscount", "SENTETIK onaylandı");

    [SqlServerFact]
    public async Task A_pending_contract_discount_is_approved_once_by_another_actor_and_the_database_refuses_changed_facts_a_second_decision_a_delete_and_attempt_changes()
    {
        await using var run = await Run.StartAsync();
        var pending = await run.PendingContractAsync("k-create");
        Assert.Equal((SalesContractState.PendingDiscountApproval, 1L, (decimal?)null), (pending.State, pending.Version, pending.DiscountLimitPercent));

        const string where = " WHERE [Id] = @id; SELECT 1";
        // CK_Contracts_MakerChecker: the creator never decides their own discount, even through raw SQL (checked before the trigger).
        Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "UPDATE [sales].[Contracts] SET [State] = 2, [DecidedBy] = [CreatedBy], [DecidedAtUtc] = SYSDATETIMEOFFSET(), "
            + "[Version] = [Version] + 1" + where, ("@id", pending.Id)))).Number);
        // TR_Contracts_Guard: a decision never changes the contract's facts and never skips a version.
        Assert.Equal(51471, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "UPDATE [sales].[Contracts] SET [State] = 2, [DecidedBy] = N'SENTETIK-raw', [DecidedAtUtc] = SYSDATETIMEOFFSET(), "
            + "[Clauses] = N'SENTETIK degisti', [Version] = [Version] + 1" + where, ("@id", pending.Id)))).Number);
        Assert.Equal(51471, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "UPDATE [sales].[Contracts] SET [State] = 2, [DecidedBy] = N'SENTETIK-raw', [DecidedAtUtc] = SYSDATETIMEOFFSET(), "
            + "[Version] = [Version] + 2" + where, ("@id", pending.Id)))).Number);
        Assert.Equal(51471, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "UPDATE [sales].[Contracts] SET [InstallmentsJson] = N'[]'" + where, ("@id", pending.Id)))).Number);

        // The real store: another actor approves the pending discount in one save (row, attempt, audit).
        SalesContractStoreResult? approved;
        await using (var scope = run.SalesStore(CompanyA))
        {
            approved = await scope.Store.DecideContractAsync(run.Envelope("k-approve", Manager, SalesPermissionManifestV1.ContractsDiscountApprove),
                Hash('b'), SalesContractCommands.ApproveDiscountV1, pending.Id,
                (contract, now) => contract.ApproveDiscount(1, Manager, now), Approved);
        }

        Assert.NotNull(approved);
        Assert.Equal((SalesContractState.Active, 2L, Manager), (approved.Contract.State, approved.Contract.Version, approved.Contract.Decision!.DecidedBy));

        // TR_Contracts_Guard: the decision is taken once; an active contract is never rejected afterwards.
        Assert.Equal(51471, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "UPDATE [sales].[Contracts] SET [State] = 3, [RejectionReason] = N'SENTETIK sonradan', [Version] = [Version] + 1" + where,
            ("@id", pending.Id)))).Number);
        // FK_ContractAttempts_Contracts_CompanyId_ContractId is checked before the AFTER trigger; the trigger's own DELETE refusal is
        // proven on a raw copy no attempt references.
        Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "DELETE FROM [sales].[Contracts]" + where, ("@id", pending.Id)))).Number);
        Assert.Equal(1, await run.ScalarAsync<int>(CopyOf, ("@id", pending.Id)));
        Assert.Equal(51471, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "DELETE FROM [sales].[Contracts] WHERE [Id] <> @id; SELECT 1", ("@id", pending.Id)))).Number);
        // TR_ContractAttempts_Guard: the attempts are append-only.
        Assert.Equal(51472, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "UPDATE [sales].[ContractAttempts] SET [ActorId] = N'SENTETIK-other' WHERE [ContractId] = @id; SELECT 1",
            ("@id", pending.Id)))).Number);
        Assert.Equal(51472, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "DELETE FROM [sales].[ContractAttempts] WHERE [ContractId] = @id; SELECT 1", ("@id", pending.Id)))).Number);

        Assert.Equal((2, 2L, 2), (
            await run.ScalarAsync<int>("SELECT [State] FROM [sales].[Contracts] WHERE [Id] = @id", ("@id", pending.Id)),
            await run.ScalarAsync<long>("SELECT [Version] FROM [sales].[Contracts] WHERE [Id] = @id", ("@id", pending.Id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[ContractAttempts] WHERE [ContractId] = @id", ("@id", pending.Id))));

        // Another company neither finds nor lists the contract.
        await using (var other = run.SalesStore(CompanyB))
        {
            Assert.Null(await other.Store.FindContractAsync(pending.Id));
            Assert.Empty(await other.Store.ListContractsAsync(50));
        }
    }

    [SqlServerFact]
    public async Task The_sales_contract_migrations_Down_passes_with_no_contract_and_refuses_51473_while_one_exists()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<SqlOperation>(new AddSalesContracts().DownOperations[0]).Sql;
        await run.ScalarAsync<int>(guard + " SELECT 1");

        await run.PendingContractAsync("k-create");

        Assert.Equal(51473, (await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(guard + " SELECT 1"))).Number);
        await using var context = run.SalesContext(CompanyA);
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        var index = Array.FindIndex(applied, id => id.EndsWith("_AddSalesContracts", StringComparison.Ordinal));
        Assert.True(index > 0);
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(applied[index]);
        var rollback = await Assert.ThrowsAsync<SqlException>(() => migrator.MigrateAsync(applied[index - 1]));
        Assert.Equal(51473, rollback.Number);
        Assert.Equal(applied[..(index + 1)], await context.Database.GetAppliedMigrationsAsync());
        await context.Database.MigrateAsync();
        Assert.Equal(applied, await context.Database.GetAppliedMigrationsAsync());
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[Contracts]"));
    }

    /// <summary>A raw copy of the stored contract under another id and number (no attempt references it).</summary>
    private const string CopyOf =
        "INSERT INTO [sales].[Contracts] ([Id],[CompanyId],[ContractNo],[PartyId],[CompanyAccountId],[CurrencyId],[MoneyProfileId],"
        + "[MoneyProfileVersion],[MoneyScale],[ValidFrom],[ValidThrough],[InstallmentsJson],[DiscountRulesJson],[Clauses],"
        + "[DiscountLimitPercent],[DiscountApprovalRequired],[State],[CreatedBy],[CreatedAtUtc],[DecidedBy],[DecidedAtUtc],"
        + "[RejectionReason],[Version],[UpdatedAtUtc],[CorrelationId]) SELECT NEWID(),[CompanyId],[ContractNo] + 1000,[PartyId],"
        + "[CompanyAccountId],[CurrencyId],[MoneyProfileId],[MoneyProfileVersion],[MoneyScale],[ValidFrom],[ValidThrough],"
        + "[InstallmentsJson],[DiscountRulesJson],[Clauses],[DiscountLimitPercent],[DiscountApprovalRequired],[State],[CreatedBy],"
        + "[CreatedAtUtc],[DecidedBy],[DecidedAtUtc],[RejectionReason],[Version],[UpdatedAtUtc],[CorrelationId] "
        + "FROM [sales].[Contracts] WHERE [Id] = @id; SELECT 1";

    private static string Hash(char fill) => new(fill, 64);

    private static MoneyProfileSnapshot Money() =>
        new(CompanyA, Currency, SalesOrderDraft.MoneyOperation, Profile, "3", Day, true, new DateOnly(2026, 9, 1), null, 2);

    private sealed class SalesScope(SalesDbContext dbContext, SalesStore store) : IAsyncDisposable
    {
        public SalesStore Store => store;
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
        public string Correlation { get; } = $"sales-s11-{Guid.NewGuid():N}";

        /// <summary>The real host composed over a fresh database (health is probed so the migrations have run).</summary>
        public static async Task<Run> StartAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(baseConnectionString);
            var run = new Run(baseConnectionString, factory);
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            return run;
        }

        /// <summary>A contract of company A with one line discount and no configured limit (SAT-D346: pending), through the real store.</summary>
        public async Task<SalesContract> PendingContractAsync(string key)
        {
            await using var scope = SalesStore(CompanyA);
            var stored = await scope.Store.StoreContractAsync(Envelope(key, Seller, SalesPermissionManifestV1.ContractsWrite), Hash('a'),
                (number, now) => SalesContract.Create(Guid.NewGuid(), CompanyA, number, Guid.NewGuid(), Guid.NewGuid(), Money(),
                    new DateOnly(2026, 11, 1), new DateOnly(2027, 10, 31),
                    [new SalesContractInstallmentInput(new DateOnly(2026, 12, 1), 1500m), new SalesContractInstallmentInput(new DateOnly(2027, 1, 1), 1500.25m)],
                    [new SalesContractDiscountRuleInput(SalesContractDiscountKind.Line, "SENTETIK hacim", 5m)],
                    "SENTETIK teslim maddesi", null, Seller, now), Created);
            Assert.Equal(SalesDraftWriteOutcome.Recorded, stored.Outcome);
            return stored.Contract;
        }

        public SalesDraftWriteEnvelope Envelope(string key, string actor, string permission) =>
            new(actor, permission, ["SENTETIK-SALES"], Correlation, null, key, "Web", "127.0.0.1", "SENTETIK-agent");

        public CompanyContext Company(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            return companyContext;
        }

        public SalesDbContext SalesContext(Guid company) =>
            new(new DbContextOptionsBuilder<SalesDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
                .Options, Company(company));

        public SalesScope SalesStore(Guid company)
        {
            var companyContext = Company(company);
            var collector = new AuditEventCollector();
            var dbContext = new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext)).Options, companyContext);
            return new SalesScope(dbContext, new SalesStore(dbContext, companyContext, collector, TimeProvider.System));
        }

        public async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            return (T)(await command.ExecuteScalarAsync())!;
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
    }
}
