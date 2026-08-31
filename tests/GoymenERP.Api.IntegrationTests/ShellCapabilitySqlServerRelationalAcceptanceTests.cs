using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using Microsoft.Data.SqlClient;

namespace GoymenERP.Api.IntegrationTests;

public sealed class ShellCapabilitySqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyAUserId = Guid.Parse(
        "20000000-0000-0000-0000-000000000001");

    private static readonly Guid OrganizationOperatorRoleId = Guid.Parse(
        "30000000-0000-0000-0000-000000000002");

    [SqlServerFact]
    public async Task ExactCompanySnapshot_TracksPersistedAuthorizationAndHasNoWriteSideEffects()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var factory = new SqlServerRelationalApiFactory(baseConnectionString);

        try
        {
            using (var healthClient = factory.CreateClient())
            using (var healthResponse = await healthClient.GetAsync("/health/live"))
            {
                Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);
            }

            await using var connection = new SqlConnection(factory.DatabaseConnectionString);
            await connection.OpenAsync();

            var auditCountBefore = await ScalarAsync<int>(
                connection,
                "SELECT COUNT(*) FROM [audit].[AuditEvents];");
            var commandExecutionCountBefore = await ScalarAsync<int>(
                connection,
                "SELECT COUNT(*) FROM [idempotency].[CommandExecutions];");

            using var companyAClient = factory.CreateClientFor(
                TestUsers.CompanyA,
                TestCompanyIds.CompanyA);
            using var companyBClient = factory.CreateClientFor(
                TestUsers.CompanyB,
                TestCompanyIds.CompanyB);

            var companyABefore = await ReadSnapshotAsync(companyAClient);
            var companyB = await ReadSnapshotAsync(companyBClient);

            AssertExactEmptySnapshot(companyABefore);
            AssertExactEmptySnapshot(companyB);
            Assert.NotEqual(companyABefore.Generation, companyB.Generation);
            Assert.DoesNotContain(
                TestCompanyIds.CompanyB.ToString("D"),
                companyABefore.RawJson,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                TestCompanyIds.CompanyA.ToString("D"),
                companyB.RawJson,
                StringComparison.OrdinalIgnoreCase);

            using (var foreignCompanyClient = factory.CreateClientFor(
                       TestUsers.CompanyA,
                       TestCompanyIds.CompanyB))
            using (var foreignCompanyResponse = await foreignCompanyClient.GetAsync(
                       "/api/v1/shell/capabilities"))
            {
                var foreignCompanyBody = await foreignCompanyResponse.Content.ReadAsStringAsync();

                Assert.Equal(HttpStatusCode.Forbidden, foreignCompanyResponse.StatusCode);
                Assert.DoesNotContain(
                    TestCompanyIds.CompanyB.ToString("D"),
                    foreignCompanyBody,
                    StringComparison.OrdinalIgnoreCase);
            }

            var assignmentCountBefore = await ScalarAsync<int>(
                connection,
                """
                SELECT COUNT(*)
                FROM [identity].[RoleAssignments]
                WHERE [UserId] = @UserId
                  AND [CompanyId] = @CompanyId
                  AND [Status] = N'Active';
                """,
                new SqlParameter("@UserId", CompanyAUserId),
                new SqlParameter("@CompanyId", TestCompanyIds.CompanyA));
            Assert.Equal(1, assignmentCountBefore);

            var permissionRowsDeleted = await ExecuteNonQueryAsync(
                connection,
                """
                DELETE FROM [identity].[RolePermissions]
                WHERE [RoleId] = @RoleId
                  AND [PermissionCode] = @PermissionCode;
                """,
                new SqlParameter("@RoleId", OrganizationOperatorRoleId),
                new SqlParameter(
                    "@PermissionCode",
                    ErpPermissions.OrganizationLocationsStatusUpdate));
            Assert.Equal(1, permissionRowsDeleted);

            Assert.Equal(
                assignmentCountBefore,
                await ScalarAsync<int>(
                    connection,
                    """
                    SELECT COUNT(*)
                    FROM [identity].[RoleAssignments]
                    WHERE [UserId] = @UserId
                      AND [CompanyId] = @CompanyId
                      AND [Status] = N'Active';
                    """,
                    new SqlParameter("@UserId", CompanyAUserId),
                    new SqlParameter("@CompanyId", TestCompanyIds.CompanyA)));

            var companyAAfter = await ReadSnapshotAsync(companyAClient);

            AssertExactEmptySnapshot(companyAAfter);
            Assert.NotEqual(companyABefore.Generation, companyAAfter.Generation);
            Assert.Equal(
                auditCountBefore,
                await ScalarAsync<int>(
                    connection,
                    "SELECT COUNT(*) FROM [audit].[AuditEvents];"));
            Assert.Equal(
                commandExecutionCountBefore,
                await ScalarAsync<int>(
                    connection,
                    "SELECT COUNT(*) FROM [idempotency].[CommandExecutions];"));
        }
        finally
        {
            await factory.DisposeAsync();
            await DropDatabaseAsync(baseConnectionString, factory.DatabaseName);
        }
    }

    private static async Task<CapabilitySnapshotProbe> ReadSnapshotAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/shell/capabilities");
        var rawJson = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        Assert.True(response.Headers.CacheControl.Private);

        using var document = JsonDocument.Parse(rawJson);
        var root = document.RootElement;
        Assert.Equal(3, root.EnumerateObject().Count());
        return new CapabilitySnapshotProbe(
            root.GetProperty("contractVersion").GetString()!,
            root.GetProperty("generation").GetString()!,
            root.GetProperty("capabilities").EnumerateArray()
                .Select(item => item.GetString()!)
                .ToArray(),
            rawJson);
    }

    private static void AssertExactEmptySnapshot(CapabilitySnapshotProbe snapshot)
    {
        Assert.Equal("SHELL-CAPABILITY-V1", snapshot.ContractVersion);
        Assert.StartsWith("hmac-sha256:", snapshot.Generation, StringComparison.Ordinal);
        Assert.Empty(snapshot.Capabilities);
        Assert.DoesNotContain("permission", snapshot.RawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("role", snapshot.RawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("scope", snapshot.RawJson, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<T> ScalarAsync<T>(
        SqlConnection connection,
        string commandText,
        params SqlParameter[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.Parameters.AddRange(parameters);
        var value = await command.ExecuteScalarAsync();
        return Assert.IsType<T>(value);
    }

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

    private static async Task DropDatabaseAsync(
        string baseConnectionString,
        string databaseName)
    {
        var builder = new SqlConnectionStringBuilder(baseConnectionString)
        {
            InitialCatalog = "master",
        };
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF DB_ID(N'{databaseName}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{databaseName}];
            END;
            """;
        await command.ExecuteNonQueryAsync();
    }

    private sealed record CapabilitySnapshotProbe(
        string ContractVersion,
        string Generation,
        IReadOnlyList<string> Capabilities,
        string RawJson);
}
