using System.Diagnostics;
using System.Net;
using GoymenERP.Api.Startup;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit.Abstractions;

namespace GoymenERP.Api.IntegrationTests;

public sealed class DatabaseSchemaCompatibilitySqlServerRelationalAcceptanceTests(ITestOutputHelper output)
{
    [SqlServerFact]
    public async Task Every_active_context_rejects_missing_and_future_history_without_repairing_it()
    {
        var baseConnection = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var configuration = new Dictionary<string, string?>
        {
            ["ProductRuntime:IssuerLocalUserId"] = Guid.NewGuid().ToString("D"),
            ["ProductRuntime:IssuerExternalIdentityId"] = Guid.NewGuid().ToString("D"),
        };
        var initialized = new SqlServerRelationalApiFactory(baseConnection, configurationOverrides: configuration);
        try
        {
            using (var bootstrap = initialized.CreateClient())
            using (var live = await bootstrap.GetAsync("/health/live"))
                Assert.Equal(HttpStatusCode.OK, live.StatusCode);

            configuration["DatabaseStartup:Mode"] = "ValidateOnly";
            await using var validated = new SqlServerRelationalApiFactory(baseConnection,
                databaseName: initialized.DatabaseName, configurationOverrides: configuration);
            using var client = validated.CreateClientFor(TestUsers.CompanyA, TestCompanyIds.CompanyA);
            var stopwatch = Stopwatch.StartNew();
            using (var ready = await client.GetAsync("/health/ready"))
                Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
            stopwatch.Stop();
            output.WriteLine("ValidateOnly ready: active_contexts=17 elapsed_ms={0}", stopwatch.ElapsedMilliseconds);

            await using var scope = validated.Services.CreateAsyncScope();
            await using var connection = new SqlConnection(initialized.DatabaseConnectionString);
            await connection.OpenAsync();
            foreach (var (name, contextType) in DatabaseSchemaCompatibilityHealthCheck.GetActiveContexts(true))
            {
                var context = (DbContext)scope.ServiceProvider.GetRequiredService(contextType);
                var history = context.GetService<IHistoryRepository>();
                var applied = await history.GetAppliedMigrationsAsync();
                Assert.NotEmpty(applied);
                var latest = applied[^1];

                await ExecuteAsync(connection, history.GetDeleteScript(latest.MigrationId));
                try
                {
                    await AssertUnavailableAsync(validated, client, name);
                    Assert.DoesNotContain(await history.GetAppliedMigrationsAsync(),
                        row => row.MigrationId == latest.MigrationId);
                }
                finally
                {
                    await ExecuteAsync(connection, history.GetInsertScript(latest));
                }

                var future = new HistoryRow("99999999999999_ReadinessFuture", latest.ProductVersion);
                await ExecuteAsync(connection, history.GetInsertScript(future));
                try
                {
                    await AssertUnavailableAsync(validated, client, name);
                    Assert.Contains(await history.GetAppliedMigrationsAsync(),
                        row => row.MigrationId == future.MigrationId);
                }
                finally
                {
                    await ExecuteAsync(connection, history.GetDeleteScript(future.MigrationId));
                }

                using var restored = await client.GetAsync("/health/ready");
                Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
            }
        }
        finally
        {
            await initialized.DisposeAsync();
            using var databasePool = new SqlConnection(initialized.DatabaseConnectionString);
            SqlConnection.ClearPool(databasePool);
            // Only the random, disposable database created by this factory is removed.
            Assert.Matches("^goymen_erp_ci_[0-9a-f]{32}$", initialized.DatabaseName);
            await using var master = new SqlConnection(new SqlConnectionStringBuilder(baseConnection)
            {
                InitialCatalog = "master",
            }.ConnectionString);
            await master.OpenAsync();
            await using var drop = master.CreateCommand();
            drop.CommandText = $"""
                IF DB_ID(@name) IS NOT NULL
                BEGIN
                    ALTER DATABASE [{initialized.DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{initialized.DatabaseName}];
                END;
                """;
            drop.Parameters.AddWithValue("@name", initialized.DatabaseName);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static async Task AssertUnavailableAsync(
        SqlServerRelationalApiFactory factory, HttpClient client, string expectedContext)
    {
        var service = factory.Services.GetRequiredService<HealthCheckService>();
        var report = await service.CheckHealthAsync(registration => registration.Tags.Contains("ready"));
        Assert.Equal(HealthStatus.Unhealthy, report.Status);
        Assert.Equal(expectedContext,
            report.Entries[DatabaseSchemaCompatibilityHealthCheck.Name].Data["context"]);
        using var ready = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.DoesNotContain(factory.DatabaseName, await ready.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        using var business = await client.GetAsync("/api/v1/organization/companies");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, business.StatusCode);
    }

    private static async Task ExecuteAsync(SqlConnection connection, string script)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = script;
        await command.ExecuteNonQueryAsync();
    }
}
