using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using GoymenERP.Api.Security;
using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using GoymenERP.Modules.Inventory.Infrastructure.Shell;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed class InventoryShellCapabilitySqlServerRelationalAcceptanceTests
{
    private const string DevelopmentUser = TestUsers.NoAssignment;
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;

    [SqlServerFact]
    public async Task Stock_position_capability_follows_persisted_Product_operational_selection_grant_and_revocation()
    {
        var baseConnection = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var factory = new SqlServerRelationalApiFactory(baseConnection);

        try
        {
            using (var startup = factory.CreateClient())
            using (var health = await startup.GetAsync("/health/live"))
                Assert.Equal(HttpStatusCode.OK, health.StatusCode);

            var roleId = await SeedInventoryReaderAsync(factory);
            using var reader = factory.CreateClientFor(DevelopmentUser, CompanyA);

            var beforeGrant = await ReadAsync(reader);
            Assert.DoesNotContain(InventoryShellCapabilityProvider.StockPositionsCapabilityId,
                beforeGrant.Capabilities);

            await using var connection = new SqlConnection(factory.DatabaseConnectionString);
            await connection.OpenAsync();
            Assert.Equal(1, await ExecuteNonQueryAsync(connection,
                """
                INSERT INTO [identity].[RolePermissions] ([RoleId], [PermissionCode])
                VALUES (@RoleId, @PermissionCode);
                """,
                new SqlParameter("@RoleId", roleId),
                new SqlParameter("@PermissionCode", ErpPermissions.ProductOperationalSelectionRead)));

            var granted = await ReadAsync(reader);
            Assert.Contains(InventoryShellCapabilityProvider.StockPositionsCapabilityId,
                granted.Capabilities);
            Assert.NotEqual(beforeGrant.Generation, granted.Generation);

            Assert.Equal(1, await ExecuteNonQueryAsync(connection,
                """
                DELETE FROM [identity].[RolePermissions]
                WHERE [RoleId] = @RoleId
                  AND [PermissionCode] = @PermissionCode;
                """,
                new SqlParameter("@RoleId", roleId),
                new SqlParameter("@PermissionCode", ErpPermissions.ProductOperationalSelectionRead)));

            var revoked = await ReadAsync(reader);
            Assert.DoesNotContain(InventoryShellCapabilityProvider.StockPositionsCapabilityId,
                revoked.Capabilities);
            Assert.NotEqual(granted.Generation, revoked.Generation);
        }
        finally
        {
            await factory.DisposeAsync();
            SqlConnection.ClearAllPools();
            await DropDatabaseAsync(baseConnection, factory.DatabaseName);
        }
    }

    private static async Task<Guid> SeedInventoryReaderAsync(SqlServerRelationalApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.True(identity.Database.IsSqlServer());

        var actorId = await identity.ExternalIdentities
            .Where(x => x.Issuer == BootstrapIdentityOptions.Issuer
                && x.Subject == DevelopmentUser)
            .Select(x => x.UserId)
            .SingleAsync();
        var roleId = Guid.NewGuid();
        var role = new RoleDefinition(roleId, $"ST01-INVENTORY-SHELL-{roleId:N}",
            "Synthetic company-scoped ST-01 shell reader", true);
        role.AddPermission(ErpPermissions.InventoryLabelsRead);
        role.AddPermission(ErpPermissions.InventoryBalancesRead);
        var assignment = new RoleAssignment(Guid.NewGuid(), actorId, roleId, CompanyA,
            DateTimeOffset.UtcNow.AddMinutes(-1), null, approvedByUserId: null);
        assignment.AddScope(AccessScopeType.Company, CompanyA);
        identity.Roles.Add(role);
        identity.RoleAssignments.Add(assignment);
        await identity.SaveChangesAsync();
        return roleId;
    }

    private static async Task<ShellCapabilitySnapshot> ReadAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<ShellCapabilitySnapshot>("/api/v1/shell/capabilities"))!;

    private static async Task<int> ExecuteNonQueryAsync(
        SqlConnection connection,
        string commandText,
        params SqlParameter[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.Parameters.AddRange(parameters);
        return await command.ExecuteNonQueryAsync();
    }

    private static async Task DropDatabaseAsync(string baseConnection, string databaseName)
    {
        if (!Regex.IsMatch(databaseName, "\\Agoymen_erp_ci_[0-9a-f]{32}\\z", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("Refusing cleanup outside this test's generated database identity.");

        await using var master = new SqlConnection(new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = "master",
        }.ConnectionString);
        await master.OpenAsync();
        await using var drop = master.CreateCommand();
        drop.CommandText = $"IF DB_ID(N'{databaseName}') IS NOT NULL BEGIN " +
                           $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
                           $"DROP DATABASE [{databaseName}]; END;";
        await drop.ExecuteNonQueryAsync();
    }
}
