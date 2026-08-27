using System.Net;
using System.Net.Http.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Organization.Domain.Sites;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed class SqlServerRelationalAcceptanceTests
{
    public const string ConnectionStringEnvironmentVariable =
        "GOYMEN_ERP_TEST_SQLSERVER_CONNECTION";

    [SqlServerFact]
    public async Task MigrationSeedWriteGuardCompositeForeignKeyAndHttpIsolation_AreEnforced()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            ConnectionStringEnvironmentVariable)!;
        var factory = new SqlServerRelationalApiFactory(baseConnectionString);

        try
        {
            using var healthClient = factory.CreateClient();
            var healthResponse = await healthClient.GetAsync("/health/live");
            Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);

            await AssertMigrationAndSeedAsync(factory.DatabaseConnectionString);
            await AssertIdentityRelationalConstraintsAsync(factory.DatabaseConnectionString);
            await AssertTenantWriteGuardAsync(factory);
            await AssertCompositeForeignKeyAsync(factory.DatabaseConnectionString);
            await AssertHttpIsolationAsync(factory, factory.DatabaseConnectionString);
        }
        finally
        {
            await factory.DisposeAsync();
            await DropDatabaseAsync(baseConnectionString, factory.DatabaseName);
        }
    }

    private static async Task AssertMigrationAndSeedAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        Assert.Equal(
            1,
            await ScalarAsync<int>(
                connection,
                "SELECT COUNT(*) FROM [organization].[__EFMigrationsHistory];"));
        Assert.Equal(
            1,
            await ScalarAsync<int>(
                connection,
                "SELECT COUNT(*) FROM [identity].[__EFMigrationsHistory];"));
        Assert.Equal(
            2,
            await ScalarAsync<int>(connection, "SELECT COUNT(*) FROM [organization].[Companies];"));
        Assert.Equal(
            6,
            await ScalarAsync<int>(connection, "SELECT COUNT(*) FROM [organization].[Sites];"));
        Assert.Equal(
            30,
            await ScalarAsync<int>(connection, "SELECT COUNT(*) FROM [organization].[Locations];"));
        Assert.Equal(
            10,
            await ScalarAsync<int>(connection, "SELECT COUNT(*) FROM [organization].[WorkCenters];"));
        Assert.Equal(
            4,
            await ScalarAsync<int>(connection, "SELECT COUNT(*) FROM [identity].[Users];"));
        Assert.Equal(
            5,
            await ScalarAsync<int>(connection, "SELECT COUNT(*) FROM [identity].[Roles];"));
        Assert.Equal(
            5,
            await ScalarAsync<int>(connection, "SELECT COUNT(*) FROM [identity].[RoleAssignments];"));
    }

    private static async Task AssertIdentityRelationalConstraintsAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var assignmentId = Guid.Parse("40000000-0000-0000-0000-000000000001");

        await using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO [identity].[RoleAssignmentScopes]
                ([RoleAssignmentId], [CompanyId], [ScopeType], [ScopeId])
            VALUES
                (@RoleAssignmentId, @CompanyId, N'Company', @WrongCompanyId);
            """;
        insert.Parameters.AddWithValue("@RoleAssignmentId", assignmentId);
        insert.Parameters.AddWithValue("@CompanyId", TestCompanyIds.CompanyA);
        insert.Parameters.AddWithValue("@WrongCompanyId", TestCompanyIds.CompanyB);

        var exception = await Assert.ThrowsAsync<SqlException>(
            () => insert.ExecuteNonQueryAsync());

        Assert.Equal(547, exception.Number);

        await using var selfApprovalInsert = connection.CreateCommand();
        selfApprovalInsert.CommandText = """
            INSERT INTO [identity].[AccessGrantRequests]
                ([Id], [RequestedByUserId], [TargetUserId], [RoleId], [CompanyId],
                 [RequiresSecondApproval], [Status], [RequestedAtUtc],
                 [DecidedByUserId], [DecidedAtUtc], [Version])
            VALUES
                (@Id, @RequesterId, @TargetId, @RoleId, @CompanyId,
                 1, N'Approved', SYSUTCDATETIME(),
                 @RequesterId, SYSUTCDATETIME(), 1);
            """;
        selfApprovalInsert.Parameters.AddWithValue("@Id", Guid.NewGuid());
        selfApprovalInsert.Parameters.AddWithValue(
            "@RequesterId",
            Guid.Parse("20000000-0000-0000-0000-000000000001"));
        selfApprovalInsert.Parameters.AddWithValue(
            "@TargetId",
            Guid.Parse("20000000-0000-0000-0000-000000000004"));
        selfApprovalInsert.Parameters.AddWithValue(
            "@RoleId",
            Guid.Parse("30000000-0000-0000-0000-000000000001"));
        selfApprovalInsert.Parameters.AddWithValue("@CompanyId", TestCompanyIds.CompanyA);

        var selfApprovalException = await Assert.ThrowsAsync<SqlException>(
            () => selfApprovalInsert.ExecuteNonQueryAsync());

        Assert.Equal(547, selfApprovalException.Number);
    }

    private static async Task AssertTenantWriteGuardAsync(
        SqlServerRelationalApiFactory factory)
    {
        await using (var unresolvedScope = factory.Services.CreateAsyncScope())
        {
            var dbContext = unresolvedScope.ServiceProvider
                .GetRequiredService<OrganizationDbContext>();
            dbContext.Sites.Add(CreateSite(TestCompanyIds.CompanyA));

            var unresolvedException = await Assert.ThrowsAsync<InvalidOperationException>(
                () => dbContext.SaveChangesAsync());
            Assert.Equal(
                "Organizasyon yazma işlemi için aktif şirket bağlamı zorunludur.",
                unresolvedException.Message);
        }

        await using (var mismatchedScope = factory.Services.CreateAsyncScope())
        {
            var companyContext = mismatchedScope.ServiceProvider
                .GetRequiredService<ICompanyContext>();
            companyContext.Set(TestCompanyIds.CompanyA);
            var dbContext = mismatchedScope.ServiceProvider
                .GetRequiredService<OrganizationDbContext>();
            dbContext.Sites.Add(CreateSite(TestCompanyIds.CompanyB));

            var mismatchedException = await Assert.ThrowsAsync<InvalidOperationException>(
                () => dbContext.SaveChangesAsync());
            Assert.Equal(
                "Organizasyon kaydının CompanyId değeri aktif şirket bağlamıyla eşleşmelidir.",
                mismatchedException.Message);
        }
    }

    private static async Task AssertCompositeForeignKeyAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var companyBSiteId = await ScalarAsync<Guid>(
            connection,
            """
            SELECT TOP (1) [Id]
            FROM [organization].[Sites]
            WHERE [CompanyId] = @CompanyId
            ORDER BY [Code];
            """,
            new SqlParameter("@CompanyId", TestCompanyIds.CompanyB));
        var rejectedLocationId = Guid.NewGuid();

        await using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO [organization].[Locations]
                ([Id], [SiteId], [IsExternal], [CompanyId], [Code], [Name],
                 [IsActive], [Version], [CreatedAtUtc], [UpdatedAtUtc])
            VALUES
                (@Id, @SiteId, 0, @CompanyId, @Code, @Name,
                 1, 1, SYSUTCDATETIME(), SYSUTCDATETIME());
            """;
        insert.Parameters.AddWithValue("@Id", rejectedLocationId);
        insert.Parameters.AddWithValue("@SiteId", companyBSiteId);
        insert.Parameters.AddWithValue("@CompanyId", TestCompanyIds.CompanyA);
        insert.Parameters.AddWithValue("@Code", $"FK-REJECT-{Guid.NewGuid():N}");
        insert.Parameters.AddWithValue("@Name", "Cross-company FK rejection probe");

        var exception = await Assert.ThrowsAsync<SqlException>(
            () => insert.ExecuteNonQueryAsync());

        Assert.Equal(547, exception.Number);
        Assert.Equal(
            0,
            await ScalarAsync<int>(
                connection,
                "SELECT COUNT(*) FROM [organization].[Locations] WHERE [Id] = @Id;",
                new SqlParameter("@Id", rejectedLocationId)));
    }

    private static async Task AssertHttpIsolationAsync(
        SqlServerRelationalApiFactory factory,
        string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var companyBLocationId = await ScalarAsync<Guid>(
            connection,
            """
            SELECT TOP (1) [Id]
            FROM [organization].[Locations]
            WHERE [CompanyId] = @CompanyId
            ORDER BY [Code];
            """,
            new SqlParameter("@CompanyId", TestCompanyIds.CompanyB));

        using var companyAClient = factory.CreateClientFor(
            TestUsers.CompanyA,
            TestCompanyIds.CompanyA);
        var directLookup = await companyAClient.GetAsync(
            $"/api/v1/organization/locations/{companyBLocationId}");
        var directLookupBody = await directLookup.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, directLookup.StatusCode);
        Assert.DoesNotContain(
            companyBLocationId.ToString(),
            directLookupBody,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            TestCompanyIds.CompanyB.ToString(),
            directLookupBody,
            StringComparison.OrdinalIgnoreCase);

        using var unauthorizedClient = factory.CreateClientFor(
            TestUsers.CompanyA,
            TestCompanyIds.CompanyB);
        var unauthorizedSelection = await unauthorizedClient.GetAsync(
            "/api/v1/organization/locations");
        var unauthorizedBody = await unauthorizedSelection.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Forbidden, unauthorizedSelection.StatusCode);
        Assert.DoesNotContain(
            TestCompanyIds.CompanyB.ToString(),
            unauthorizedBody,
            StringComparison.OrdinalIgnoreCase);

        var viewerLocation = await ReadLocationProbeAsync(
            connection,
            """
            SELECT TOP (1) [Id], [IsActive], [Version]
            FROM [organization].[Locations]
            WHERE [CompanyId] = @CompanyId AND [Code] = N'OSM-H0';
            """,
            new SqlParameter("@CompanyId", TestCompanyIds.CompanyA));
        using var viewerClient = factory.CreateClientFor(
            TestUsers.CompanyAViewer,
            TestCompanyIds.CompanyA);
        var viewerLocations = await viewerClient.GetFromJsonAsync<PageResult<LocationSummary>>(
            "/api/v1/organization/locations?pageSize=100");
        var viewerWrite = await viewerClient.PutAsJsonAsync(
            $"/api/v1/organization/locations/{viewerLocation.Id}/active-state",
            new SetLocationActiveState(!viewerLocation.IsActive, viewerLocation.Version));

        Assert.Equal(8, Assert.IsType<PageResult<LocationSummary>>(viewerLocations).TotalCount);
        Assert.Equal(HttpStatusCode.Forbidden, viewerWrite.StatusCode);
    }

    private static Site CreateSite(Guid companyId)
    {
        return new Site(
            Guid.NewGuid(),
            companyId,
            $"REL-{Guid.NewGuid():N}",
            "Relational write guard probe",
            SiteType.Factory);
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

    private static async Task<LocationProbe> ReadLocationProbeAsync(
        SqlConnection connection,
        string commandText,
        params SqlParameter[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.Parameters.AddRange(parameters);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new LocationProbe(
            reader.GetGuid(0),
            reader.GetBoolean(1),
            reader.GetInt64(2));
    }

    private sealed record LocationProbe(Guid Id, bool IsActive, long Version);

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
}

public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(
                SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)))
        {
            Skip = $"{SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable} tanımlı değil.";
        }
    }
}

