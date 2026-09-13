using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Organization.Domain.Sites;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
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
            await AssertIdempotencyConcurrencyAndAtomicityAsync(
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
            3,
            await ScalarAsync<int>(
                connection,
                "SELECT COUNT(*) FROM [organization].[__EFMigrationsHistory];"));
        Assert.Equal(
            2,
            await ScalarAsync<int>(
                connection,
                "SELECT COUNT(*) FROM [identity].[__EFMigrationsHistory];"));
        Assert.Equal(
            2,
            await ScalarAsync<int>(
                connection,
                "SELECT COUNT(*) FROM [audit].[__EFMigrationsHistory];"));
        Assert.Equal(1, await ScalarAsync<int>(connection,
            "SELECT COUNT(*) FROM [identity].[__EFMigrationsHistory] WHERE [MigrationId] = '20260912175120_AddExplicitProductAssignments';"));
        Assert.Equal(1, await ScalarAsync<int>(connection,
            "SELECT COUNT(*) FROM [audit].[__EFMigrationsHistory] WHERE [MigrationId] = '20260912173914_AddGlobalProductAuditSubject';"));
        Assert.Equal(0, await ScalarAsync<int>(connection,
            "SELECT COUNT(*) FROM [identity].[ProductMasterAssignments];"));
        Assert.Equal(
            1,
            await ScalarAsync<int>(
                connection,
                "SELECT COUNT(*) FROM [idempotency].[__EFMigrationsHistory];"));
        Assert.Equal(
            3,
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
            5,
            await ScalarAsync<int>(connection, "SELECT COUNT(*) FROM [identity].[Users];"));
        Assert.Equal(
            5,
            await ScalarAsync<int>(connection, "SELECT COUNT(*) FROM [identity].[Roles];"));
        Assert.Equal(
            7,
            await ScalarAsync<int>(connection, "SELECT COUNT(*) FROM [identity].[RoleAssignments];"));
        await AssertTopologyCountsAsync(
            connection,
            TestCompanyIds.CompanyA,
            expectedSites: 3,
            expectedLocations: 15,
            expectedWorkCenters: 5);
        await AssertTopologyCountsAsync(
            connection,
            TestCompanyIds.CompanyB,
            expectedSites: 3,
            expectedLocations: 15,
            expectedWorkCenters: 5);
        await AssertTopologyCountsAsync(
            connection,
            TestCompanyIds.CompanyC,
            expectedSites: 0,
            expectedLocations: 0,
            expectedWorkCenters: 0);
        Assert.Equal(
            1,
            await ScalarAsync<int>(
                connection,
                "SELECT COUNT(*) FROM [identity].[RoleAssignments] WHERE [CompanyId] = @CompanyId;",
                new SqlParameter("@CompanyId", TestCompanyIds.CompanyC)));
        Assert.Equal(
            0,
            await ScalarAsync<int>(connection, "SELECT COUNT(*) FROM [audit].[AuditEvents];"));
        Assert.Equal(
            0,
            await ScalarAsync<int>(
                connection,
                "SELECT COUNT(*) FROM [idempotency].[CommandExecutions];"));
    }

    private static async Task AssertTopologyCountsAsync(
        SqlConnection connection,
        Guid companyId,
        int expectedSites,
        int expectedLocations,
        int expectedWorkCenters)
    {
        var companyIdParameter = new SqlParameter("@CompanyId", companyId);
        Assert.Equal(
            expectedSites,
            await ScalarAsync<int>(
                connection,
                "SELECT COUNT(*) FROM [organization].[Sites] WHERE [CompanyId] = @CompanyId;",
                companyIdParameter));
        Assert.Equal(
            expectedLocations,
            await ScalarAsync<int>(
                connection,
                "SELECT COUNT(*) FROM [organization].[Locations] WHERE [CompanyId] = @CompanyId;",
                new SqlParameter("@CompanyId", companyId)));
        Assert.Equal(
            expectedWorkCenters,
            await ScalarAsync<int>(
                connection,
                "SELECT COUNT(*) FROM [organization].[WorkCenters] WHERE [CompanyId] = @CompanyId;",
                new SqlParameter("@CompanyId", companyId)));
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

        await using (var persistedAuditScope = factory.Services.CreateAsyncScope())
        {
            persistedAuditScope.ServiceProvider
                .GetRequiredService<ICompanyContext>()
                .Set(TestCompanyIds.CompanyA);
            var auditDbContext = persistedAuditScope.ServiceProvider
                .GetRequiredService<AuditDbContext>();
            var persistedAuditEvent = await auditDbContext.AuditEvents
                .AsNoTracking()
                .SingleAsync(x => x.CorrelationId == correlationId);
            Assert.Equal(
                AuditEventIntegrity.Compute(persistedAuditEvent.ToIntegrityEnvelope()),
                persistedAuditEvent.IntegrityHash);
        }

        using var siteAuditReader = factory.CreateClientFor(
            TestUsers.SiteAuditReader,
            TestCompanyIds.CompanyA);
        var siteScopedAuditRead = await siteAuditReader.GetAsync("/api/v1/audit/events");
        Assert.Equal(HttpStatusCode.Forbidden, siteScopedAuditRead.StatusCode);

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

    private static async Task AssertIdempotencyConcurrencyAndAtomicityAsync(
        SqlServerRelationalApiFactory factory,
        string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var replayLocation = await ReadLocationProbeAsync(
            connection,
            """
            SELECT [Id], [IsActive], [Version]
            FROM [organization].[Locations]
            WHERE [CompanyId] = @CompanyId AND [Code] = N'EXT-DOGRULUK';
            """,
            new SqlParameter("@CompanyId", TestCompanyIds.CompanyA));
        const string replayKey = "sql-idempotency-replay";
        var replayKeyHash = IdempotencyHash.ComputeKey(replayKey);
        var firstCorrelation = $"sql-idem-first-{Guid.NewGuid():N}";
        var replayCorrelation = $"sql-idem-replay-{Guid.NewGuid():N}";
        var replayCommand = new SetLocationActiveState(
            !replayLocation.IsActive,
            replayLocation.Version,
            "SQL idempotency replay testi");
        using var firstClient = factory.CreateClientFor(
            TestUsers.CompanyA,
            TestCompanyIds.CompanyA);
        SetRequestIdentity(firstClient, replayKey, firstCorrelation);
        var firstResponse = await firstClient.PutAsJsonAsync(
            $"/api/v1/organization/locations/{replayLocation.Id}/active-state",
            replayCommand);
        using var replayClient = factory.CreateClientFor(
            TestUsers.CompanyA,
            TestCompanyIds.CompanyA);
        SetRequestIdentity(replayClient, replayKey, replayCorrelation);
        var replayResponse = await replayClient.PutAsJsonAsync(
            $"/api/v1/organization/locations/{replayLocation.Id}/active-state",
            replayCommand);
        using var payloadConflictClient = factory.CreateClientFor(
            TestUsers.CompanyA,
            TestCompanyIds.CompanyA);
        var payloadConflictCorrelation = $"sql-idem-conflict-{Guid.NewGuid():N}";
        SetRequestIdentity(payloadConflictClient, replayKey, payloadConflictCorrelation);
        var payloadConflict = await payloadConflictClient.PutAsJsonAsync(
            $"/api/v1/organization/locations/{replayLocation.Id}/active-state",
            replayCommand with { Reason = "Farklı SQL payload" });
        var replayLocationAfter = await ReadLocationProbeAsync(
            connection,
            """
            SELECT [Id], [IsActive], [Version]
            FROM [organization].[Locations]
            WHERE [Id] = @Id;
            """,
            new SqlParameter("@Id", replayLocation.Id));

        Assert.Equal(HttpStatusCode.NoContent, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, replayResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, payloadConflict.StatusCode);
        Assert.Equal(
            "true",
            Assert.Single(replayResponse.Headers.GetValues("Idempotency-Replayed")));
        Assert.Equal(replayLocation.Version + 1, replayLocationAfter.Version);
        Assert.Equal(
            1,
            await ScalarAsync<int>(
                connection,
                "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [IdempotencyKeyHash] = @KeyHash;",
                new SqlParameter("@KeyHash", replayKeyHash)));
        Assert.Equal(
            (int)HttpStatusCode.NoContent,
            await ScalarAsync<int>(
                connection,
                "SELECT [HttpStatusCode] FROM [idempotency].[CommandExecutions] WHERE [IdempotencyKeyHash] = @KeyHash;",
                new SqlParameter("@KeyHash", replayKeyHash)));
        Assert.Contains(
            "\"StatusCode\":204",
            await ScalarAsync<string>(
                connection,
                "SELECT [ResponseJson] FROM [idempotency].[CommandExecutions] WHERE [IdempotencyKeyHash] = @KeyHash;",
                new SqlParameter("@KeyHash", replayKeyHash)),
            StringComparison.Ordinal);
        Assert.Equal(
            1,
            await ScalarAsync<int>(
                connection,
                "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [CorrelationId] = @CorrelationId;",
                new SqlParameter("@CorrelationId", firstCorrelation)));
        Assert.Equal(
            0,
            await ScalarAsync<int>(
                connection,
                "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [CorrelationId] IN (@Replay, @Conflict);",
                new SqlParameter("@Replay", replayCorrelation),
                new SqlParameter("@Conflict", payloadConflictCorrelation)));

        var scopeAllowedLocation = await ReadLocationProbeAsync(
            connection,
            "SELECT [Id], [IsActive], [Version] FROM [organization].[Locations] WHERE [CompanyId] = @CompanyId AND [Code] = N'GAZ-DEP';",
            new SqlParameter("@CompanyId", TestCompanyIds.CompanyA));
        var companyAOperatorAssignmentId = Guid.Parse(
            "40000000-0000-0000-0000-000000000001");
        await ExecuteNonQueryAsync(
            connection,
            "DELETE FROM [identity].[RoleAssignmentScopes] WHERE [RoleAssignmentId] = @AssignmentId;",
            new SqlParameter("@AssignmentId", companyAOperatorAssignmentId));
        await ExecuteNonQueryAsync(
            connection,
            "INSERT INTO [identity].[RoleAssignmentScopes] ([RoleAssignmentId], [CompanyId], [ScopeType], [ScopeId]) VALUES (@AssignmentId, @CompanyId, N'Location', @ScopeId);",
            new SqlParameter("@AssignmentId", companyAOperatorAssignmentId),
            new SqlParameter("@CompanyId", TestCompanyIds.CompanyA),
            new SqlParameter("@ScopeId", scopeAllowedLocation.Id));
        HttpResponseMessage deniedReplayResponse;
        var deniedReplayCorrelation = $"sql-idem-scope-denied-{Guid.NewGuid():N}";
        try
        {
            using var deniedReplayClient = factory.CreateClientFor(
                TestUsers.CompanyA,
                TestCompanyIds.CompanyA);
            SetRequestIdentity(deniedReplayClient, replayKey, deniedReplayCorrelation);
            deniedReplayResponse = await deniedReplayClient.PutAsJsonAsync(
                $"/api/v1/organization/locations/{replayLocation.Id}/active-state",
                replayCommand);
        }
        finally
        {
            await ExecuteNonQueryAsync(
                connection,
                "DELETE FROM [identity].[RoleAssignmentScopes] WHERE [RoleAssignmentId] = @AssignmentId;",
                new SqlParameter("@AssignmentId", companyAOperatorAssignmentId));
            await ExecuteNonQueryAsync(
                connection,
                "INSERT INTO [identity].[RoleAssignmentScopes] ([RoleAssignmentId], [CompanyId], [ScopeType], [ScopeId]) VALUES (@AssignmentId, @CompanyId, N'Company', @CompanyId);",
                new SqlParameter("@AssignmentId", companyAOperatorAssignmentId),
                new SqlParameter("@CompanyId", TestCompanyIds.CompanyA));
        }

        Assert.Equal(HttpStatusCode.NotFound, deniedReplayResponse.StatusCode);
        Assert.False(deniedReplayResponse.Headers.Contains("Idempotency-Replayed"));
        Assert.False(deniedReplayResponse.Headers.Contains("Idempotency-Original-Correlation-Id"));
        Assert.Equal(
            1,
            await ScalarAsync<int>(
                connection,
                "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [IdempotencyKeyHash] = @KeyHash;",
                new SqlParameter("@KeyHash", replayKeyHash)));
        Assert.Equal(
            0,
            await ScalarAsync<int>(
                connection,
                "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [CorrelationId] = @CorrelationId;",
                new SqlParameter("@CorrelationId", deniedReplayCorrelation)));

        const string companyScopedKey = "sql-company-scoped-key";
        var companyScopedKeyHash = IdempotencyHash.ComputeKey(companyScopedKey);
        var companyALocation = await ReadLocationProbeAsync(
            connection,
            "SELECT [Id], [IsActive], [Version] FROM [organization].[Locations] WHERE [CompanyId] = @CompanyId AND [Code] = N'EXT-BASTUG';",
            new SqlParameter("@CompanyId", TestCompanyIds.CompanyA));
        var companyBLocation = await ReadLocationProbeAsync(
            connection,
            "SELECT [Id], [IsActive], [Version] FROM [organization].[Locations] WHERE [CompanyId] = @CompanyId AND [Code] = N'EXT-BASTUG';",
            new SqlParameter("@CompanyId", TestCompanyIds.CompanyB));
        using var companyAClient = factory.CreateClientFor(
            TestUsers.CompanyA,
            TestCompanyIds.CompanyA);
        using var companyBClient = factory.CreateClientFor(
            TestUsers.CompanyB,
            TestCompanyIds.CompanyB);
        SetRequestIdentity(companyAClient, companyScopedKey, $"sql-company-a-{Guid.NewGuid():N}");
        SetRequestIdentity(companyBClient, companyScopedKey, $"sql-company-b-{Guid.NewGuid():N}");
        var companyScopedResponses = await Task.WhenAll(
            companyAClient.PutAsJsonAsync(
                $"/api/v1/organization/locations/{companyALocation.Id}/active-state",
                new SetLocationActiveState(
                    !companyALocation.IsActive,
                    companyALocation.Version,
                    "Şirket A SQL idempotency izolasyonu")),
            companyBClient.PutAsJsonAsync(
                $"/api/v1/organization/locations/{companyBLocation.Id}/active-state",
                new SetLocationActiveState(
                    !companyBLocation.IsActive,
                    companyBLocation.Version,
                    "Şirket B SQL idempotency izolasyonu")));

        Assert.All(
            companyScopedResponses,
            response => Assert.Equal(HttpStatusCode.NoContent, response.StatusCode));
        Assert.Equal(
            2,
            await ScalarAsync<int>(
                connection,
                "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [IdempotencyKeyHash] = @KeyHash;",
                new SqlParameter("@KeyHash", companyScopedKeyHash)));
        Assert.Equal(
            2,
            await ScalarAsync<int>(
                connection,
                "SELECT COUNT(DISTINCT [CompanyId]) FROM [idempotency].[CommandExecutions] WHERE [IdempotencyKeyHash] = @KeyHash;",
                new SqlParameter("@KeyHash", companyScopedKeyHash)));

        var concurrentLocation = await ReadLocationProbeAsync(
            connection,
            """
            SELECT [Id], [IsActive], [Version]
            FROM [organization].[Locations]
            WHERE [CompanyId] = @CompanyId AND [Code] = N'EXT-TOSCELIK';
            """,
            new SqlParameter("@CompanyId", TestCompanyIds.CompanyA));
        const string concurrentKey = "sql-concurrent-same-key";
        var concurrentCorrelations = new[]
        {
            $"sql-concurrent-a-{Guid.NewGuid():N}",
            $"sql-concurrent-b-{Guid.NewGuid():N}",
        };
        using var concurrentClientA = factory.CreateClientFor(
            TestUsers.CompanyA,
            TestCompanyIds.CompanyA);
        using var concurrentClientB = factory.CreateClientFor(
            TestUsers.CompanyA,
            TestCompanyIds.CompanyA);
        SetRequestIdentity(concurrentClientA, concurrentKey, concurrentCorrelations[0]);
        SetRequestIdentity(concurrentClientB, concurrentKey, concurrentCorrelations[1]);
        var concurrentCommand = new SetLocationActiveState(
            !concurrentLocation.IsActive,
            concurrentLocation.Version,
            "Aynı anahtar paralel ağ tekrarı");
        var concurrentResponses = await Task.WhenAll(
            concurrentClientA.PutAsJsonAsync(
                $"/api/v1/organization/locations/{concurrentLocation.Id}/active-state",
                concurrentCommand),
            concurrentClientB.PutAsJsonAsync(
                $"/api/v1/organization/locations/{concurrentLocation.Id}/active-state",
                concurrentCommand));
        var concurrentLocationAfter = await ReadLocationProbeAsync(
            connection,
            "SELECT [Id], [IsActive], [Version] FROM [organization].[Locations] WHERE [Id] = @Id;",
            new SqlParameter("@Id", concurrentLocation.Id));

        Assert.All(
            concurrentResponses,
            response => Assert.Equal(HttpStatusCode.NoContent, response.StatusCode));
        Assert.Single(
            concurrentResponses,
            response => response.Headers.Contains("Idempotency-Replayed"));
        Assert.Equal(concurrentLocation.Version + 1, concurrentLocationAfter.Version);
        Assert.Equal(
            1,
            await ScalarAsync<int>(
                connection,
                "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [IdempotencyKeyHash] = @KeyHash;",
                new SqlParameter("@KeyHash", IdempotencyHash.ComputeKey(concurrentKey))));
        Assert.Equal(
            1,
            await ScalarAsync<int>(
                connection,
                "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [CorrelationId] IN (@First, @Second);",
                new SqlParameter("@First", concurrentCorrelations[0]),
                new SqlParameter("@Second", concurrentCorrelations[1])));

        var concurrencyLocation = await ReadLocationProbeAsync(
            connection,
            """
            SELECT [Id], [IsActive], [Version]
            FROM [organization].[Locations]
            WHERE [CompanyId] = @CompanyId AND [Code] = N'EXT-SADILER';
            """,
            new SqlParameter("@CompanyId", TestCompanyIds.CompanyA));
        var concurrencyCorrelations = new[]
        {
            $"sql-version-a-{Guid.NewGuid():N}",
            $"sql-version-b-{Guid.NewGuid():N}",
        };
        using var concurrencyClientA = factory.CreateClientFor(
            TestUsers.CompanyA,
            TestCompanyIds.CompanyA);
        using var concurrencyClientB = factory.CreateClientFor(
            TestUsers.CompanyA,
            TestCompanyIds.CompanyA);
        SetRequestIdentity(
            concurrencyClientA,
            $"sql-version-key-a-{Guid.NewGuid():N}",
            concurrencyCorrelations[0]);
        SetRequestIdentity(
            concurrencyClientB,
            $"sql-version-key-b-{Guid.NewGuid():N}",
            concurrencyCorrelations[1]);
        var concurrencyCommand = new SetLocationActiveState(
            !concurrencyLocation.IsActive,
            concurrencyLocation.Version,
            "SQL optimistic concurrency yarışı");
        var concurrencyResponses = await Task.WhenAll(
            concurrencyClientA.PutAsJsonAsync(
                $"/api/v1/organization/locations/{concurrencyLocation.Id}/active-state",
                concurrencyCommand),
            concurrencyClientB.PutAsJsonAsync(
                $"/api/v1/organization/locations/{concurrencyLocation.Id}/active-state",
                concurrencyCommand));
        Assert.Contains(concurrencyResponses, x => x.StatusCode == HttpStatusCode.NoContent);
        var concurrencyConflict = Assert.Single(
            concurrencyResponses,
            x => x.StatusCode == HttpStatusCode.Conflict);
        await AssertVersionConflictAsync(
            concurrencyConflict,
            concurrencyLocation.Version,
            concurrencyLocation.Version + 1);
        Assert.Equal(
            1,
            await ScalarAsync<int>(
                connection,
                "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [CorrelationId] IN (@First, @Second);",
                new SqlParameter("@First", concurrencyCorrelations[0]),
                new SqlParameter("@Second", concurrencyCorrelations[1])));
        var persistedConflictJson = await ScalarAsync<string>(
            connection,
            "SELECT [ResponseJson] FROM [idempotency].[CommandExecutions] WHERE [Outcome] = N'VersionConflict' AND [OriginalCorrelationId] IN (@First, @Second);",
            new SqlParameter("@First", concurrencyCorrelations[0]),
            new SqlParameter("@Second", concurrencyCorrelations[1]));
        using (var persistedConflict = JsonDocument.Parse(persistedConflictJson))
        {
            var conflict = persistedConflict.RootElement.GetProperty("Conflict");
            Assert.Equal(
                concurrencyLocation.Version,
                conflict.GetProperty("ExpectedVersion").GetInt64());
            Assert.Equal(
                concurrencyLocation.Version + 1,
                conflict.GetProperty("CurrentVersion").GetInt64());
        }

        var rejectedLocation = await ReadLocationProbeAsync(
            connection,
            """
            SELECT [Id], [IsActive], [Version]
            FROM [organization].[Locations]
            WHERE [CompanyId] = @CompanyId AND [Code] = N'EXT-ALYA';
            """,
            new SqlParameter("@CompanyId", TestCompanyIds.CompanyA));
        const string rejectedKey = "sql-idempotency-insert-rejection";
        var rejectedCorrelation = $"sql-idem-reject-{Guid.NewGuid():N}";
        await ExecuteNonQueryAsync(
            connection,
            """
            CREATE TRIGGER [idempotency].[TR_CommandExecutions_RejectInsertProbe]
            ON [idempotency].[CommandExecutions]
            AFTER INSERT
            AS
            BEGIN
                SET NOCOUNT ON;
                THROW 51011, 'Idempotency atomicity rejection probe.', 1;
            END;
            """);
        HttpResponseMessage rejectedResponse;
        try
        {
            using var rejectedClient = factory.CreateClientFor(
                TestUsers.CompanyA,
                TestCompanyIds.CompanyA);
            SetRequestIdentity(rejectedClient, rejectedKey, rejectedCorrelation);
            rejectedResponse = await rejectedClient.PutAsJsonAsync(
                $"/api/v1/organization/locations/{rejectedLocation.Id}/active-state",
                new SetLocationActiveState(
                    !rejectedLocation.IsActive,
                    rejectedLocation.Version,
                    "Idempotency atomik rollback testi"));
        }
        finally
        {
            await ExecuteNonQueryAsync(
                connection,
                "DROP TRIGGER [idempotency].[TR_CommandExecutions_RejectInsertProbe];");
        }

        Assert.Equal(HttpStatusCode.InternalServerError, rejectedResponse.StatusCode);
        Assert.Equal(
            rejectedLocation,
            await ReadLocationProbeAsync(
                connection,
                "SELECT [Id], [IsActive], [Version] FROM [organization].[Locations] WHERE [Id] = @Id;",
                new SqlParameter("@Id", rejectedLocation.Id)));
        Assert.Equal(
            0,
            await ScalarAsync<int>(
                connection,
                "SELECT COUNT(*) FROM [idempotency].[CommandExecutions] WHERE [IdempotencyKeyHash] = @KeyHash;",
                new SqlParameter("@KeyHash", IdempotencyHash.ComputeKey(rejectedKey))));
        Assert.Equal(
            0,
            await ScalarAsync<int>(
                connection,
                "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [CorrelationId] = @CorrelationId;",
                new SqlParameter("@CorrelationId", rejectedCorrelation)));

        var updateException = await Assert.ThrowsAsync<SqlException>(() => ExecuteNonQueryAsync(
            connection,
            "UPDATE [idempotency].[CommandExecutions] SET [Outcome] = N'Succeeded' WHERE [IdempotencyKeyHash] = @KeyHash;",
            new SqlParameter("@KeyHash", replayKeyHash)));
        Assert.Equal(51010, updateException.Number);
        var deleteException = await Assert.ThrowsAsync<SqlException>(() => ExecuteNonQueryAsync(
            connection,
            "DELETE FROM [idempotency].[CommandExecutions] WHERE [IdempotencyKeyHash] = @KeyHash;",
            new SqlParameter("@KeyHash", replayKeyHash)));
        Assert.Equal(51010, deleteException.Number);

        var rowVersion = await ScalarAsync<byte[]>(
            connection,
            "SELECT [RowVersion] FROM [organization].[Locations] WHERE [Id] = @Id;",
            new SqlParameter("@Id", rejectedLocation.Id));
        await using var transaction = await connection.BeginTransactionAsync();
        await using var firstRowVersionUpdate = connection.CreateCommand();
        firstRowVersionUpdate.Transaction = (SqlTransaction)transaction;
        firstRowVersionUpdate.CommandText =
            "UPDATE [organization].[Locations] SET [Version] = [Version] + 1 WHERE [Id] = @Id AND [RowVersion] = @RowVersion;";
        firstRowVersionUpdate.Parameters.AddWithValue("@Id", rejectedLocation.Id);
        firstRowVersionUpdate.Parameters.AddWithValue("@RowVersion", rowVersion);
        Assert.Equal(1, await firstRowVersionUpdate.ExecuteNonQueryAsync());
        await using var staleRowVersionUpdate = connection.CreateCommand();
        staleRowVersionUpdate.Transaction = (SqlTransaction)transaction;
        staleRowVersionUpdate.CommandText =
            "UPDATE [organization].[Locations] SET [Version] = [Version] + 1 WHERE [Id] = @Id AND [RowVersion] = @RowVersion;";
        staleRowVersionUpdate.Parameters.AddWithValue("@Id", rejectedLocation.Id);
        staleRowVersionUpdate.Parameters.AddWithValue("@RowVersion", rowVersion);
        Assert.Equal(0, await staleRowVersionUpdate.ExecuteNonQueryAsync());
        await transaction.RollbackAsync();
    }

    private static void SetRequestIdentity(
        HttpClient client,
        string idempotencyKey,
        string correlationId)
    {
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", idempotencyKey);
        client.DefaultRequestHeaders.Remove("X-Correlation-Id");
        client.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);
    }

    private static async Task AssertVersionConflictAsync(
        HttpResponseMessage response,
        long expectedVersion,
        long currentVersion)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var conflict = document.RootElement.GetProperty("conflict");
        Assert.Equal(expectedVersion, conflict.GetProperty("expectedVersion").GetInt64());
        Assert.Equal(currentVersion, conflict.GetProperty("currentVersion").GetInt64());
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
    private readonly Action<IServiceCollection>? _configureServices;
    private readonly bool _applyTestCompanyBTopologyTemplates;

    public SqlServerRelationalApiFactory(
        string baseConnectionString,
        Action<IServiceCollection>? configureServices = null,
        bool applyTestCompanyBTopologyTemplates = true,
        string? databaseName = null)
    {
        _configureServices = configureServices;
        _applyTestCompanyBTopologyTemplates = applyTestCompanyBTopologyTemplates;
        DatabaseName = databaseName ?? $"goymen_erp_ci_{Guid.NewGuid():N}";
        var builder = new SqlConnectionStringBuilder(baseConnectionString)
        {
            InitialCatalog = DatabaseName,
        };
        _databaseConnectionString = builder.ConnectionString;
    }

    public string DatabaseName { get; }

    public string DatabaseConnectionString => _databaseConnectionString;

    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        // Protocol-negative SQL probes remove this explicitly; all existing
        // domain/authorization/replay assertions still execute against the real API.
        client.DefaultRequestHeaders.Add(
            GoymenERP.Api.Contracts.CounterpartyClientContract.HeaderName,
            GoymenERP.Api.Contracts.CounterpartyClientContract.Version);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("DB_CONNECTION_STRING", _databaseConnectionString);
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var testConfiguration = new Dictionary<string, string?>
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
                ["BootstrapIdentity:Users:4:UserId"] = TestUsers.SiteAuditReader,
                ["BootstrapIdentity:Users:4:DisplayName"] = "Dar Kapsam Audit SQL Test Kullanıcısı",
                ["BootstrapIdentity:Users:5:UserId"] = TestUsers.Unprovisioned,
                ["BootstrapIdentity:Users:5:DisplayName"] = "ERP Kaydı Olmayan SQL Test Kullanıcısı",
                ["BootstrapIdentity:Users:6:UserId"] = TestUsers.NoAssignment,
                ["BootstrapIdentity:Users:6:DisplayName"] = "Atamasız ERP SQL Test Kullanıcısı",
            };
            if (_applyTestCompanyBTopologyTemplates)
            {
                // Test-only foreign-company topology for relational isolation probes.
                // Production runtime seed keeps GSP/GIN company-only.
                testConfiguration["OrganizationSeed:Companies:1:ApplyTopologyTemplates"] =
                    "true";
            }

            configuration.AddInMemoryCollection(testConfiguration);
        });
        if (_configureServices is not null)
        {
            builder.ConfigureServices(_configureServices);
        }
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
