using System.Net.Http.Json;
using System.Text.RegularExpressions;
using GoymenERP.Api.ProductRuntime;
using GoymenERP.Api.Shell;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Product's real shell capability provider through the real host on a real SQL Server: Product's four
/// screens and activation control follow the matching global Product authority. The grant is a synthetic, isolated Product master
/// assignment in this test's own database; it is not a Sinan or production grant and is never seeded.
/// </summary>
public sealed class ProductShellCapabilitySqlServerRelationalAcceptanceTests
{
    private static readonly Guid ActorId = Guid.Parse("20000000-0000-0000-0000-000000000003");
    private static readonly Guid ActorExternalId = Guid.Parse("21000000-0000-0000-0000-000000000003");
    private static readonly Guid SystemRoleId = Guid.Parse("30000000-0000-0000-0000-000000000003");
    private static readonly Guid CompanyA = Guid.Parse("10000000-0000-0000-0000-000000000001");

    private static readonly string[] ProductCapabilities =
    [
        "product.items.activate", "product.items.create", "product.items.deactivate", "product.items.detail",
        "product.items.edit", "product.items.list", "product.items.reactivate",
    ];

    [SqlServerFact]
    public async Task Product_screens_follow_the_global_Product_authority_through_the_real_provider()
    {
        var baseConnection = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var databaseName = "goymen_erp_ci_" + Guid.NewGuid().ToString("N");
        var connection = new SqlConnectionStringBuilder(baseConnection) { InitialCatalog = databaseName }.ConnectionString;
        // The real host with its real providers; only the Product runtime is configured, as the pilot is.
        var factory = new SqlServerRelationalApiFactory(baseConnection, services =>
        {
            services.RemoveAll<ProductRuntimeOptions>();
            services.AddSingleton(new ProductRuntimeOptions(connection,
                new ProductPilotIssuerBinding(ActorId, ActorExternalId), Enumerable.Range(1, 32).Select(value => (byte)value).ToArray()));
        }, databaseName: databaseName);
        try
        {
            using var startup = factory.CreateClient();
            using var administrator = factory.CreateClientFor("group-bootstrap-admin", CompanyA);
            using var companyOnly = factory.CreateClientFor("company-a-user", CompanyA);

            // No Product grant yet: the company administrator role alone opens no Product screen.
            Assert.Empty(ProductOnly(await ReadAsync(administrator)));

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                foreach (var operation in Enum.GetValues<ProductMasterOperation>())
                    identity.Set<ProductMasterAssignment>().Add(new ProductMasterAssignment(Guid.NewGuid(), ActorId,
                        SystemRoleId, operation, ActorId, ActorExternalId, Guid.NewGuid(),
                        "Synthetic isolated SQL fixture; not a Sinan or production grant", DateTimeOffset.UtcNow.AddMinutes(-1), null));
                await identity.SaveChangesAsync();
            }

            // Granted: all four screens plus the three lifecycle controls; the other actor, without a Product grant, none.
            Assert.Equal(ProductCapabilities, ProductOnly(await ReadAsync(administrator)));
            Assert.Empty(ProductOnly(await ReadAsync(companyOnly)));

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                var activation = await identity.Set<ProductMasterAssignment>().SingleAsync(row =>
                    row.UserId == ActorId && row.Operation == ProductMasterOperation.Activate && row.IsActive);
                activation.Revoke(Guid.NewGuid(), "Isolated Product activation revocation", DateTimeOffset.UtcNow);
                await identity.SaveChangesAsync();
            }

            // Only the approval control goes: the deactivation / reactivation grants are their own.
            Assert.Equal(
                [
                    "product.items.create", "product.items.deactivate", "product.items.detail", "product.items.edit",
                    "product.items.list", "product.items.reactivate",
                ],
                ProductOnly(await ReadAsync(administrator)));

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                foreach (var grant in await identity.Set<ProductMasterAssignment>().Where(row => row.UserId == ActorId && row.IsActive).ToArrayAsync())
                    grant.Revoke(Guid.NewGuid(), "Isolated Product shell revocation", DateTimeOffset.UtcNow);
                await identity.SaveChangesAsync();
            }

            // Revoked: none again, while the company administrator role stays assigned.
            Assert.Empty(ProductOnly(await ReadAsync(administrator)));
        }
        finally
        {
            await factory.DisposeAsync();
            if (!Regex.IsMatch(databaseName, "\\Agoymen_erp_ci_[0-9a-f]{32}\\z", RegexOptions.CultureInvariant))
                throw new InvalidOperationException("Refusing cleanup outside this test's generated database identity.");
            await using var master = new SqlConnection(new SqlConnectionStringBuilder(baseConnection) { InitialCatalog = "master" }.ConnectionString);
            await master.OpenAsync();
            await using var drop = master.CreateCommand();
            drop.CommandText = $"IF DB_ID(N'{databaseName}') IS NOT NULL BEGIN ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]; END;";
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static async Task<ShellCapabilitySnapshot> ReadAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<ShellCapabilitySnapshot>("/api/v1/shell/capabilities"))!;

    private static string[] ProductOnly(ShellCapabilitySnapshot snapshot) =>
        [.. snapshot.Capabilities.Where(capability => capability.StartsWith("product.", StringComparison.Ordinal)).Order(StringComparer.Ordinal)];
}
