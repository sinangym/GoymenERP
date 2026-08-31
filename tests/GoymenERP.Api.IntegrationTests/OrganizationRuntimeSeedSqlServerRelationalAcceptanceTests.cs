using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GoymenERP.Api.IntegrationTests;

public sealed class OrganizationRuntimeSeedSqlServerRelationalAcceptanceTests
{
    private const string PreFiscalMigration =
        "20260827174140_AddSqlRowVersionConcurrency";
    private const string GdcLegalName =
        "Göymen Demir Çelik İnş. San. ve Tic. Ltd. Şti.";
    private const string GspLegalName =
        "Göymen Sac Profil San. ve Tic. Ltd. Şti.";
    private const string GinLegalName = "Göymen İnşaat";
    private const string GdcVkn = "4100046586";
    private const string GspVkn = "4101507252";
    private const string GinPlaceholderVkn = "1111111111";

    private static readonly Guid LegacySiteId = Guid.Parse(
        "11000000-0000-0000-0000-000000000099");
    private static readonly Guid LegacyLocationId = Guid.Parse(
        "12000000-0000-0000-0000-000000000099");
    private static readonly Guid LegacyWorkCenterId = Guid.Parse(
        "13000000-0000-0000-0000-000000000099");
    private static readonly Guid CustomGspSiteId = Guid.Parse(
        "11000000-0000-0000-0000-000000000098");
    private static readonly Guid CustomGspLocationId = Guid.Parse(
        "12000000-0000-0000-0000-000000000098");
    private static readonly Guid CustomGspWorkCenterId = Guid.Parse(
        "13000000-0000-0000-0000-000000000098");

    private static readonly LegacySiteTemplate[] LegacyGspSiteTemplates =
    [
        new(
            "SITE-OSM",
            "Factory",
            false,
            ["OSM-H0", "OSM-H1", "OSM-H2", "OSM-H3", "OSM-H4", "OSM-H5", "OSM-H6", "OSM-H7"]),
        new("SITE-GAZ", "Warehouse", false, ["GAZ-DEP"]),
        new(
            "SITE-EXT",
            "ExternalWarehouse",
            true,
            ["EXT-BASTUG", "EXT-SELIMOGLU", "EXT-ALYA", "EXT-SADILER", "EXT-TOSCELIK", "EXT-DOGRULUK"]),
    ];

    private static readonly LegacyWorkCenterTemplate[] LegacyGspWorkCenterTemplates =
    [
        new("WC-PAINT", "OSM-H0", "Paint"),
        new("WC-TUBE", "OSM-H1", "TubeProfile"),
        new("WC-SLIT", "OSM-H3", "Slitting"),
        new("WC-CR", "OSM-H3", "ColdRolling"),
        new("WC-CUT", "OSM-H6", "CutToLength"),
    ];

    [SqlServerFact]
    public async Task FreshRuntimeSeed_PersistsApprovedFiscalRowsAndProductionTopologyWithoutDirectoryLeak()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var factory = new SqlServerRelationalApiFactory(
            baseConnectionString,
            applyTestCompanyBTopologyTemplates: false);

        try
        {
            using var healthClient = factory.CreateClient();
            using var healthResponse = await healthClient.GetAsync("/health/live");
            Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);

            await using var connection = new SqlConnection(factory.DatabaseConnectionString);
            await connection.OpenAsync();

