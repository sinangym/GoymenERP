using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Shell;
using GoymenERP.Modules.Counterparty.Infrastructure.Shell;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

public sealed class ShellCapabilitySqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyAUserId = Guid.Parse(
        "20000000-0000-0000-0000-000000000001");

    private static readonly Guid GroupAdministratorUserId = Guid.Parse(
        "20000000-0000-0000-0000-000000000003");

    private static readonly Guid OrganizationOperatorRoleId = Guid.Parse(
        "30000000-0000-0000-0000-000000000002");

    private static readonly Guid OrganizationReaderRoleId = Guid.Parse(
        "30000000-0000-0000-0000-000000000001");

    private static readonly Guid SystemAdministratorRoleId = Guid.Parse(
        "30000000-0000-0000-0000-000000000003");

    [SqlServerFact]
    public async Task SelectedCompanySnapshot_FailsClosedWhenCompanyIsDeactivatedDuringAuthorization()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        SqlServerRelationalApiFactory? factory = null;
        var observer = new DeactivatingAuthorizationSnapshotReadObserver(
            () => factory!.DatabaseConnectionString,
            TestCompanyIds.CompanyA);
        factory = new SqlServerRelationalApiFactory(
            baseConnectionString,
            services =>
            {
                services.RemoveAll<IAuthorizationSnapshotReadObserver>();
                services.AddSingleton<IAuthorizationSnapshotReadObserver>(observer);
            });

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

            using var client = factory.CreateClientFor(
                TestUsers.CompanyA,
                TestCompanyIds.CompanyA);
            using (var response = await client.GetAsync("/api/v1/shell/capabilities"))
            {
                var body = await response.Content.ReadAsStringAsync();

                Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
                Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
                Assert.True(response.Headers.CacheControl.Private);
                using var problem = JsonDocument.Parse(body);
                Assert.Equal(
                    "service_unavailable",
                    problem.RootElement.GetProperty("code").GetString());
                Assert.DoesNotContain(
                    TestCompanyIds.CompanyA.ToString("D"),
                    body,
                    StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("GDC", body, StringComparison.Ordinal);
                Assert.DoesNotContain(
                    "Göymen Demir Çelik İnş. San. ve Tic. Ltd. Şti.",
                    body,
                    StringComparison.Ordinal);
                Assert.DoesNotContain("4100046586", body, StringComparison.Ordinal);
            }

            Assert.Equal(1, observer.DeactivationCount);
            Assert.Equal(
                0,
                await ScalarAsync<int>(
                    connection,
                    "SELECT CONVERT(int, [IsActive]) FROM [organization].[Companies] WHERE [Id] = @CompanyId;",
                    new SqlParameter("@CompanyId", TestCompanyIds.CompanyA)));
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

            Assert.Equal(
                1,
                await ExecuteNonQueryAsync(
                    connection,
                    "UPDATE [organization].[Companies] SET [IsActive] = 1 WHERE [Id] = @CompanyId;",
                    new SqlParameter("@CompanyId", TestCompanyIds.CompanyA)));
            var recovered = await ReadSnapshotAsync(client);
            AssertExactEmptySnapshot(recovered);
        }
        finally
        {
            if (factory is not null)
            {
                await factory.DisposeAsync();
                await DropDatabaseAsync(baseConnectionString, factory.DatabaseName);
            }
        }
    }

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

            var deactivated = await ExecuteNonQueryAsync(
                connection,
                "UPDATE [organization].[Companies] SET [IsActive] = 0 WHERE [Id] = @CompanyId;",
                new SqlParameter("@CompanyId", TestCompanyIds.CompanyB));
            Assert.Equal(1, deactivated);

            using var inactiveCompanyResponse = await companyBClient.GetAsync(
                "/api/v1/shell/capabilities");
            var inactiveCompanyBody = await inactiveCompanyResponse.Content.ReadAsStringAsync();
            Assert.Equal(
                HttpStatusCode.ServiceUnavailable,
                inactiveCompanyResponse.StatusCode);
            Assert.Contains(
                "no-store",
                inactiveCompanyResponse.Headers.CacheControl!.ToString());
            Assert.True(inactiveCompanyResponse.Headers.CacheControl.Private);
            using (var inactiveCompanyProblem = JsonDocument.Parse(inactiveCompanyBody))
            {
                Assert.Equal(
                    "service_unavailable",
                    inactiveCompanyProblem.RootElement.GetProperty("code").GetString());
            }
            Assert.DoesNotContain(
                TestCompanyIds.CompanyB.ToString("D"),
                inactiveCompanyBody,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("GSP", inactiveCompanyBody, StringComparison.Ordinal);
            Assert.DoesNotContain(
                "Göymen Sac Profil San. ve Tic. Ltd. Şti.",
                inactiveCompanyBody,
                StringComparison.Ordinal);
            Assert.DoesNotContain("4101507252", inactiveCompanyBody, StringComparison.Ordinal);
            Assert.DoesNotContain(
                "vkn",
                inactiveCompanyBody,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                "invoiceCapable",
                inactiveCompanyBody,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await factory.DisposeAsync();
            await DropDatabaseAsync(baseConnectionString, factory.DatabaseName);
        }
    }

    [SqlServerFact]
    public async Task CounterpartyCapability_RequiresSelectedCompanyAndTracksPermissionRevocation()
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

            using var administratorClient = factory.CreateClientFor(
                TestUsers.GroupAdmin,
                TestCompanyIds.CompanyA);
            using var operatorClient = factory.CreateClientFor(
                TestUsers.CompanyA,
                TestCompanyIds.CompanyA);
            using var groupClient = factory.CreateClient();
            groupClient.DefaultRequestHeaders.Add(
                "X-Development-User",
                TestUsers.GroupAdmin);
            groupClient.DefaultRequestHeaders.Add(
                "X-Correlation-Id",
                Guid.NewGuid().ToString("N"));

            var granted = await ReadSnapshotAsync(administratorClient);
            var denied = await ReadSnapshotAsync(operatorClient);
            var group = await ReadSnapshotAsync(groupClient);
            Assert.Equal(
                [CounterpartyShellCapabilityProvider.ListCapabilityId],
                granted.Capabilities);
            Assert.Empty(denied.Capabilities);
            Assert.Empty(group.Capabilities);
            Assert.DoesNotContain(
                TestCompanyIds.CompanyA.ToString("D"),
                group.RawJson,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                TestCompanyIds.CompanyB.ToString("D"),
                group.RawJson,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                TestCompanyIds.CompanyC.ToString("D"),
                group.RawJson,
                StringComparison.OrdinalIgnoreCase);

            await using var connection = new SqlConnection(factory.DatabaseConnectionString);
            await connection.OpenAsync();
            var deactivatedOtherCompanies = await ExecuteNonQueryAsync(
                connection,
                """
                UPDATE [organization].[Companies]
                SET [IsActive] = 0
                WHERE [Id] IN (@CompanyB, @CompanyC)
                  AND [IsActive] = 1;
                """,
                new SqlParameter("@CompanyB", TestCompanyIds.CompanyB),
                new SqlParameter("@CompanyC", TestCompanyIds.CompanyC));
            Assert.Equal(2, deactivatedOtherCompanies);

            var oneActiveCompanyGroup = await ReadSnapshotAsync(groupClient);
            Assert.Empty(oneActiveCompanyGroup.Capabilities);

            var reactivatedOtherCompanies = await ExecuteNonQueryAsync(
                connection,
                """
                UPDATE [organization].[Companies]
                SET [IsActive] = 1
                WHERE [Id] IN (@CompanyB, @CompanyC)
                  AND [IsActive] = 0;
                """,
                new SqlParameter("@CompanyB", TestCompanyIds.CompanyB),
                new SqlParameter("@CompanyC", TestCompanyIds.CompanyC));
            Assert.Equal(2, reactivatedOtherCompanies);

            var deactivated = await ExecuteNonQueryAsync(
                connection,
                """
                UPDATE [organization].[Companies]
                SET [IsActive] = 0
                WHERE [Id] = @CompanyId
                  AND [IsActive] = 1;
                """,
                new SqlParameter("@CompanyId", TestCompanyIds.CompanyA));
            Assert.Equal(1, deactivated);

            using (var inactiveCompanyRead = await administratorClient.GetAsync(
                       "/api/v1/counterparties"))
            {
                var inactiveBody = await inactiveCompanyRead.Content.ReadAsStringAsync();
                Assert.True(
                    inactiveCompanyRead.StatusCode == HttpStatusCode.Forbidden,
                    $"Expected 403 for an inactive selected company, got "
                    + $"{(int)inactiveCompanyRead.StatusCode}: {inactiveBody}");
                Assert.DoesNotContain(
                    TestCompanyIds.CompanyA.ToString("D"),
                    inactiveBody,
                    StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(
                    CounterpartyShellCapabilityProvider.ListCapabilityId,
                    inactiveBody,
                    StringComparison.Ordinal);
            }

            var reactivated = await ExecuteNonQueryAsync(
                connection,
                """
                UPDATE [organization].[Companies]
                SET [IsActive] = 1
                WHERE [Id] = @CompanyId
                  AND [IsActive] = 0;
                """,
                new SqlParameter("@CompanyId", TestCompanyIds.CompanyA));
            Assert.Equal(1, reactivated);

            var restored = await ReadSnapshotAsync(administratorClient);
            Assert.Equal(
                [CounterpartyShellCapabilityProvider.ListCapabilityId],
                restored.Capabilities);

            var deleted = await ExecuteNonQueryAsync(
                connection,
                """
                DELETE FROM [identity].[RolePermissions]
                WHERE [RoleId] = @RoleId
                  AND [PermissionCode] = @PermissionCode;
                """,
                new SqlParameter("@RoleId", SystemAdministratorRoleId),
                new SqlParameter(
                    "@PermissionCode",
                    ErpPermissions.CounterpartyPartiesRead));
            Assert.Equal(1, deleted);

            var revoked = await ReadSnapshotAsync(administratorClient);
            Assert.DoesNotContain(
                CounterpartyShellCapabilityProvider.ListCapabilityId,
                revoked.Capabilities);
            Assert.NotEqual(restored.Generation, revoked.Generation);
            Assert.DoesNotContain(
                ErpPermissions.CounterpartyPartiesRead,
                revoked.RawJson,
                StringComparison.Ordinal);
        }
        finally
        {
            await factory.DisposeAsync();
            await DropDatabaseAsync(baseConnectionString, factory.DatabaseName);
        }
    }

    [SqlServerFact]
    public async Task HeaderlessGroupCapability_UsesPerCompanyPermissionsAndTracksRevocation()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var provider = new GroupReadTestProvider();
        var authorityRevision = ShellCapabilityRegistry.ComputeCandidateRevision([provider]);
        var factory = new SqlServerRelationalApiFactory(
            baseConnectionString,
            services =>
            {
                services.RemoveAll<ShellCapabilityAuthorityCatalog>();
                services.RemoveAll<IShellCapabilityProvider>();
                services.AddSingleton(new ShellCapabilityAuthorityCatalog([authorityRevision]));
                services.AddSingleton<IShellCapabilityProvider>(provider);
            });

        try
        {
            using (var healthClient = factory.CreateClient())
            using (var healthResponse = await healthClient.GetAsync("/health/live"))
            {
                Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);
            }

            using var groupClient = factory.CreateClient();
            groupClient.DefaultRequestHeaders.Add(
                "X-Development-User",
                TestUsers.GroupAdmin);
            groupClient.DefaultRequestHeaders.Add(
                "X-Correlation-Id",
                Guid.NewGuid().ToString("N"));
            using var companyAClient = factory.CreateClientFor(
                TestUsers.GroupAdmin,
                TestCompanyIds.CompanyA);
            using var companyBClient = factory.CreateClientFor(
                TestUsers.GroupAdmin,
                TestCompanyIds.CompanyB);
            using var companyCClient = factory.CreateClientFor(
                TestUsers.GroupAdmin,
                TestCompanyIds.CompanyC);

            var initialGroup = await ReadSnapshotAsync(groupClient);
            var initialCompanyA = await ReadSnapshotAsync(companyAClient);
            var initialCompanyB = await ReadSnapshotAsync(companyBClient);
            var initialCompanyC = await ReadSnapshotAsync(companyCClient);
            Assert.Equal([GroupReadTestProvider.CapabilityId], initialGroup.Capabilities);
            Assert.Equal([GroupReadTestProvider.CapabilityId], initialCompanyA.Capabilities);
            Assert.Equal([GroupReadTestProvider.CapabilityId], initialCompanyB.Capabilities);
            Assert.Equal([GroupReadTestProvider.CapabilityId], initialCompanyC.Capabilities);
            Assert.NotEqual(initialGroup.Generation, initialCompanyA.Generation);

            await using var connection = new SqlConnection(factory.DatabaseConnectionString);
            await connection.OpenAsync();
            Assert.Equal(
                1,
                await ExecuteNonQueryAsync(
                    connection,
                    """
                    INSERT INTO [identity].[RolePermissions] ([RoleId], [PermissionCode])
                    VALUES (@RoleId, @PermissionCode);
                    """,
                    new SqlParameter("@RoleId", OrganizationReaderRoleId),
                    new SqlParameter(
                        "@PermissionCode",
                        ErpPermissions.CounterpartyPartiesRead)));
            using (var narrowGroupClient = factory.CreateClient())
            using (var narrowSelectedClient = factory.CreateClientFor(
                       TestUsers.CompanyAViewer,
                       TestCompanyIds.CompanyA))
            {
                narrowGroupClient.DefaultRequestHeaders.Add(
                    "X-Development-User",
                    TestUsers.CompanyAViewer);
                narrowGroupClient.DefaultRequestHeaders.Add(
                    "X-Correlation-Id",
                    Guid.NewGuid().ToString("N"));
                var narrowGroup = await ReadSnapshotAsync(narrowGroupClient);
                var narrowSelected = await ReadSnapshotAsync(narrowSelectedClient);
                Assert.Empty(narrowGroup.Capabilities);
                Assert.Empty(narrowSelected.Capabilities);
            }

            Assert.Equal(
                1,
                await DowngradeGroupAssignmentAsync(connection, TestCompanyIds.CompanyA));

            var companyARevokedGroup = await ReadSnapshotAsync(groupClient);
            var companyARevoked = await ReadSnapshotAsync(companyAClient);
            var companyBStillGranted = await ReadSnapshotAsync(companyBClient);
            var companyCStillGranted = await ReadSnapshotAsync(companyCClient);
            Assert.Equal(
                [GroupReadTestProvider.CapabilityId],
                companyARevokedGroup.Capabilities);
            Assert.Empty(companyARevoked.Capabilities);
            Assert.Equal(
                [GroupReadTestProvider.CapabilityId],
                companyBStillGranted.Capabilities);
            Assert.Equal(
                [GroupReadTestProvider.CapabilityId],
                companyCStillGranted.Capabilities);
            Assert.NotEqual(initialGroup.Generation, companyARevokedGroup.Generation);

            Assert.Equal(
                1,
                await DowngradeGroupAssignmentAsync(connection, TestCompanyIds.CompanyB));
            var companyBRevokedGroup = await ReadSnapshotAsync(groupClient);
            var companyBRevoked = await ReadSnapshotAsync(companyBClient);
            var companyCAfterBRevocation = await ReadSnapshotAsync(companyCClient);
            Assert.Equal(
                [GroupReadTestProvider.CapabilityId],
                companyBRevokedGroup.Capabilities);
            Assert.Empty(companyBRevoked.Capabilities);
            Assert.Equal(
                [GroupReadTestProvider.CapabilityId],
                companyCAfterBRevocation.Capabilities);
            Assert.NotEqual(companyARevokedGroup.Generation, companyBRevokedGroup.Generation);

            Assert.Equal(
                1,
                await DowngradeGroupAssignmentAsync(connection, TestCompanyIds.CompanyC));
            var fullyRevokedGroup = await ReadSnapshotAsync(groupClient);
            Assert.Empty(fullyRevokedGroup.Capabilities);
            Assert.NotEqual(companyBRevokedGroup.Generation, fullyRevokedGroup.Generation);
            Assert.DoesNotContain(
                ErpPermissions.CounterpartyPartiesRead,
                fullyRevokedGroup.RawJson,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                TestCompanyIds.CompanyA.ToString("D"),
                fullyRevokedGroup.RawJson,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                TestCompanyIds.CompanyB.ToString("D"),
                fullyRevokedGroup.RawJson,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                TestCompanyIds.CompanyC.ToString("D"),
                fullyRevokedGroup.RawJson,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await factory.DisposeAsync();
            await DropDatabaseAsync(baseConnectionString, factory.DatabaseName);
        }
    }

    [SqlServerFact]
    public async Task AuthorizationSnapshot_NeverPublishesSyntheticMultiPermissionGrant()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var provider = new MultiPermissionTestProvider();
        var authorityRevision = ShellCapabilityRegistry.ComputeCandidateRevision([provider]);
        var factory = new SqlServerRelationalApiFactory(
            baseConnectionString,
            services =>
            {
                services.RemoveAll<ShellCapabilityAuthorityCatalog>();
                services.RemoveAll<IShellCapabilityProvider>();
                services.AddSingleton(new ShellCapabilityAuthorityCatalog([authorityRevision]));
                services.AddSingleton<IShellCapabilityProvider>(provider);
            });

        try
        {
            using (var healthClient = factory.CreateClient())
            using (var healthResponse = await healthClient.GetAsync("/health/live"))
            {
                Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);
            }

            await using var setupConnection = new SqlConnection(
                factory.DatabaseConnectionString);
            await setupConnection.OpenAsync();
            Assert.Equal(
                1,
                await ScalarAsync<int>(
                    setupConnection,
                    """
                    SELECT CONVERT(int, [is_read_committed_snapshot_on])
                    FROM [sys].[databases]
                    WHERE [database_id] = DB_ID();
                    """));
            await SetPermissionPairAsync(
                setupConnection,
                includeFirst: true,
                includeSecond: false);

            using var client = factory.CreateClientFor(
                TestUsers.CompanyA,
                TestCompanyIds.CompanyA);
            CapabilitySnapshotProbe initial;
            using (var commandObserver = new AuthorizationProjectionCommandObserver(
                       factory.DatabaseName))
            {
                initial = await ReadSnapshotAsync(client);
                Assert.Equal(1, commandObserver.CommandCount);
            }
            Assert.DoesNotContain(MultiPermissionTestProvider.CapabilityId, initial.Capabilities);

            await SetPermissionPairAsync(
                setupConnection,
                includeFirst: true,
                includeSecond: true);
            var granted = await ReadSnapshotAsync(client);
            Assert.Contains(MultiPermissionTestProvider.CapabilityId, granted.Capabilities);

            await SetPermissionPairAsync(
                setupConnection,
                includeFirst: true,
                includeSecond: false);
            var revoked = await ReadSnapshotAsync(client);
            Assert.DoesNotContain(MultiPermissionTestProvider.CapabilityId, revoked.Capabilities);
            Assert.NotEqual(granted.Generation, revoked.Generation);

            for (var iteration = 0; iteration < 6; iteration++)
            {
                var includeFirst = iteration % 2 != 0;
                var includeSecond = !includeFirst;
                await WithUncommittedPermissionPairAsync(
                    factory.DatabaseConnectionString,
                    includeFirst,
                    includeSecond,
                    async () =>
                    {
                        var overlappingReads = Enumerable.Range(0, 4)
                            .Select(async _ =>
                            {
                                using var readerClient = factory.CreateClientFor(
                                    TestUsers.CompanyA,
                                    TestCompanyIds.CompanyA);
                                return await ReadSnapshotAsync(readerClient);
                            })
                            .ToArray();
                        var allReads = Task.WhenAll(overlappingReads);
                        var completed = await Task.WhenAny(
                            allReads,
                            Task.Delay(TimeSpan.FromSeconds(30)));
                        Assert.Same(allReads, completed);
                        foreach (var snapshot in await allReads)
                        {
                            Assert.DoesNotContain(
                                MultiPermissionTestProvider.CapabilityId,
                                snapshot.Capabilities);
                        }
                    });

                var committedSnapshot = await ReadSnapshotAsync(client);
                Assert.DoesNotContain(
                    MultiPermissionTestProvider.CapabilityId,
                    committedSnapshot.Capabilities);
            }
        }
        finally
        {
            await factory.DisposeAsync();
            await DropDatabaseAsync(baseConnectionString, factory.DatabaseName);
        }
    }

    [SqlServerFact]
    public async Task AuthorizationSnapshotStartup_FailsClosedWhenRcsiIsDisabled()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var factory = new SqlServerRelationalApiFactory(baseConnectionString);
        await CreateDatabaseWithRcsiDisabledAsync(
            baseConnectionString,
            factory.DatabaseName);

        try
        {
            var exception = await Record.ExceptionAsync(async () =>
            {
                using var client = factory.CreateClient();
                using var response = await client.GetAsync("/health/live");
            });

            Assert.NotNull(exception);
            Assert.Contains(
                "Identity authorization snapshot requires SQL Server "
                + "READ_COMMITTED_SNAPSHOT ON.",
                exception.ToString(),
                StringComparison.Ordinal);
        }
        finally
        {
            await factory.DisposeAsync();
            await DropDatabaseAsync(baseConnectionString, factory.DatabaseName);
        }
    }

    private static async Task SetPermissionPairAsync(
        SqlConnection connection,
        bool includeFirst,
        bool includeSecond)
    {
        await using var transaction = await connection.BeginTransactionAsync();
        await WritePermissionPairAsync(
            connection,
            (SqlTransaction)transaction,
            includeFirst,
            includeSecond);
        await transaction.CommitAsync();
    }

    private static async Task WithUncommittedPermissionPairAsync(
        string connectionString,
        bool includeFirst,
        bool includeSecond,
        Func<Task> whileTransactionIsOpen)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await WritePermissionPairAsync(
            connection,
            (SqlTransaction)transaction,
            includeFirst,
            includeSecond);
        await whileTransactionIsOpen();
        await transaction.CommitAsync();
    }

    private static async Task WritePermissionPairAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        bool includeFirst,
        bool includeSecond)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            DELETE FROM [identity].[RolePermissions]
            WHERE [RoleId] = @RoleId
              AND [PermissionCode] IN (@FirstPermission, @SecondPermission);

            IF @IncludeFirst = 1
                INSERT INTO [identity].[RolePermissions] ([RoleId], [PermissionCode])
                VALUES (@RoleId, @FirstPermission);

            IF @IncludeSecond = 1
                INSERT INTO [identity].[RolePermissions] ([RoleId], [PermissionCode])
                VALUES (@RoleId, @SecondPermission);
            """;
        command.Parameters.AddRange(
        [
            new SqlParameter("@RoleId", OrganizationOperatorRoleId),
            new SqlParameter("@FirstPermission", ErpPermissions.OrganizationLocationsRead),
            new SqlParameter(
                "@SecondPermission",
                ErpPermissions.OrganizationLocationsStatusUpdate),
            new SqlParameter("@IncludeFirst", includeFirst),
            new SqlParameter("@IncludeSecond", includeSecond),
        ]);
        await command.ExecuteNonQueryAsync();
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

    private static Task<int> DowngradeGroupAssignmentAsync(
        SqlConnection connection,
        Guid companyId)
    {
        return ExecuteNonQueryAsync(
            connection,
            """
            UPDATE [identity].[RoleAssignments]
            SET [RoleId] = @OperatorRoleId,
                [Version] = [Version] + 1
            WHERE [UserId] = @UserId
              AND [CompanyId] = @CompanyId
              AND [RoleId] = @AdministratorRoleId;
            """,
            new SqlParameter("@OperatorRoleId", OrganizationOperatorRoleId),
            new SqlParameter("@UserId", GroupAdministratorUserId),
            new SqlParameter("@CompanyId", companyId),
            new SqlParameter("@AdministratorRoleId", SystemAdministratorRoleId));
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

    private static async Task CreateDatabaseWithRcsiDisabledAsync(
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
            CREATE DATABASE [{databaseName}];
            ALTER DATABASE [{databaseName}]
                SET READ_COMMITTED_SNAPSHOT OFF WITH ROLLBACK IMMEDIATE;
            """;
        await command.ExecuteNonQueryAsync();
    }

    private sealed record CapabilitySnapshotProbe(
        string ContractVersion,
        string Generation,
        IReadOnlyList<string> Capabilities,
        string RawJson);

    private sealed class DeactivatingAuthorizationSnapshotReadObserver(
        Func<string> connectionStringAccessor,
        Guid companyId)
        : IAuthorizationSnapshotReadObserver
    {
        private int _deactivationCount;

        public int DeactivationCount => Volatile.Read(ref _deactivationCount);

        public void ObserveDatabaseRead()
        {
            if (Interlocked.CompareExchange(ref _deactivationCount, 1, 0) != 0)
            {
                return;
            }

            using var connection = new SqlConnection(connectionStringAccessor());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "UPDATE [organization].[Companies] SET [IsActive] = 0 WHERE [Id] = @CompanyId;";
            command.Parameters.Add(new SqlParameter("@CompanyId", companyId));
            if (command.ExecuteNonQuery() != 1)
            {
                throw new InvalidOperationException(
                    "Shell capability yarış probu seçili şirketi pasifleştiremedi.");
            }
        }
    }

    private sealed class AuthorizationProjectionCommandObserver :
        IObserver<DiagnosticListener>,
        IObserver<KeyValuePair<string, object?>>,
        IDisposable
    {
        private readonly string _databaseName;
        private readonly ConcurrentBag<IDisposable> _listenerSubscriptions = [];
        private readonly IDisposable _allListenersSubscription;
        private int _commandCount;

        public AuthorizationProjectionCommandObserver(string databaseName)
        {
            _databaseName = databaseName;
            _allListenersSubscription = DiagnosticListener.AllListeners.Subscribe(this);
        }

        public int CommandCount => Volatile.Read(ref _commandCount);

        public void OnNext(DiagnosticListener listener)
        {
            if (string.Equals(
                    listener.Name,
                    "Microsoft.EntityFrameworkCore",
                    StringComparison.Ordinal))
            {
                _listenerSubscriptions.Add(listener.Subscribe(this));
            }
        }

        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (!string.Equals(
                    value.Key,
                    RelationalEventId.CommandExecuting.Name,
                    StringComparison.Ordinal)
                || value.Value is not CommandEventData commandEvent
                || !string.Equals(
                    commandEvent.Command.Connection?.Database,
                    _databaseName,
                    StringComparison.Ordinal)
                || !commandEvent.Command.CommandText.Contains(
                    "FROM [identity].[Users] AS [u]",
                    StringComparison.Ordinal)
                || !commandEvent.Command.CommandText.Contains(
                    "[identity].[RolePermissions]",
                    StringComparison.Ordinal)
                || !commandEvent.Command.CommandText.Contains(
                    "[identity].[RoleAssignmentScopes]",
                    StringComparison.Ordinal))
            {
                return;
            }

            Interlocked.Increment(ref _commandCount);
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void Dispose()
        {
            _allListenersSubscription.Dispose();
            foreach (var subscription in _listenerSubscriptions)
            {
                subscription.Dispose();
            }
        }
    }

    private sealed class MultiPermissionTestProvider : IShellCapabilityProvider
    {
        public const string CapabilityId = "test.multi-permission.read";

        public string Owner => "TEST-OWNER";

        public string ContractVersion => "TEST-OWNER-CONTRACT-V1";

        public IReadOnlyCollection<ShellCapabilityDeclaration> GetDeclarations() =>
        [
            new ShellCapabilityDeclaration(
                CapabilityId,
                "TEST-MULTI-PERMISSION-ROUTE",
                "/test/multi-permission",
                "TEST-MULTI-PERMISSION-01",
                ShellCapabilityContextRequirement.SingleCompany,
                ShellCapabilityScopeRequirement.CompanyWide,
                [
                    ErpPermissions.OrganizationLocationsRead,
                    ErpPermissions.OrganizationLocationsStatusUpdate,
                ],
                ImplementationAvailable: true),
        ];
    }

    private sealed class GroupReadTestProvider : IShellCapabilityProvider
    {
        public const string CapabilityId = "test.group-read";

        public string Owner => "TEST-GROUP-READ";

        public string ContractVersion => "1.0";

        public IReadOnlyCollection<ShellCapabilityDeclaration> GetDeclarations() =>
        [
            new ShellCapabilityDeclaration(
                CapabilityId,
                "test.group-read",
                "/test/group-read",
                "TEST-GROUP-READ-01",
                ShellCapabilityContextRequirement.SingleOrAuthorizedMultiCompanyRead,
                ShellCapabilityScopeRequirement.CompanyWide,
                [ErpPermissions.CounterpartyPartiesRead],
                ImplementationAvailable: true),
        ];
    }
}