internal sealed class SqlServerRelationalApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseConnectionString;

    public SqlServerRelationalApiFactory(string baseConnectionString)
    {
        DatabaseName = $"goymen_erp_ci_{Guid.NewGuid():N}";
        var builder = new SqlConnectionStringBuilder(baseConnectionString)
        {
            InitialCatalog = DatabaseName,
        };
        _databaseConnectionString = builder.ConnectionString;
    }

    public string DatabaseName { get; }

    public string DatabaseConnectionString => _databaseConnectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("DB_CONNECTION_STRING", _databaseConnectionString);
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Mode"] = "DevelopmentBootstrap",
                ["BootstrapIdentity:HeaderName"] = "X-Development-User",
                ["BootstrapIdentity:Users:0:UserId"] = TestUsers.CompanyA,
                ["BootstrapIdentity:Users:0:DisplayName"] = "Şirket A SQL Test Kullanıcısı",
                ["BootstrapIdentity:Users:1:UserId"] = TestUsers.CompanyB,
                ["BootstrapIdentity:Users:1:DisplayName"] = "Şirket B SQL Test Kullanıcısı",
                ["BootstrapIdentity:Users:2:UserId"] = TestUsers.CompanyAViewer,
                ["BootstrapIdentity:Users:2:DisplayName"] = "Şirket A SQL Salt Okuma Kullanıcısı",
            });
        });
    }

    public HttpClient CreateClientFor(string userId, Guid selectedCompanyId)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Development-User", userId);
        client.DefaultRequestHeaders.Add("X-Company-Id", selectedCompanyId.ToString());
        client.DefaultRequestHeaders.Add("X-Correlation-Id", Guid.NewGuid().ToString("N"));
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        return client;
    }
}
