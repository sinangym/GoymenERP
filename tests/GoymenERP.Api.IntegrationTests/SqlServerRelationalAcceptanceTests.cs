using System.Net;
using System.Net.Http.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
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
            await AssertAuditAppendOnlyAtomicityAndIsolationAsync(
                factory,
                factory.DatabaseConnectionString);
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
            1,
            await ScalarAsync<int>(
                connection,
                "SELECT COUNT(*) FROM [audit].[__EFMigrationsHistory];"));
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
        Assert.Equal(
            0,
            await ScalarAsync<int>(connection, "SELECT COUNT(*) FROM [audit].[AuditEvents];"));
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
            new SetLocationActiveState(
                !viewerLocation.IsActive,
                viewerLocation.Version,
                "Yetkisiz ilişkisel yazma testi"));

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

    private static async Task AssertAuditAppendOnlyAtomicityAndIsolationAsync(
        SqlServerRelationalApiFactory factory,
        string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var companyALocation = await ReadLocationProbeAsync(
            connection,
            """
            SELECT TOP (1) [Id], [IsActive], [Version]
            FROM [organization].[Locations]
            WHERE [CompanyId] = @CompanyId AND [Code] <> N'OSM-H0'
            ORDER BY [Code] DESC;
            """,
            new SqlParameter("@CompanyId", TestCompanyIds.CompanyA));
        var rejectedCorrelationId = $"audit-reject-{Guid.NewGuid():N}";

        await ExecuteNonQueryAsync(
            connection,
            """
            CREATE TRIGGER [audit].[TR_AuditEvents_RejectInsertProbe]
            ON [audit].[AuditEvents]
            AFTER INSERT
            AS
            BEGIN
                SET NOCOUNT ON;
                THROW 51001, 'Relational atomicity rejection probe.', 1;
            END;
            """);

        HttpResponseMessage rejectedResponse;
        try
        {
            using var rejectedClient = factory.CreateClientFor(
                TestUsers.CompanyA,
                TestCompanyIds.CompanyA);
            rejectedClient.DefaultRequestHeaders.Remove("X-Correlation-Id");
            rejectedClient.DefaultRequestHeaders.Add(
                "X-Correlation-Id",
                rejectedCorrelationId);
            rejectedResponse = await rejectedClient.PutAsJsonAsync(
                $"/api/v1/organization/locations/{companyALocation.Id}/active-state",
                new SetLocationActiveState(
                    !companyALocation.IsActive,
                    companyALocation.Version,
                    "Atomik audit rollback testi"));
        }
        finally
        {
            await ExecuteNonQueryAsync(
                connection,
                "DROP TRIGGER [audit].[TR_AuditEvents_RejectInsertProbe];");
        }

        Assert.Equal(HttpStatusCode.InternalServerError, rejectedResponse.StatusCode);
        var afterRejectedCommand = await ReadLocationProbeAsync(
            connection,
            """
            SELECT [Id], [IsActive], [Version]
            FROM [organization].[Locations]
            WHERE [Id] = @Id;
            """,
            new SqlParameter("@Id", companyALocation.Id));
        Assert.Equal(companyALocation, afterRejectedCommand);
        Assert.Equal(
            0,
            await ScalarAsync<int>(
                connection,
                "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [CorrelationId] = @CorrelationId;",
                new SqlParameter("@CorrelationId", rejectedCorrelationId)));

        var correlationId = $"audit-success-{Guid.NewGuid():N}";
        var causationId = $"http-{Guid.NewGuid():N}";
        using var companyAClient = factory.CreateClientFor(
            TestUsers.CompanyA,
            TestCompanyIds.CompanyA);
        companyAClient.DefaultRequestHeaders.Remove("X-Correlation-Id");
        companyAClient.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);
        companyAClient.DefaultRequestHeaders.Add("X-Causation-Id", causationId);
        var successResponse = await companyAClient.PutAsJsonAsync(
            $"/api/v1/organization/locations/{companyALocation.Id}/active-state",
            new SetLocationActiveState(
                !companyALocation.IsActive,
                companyALocation.Version,
                "SQL Server kalıcı audit kabul testi"));

        Assert.Equal(HttpStatusCode.NoContent, successResponse.StatusCode);
        Assert.Equal(
            1,
            await ScalarAsync<int>(
                connection,
                "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [CorrelationId] = @CorrelationId;",
                new SqlParameter("@CorrelationId", correlationId)));
        Assert.Equal(
            TestCompanyIds.CompanyA,
            await ScalarAsync<Guid>(
                connection,
                "SELECT [CompanyId] FROM [audit].[AuditEvents] WHERE [CorrelationId] = @CorrelationId;",
                new SqlParameter("@CorrelationId", correlationId)));
        Assert.Equal(
            "SQL Server kalıcı audit kabul testi",
            await ScalarAsync<string>(
                connection,
                "SELECT [Reason] FROM [audit].[AuditEvents] WHERE [CorrelationId] = @CorrelationId;",
                new SqlParameter("@CorrelationId", correlationId)));
        Assert.Equal(
            causationId,
            await ScalarAsync<string>(
                connection,
                "SELECT [CausationId] FROM [audit].[AuditEvents] WHERE [CorrelationId] = @CorrelationId;",
                new SqlParameter("@CorrelationId", correlationId)));
        Assert.Equal(
            64,
            (await ScalarAsync<string>(
                connection,
                "SELECT [IntegrityHash] FROM [audit].[AuditEvents] WHERE [CorrelationId] = @CorrelationId;",
                new SqlParameter("@CorrelationId", correlationId))).Length);

        var afterSuccessfulCommand = await ReadLocationProbeAsync(
            connection,
            """
            SELECT [Id], [IsActive], [Version]
            FROM [organization].[Locations]
            WHERE [Id] = @Id;
            """,
            new SqlParameter("@Id", companyALocation.Id));
        using var noChangeClient = factory.CreateClientFor(
            TestUsers.CompanyA,
            TestCompanyIds.CompanyA);
        var noChangeResponse = await noChangeClient.PutAsJsonAsync(
            $"/api/v1/organization/locations/{companyALocation.Id}/active-state",
            new SetLocationActiveState(
                afterSuccessfulCommand.IsActive,
                afterSuccessfulCommand.Version,
                "No-change audit ve keyset testi"));
        Assert.Equal(HttpStatusCode.NoContent, noChangeResponse.StatusCode);

        var updateException = await Assert.ThrowsAsync<SqlException>(() => ExecuteNonQueryAsync(
            connection,
            "UPDATE [audit].[AuditEvents] SET [Reason] = N'Değiştirildi' WHERE [CorrelationId] = @CorrelationId;",
            new SqlParameter("@CorrelationId", correlationId)));
        Assert.Equal(51000, updateException.Number);
        var deleteException = await Assert.ThrowsAsync<SqlException>(() => ExecuteNonQueryAsync(
            connection,
            "DELETE FROM [audit].[AuditEvents] WHERE [CorrelationId] = @CorrelationId;",
            new SqlParameter("@CorrelationId", correlationId)));
        Assert.Equal(51000, deleteException.Number);

        var companyBLocation = await ReadLocationProbeAsync(
            connection,
            """
            SELECT TOP (1) [Id], [IsActive], [Version]
            FROM [organization].[Locations]
            WHERE [CompanyId] = @CompanyId
            ORDER BY [Code] DESC;
            """,
            new SqlParameter("@CompanyId", TestCompanyIds.CompanyB));
        var companyBCorrelationId = $"audit-company-b-{Guid.NewGuid():N}";
        using var companyBClient = factory.CreateClientFor(
            TestUsers.CompanyB,
            TestCompanyIds.CompanyB);
        companyBClient.DefaultRequestHeaders.Remove("X-Correlation-Id");
        companyBClient.DefaultRequestHeaders.Add("X-Correlation-Id", companyBCorrelationId);
        var companyBWrite = await companyBClient.PutAsJsonAsync(
            $"/api/v1/organization/locations/{companyBLocation.Id}/active-state",
            new SetLocationActiveState(
                !companyBLocation.IsActive,
                companyBLocation.Version,
                "Audit şirket izolasyonu ilişkisel testi"));
        Assert.Equal(HttpStatusCode.NoContent, companyBWrite.StatusCode);

        using var adminAClient = factory.CreateClientFor(
            TestUsers.GroupAdmin,
            TestCompanyIds.CompanyA);
        var companyBProbeFromA = await adminAClient.GetFromJsonAsync<AuditEventPage>(
            $"/api/v1/audit/events?correlationId={companyBCorrelationId}");
        Assert.Empty(Assert.IsType<AuditEventPage>(companyBProbeFromA).Items);
        using var adminBClient = factory.CreateClientFor(
            TestUsers.GroupAdmin,
            TestCompanyIds.CompanyB);
        var companyBResult = await adminBClient.GetFromJsonAsync<AuditEventPage>(
            $"/api/v1/audit/events?correlationId={companyBCorrelationId}");
        Assert.Equal(
            TestCompanyIds.CompanyB,
            Assert.Single(Assert.IsType<AuditEventPage>(companyBResult).Items).CompanyId);

        var firstPage = await adminAClient.GetFromJsonAsync<AuditEventPage>(
            "/api/v1/audit/events?pageSize=1");
        var firstPageResult = Assert.IsType<AuditEventPage>(firstPage);
        Assert.Single(firstPageResult.Items);
        Assert.NotNull(firstPageResult.NextCursor);
        var secondPage = await adminAClient.GetFromJsonAsync<AuditEventPage>(
            $"/api/v1/audit/events?pageSize=1&cursor={Uri.EscapeDataString(firstPageResult.NextCursor)}");
        Assert.NotEqual(
            Assert.Single(firstPageResult.Items).Id,
            Assert.Single(Assert.IsType<AuditEventPage>(secondPage).Items).Id);
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

    private static async Task ExecuteNonQueryAsync(
        SqlConnection connection,
        string commandText,
        params SqlParameter[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
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
                ["BootstrapIdentity:Users:3:UserId"] = TestUsers.GroupAdmin,
                ["BootstrapIdentity:Users:3:DisplayName"] = "Grup SQL Test Yöneticisi",
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
