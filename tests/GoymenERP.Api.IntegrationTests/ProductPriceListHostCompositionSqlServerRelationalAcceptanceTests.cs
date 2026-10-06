using System.Net;
using System.Text.RegularExpressions;
using GoymenERP.Api.ProductRuntime;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using GoymenERP.Modules.Product.Infrastructure.PriceLists;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit.Sdk;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Central host-only diagnostic: the normal disposable SQL host must initialize Product before the
/// price-list context. This creates no Product master, fixture, permission or grant.
/// </summary>
public sealed class ProductPriceListHostCompositionSqlServerRelationalAcceptanceTests
{
    private const string Marker = "CI_SAFE_DIAGNOSTIC:";
    private const int MaximumExceptionDepth = 8;

    [SqlServerFact]
    public async Task Product_runtime_and_price_list_migrations_initialize_in_one_disposable_host()
    {
        var baseConnection = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var databaseName = "goymen_erp_ci_" + Guid.NewGuid().ToString("N");
        var connection = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = databaseName,
        }.ConnectionString;
        var actor = Guid.NewGuid();
        var factory = new SqlServerRelationalApiFactory(baseConnection, services =>
        {
            services.RemoveAll<ProductRuntimeOptions>();
            services.AddSingleton(new ProductRuntimeOptions(
                connection,
                new ProductPilotIssuerBinding(actor, Guid.NewGuid()),
                new byte[32]));
        }, databaseName: databaseName);

        try
        {
            try
            {
                using var client = factory.CreateClient();
                using var health = await client.GetAsync("/health/live");
                if (health.StatusCode != HttpStatusCode.OK)
                {
                    throw new SafeDiagnosticException("product_price_list_startup_http_failure");
                }

                await using var scope = factory.Services.CreateAsyncScope();
                var productMigrations = await scope.ServiceProvider
                    .GetRequiredService<ProductDbContext>()
                    .Database.GetAppliedMigrationsAsync();
                var priceListMigrations = await scope.ServiceProvider
                    .GetRequiredService<ProductPriceListDbContext>()
                    .Database.GetAppliedMigrationsAsync();

                Assert.Contains("20260912175128_InitialProductDrafts", productMigrations);
                Assert.Contains("20260930113416_AddProductPriceLists", priceListMigrations);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (SafeDiagnosticException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new SafeDiagnosticException(Classify(exception));
            }
        }
        finally
        {
            await factory.DisposeAsync();
            if (!Regex.IsMatch(databaseName, "\\Agoymen_erp_ci_[0-9a-f]{32}\\z", RegexOptions.CultureInvariant))
            {
                throw new InvalidOperationException("Refusing cleanup outside the disposable SQL test database.");
            }

            var master = new SqlConnectionStringBuilder(connection) { InitialCatalog = "master" }.ConnectionString;
            await using var cleanupConnection = new SqlConnection(master);
            await cleanupConnection.OpenAsync();
            await using var command = cleanupConnection.CreateCommand();
            command.CommandText = $"IF DB_ID(N'{databaseName}') IS NOT NULL BEGIN ALTER DATABASE [{databaseName}] "
                + $"SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]; END;";
            await command.ExecuteNonQueryAsync();
        }
    }

    private static string Classify(Exception exception)
    {
        var current = exception;
        var sawInvalidOperation = false;
        for (var depth = 0; depth < MaximumExceptionDepth && current is not null; depth++, current = current.InnerException!)
        {
            if (current is SqlException sqlException)
            {
                return sqlException.Number switch
                {
                    208 or 547 or 1776 or 2601 or 2627 or 2714 => $"product_price_list_startup_sql_{sqlException.Number}",
                    _ => "product_price_list_startup_unknown",
                };
            }

            if (current is DbUpdateException)
            {
                return "product_price_list_startup_ef_db_update";
            }

            sawInvalidOperation |= current is InvalidOperationException;
        }

        return sawInvalidOperation
            ? "product_price_list_startup_invalid_operation"
            : "product_price_list_startup_unknown";
    }

    private sealed class SafeDiagnosticException(string code) : XunitException(Marker + code);
}
