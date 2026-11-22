using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using GoymenERP.Modules.Reporting.Infrastructure.Persistence;
using GoymenERP.Modules.Reporting.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

// WP14 real-SQL evidence for the report centre's own tables and for the Inventory owner read it uses. SENTETIK rows only: this
// is storage, constraint, tenant-filter and forward-fix evidence, not a production owner composition.
public sealed class ReportingSqlServerRelationalAcceptanceTests
{
    private static readonly Guid Owner = Guid.Parse("c1000000-0000-4000-8000-000000000001");
    private const string EmptyJson = "{}";

    [SqlServerFact]
    public async Task Reporting_Down_allows_an_empty_store_and_refuses_with_52270_while_an_export_record_exists()
    {
        await WithDatabaseAsync(async (connectionString, _) =>
        {
            var guard = Assert.IsType<SqlOperation>(new AddReportingCenter().DownOperations[0]).Sql;
            await ExecAsync(connectionString, guard);
            await ExecAsync(connectionString, Export(TestCompanyIds.CompanyA, Guid.NewGuid(), "csv", 1));
            var refusal = await Assert.ThrowsAsync<SqlException>(() => ExecAsync(connectionString, guard));
            Assert.Equal(52270, refusal.Number);
            Assert.Equal(1, await CountAsync(connectionString, "ExportRecords", TestCompanyIds.CompanyA));
        });
    }

    [SqlServerFact]
    public async Task Reporting_checks_and_the_active_name_index_refuse_bad_rows_and_the_tenant_filter_hides_other_companies()
    {
        await WithDatabaseAsync(async (connectionString, _) =>
        {
            var badFormat = Sql(await Assert.ThrowsAnyAsync<Exception>(() =>
                ExecAsync(connectionString, Export(TestCompanyIds.CompanyA, Guid.NewGuid(), "pdf", 1))));
            Assert.Equal(547, badFormat.Number);
            var pageless = Sql(await Assert.ThrowsAnyAsync<Exception>(() =>
                ExecAsync(connectionString, Export(TestCompanyIds.CompanyA, Guid.NewGuid(), "csv", null))));
            Assert.Equal(547, pageless.Number);

            await ExecAsync(connectionString, View(TestCompanyIds.CompanyA, Guid.NewGuid(), "Ayın stoku", 1));
            var duplicate = Sql(await Assert.ThrowsAnyAsync<Exception>(() =>
                ExecAsync(connectionString, View(TestCompanyIds.CompanyA, Guid.NewGuid(), "Ayın stoku", 1))));
            Assert.Contains(duplicate.Number, new[] { 2601, 2627 });
            // A passive view keeps its name; a new active one may reuse it.
            await ExecAsync(connectionString, View(TestCompanyIds.CompanyA, Guid.NewGuid(), "Ayın stoku", 0));
            await ExecAsync(connectionString, View(TestCompanyIds.CompanyB, Guid.NewGuid(), "Ayın stoku", 1));

            await using var companyA = Db(connectionString, TestCompanyIds.CompanyA);
            Assert.Equal(2, await companyA.SavedViews.CountAsync());
            await using var companyB = Db(connectionString, TestCompanyIds.CompanyB);
            Assert.Single(await companyB.SavedViews.ToListAsync());
        });
    }

    [SqlServerFact]
    public async Task Inventory_stock_aging_port_translates_to_SQL_and_refuses_another_company_before_reading()
    {
        await WithDatabaseAsync(async (_, services) =>
        {
            await using var scope = services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(TestCompanyIds.CompanyA);
            var port = scope.ServiceProvider.GetRequiredService<IInventoryStockAgingReadPortV1>();
            var page = await port.ReadAsync(new InventoryStockAgingQueryV1(TestCompanyIds.CompanyA, 1, 50, null, null));
            Assert.Equal((InventoryStockAgingOutcomeV1.Found, TestCompanyIds.CompanyA, false), (page.Outcome, page.CompanyId, page.HasMore));
            Assert.Empty(page.Items);
            var foreign = await port.ReadAsync(new InventoryStockAgingQueryV1(TestCompanyIds.CompanyB, 1, 50, null, null));
            Assert.Equal(InventoryStockAgingOutcomeV1.Denied, foreign.Outcome);
        });
    }

    private static string Export(Guid company, Guid id, string format, int? page) => $"""
        INSERT INTO [reporting].[ExportRecords] ([CompanyId],[Id],[ActorUserId],[Kind],[ReportId],[Format],[ParametersJson],[ColumnsJson],[Page],[PageSize],[RowCount],[ByteLength],[ContentSha256],[RecordedAtUtc],[CorrelationId],[CausationId])
        VALUES ('{company}','{id}','{Owner}',1,'stock-aging','{format}','{EmptyJson}','[]',{(page is null ? "NULL" : page.ToString())},{(page is null ? "NULL" : "50")},0,10,'{new string('a', 64)}',SYSUTCDATETIME(),'corr',NULL);
        """;

    private static string View(Guid company, Guid id, string name, int active) => $"""
        INSERT INTO [reporting].[SavedViews] ([CompanyId],[Id],[OwnerUserId],[ReportId],[Name],[ParametersJson],[ColumnsJson],[Version],[IsActive],[CreatedAtUtc],[UpdatedAtUtc])
        VALUES ('{company}','{id}','{Owner}','stock-aging',N'{name}','{EmptyJson}','["labelCode"]',1,{active},SYSUTCDATETIME(),SYSUTCDATETIME());
        """;

    private static SqlException Sql(Exception exception) => exception as SqlException
        ?? exception.InnerException as SqlException ?? throw new Xunit.Sdk.XunitException("Expected a SqlException, got " + exception.GetType());

    private static async Task ExecAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> CountAsync(string connectionString, string table, Guid company)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand($"SELECT COUNT(*) FROM [reporting].[{table}] WHERE [CompanyId] = @company", connection);
        command.Parameters.AddWithValue("@company", company);
        return (int)(await command.ExecuteScalarAsync())!;
    }

    private static ReportingDbContext Db(string connectionString, Guid company)
    {
        var context = new CompanyContext();
        context.Set(company);
        return new ReportingDbContext(new DbContextOptionsBuilder<ReportingDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", ReportingDbContext.Schema)).Options, context);
    }

    private static async Task WithDatabaseAsync(Func<string, IServiceProvider, Task> test)
    {
        var baseConnection = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var databaseName = $"GoymenERP_Reporting_{Guid.NewGuid():N}";
        await using var factory = new SqlServerRelationalApiFactory(baseConnection, databaseName: databaseName);
        string connectionString;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            connectionString = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>().Database.GetConnectionString()!;
        }

        try
        {
            await using (var migrate = Db(connectionString, TestCompanyIds.CompanyA))
            {
                await migrate.Database.MigrateAsync();
            }

            await test(connectionString, factory.Services);
        }
        finally
        {
            await using var cleanupScope = factory.Services.CreateAsyncScope();
            await cleanupScope.ServiceProvider.GetRequiredService<OrganizationDbContext>().Database.EnsureDeletedAsync();
        }
    }
}
