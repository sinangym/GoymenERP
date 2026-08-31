using System.Data.Common;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Infrastructure;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Seeding;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GoymenERP.Api.IntegrationTests;

public sealed class IdentityPermissionReconciliationSqlServerRelationalAcceptanceTests
{
    private const string ContractPath =
        "src/backend/Modules/Counterparty/COUNTERPARTY_ACCOUNT_CREATE_ROLE_POLICY.json";
    private const string IdentitySeedPath = "src/backend/GoymenERP.Api/Seed/identity-seed.json";
    private const string SystemAdministratorRoleCode = "SYSTEM_ADMINISTRATOR";
    private const string CustomPermission = "custom.identity.permission.keep";
    private static readonly Guid SystemAdministratorRoleId = Guid.Parse(
        "30000000-0000-0000-0000-000000000003");
    private static readonly Guid SystemAdministratorUserId = Guid.Parse(
        "20000000-0000-0000-0000-000000000003");
    private static readonly Guid OrganizationOperatorUserId = Guid.Parse(
        "20000000-0000-0000-0000-000000000001");
    private static readonly Guid OrganizationViewerUserId = Guid.Parse(
        "20000000-0000-0000-0000-000000000004");

    [SqlServerFact]
    public async Task FreshDatabase_SeedsOnlyCanonicalAdministratorGrant()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var factory = new SqlServerRelationalApiFactory(baseConnectionString);

        try
        {
            using var client = factory.CreateClient();
            using var response = await client.GetAsync("/health/live");
            response.EnsureSuccessStatusCode();

            await using var connection = new SqlConnection(factory.DatabaseConnectionString);
            await connection.OpenAsync();
            Assert.Equal(
                [
                    "ORGANIZATION_OPERATOR",
                    "ORGANIZATION_READER",
                    "SECURITY_ADMINISTRATOR",
                    "SECURITY_APPROVER",
                    SystemAdministratorRoleCode,
                ],
                await ReadStringsAsync(
                    connection,
                    "SELECT [Code] FROM [identity].[Roles] ORDER BY [Code];"));
            Assert.Equal(
                1,
                await ScalarAsync<int>(
                    connection,
                    """
                    SELECT COUNT(*)
                    FROM [identity].[Roles]
                    WHERE [Id] = @RoleId
                      AND [Code] = @RoleCode
                      AND [Name] = N'Sistem Yöneticisi'
                      AND [IsSystem] = 1;
                    """,
                    new SqlParameter("@RoleId", SystemAdministratorRoleId),
                    new SqlParameter("@RoleCode", SystemAdministratorRoleCode)));
            await AssertOnlyAdministratorHasTargetPermissionAsync(connection);
            Assert.Equal(
                0,
                await ScalarAsync<int>(
                    connection,
                    "SELECT COUNT(*) FROM [identity].[Roles] WHERE [Code] = N'COUNTERPARTY_ACCOUNT_OPERATOR';"));
            Assert.Equal(
                7,
                await ScalarAsync<int>(
                    connection,
                    "SELECT COUNT(*) FROM [identity].[RoleAssignments];"));

            var options = new DbContextOptionsBuilder<IdentityDbContext>()
                .UseSqlServer(
                    factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "identity"))
                .Options;
            await using var dbContext = new IdentityDbContext(options);
            var adminAuthorization = new ErpAuthorizationService(
                dbContext,
                new FakeCurrentUser(SystemAdministratorUserId, TestCompanyIds.CompanyA));
            var operatorAuthorization = new ErpAuthorizationService(
                dbContext,
                new FakeCurrentUser(OrganizationOperatorUserId, TestCompanyIds.CompanyA));
            var viewerAuthorization = new ErpAuthorizationService(
                dbContext,
                new FakeCurrentUser(OrganizationViewerUserId, TestCompanyIds.CompanyA));