            AssertApprovedCompanyRows(await ReadCompanyRowsAsync(connection));
            await AssertProductionTopologyAsync(connection);
            await AssertFiscalConstraintsAsync(connection);
            await AssertDirectoryDoesNotExposeFiscalDataAsync(factory);
        }
        finally
        {
            await factory.DisposeAsync();
            await DropDatabaseAsync(baseConnectionString, factory.DatabaseName);
        }
    }

    [SqlServerFact]
    public async Task LegacyPlaceholderUpgrade_PreservesCompanyIdsAndOrganizationForeignKeys()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var factory = new SqlServerRelationalApiFactory(
            baseConnectionString,
            applyTestCompanyBTopologyTemplates: false);

        try
        {
            await PrepareLegacyOrganizationDatabaseAsync(factory.DatabaseConnectionString);

            using var healthClient = factory.CreateClient();
            using var healthResponse = await healthClient.GetAsync("/health/live");
            Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);

            await using var connection = new SqlConnection(factory.DatabaseConnectionString);
            await connection.OpenAsync();

            var companies = await ReadCompanyRowsAsync(connection);
            AssertApprovedCompanyRows(companies);
            Assert.Equal(2, companies.Single(company => company.Code == "GDC").Version);
            Assert.Equal(2, companies.Single(company => company.Code == "GSP").Version);
            Assert.Equal(1, companies.Single(company => company.Code == "GIN").Version);
            Assert.Equal(
                0,
                await ScalarAsync<int>(
                    connection,
                    """
                    SELECT COUNT(*)
                    FROM [organization].[Companies]
                    WHERE [Code] IN (N'TBD-GOY-01', N'TBD-GOY-02');
                    """));

            await AssertLegacyForeignKeysAsync(connection);
            await AssertLegacyGspDeterministicTopologyRetiredAsync(connection);
            await AssertCustomGspTopologyRemainsActiveAsync(connection);
            Assert.Equal(
                3,
                await ScalarAsync<int>(
                    connection,
                    "SELECT COUNT(*) FROM [organization].[__EFMigrationsHistory];"));
        }
        finally
        {
            await factory.DisposeAsync();
            await DropDatabaseAsync(baseConnectionString, factory.DatabaseName);
        }
    }

    [SqlServerFact]
    public async Task LegacyAliasCollision_RollsBackFiscalMigrationAtomically()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var factory = new SqlServerRelationalApiFactory(
            baseConnectionString,
            applyTestCompanyBTopologyTemplates: false);
        var wrongLegacyCompanyId = Guid.Parse(
            "10000000-0000-0000-0000-000000000099");

        try
        {
            await AssertFiscalMigrationCollisionRollsBackAtomicallyAsync(
                factory.DatabaseConnectionString,
                wrongLegacyCompanyId,
                "TBD-GOY-01",
                "Yanlış kimlikli legacy alias",
                isPlaceholder: true,
                version: 1,
                expectedSqlError: 51024);
        }
        finally
        {
            await factory.DisposeAsync();
            await DropDatabaseAsync(baseConnectionString, factory.DatabaseName);
        }
    }

    [SqlServerFact]
    public async Task GinCodeOwnedByUnexpectedCompany_RollsBackFiscalMigrationAtomically()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var factory = new SqlServerRelationalApiFactory(
            baseConnectionString,
            applyTestCompanyBTopologyTemplates: false);
        var wrongGinOwnerId = Guid.Parse(
            "10000000-0000-0000-0000-000000000099");

        try
        {
            await AssertFiscalMigrationCollisionRollsBackAtomicallyAsync(
                factory.DatabaseConnectionString,
                wrongGinOwnerId,
                "GIN",
                "Yanlış kimlikli GIN şirketi",
                isPlaceholder: false,
                version: 7,
                expectedSqlError: 51026);
        }
        finally
        {
            await factory.DisposeAsync();
            await DropDatabaseAsync(baseConnectionString, factory.DatabaseName);
        }
    }

    [SqlServerFact]
    public async Task FixedGinIdWithUnexpectedCode_RollsBackFiscalMigrationAtomically()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var factory = new SqlServerRelationalApiFactory(
            baseConnectionString,
            applyTestCompanyBTopologyTemplates: false);

        try
        {
            await AssertFiscalMigrationCollisionRollsBackAtomicallyAsync(
                factory.DatabaseConnectionString,
                TestCompanyIds.CompanyC,
                "WRONG-GIN-CODE",
                "Yanlış kodlu sabit GIN şirketi",
                isPlaceholder: false,
                version: 9,
                expectedSqlError: 51027);
        }
        finally
        {
            await factory.DisposeAsync();
            await DropDatabaseAsync(baseConnectionString, factory.DatabaseName);
        }
    }

    [SqlServerFact]
    public async Task FixedGinIdAndCode_WithLegacySemanticDrift_IsNormalizedByFiscalMigration()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var factory = new SqlServerRelationalApiFactory(
            baseConnectionString,
            applyTestCompanyBTopologyTemplates: false);

        try
        {
            await MigrateOrganizationDatabaseAsync(
                factory.DatabaseConnectionString,
                PreFiscalMigration);
            await using (var connection = new SqlConnection(factory.DatabaseConnectionString))
            {
                await connection.OpenAsync();
                await InsertLegacyCompanyAsync(
                    connection,
                    TestCompanyIds.CompanyC,
                    "GIN",
                    "Eski ve yanlış GIN hukuki unvanı",
                    isPlaceholder: false,
                    version: 11);
            }

            await MigrateOrganizationDatabaseAsync(
                factory.DatabaseConnectionString,
                targetMigration: null);

            await using var verification = new SqlConnection(factory.DatabaseConnectionString);
            await verification.OpenAsync();
            var company = Assert.Single(await ReadCompanyRowsAsync(verification));
            AssertCompany(
                company,
                TestCompanyIds.CompanyC,
                "GIN",
                GinLegalName,
                GinPlaceholderVkn,
                isPlaceholder: true,
                invoiceCapable: false);
            Assert.Equal(12, company.Version);
            Assert.Equal(
                3,
                await ScalarAsync<int>(
                    verification,
                    "SELECT COUNT(*) FROM [organization].[__EFMigrationsHistory];"));
        }
        finally
        {
            await factory.DisposeAsync();
            await DropDatabaseAsync(baseConnectionString, factory.DatabaseName);
        }
    }

    private static async Task AssertFiscalMigrationCollisionRollsBackAtomicallyAsync(
        string connectionString,
        Guid companyId,
        string code,
        string legalName,
        bool isPlaceholder,
        long version,
        int expectedSqlError)
    {
        await MigrateOrganizationDatabaseAsync(connectionString, PreFiscalMigration);
        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await InsertLegacyCompanyAsync(
                connection,
                companyId,
                code,
                legalName,
                isPlaceholder,
                version);
        }

        var exception = await Assert.ThrowsAsync<SqlException>(() =>
            MigrateOrganizationDatabaseAsync(connectionString, targetMigration: null));
        Assert.Equal(expectedSqlError, exception.Number);

        await using var verification = new SqlConnection(connectionString);
        await verification.OpenAsync();
        Assert.Equal(
            2,
            await ScalarAsync<int>(
                verification,
                "SELECT COUNT(*) FROM [organization].[__EFMigrationsHistory];"));
        Assert.Equal(
            0,
            await ScalarAsync<int>(
                verification,
                """
                SELECT COUNT(*)
                FROM [organization].[__EFMigrationsHistory]
                WHERE [MigrationId] = N'20260831142045_AddCompanyFiscalRuntimeSeed';
                """));
        Assert.Equal(
            0,
            await ScalarAsync<int>(
                verification,
                """
                SELECT COUNT(*)
                FROM [sys].[columns]
                WHERE [object_id] = OBJECT_ID(N'[organization].[Companies]')
                  AND [name] IN (N'Vkn', N'InvoiceCapable');
                """));

        await using var command = verification.CreateCommand();
        command.CommandText =
            """
            SELECT [Id], [Code], [LegalName], [IsPlaceholder], [IsActive], [Version]
            FROM [organization].[Companies]
            WHERE [Id] = @Id;
            """;
        command.Parameters.AddWithValue("@Id", companyId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(companyId, reader.GetGuid(0));
        Assert.Equal(code, reader.GetString(1));
        Assert.Equal(legalName, reader.GetString(2));
        Assert.Equal(isPlaceholder, reader.GetBoolean(3));
        Assert.True(reader.GetBoolean(4));
        Assert.Equal(version, reader.GetInt64(5));
        Assert.False(await reader.ReadAsync());
    }

    private static Task InsertLegacyCompanyAsync(
        SqlConnection connection,
        Guid companyId,
        string code,
        string legalName,
        bool isPlaceholder,
        long version)
    {
        return ExecuteAsync(
            connection,
            """
            INSERT INTO [organization].[Companies]
                ([Id], [Code], [LegalName], [IsPlaceholder], [IsActive], [Version],
                 [CreatedAtUtc], [UpdatedAtUtc])
            VALUES
                (@Id, @Code, @LegalName, @IsPlaceholder, 1, @Version,
                 SYSUTCDATETIME(), SYSUTCDATETIME());
            """,
            new SqlParameter("@Id", companyId),
            new SqlParameter("@Code", code),
            new SqlParameter("@LegalName", legalName),
            new SqlParameter("@IsPlaceholder", isPlaceholder),
            new SqlParameter("@Version", version));
    }

    private static async Task PrepareLegacyOrganizationDatabaseAsync(string connectionString)
    {
        await MigrateOrganizationDatabaseAsync(connectionString, PreFiscalMigration);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await ExecuteAsync(
            connection,
            """
            INSERT INTO [organization].[Companies]
                ([Id], [Code], [LegalName], [IsPlaceholder], [IsActive], [Version],
                 [CreatedAtUtc], [UpdatedAtUtc])
            VALUES
                (@CompanyAId, N'TBD-GOY-01', N'Göymen Grup Şirket 1 (Teyit Bekliyor)',
                 1, 1, 1, SYSUTCDATETIME(), SYSUTCDATETIME()),
                (@CompanyBId, N'TBD-GOY-02', N'Göymen Grup Şirket 2 (Teyit Bekliyor)',
                 1, 1, 1, SYSUTCDATETIME(), SYSUTCDATETIME());

            INSERT INTO [organization].[Sites]
                ([Id], [Type], [CompanyId], [Code], [Name], [IsActive], [Version],
                 [CreatedAtUtc], [UpdatedAtUtc])
            VALUES
                (@SiteId, N'Factory', @CompanyAId, N'LEGACY-SITE', N'Legacy Site',
                 1, 1, SYSUTCDATETIME(), SYSUTCDATETIME());

            INSERT INTO [organization].[Locations]
                ([Id], [SiteId], [IsExternal], [CompanyId], [Code], [Name],
                 [IsActive], [Version], [CreatedAtUtc], [UpdatedAtUtc])
            VALUES
                (@LocationId, @SiteId, 0, @CompanyAId, N'LEGACY-LOCATION',
                 N'Legacy Location', 1, 1, SYSUTCDATETIME(), SYSUTCDATETIME());

            INSERT INTO [organization].[WorkCenters]
                ([Id], [LocationId], [Kind], [CompanyId], [Code], [Name],
                 [IsActive], [Version], [CreatedAtUtc], [UpdatedAtUtc])
            VALUES
                (@WorkCenterId, @LocationId, N'Paint', @CompanyAId, N'LEGACY-WC',
                 N'Legacy Work Center', 1, 1, SYSUTCDATETIME(), SYSUTCDATETIME());
            """,
            new SqlParameter("@CompanyAId", TestCompanyIds.CompanyA),
            new SqlParameter("@CompanyBId", TestCompanyIds.CompanyB),
            new SqlParameter("@SiteId", LegacySiteId),
            new SqlParameter("@LocationId", LegacyLocationId),
            new SqlParameter("@WorkCenterId", LegacyWorkCenterId));
        await InsertLegacyGspTopologyAsync(connection);
    }

    private static async Task MigrateOrganizationDatabaseAsync(
        string connectionString,
        string? targetMigration)
    {
        var options = new DbContextOptionsBuilder<OrganizationDbContext>()
            .UseSqlServer(
                connectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "organization"))
            .Options;
        await using (var dbContext = new OrganizationDbContext(
                         options,
                         new CompanyContext(),
                         new OrganizationBootstrapWriteScope()))
        {
            await dbContext.GetService<IMigrator>().MigrateAsync(targetMigration);
        }
    }

    private static async Task InsertLegacyGspTopologyAsync(SqlConnection connection)
    {
        foreach (var site in LegacyGspSiteTemplates)
        {
            var siteId = CreateDeterministicGuid(
                TestCompanyIds.CompanyB,
                "site",
                site.Code);
            await ExecuteAsync(
                connection,
                """
                INSERT INTO [organization].[Sites]
                    ([Id], [Type], [CompanyId], [Code], [Name], [IsActive], [Version],
                     [CreatedAtUtc], [UpdatedAtUtc])
                VALUES
                    (@Id, @Type, @CompanyId, @Code, @Name, 1, 1,
                     SYSUTCDATETIME(), SYSUTCDATETIME());
                """,
                new SqlParameter("@Id", siteId),
                new SqlParameter("@Type", site.Type),
                new SqlParameter("@CompanyId", TestCompanyIds.CompanyB),
                new SqlParameter("@Code", site.Code),
                new SqlParameter("@Name", $"Legacy {site.Code}"));

            foreach (var locationCode in site.LocationCodes)
            {
                await ExecuteAsync(
                    connection,
                    """
                    INSERT INTO [organization].[Locations]
                        ([Id], [SiteId], [IsExternal], [CompanyId], [Code], [Name],
                         [IsActive], [Version], [CreatedAtUtc], [UpdatedAtUtc])
                    VALUES
                        (@Id, @SiteId, @IsExternal, @CompanyId, @Code, @Name,
                         1, 1, SYSUTCDATETIME(), SYSUTCDATETIME());
                    """,
                    new SqlParameter(
                        "@Id",
                        CreateDeterministicGuid(
                            TestCompanyIds.CompanyB,
                            "location",
                            locationCode)),
                    new SqlParameter("@SiteId", siteId),
                    new SqlParameter("@IsExternal", site.IsExternal),
                    new SqlParameter("@CompanyId", TestCompanyIds.CompanyB),
                    new SqlParameter("@Code", locationCode),
                    new SqlParameter("@Name", $"Legacy {locationCode}"));
            }
        }

        foreach (var workCenter in LegacyGspWorkCenterTemplates)
        {
            await ExecuteAsync(
                connection,
                """
                INSERT INTO [organization].[WorkCenters]
                    ([Id], [LocationId], [Kind], [CompanyId], [Code], [Name],
                     [IsActive], [Version], [CreatedAtUtc], [UpdatedAtUtc])
                VALUES
                    (@Id, @LocationId, @Kind, @CompanyId, @Code, @Name,
                     1, 1, SYSUTCDATETIME(), SYSUTCDATETIME());
                """,
                new SqlParameter(
                    "@Id",
                    CreateDeterministicGuid(
                        TestCompanyIds.CompanyB,
                        "work-center",
                        workCenter.Code)),
                new SqlParameter(
                    "@LocationId",
                    CreateDeterministicGuid(
                        TestCompanyIds.CompanyB,
                        "location",
                        workCenter.LocationCode)),
                new SqlParameter("@Kind", workCenter.Kind),
                new SqlParameter("@CompanyId", TestCompanyIds.CompanyB),
                new SqlParameter("@Code", workCenter.Code),
                new SqlParameter("@Name", $"Legacy {workCenter.Code}"));
        }

        await ExecuteAsync(
            connection,
            """
            INSERT INTO [organization].[Sites]
                ([Id], [Type], [CompanyId], [Code], [Name], [IsActive], [Version],
                 [CreatedAtUtc], [UpdatedAtUtc])
            VALUES
                (@SiteId, N'Warehouse', @CompanyId, N'CUSTOM-GSP-SITE',
                 N'Custom GSP Site', 1, 1, SYSUTCDATETIME(), SYSUTCDATETIME());

            INSERT INTO [organization].[Locations]
                ([Id], [SiteId], [IsExternal], [CompanyId], [Code], [Name],
                 [IsActive], [Version], [CreatedAtUtc], [UpdatedAtUtc])
            VALUES
                (@LocationId, @SiteId, 0, @CompanyId, N'CUSTOM-GSP-LOCATION',
                 N'Custom GSP Location', 1, 1, SYSUTCDATETIME(), SYSUTCDATETIME());

            INSERT INTO [organization].[WorkCenters]
                ([Id], [LocationId], [Kind], [CompanyId], [Code], [Name],
                 [IsActive], [Version], [CreatedAtUtc], [UpdatedAtUtc])
            VALUES
                (@WorkCenterId, @LocationId, N'Paint', @CompanyId, N'CUSTOM-GSP-WC',
                 N'Custom GSP Work Center', 1, 1, SYSUTCDATETIME(), SYSUTCDATETIME());
            """,
            new SqlParameter("@SiteId", CustomGspSiteId),
            new SqlParameter("@LocationId", CustomGspLocationId),
            new SqlParameter("@WorkCenterId", CustomGspWorkCenterId),
            new SqlParameter("@CompanyId", TestCompanyIds.CompanyB));
    }

    private static async Task AssertLegacyGspDeterministicTopologyRetiredAsync(
        SqlConnection connection)
    {
        var locationCount = 0;
        foreach (var site in LegacyGspSiteTemplates)
        {
            var siteId = CreateDeterministicGuid(
                TestCompanyIds.CompanyB,
                "site",
                site.Code);
            AssertRetiredTopologyRow(
                await ReadTopologyRowAsync(connection, "Sites", null, siteId),
                expectedParentId: null);

            foreach (var locationCode in site.LocationCodes)
            {
                var locationId = CreateDeterministicGuid(
                    TestCompanyIds.CompanyB,
                    "location",
                    locationCode);
                AssertRetiredTopologyRow(
                    await ReadTopologyRowAsync(
                        connection,
                        "Locations",
                        "SiteId",
                        locationId),
                    siteId);
                locationCount++;
            }
        }

        foreach (var workCenter in LegacyGspWorkCenterTemplates)
        {
            AssertRetiredTopologyRow(
                await ReadTopologyRowAsync(
                    connection,
                    "WorkCenters",
                    "LocationId",
                    CreateDeterministicGuid(
                        TestCompanyIds.CompanyB,
                        "work-center",
                        workCenter.Code)),
                CreateDeterministicGuid(
                    TestCompanyIds.CompanyB,
                    "location",
                    workCenter.LocationCode));
        }

        Assert.Equal(3, LegacyGspSiteTemplates.Length);
        Assert.Equal(15, locationCount);
        Assert.Equal(5, LegacyGspWorkCenterTemplates.Length);
    }

    private static async Task AssertCustomGspTopologyRemainsActiveAsync(
        SqlConnection connection)
    {
        AssertActiveTopologyRow(
            await ReadTopologyRowAsync(
                connection,
                "Sites",
                null,
                CustomGspSiteId),
            expectedParentId: null);
        AssertActiveTopologyRow(
            await ReadTopologyRowAsync(
                connection,
                "Locations",
                "SiteId",
                CustomGspLocationId),
            CustomGspSiteId);
        AssertActiveTopologyRow(
            await ReadTopologyRowAsync(
                connection,
                "WorkCenters",
                "LocationId",
                CustomGspWorkCenterId),
            CustomGspLocationId);
    }

    private static void AssertRetiredTopologyRow(
        TopologyRow row,
        Guid? expectedParentId)
    {
        Assert.Equal(TestCompanyIds.CompanyB, row.CompanyId);
        Assert.Equal(expectedParentId, row.ParentId);
        Assert.False(row.IsActive);
        Assert.Equal(2, row.Version);
    }

    private static void AssertActiveTopologyRow(
        TopologyRow row,
        Guid? expectedParentId)
    {
        Assert.Equal(TestCompanyIds.CompanyB, row.CompanyId);
        Assert.Equal(expectedParentId, row.ParentId);
        Assert.True(row.IsActive);
        Assert.Equal(1, row.Version);
    }

    private static async Task<TopologyRow> ReadTopologyRowAsync(
        SqlConnection connection,
        string table,
        string? parentColumn,
        Guid id)
    {
        await using var command = connection.CreateCommand();
        var parentProjection = parentColumn is null
            ? "CAST(NULL AS uniqueidentifier)"
            : $"[{parentColumn}]";
        command.CommandText = $"""
            SELECT [CompanyId], [IsActive], [Version], {parentProjection}
            FROM [organization].[{table}]
            WHERE [Id] = @Id;
            """;
        command.Parameters.AddWithValue("@Id", id);
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        var row = new TopologyRow(
            reader.GetGuid(0),
            reader.GetBoolean(1),
            reader.GetInt64(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3));
        Assert.False(await reader.ReadAsync());
        return row;
    }

    private static Guid CreateDeterministicGuid(
        Guid companyId,
        string entityType,
        string code)
    {
        var value = $"{companyId:N}|{entityType}|{code.Trim().ToUpperInvariant()}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        Span<byte> guidBytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(guidBytes);
        return new Guid(guidBytes);
    }

    private static async Task AssertProductionTopologyAsync(SqlConnection connection)
    {
        await AssertTopologyAsync(connection, TestCompanyIds.CompanyA, 3, 15, 5);
        await AssertTopologyAsync(connection, TestCompanyIds.CompanyB, 0, 0, 0);
        await AssertTopologyAsync(connection, TestCompanyIds.CompanyC, 0, 0, 0);
    }

    private static async Task AssertTopologyAsync(
        SqlConnection connection,
        Guid companyId,
        int expectedSites,
        int expectedLocations,
        int expectedWorkCenters)
    {
        Assert.Equal(
            expectedSites,
            await ScalarAsync<int>(
                connection,
                "SELECT COUNT(*) FROM [organization].[Sites] WHERE [CompanyId] = @CompanyId;",
                new SqlParameter("@CompanyId", companyId)));
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

    private static async Task AssertFiscalConstraintsAsync(SqlConnection connection)
    {
        var constraints = await ReadStringsAsync(
            connection,
            """
            SELECT [name]
            FROM [sys].[check_constraints]
            WHERE [parent_object_id] = OBJECT_ID(N'[organization].[Companies]')
              AND [name] IN (
                  N'CK_Companies_Placeholder_InvoiceDisabled',
                  N'CK_Companies_Vkn_Format')
            ORDER BY [name];
            """);
        Assert.Equal(
            [
                "CK_Companies_Placeholder_InvoiceDisabled",
                "CK_Companies_Vkn_Format",
            ],
            constraints);

        var invalidVkn = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(
            connection,
            "UPDATE [organization].[Companies] SET [Vkn] = N'410004658X' WHERE [Id] = @Id;",
            new SqlParameter("@Id", TestCompanyIds.CompanyA)));
        Assert.Equal(547, invalidVkn.Number);

        var placeholderInvoice = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(
            connection,
            "UPDATE [organization].[Companies] SET [InvoiceCapable] = 1 WHERE [Id] = @Id;",
            new SqlParameter("@Id", TestCompanyIds.CompanyC)));
        Assert.Equal(547, placeholderInvoice.Number);
    }

    private static async Task AssertDirectoryDoesNotExposeFiscalDataAsync(
        SqlServerRelationalApiFactory factory)
    {
        using var client = factory.CreateClientFor(
            TestUsers.GroupAdmin,
            TestCompanyIds.CompanyA);
        using var response = await client.GetAsync("/api/v1/organization/companies");
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.DoesNotContain("\"vkn\"", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"invoiceCapable\"", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(GdcVkn, payload, StringComparison.Ordinal);
        Assert.DoesNotContain(GspVkn, payload, StringComparison.Ordinal);
        Assert.DoesNotContain(GinPlaceholderVkn, payload, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(payload);
        var companies = document.RootElement.EnumerateArray().ToArray();
        Assert.Equal(["GDC", "GIN", "GSP"], companies.Select(company =>
            company.GetProperty("code").GetString()!).ToArray());
        foreach (var company in companies)
        {
            Assert.Equal(
                ["id", "code", "legalName", "isPlaceholder"],
                company.EnumerateObject().Select(property => property.Name).ToArray());
        }
    }

    private static async Task AssertLegacyForeignKeysAsync(SqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT s.[CompanyId], l.[CompanyId], l.[SiteId],
                   w.[CompanyId], w.[LocationId]
            FROM [organization].[Sites] AS s
            INNER JOIN [organization].[Locations] AS l
                ON l.[CompanyId] = s.[CompanyId] AND l.[SiteId] = s.[Id]
            INNER JOIN [organization].[WorkCenters] AS w
                ON w.[CompanyId] = l.[CompanyId] AND w.[LocationId] = l.[Id]
            WHERE s.[Id] = @SiteId
              AND l.[Id] = @LocationId
              AND w.[Id] = @WorkCenterId;
            """;
        command.Parameters.AddRange(
        [
            new SqlParameter("@SiteId", LegacySiteId),
            new SqlParameter("@LocationId", LegacyLocationId),
            new SqlParameter("@WorkCenterId", LegacyWorkCenterId),
        ]);
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal(TestCompanyIds.CompanyA, reader.GetGuid(0));
        Assert.Equal(TestCompanyIds.CompanyA, reader.GetGuid(1));
        Assert.Equal(LegacySiteId, reader.GetGuid(2));
        Assert.Equal(TestCompanyIds.CompanyA, reader.GetGuid(3));
        Assert.Equal(LegacyLocationId, reader.GetGuid(4));
        Assert.False(await reader.ReadAsync());
    }

    private static void AssertApprovedCompanyRows(IReadOnlyList<CompanyFiscalRow> companies)
    {
        Assert.Collection(
            companies,
            company => AssertCompany(
                company,
                TestCompanyIds.CompanyA,
                "GDC",
                GdcLegalName,
                GdcVkn,
                isPlaceholder: false,
                invoiceCapable: true),
            company => AssertCompany(
                company,
                TestCompanyIds.CompanyC,
                "GIN",
                GinLegalName,
                GinPlaceholderVkn,
                isPlaceholder: true,
                invoiceCapable: false),
            company => AssertCompany(
                company,
                TestCompanyIds.CompanyB,
                "GSP",
                GspLegalName,
                GspVkn,
                isPlaceholder: false,
                invoiceCapable: true));
    }

    private static void AssertCompany(
        CompanyFiscalRow company,
        Guid id,
        string code,
        string legalName,
        string vkn,
        bool isPlaceholder,
        bool invoiceCapable)
    {
        Assert.Equal(id, company.Id);
        Assert.Equal(code, company.Code);
        Assert.Equal(legalName, company.LegalName);
        Assert.Equal(vkn, company.Vkn);
        Assert.Equal(isPlaceholder, company.IsPlaceholder);
        Assert.Equal(invoiceCapable, company.InvoiceCapable);
        Assert.True(company.IsActive);
    }

    private static async Task<List<CompanyFiscalRow>> ReadCompanyRowsAsync(
        SqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT [Id], [Code], [LegalName], [Vkn], [IsPlaceholder],
                   [InvoiceCapable], [IsActive], [Version]
            FROM [organization].[Companies]
            ORDER BY [Code];
            """;
        await using var reader = await command.ExecuteReaderAsync();
        var companies = new List<CompanyFiscalRow>();
        while (await reader.ReadAsync())
        {
            companies.Add(new CompanyFiscalRow(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetBoolean(4),
                reader.GetBoolean(5),
                reader.GetBoolean(6),
                reader.GetInt64(7)));
        }

        return companies;
    }

    private static async Task<List<string>> ReadStringsAsync(
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
        params SqlParameter[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.Parameters.AddRange(parameters);
        return Assert.IsType<T>(await command.ExecuteScalarAsync());
    }

    private static async Task ExecuteAsync(
        SqlConnection connection,
        string commandText,
        params SqlParameter[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
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

    private sealed record CompanyFiscalRow(
        Guid Id,
        string Code,
        string LegalName,
        string Vkn,
        bool IsPlaceholder,
        bool InvoiceCapable,
        bool IsActive,
        long Version);

    private sealed record LegacySiteTemplate(
        string Code,
        string Type,
        bool IsExternal,
        IReadOnlyList<string> LocationCodes);

    private sealed record LegacyWorkCenterTemplate(
        string Code,
        string LocationCode,
        string Kind);

    private sealed record TopologyRow(
        Guid CompanyId,
        bool IsActive,
        long Version,
        Guid? ParentId);
}
