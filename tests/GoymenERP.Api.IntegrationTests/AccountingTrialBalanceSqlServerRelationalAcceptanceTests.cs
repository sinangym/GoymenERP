using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Accounting.Domain.Periods;
using GoymenERP.Modules.Accounting.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Real SQL proof for the read aggregate. All rows are synthetic; no real chart, account code,
/// mapping, grant or production data is claimed or created.
/// </summary>
public sealed class AccountingTrialBalanceSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly Guid CurrencyA = Guid.Parse("91944604-c73d-4879-b70d-c9c4e3dc6eb8");
    private static readonly Guid CurrencyB = Guid.Parse("926f84e8-7e70-410c-88d5-01c4b25e14b9");
    private static readonly Guid AccountA = Guid.Parse("a13aaeae-2ef9-46cc-8a94-b8fe73c97df5");
    private static readonly Guid AccountB = Guid.Parse("b1469011-360d-4da1-810f-255cb764b550");

    [SqlServerFact]
    public async Task Posted_journals_are_grouped_by_company_date_account_and_currency_without_writes()
    {
        await using var run = await Run.StartAsync();
        await SeedAsync(run, CompanyA, includeReversal: true);
        await SeedAsync(run, CompanyB, includeReversal: false);

        var company = new CompanyContext();
        company.Set(CompanyA);
        await using var context = run.Context(company);
        var reader = new AccountingTrialBalanceReader(context, company);

        var rows = await reader.ReadAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), null);

        Assert.Equal(4, rows.Count);
        AssertRow(rows, AccountA, CurrencyA, 125m, 100m);
        AssertRow(rows, AccountB, CurrencyA, 100m, 125m);
        AssertRow(rows, AccountA, CurrencyB, 50m, 0m);
        AssertRow(rows, AccountB, CurrencyB, 0m, 50m);
        Assert.DoesNotContain(rows, row => row.Debit == 999m || row.Credit == 999m);
        Assert.False(context.ChangeTracker.HasChanges());

        var oneCurrency = await reader.ReadAsync(
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), CurrencyB);
        Assert.Equal(2, oneCurrency.Count);
        Assert.All(oneCurrency, row => Assert.Equal(CurrencyB, row.CurrencyId));
        Assert.Empty(await reader.ReadAsync(
            new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), null));
        Assert.Equal(5, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM accounting.Journals WHERE CompanyId = @company",
            ("@company", CompanyA)));
    }

    private static void AssertRow(
        IReadOnlyList<TrialBalanceReadRowV1> rows,
        Guid account,
        Guid currency,
        decimal debit,
        decimal credit)
    {
        var row = Assert.Single(rows, candidate => candidate.AccountId == account && candidate.CurrencyId == currency);
        Assert.Equal((debit, credit), (row.Debit, row.Credit));
    }

    private static async Task SeedAsync(Run run, Guid companyId, bool includeReversal)
    {
        var company = new CompanyContext();
        company.Set(companyId);
        await using var context = run.Context(company);
        var now = new DateTimeOffset(2026, 9, 24, 9, 0, 0, TimeSpan.Zero);
        var periodId = Guid.NewGuid();
        context.Periods.Add(new PeriodRow
        {
            Id = periodId,
            CompanyId = companyId,
            StartsOn = new DateOnly(2026, 1, 1),
            EndsOn = new DateOnly(2026, 12, 31),
            Status = (int)AccountingPeriodStatus.Open,
            PreparedByUserId = Guid.NewGuid(),
            PreparedAtUtc = now,
            ApprovedByUserId = Guid.NewGuid(),
            ApprovedAtUtc = now,
            Version = 1,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });
        await context.SaveChangesAsync();

        if (companyId == CompanyB)
        {
            await AddJournalAsync(context, companyId, periodId, CurrencyA, new DateOnly(2026, 9, 10),
                999m, null, JournalKind.Original, now);
            return;
        }

        var original = await AddJournalAsync(context, companyId, periodId, CurrencyA, new DateOnly(2026, 9, 5),
            100m, null, JournalKind.Original, now);
        if (includeReversal)
        {
            await AddJournalAsync(context, companyId, periodId, CurrencyA, new DateOnly(2026, 9, 6),
                100m, original, JournalKind.Reversal, now.AddMinutes(1));
        }

        await AddJournalAsync(context, companyId, periodId, CurrencyA, new DateOnly(2026, 9, 7),
            25m, null, JournalKind.Original, now.AddMinutes(2));
        await AddJournalAsync(context, companyId, periodId, CurrencyB, new DateOnly(2026, 9, 8),
            50m, null, JournalKind.Original, now.AddMinutes(3));
        await AddJournalAsync(context, companyId, periodId, CurrencyA, new DateOnly(2026, 10, 8),
            10m, null, JournalKind.Original, now.AddMinutes(4));
    }

    private static async Task<Guid> AddJournalAsync(
        AccountingDbContext context,
        Guid companyId,
        Guid periodId,
        Guid currencyId,
        DateOnly accountingDate,
        decimal amount,
        Guid? linkedJournalId,
        JournalKind kind,
        DateTimeOffset storedAt)
    {
        var journalId = Guid.NewGuid();
        context.Journals.Add(new JournalRow
        {
            Id = journalId,
            CompanyId = companyId,
            Kind = (int)kind,
            SourceId = Guid.NewGuid(),
            PostingPurpose = "synthetic-trial-balance",
            PostingRuleVersion = "synthetic-rule-v1",
            CurrencyId = currencyId,
            TransactionDate = accountingDate,
            AccountingDate = accountingDate,
            RecordedAtUtc = storedAt,
            LinkedJournalId = linkedJournalId,
            PeriodId = periodId,
            StoredAtUtc = storedAt,
            StoredBy = "synthetic-trial-balance-writer",
            CorrelationId = $"trial-{journalId:N}",
        });
        context.JournalLines.AddRange(
            new JournalLineRow
            {
                CompanyId = companyId,
                JournalId = journalId,
                LineNumber = 1,
                AccountId = AccountA,
                Debit = kind == JournalKind.Reversal ? 0m : amount,
                Credit = kind == JournalKind.Reversal ? amount : 0m,
            },
            new JournalLineRow
            {
                CompanyId = companyId,
                JournalId = journalId,
                LineNumber = 2,
                AccountId = AccountB,
                Debit = kind == JournalKind.Reversal ? amount : 0m,
                Credit = kind == JournalKind.Reversal ? 0m : amount,
            });
        await context.SaveChangesAsync();
        return journalId;
    }

    private sealed class Run(string baseConnectionString, SqlServerRelationalApiFactory factory) : IAsyncDisposable
    {
        public SqlServerRelationalApiFactory Factory { get; } = factory;

        public static async Task<Run> StartAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(
                SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(baseConnectionString);
            var run = new Run(baseConnectionString, factory);
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            var company = new CompanyContext();
            company.Set(CompanyA);
            await using var context = run.Context(company);
            await context.Database.MigrateAsync();
            return run;
        }

        public AccountingDbContext Context(CompanyContext companyContext)
        {
            var options = new DbContextOptionsBuilder<AccountingDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", AccountingDbContext.Schema))
                .Options;
            return new AccountingDbContext(options, companyContext);
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