            Assert.True(await adminAuthorization.HasPermissionAsync(
                ErpPermissions.CounterpartyAccountsCreate,
                TestCompanyIds.CompanyA));
            Assert.False(await operatorAuthorization.HasPermissionAsync(
                ErpPermissions.CounterpartyAccountsCreate,
                TestCompanyIds.CompanyA));
            Assert.False(await viewerAuthorization.HasPermissionAsync(
                ErpPermissions.CounterpartyAccountsCreate,
                TestCompanyIds.CompanyA));
        }
        finally
        {
            await factory.DisposeAsync();
            await DropDatabaseAsync(baseConnectionString, factory.DatabaseName);
        }
    }

    [SqlServerFact]
    public async Task ExistingInstall_ReconcilesOptionAFailClosedAndDuplicateSafe()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var databaseName = $"goymen_erp_identity_reconcile_{Guid.NewGuid():N}";
        var connectionString = BuildDatabaseConnectionString(
            baseConnectionString,
            databaseName);
        var serviceProvider = BuildIdentityServiceProvider(connectionString);

        try
        {
            await SeedLegacyIdentitySnapshotAsync(serviceProvider);
            var optionAPlan = ReadOptionAPlan();

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await ExecuteNonQueryAsync(
                connection,
                """
                INSERT INTO [identity].[RolePermissions] ([RoleId], [PermissionCode])
                VALUES (@RoleId, @PermissionCode);
                """,
                new SqlParameter("@RoleId", SystemAdministratorRoleId),
                new SqlParameter("@PermissionCode", CustomPermission));
            Assert.Equal(
                0,
                await CountPermissionAsync(
                    connection,
                    SystemAdministratorRoleId,
                    ErpPermissions.CounterpartyAccountsCreate));

            var beforeUpgrade = await ReadSnapshotAsync(connection);
            var firstResult = await ReconcileAsync(serviceProvider, optionAPlan);
            var afterUpgrade = await ReadSnapshotAsync(connection);

            Assert.True(firstResult.Changed);
            Assert.Equal(1, firstResult.RolesChecked);
            Assert.Equal(1, firstResult.PermissionsAdded);
            Assert.Equal(beforeUpgrade.Roles, afterUpgrade.Roles);
            Assert.Equal(beforeUpgrade.Assignments, afterUpgrade.Assignments);
            Assert.Equal(beforeUpgrade.Scopes, afterUpgrade.Scopes);
            Assert.Equal(
                1,
                await CountPermissionAsync(
                    connection,
                    SystemAdministratorRoleId,
                    CustomPermission));
            await AssertOnlyAdministratorHasTargetPermissionAsync(connection);

            var secondResult = await ReconcileAsync(serviceProvider, optionAPlan);
            Assert.False(secondResult.Changed);
            Assert.Equal(0, secondResult.PermissionsAdded);
            Assert.Equal(afterUpgrade, await ReadSnapshotAsync(connection));

            await DeletePermissionAsync(
                connection,
                SystemAdministratorRoleId,
                ErpPermissions.CounterpartyAccountsCreate);
            var beforeParallel = await ReadSnapshotAsync(connection);
            var startGate = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var parallelTasks = Enumerable.Range(0, 2)
                .Select(_ => ReconcileAfterGateAsync(
                    serviceProvider,
                    optionAPlan,
                    startGate.Task))
                .ToArray();
            startGate.SetResult(true);
            var parallelResults = await Task.WhenAll(parallelTasks);
            var afterParallel = await ReadSnapshotAsync(connection);

            Assert.Equal(1, parallelResults.Sum(result => result.PermissionsAdded));
            Assert.All(parallelResults, result => Assert.Equal(1, result.RolesChecked));
            Assert.Equal(beforeParallel.Roles, afterParallel.Roles);
            Assert.Equal(beforeParallel.Assignments, afterParallel.Assignments);
            Assert.Equal(beforeParallel.Scopes, afterParallel.Scopes);
            await AssertOnlyAdministratorHasTargetPermissionAsync(connection);

            await AssertRejectedInsertRollsBackAsync(
                connection,
                serviceProvider,
                optionAPlan);
            await AssertIdentityAndCodeCollisionsFailClosedAsync(
                connection,
                serviceProvider,
                optionAPlan);
            await AssertMissingAdministratorFailsClosedAsync(
                connection,
                serviceProvider,
                optionAPlan);
        }
        finally
        {
            await serviceProvider.DisposeAsync();
            await DropDatabaseAsync(baseConnectionString, databaseName);
        }
    }

    private static async Task AssertRejectedInsertRollsBackAsync(
        SqlConnection connection,
        IServiceProvider serviceProvider,
        SystemRolePermissionReconciliationPlan optionAPlan)
    {
        await DeletePermissionAsync(
            connection,
            SystemAdministratorRoleId,
            ErpPermissions.CounterpartyAccountsCreate);
        await DeletePermissionAsync(
            connection,
            SystemAdministratorRoleId,
            ErpPermissions.CounterpartyAccountsStatusUpdate);
        var rollbackPlan = new SystemRolePermissionReconciliationPlan(
            [
                new SystemRolePermissionTarget(
                    SystemAdministratorRoleId,
                    SystemAdministratorRoleCode,
                    [
                        ErpPermissions.CounterpartyAccountsCreate,
                        ErpPermissions.CounterpartyAccountsStatusUpdate,
                    ]),
            ]);
        var beforeFailure = await ReadSnapshotAsync(connection);
        await ExecuteNonQueryAsync(
            connection,
            """
            CREATE TRIGGER [identity].[TR_RolePermissions_RejectOptionAProbe]
            ON [identity].[RolePermissions]
            AFTER INSERT
            AS
            BEGIN
                SET NOCOUNT ON;
                IF EXISTS (
                    SELECT 1
                    FROM inserted
                    WHERE [RoleId] = '30000000-0000-0000-0000-000000000003'
                      AND [PermissionCode] IN (
                          N'counterparty.accounts.create',
                          N'counterparty.accounts.status.update'))
                BEGIN
                    THROW 51021, 'Identity reconciliation rollback probe.', 1;
                END;
            END;
            """);
        try
        {
            await Assert.ThrowsAsync<DbUpdateException>(
                () => ReconcileAsync(serviceProvider, rollbackPlan));
        }
        finally
        {
            await ExecuteNonQueryAsync(
                connection,
                "DROP TRIGGER [identity].[TR_RolePermissions_RejectOptionAProbe];");
        }

        Assert.Equal(beforeFailure, await ReadSnapshotAsync(connection));
        await ExecuteNonQueryAsync(
            connection,
            """
            INSERT INTO [identity].[RolePermissions] ([RoleId], [PermissionCode])
            VALUES (@RoleId, @PermissionCode);
            """,
            new SqlParameter("@RoleId", SystemAdministratorRoleId),
            new SqlParameter(
                "@PermissionCode",
                ErpPermissions.CounterpartyAccountsStatusUpdate));
        var recoveryResult = await ReconcileAsync(serviceProvider, optionAPlan);
        Assert.Equal(1, recoveryResult.PermissionsAdded);
    }

    private static async Task AssertIdentityAndCodeCollisionsFailClosedAsync(
        SqlConnection connection,
        IServiceProvider serviceProvider,
        SystemRolePermissionReconciliationPlan optionAPlan)
    {
        await DeletePermissionAsync(
            connection,
            SystemAdministratorRoleId,
            ErpPermissions.CounterpartyAccountsCreate);
        await ExecuteNonQueryAsync(
            connection,
            "UPDATE [identity].[Roles] SET [Code] = N'SYSTEM_ADMINISTRATOR_DRIFT' WHERE [Id] = @RoleId;",
            new SqlParameter("@RoleId", SystemAdministratorRoleId));
        var beforeIdCollision = await ReadSnapshotAsync(connection);

        await Assert.ThrowsAsync<SystemRolePermissionReconciliationException>(
            () => ReconcileAsync(serviceProvider, optionAPlan));
        Assert.Equal(beforeIdCollision, await ReadSnapshotAsync(connection));

        await ExecuteNonQueryAsync(
            connection,
            "UPDATE [identity].[Roles] SET [Code] = @RoleCode WHERE [Id] = @RoleId;",
            new SqlParameter("@RoleCode", SystemAdministratorRoleCode),
            new SqlParameter("@RoleId", SystemAdministratorRoleId));
        await ReconcileAsync(serviceProvider, optionAPlan);

        await DeletePermissionAsync(
            connection,
            SystemAdministratorRoleId,
            ErpPermissions.CounterpartyAccountsCreate);
        var impostorRoleId = Guid.NewGuid();
        await ExecuteNonQueryAsync(
            connection,
            "UPDATE [identity].[Roles] SET [Code] = N'SYSTEM_ADMINISTRATOR_ORIGINAL' WHERE [Id] = @RoleId;",
            new SqlParameter("@RoleId", SystemAdministratorRoleId));
        await ExecuteNonQueryAsync(
            connection,
            """
            INSERT INTO [identity].[Roles]
                ([Id], [Code], [Name], [IsSystem], [IsActive],
                 [CreatedAtUtc], [UpdatedAtUtc], [Version])
            VALUES
                (@RoleId, @RoleCode, N'Çakışma Test Rolü', 1, 1,
                 SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), 1);
            """,
            new SqlParameter("@RoleId", impostorRoleId),
            new SqlParameter("@RoleCode", SystemAdministratorRoleCode));
        var beforeCodeCollision = await ReadSnapshotAsync(connection);

        await Assert.ThrowsAsync<SystemRolePermissionReconciliationException>(
            () => ReconcileAsync(serviceProvider, optionAPlan));
        Assert.Equal(beforeCodeCollision, await ReadSnapshotAsync(connection));

        await ExecuteNonQueryAsync(
            connection,
            "DELETE FROM [identity].[Roles] WHERE [Id] = @RoleId;",
            new SqlParameter("@RoleId", impostorRoleId));
        await ExecuteNonQueryAsync(
            connection,
            "UPDATE [identity].[Roles] SET [Code] = @RoleCode WHERE [Id] = @RoleId;",
            new SqlParameter("@RoleCode", SystemAdministratorRoleCode),
            new SqlParameter("@RoleId", SystemAdministratorRoleId));
        await ReconcileAsync(serviceProvider, optionAPlan);
    }

    private static async Task AssertMissingAdministratorFailsClosedAsync(
        SqlConnection connection,
        IServiceProvider serviceProvider,
        SystemRolePermissionReconciliationPlan optionAPlan)
    {
        await ExecuteNonQueryAsync(
            connection,
            """
            DELETE scope
            FROM [identity].[RoleAssignmentScopes] AS scope
            INNER JOIN [identity].[RoleAssignments] AS assignment
                ON assignment.[Id] = scope.[RoleAssignmentId]
            WHERE assignment.[RoleId] = @RoleId;

            DELETE FROM [identity].[RoleAssignments] WHERE [RoleId] = @RoleId;
            DELETE FROM [identity].[RolePermissions] WHERE [RoleId] = @RoleId;
            DELETE FROM [identity].[Roles] WHERE [Id] = @RoleId;
            """,
            new SqlParameter("@RoleId", SystemAdministratorRoleId));
        var beforeMissingRole = await ReadSnapshotAsync(connection);

        await Assert.ThrowsAsync<SystemRolePermissionReconciliationException>(
            () => ReconcileAsync(serviceProvider, optionAPlan));

        Assert.Equal(beforeMissingRole, await ReadSnapshotAsync(connection));
        Assert.Equal(
            0,
            await ScalarAsync<int>(
                connection,
                "SELECT COUNT(*) FROM [identity].[Roles] WHERE [Id] = @RoleId OR [Code] = @RoleCode;",
                new SqlParameter("@RoleId", SystemAdministratorRoleId),
                new SqlParameter("@RoleCode", SystemAdministratorRoleCode)));
        Assert.Equal(
            0,
            await ScalarAsync<int>(
                connection,
                "SELECT COUNT(*) FROM [identity].[RoleAssignments] WHERE [RoleId] = @RoleId;",
                new SqlParameter("@RoleId", SystemAdministratorRoleId)));
    }

    private static async Task SeedLegacyIdentitySnapshotAsync(IServiceProvider serviceProvider)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await dbContext.Database.MigrateAsync();
        var seed = ReadIdentitySeed();
        var administrator = seed.Roles.Single(role =>
            role.Id == SystemAdministratorRoleId
            && string.Equals(
                role.Code,
                SystemAdministratorRoleCode,
                StringComparison.Ordinal));
        Assert.True(administrator.Permissions.Remove(
            ErpPermissions.CounterpartyAccountsCreate));
        var seeder = new IdentityDataSeeder(dbContext, Options.Create(seed));
        await seeder.SeedAsync(CancellationToken.None);
    }

    private static IdentitySeedOptions ReadIdentitySeed()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(FindRepositoryRoot(), IdentitySeedPath)));
        return JsonSerializer.Deserialize<IdentitySeedOptions>(
            document.RootElement.GetProperty(IdentitySeedOptions.SectionName).GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Identity seed okunamadı.");
    }

    private static SystemRolePermissionReconciliationPlan ReadOptionAPlan()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(FindRepositoryRoot(), ContractPath)));
        var root = document.RootElement;
        Assert.Equal("CARI-PERM-001", root.GetProperty("decision_id").GetString());
        var role = Assert.Single(root.GetProperty("roles").EnumerateArray().ToArray());
        Assert.False(role.GetProperty("create_if_missing").GetBoolean());
        var target = new SystemRolePermissionTarget(
            Guid.Parse(role.GetProperty("id").GetString()!),
            role.GetProperty("code").GetString()!,
            role.GetProperty("permissions_to_ensure")
                .EnumerateArray()
                .Select(permission => permission.GetString()!)
                .ToArray());
        return new SystemRolePermissionReconciliationPlan([target]);
    }

    private static ServiceProvider BuildIdentityServiceProvider(string connectionString)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DB_PROVIDER"] = "SqlServer",
                ["DB_CONNECTION_STRING"] = connectionString,
            })
            .Build();
        var services = new ServiceCollection();
        services.AddIdentityModule(configuration);
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
        });
    }

    private static async Task<SystemRolePermissionReconciliationResult> ReconcileAsync(
        IServiceProvider serviceProvider,
        SystemRolePermissionReconciliationPlan plan)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var reconciler = scope.ServiceProvider.GetRequiredService<ISystemRolePermissionReconciler>();
        return await reconciler.ReconcileAsync(plan);
    }

    private static async Task<SystemRolePermissionReconciliationResult> ReconcileAfterGateAsync(
        IServiceProvider serviceProvider,
        SystemRolePermissionReconciliationPlan plan,
        Task gate)
    {
        await gate;
        return await ReconcileAsync(serviceProvider, plan);
    }

    private static async Task AssertOnlyAdministratorHasTargetPermissionAsync(
        SqlConnection connection)
    {
        Assert.Equal(
            1,
            await ScalarAsync<int>(
                connection,
                "SELECT COUNT(*) FROM [identity].[RolePermissions] WHERE [PermissionCode] = @PermissionCode;",
                new SqlParameter(
                    "@PermissionCode",
                    ErpPermissions.CounterpartyAccountsCreate)));
        Assert.Equal(
            1,
            await CountPermissionAsync(
                connection,
                SystemAdministratorRoleId,
                ErpPermissions.CounterpartyAccountsCreate));
        Assert.Equal(
            0,
            await ScalarAsync<int>(
                connection,
                """
                SELECT COUNT(*)
                FROM [identity].[RolePermissions]
                WHERE [PermissionCode] = @PermissionCode
                  AND [RoleId] <> @RoleId;
                """,
                new SqlParameter(
                    "@PermissionCode",
                    ErpPermissions.CounterpartyAccountsCreate),
                new SqlParameter("@RoleId", SystemAdministratorRoleId)));
    }

    private static Task<int> CountPermissionAsync(
        SqlConnection connection,
        Guid roleId,
        string permissionCode) =>
        ScalarAsync<int>(
            connection,
            """
            SELECT COUNT(*)
            FROM [identity].[RolePermissions]
            WHERE [RoleId] = @RoleId AND [PermissionCode] = @PermissionCode;
            """,
            new SqlParameter("@RoleId", roleId),
            new SqlParameter("@PermissionCode", permissionCode));

    private static Task DeletePermissionAsync(
        SqlConnection connection,
        Guid roleId,
        string permissionCode) =>
        ExecuteNonQueryAsync(
            connection,
            """
            DELETE FROM [identity].[RolePermissions]
            WHERE [RoleId] = @RoleId AND [PermissionCode] = @PermissionCode;
            """,
            new SqlParameter("@RoleId", roleId),
            new SqlParameter("@PermissionCode", permissionCode));

    private static async Task<IdentitySnapshot> ReadSnapshotAsync(SqlConnection connection)
    {
        return new IdentitySnapshot(
            await QueryJsonAsync(
                connection,
                """
                SELECT [Id], [Code], [Name], [IsSystem], [IsActive],
                       [CreatedAtUtc], [UpdatedAtUtc], [Version]
                FROM [identity].[Roles]
                ORDER BY [Id]
                FOR JSON PATH, INCLUDE_NULL_VALUES;
                """),
            await QueryJsonAsync(
                connection,
                """
                SELECT [RoleId], [PermissionCode]
                FROM [identity].[RolePermissions]
                ORDER BY [RoleId], [PermissionCode]
                FOR JSON PATH, INCLUDE_NULL_VALUES;
                """),
            await QueryJsonAsync(
                connection,
                """
                SELECT [Id], [UserId], [RoleId], [CompanyId], [ValidFromUtc],
                       [ValidUntilUtc], [ApprovedByUserId], [Status], [CreatedAtUtc], [Version]
                FROM [identity].[RoleAssignments]
                ORDER BY [Id]
                FOR JSON PATH, INCLUDE_NULL_VALUES;
                """),
            await QueryJsonAsync(
                connection,
                """
                SELECT [RoleAssignmentId], [CompanyId], [ScopeType], [ScopeId]
                FROM [identity].[RoleAssignmentScopes]
                ORDER BY [RoleAssignmentId], [ScopeType], [ScopeId]
                FOR JSON PATH, INCLUDE_NULL_VALUES;
                """));
    }

    private static async Task<string> QueryJsonAsync(
        SqlConnection connection,
        string commandText)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        return Assert.IsType<string>(await command.ExecuteScalarAsync());
    }

    private static async Task<IReadOnlyList<string>> ReadStringsAsync(
        SqlConnection connection,
        string commandText)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private static async Task<T> ScalarAsync<T>(
        SqlConnection connection,
        string commandText,
        params DbParameter[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.Parameters.AddRange(parameters);
        return Assert.IsType<T>(await command.ExecuteScalarAsync());
    }

    private static async Task ExecuteNonQueryAsync(
        SqlConnection connection,
        string commandText,
        params DbParameter[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static string BuildDatabaseConnectionString(
        string baseConnectionString,
        string databaseName)
    {
        return new SqlConnectionStringBuilder(baseConnectionString)
        {
            InitialCatalog = databaseName,
        }.ConnectionString;
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

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, ContractPath)))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("GöymenERP repository kökü bulunamadı.");
    }

    private sealed record IdentitySnapshot(
        string Roles,
        string Permissions,
        string Assignments,
        string Scopes);

    private sealed class FakeCurrentUser(Guid userId, params Guid[] companyIds)
        : ICurrentUser
    {
        public bool IsAuthenticated => true;

        public string UserId => userId.ToString("D");

        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = companyIds.ToHashSet();

        public bool CanAccessCompany(Guid companyId) => AllowedCompanyIds.Contains(companyId);
    }
}
