using System.Net;
using System.Net.Http.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// F-023 (F023-5a) against the real provider and a real database: the migrated audit table carries the
/// (CompanyId, Outcome, OccurredAtUtc DESC, Id DESC) index, and the outcome-filtered HTTP read returns only that
/// outcome of the selected company on the same keyset. Company B reading refused attempts sees none of company A's
/// (acceptance 4). SENTETIK / NOT_ENV.
/// </summary>
public sealed class AuditOutcomeFilterSqlServerRelationalAcceptanceTests
{
    private const string OutcomeIndexName = "IX_AuditEvents_CompanyId_Outcome_OccurredAtUtc_Id";

    [SqlServerFact]
    public async Task The_outcome_filter_reads_only_that_outcome_of_the_selected_company_over_the_migrated_index()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var factory = new SqlServerRelationalApiFactory(baseConnectionString);

        try
        {
            using var companyAClient = factory.CreateClientFor(TestUsers.GroupAdmin, TestCompanyIds.CompanyA);
            using var companyBClient = factory.CreateClientFor(TestUsers.GroupAdmin, TestCompanyIds.CompanyB);
            var correlationId = $"f023-5a-{Guid.NewGuid():N}";
            await SeedSucceededEventAsync(factory, TestCompanyIds.CompanyA, correlationId);
            await RecordRefusedAttemptAsync(factory, TestCompanyIds.CompanyA, correlationId);
            await RecordRefusedAttemptAsync(factory, TestCompanyIds.CompanyA, correlationId);
            await RecordRefusedAttemptAsync(factory, TestCompanyIds.CompanyB, correlationId);

            Assert.Equal(
                "CompanyId:0,Outcome:0,OccurredAtUtc:1,Id:1",
                await ReadIndexKeyColumnsAsync(factory.DatabaseConnectionString));

            var filter = $"correlationId={correlationId}";
            var refused = await ReadPageAsync(companyAClient, $"/api/v1/audit/events?{filter}&outcome=Refused");
            Assert.Equal(2, refused.Items.Count);
            Assert.All(refused.Items, item =>
            {
                Assert.Equal(AuditOutcomes.Refused, item.Outcome);
                Assert.Equal(TestCompanyIds.CompanyA, item.CompanyId);
            });
            var succeeded = Assert.Single(
                (await ReadPageAsync(companyAClient, $"/api/v1/audit/events?{filter}&outcome=Succeeded")).Items);
            Assert.Equal(AuditOutcomes.Succeeded, succeeded.Outcome);
            Assert.Equal(TestCompanyIds.CompanyA, succeeded.CompanyId);

            var firstPage = await ReadPageAsync(
                companyAClient,
                $"/api/v1/audit/events?{filter}&outcome=Refused&pageSize=1");
            var firstItem = Assert.Single(firstPage.Items);
            Assert.NotNull(firstPage.NextCursor);
            var secondPage = await ReadPageAsync(
                companyAClient,
                $"/api/v1/audit/events?{filter}&outcome=Refused&pageSize=1&cursor={Uri.EscapeDataString(firstPage.NextCursor)}");
            var secondItem = Assert.Single(secondPage.Items);
            Assert.Null(secondPage.NextCursor);
            Assert.Equal(
                refused.Items.Select(item => item.Id).ToArray(),
                new[] { firstItem.Id, secondItem.Id });

            var companyBItem = Assert.Single(
                (await ReadPageAsync(companyBClient, $"/api/v1/audit/events?{filter}&outcome=Refused")).Items);
            Assert.Equal(TestCompanyIds.CompanyB, companyBItem.CompanyId);
            Assert.DoesNotContain(companyBItem.Id, refused.Items.Select(item => item.Id));

            using var invalid = await companyAClient.GetAsync($"/api/v1/audit/events?{filter}&outcome=refused");
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        finally
        {
            await factory.DisposeAsync();
            await DropDatabaseAsync(baseConnectionString, factory.DatabaseName);
        }
    }

    private static async Task<AuditEventPage> ReadPageAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Assert.IsType<AuditEventPage>(await response.Content.ReadFromJsonAsync<AuditEventPage>());
    }

    private static async Task<string?> ReadIndexKeyColumnsAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT STRING_AGG(CONCAT(c.[name], ':', CONVERT(int, ic.[is_descending_key])), ',')
                WITHIN GROUP (ORDER BY ic.[key_ordinal])
            FROM sys.indexes AS i
            JOIN sys.index_columns AS ic ON ic.[object_id] = i.[object_id] AND ic.[index_id] = i.[index_id]
            JOIN sys.columns AS c ON c.[object_id] = ic.[object_id] AND c.[column_id] = ic.[column_id]
            WHERE i.[object_id] = OBJECT_ID(N'[audit].[AuditEvents]')
              AND i.[name] = @name
              AND ic.[is_included_column] = 0;
            """;
        command.Parameters.AddWithValue("@name", OutcomeIndexName);
        return await command.ExecuteScalarAsync() as string;
    }

    private static async Task SeedSucceededEventAsync(
        SqlServerRelationalApiFactory factory,
        Guid company,
        string correlationId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(company);
        scope.ServiceProvider.GetRequiredService<IAuditEventCollector>().Enqueue(new AuditEventDraft(
            company,
            "platform.f023-5a-outcome-filter.probe.v1",
            "PlatformF0235aOutcomeFilterProbe",
            Guid.NewGuid().ToString("N"),
            "Probe",
            null,
            null,
            AuditActorTypes.System,
            "f023-5a-outcome-filter-test",
            ErpPermissions.IdentityAuditRead,
            ["SYSTEM_ADMINISTRATOR"],
            "{}",
            "{}",
            "F023-5a gerçek SQL kabul testi",
            null,
            AuditAiInvolvement.None,
            null,
            SourceChannels.System,
            null,
            null,
            correlationId,
            null,
            $"idem-{Guid.NewGuid():N}"));
        await scope.ServiceProvider.GetRequiredService<OrganizationDbContext>().SaveChangesAsync();
    }

    private static async Task RecordRefusedAttemptAsync(
        SqlServerRelationalApiFactory factory,
        Guid company,
        string correlationId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContext>().Set(company);
        var result = await scope.ServiceProvider.GetRequiredService<IRefusedAttemptRecorderV1>()
            .RecordStandaloneAsync(new RefusedAttemptV1
            {
                CompanyId = company,
                CommandName = "purchasing.purchase-request.approve.v1",
                Operation = "Approve",
                TargetType = "Purchasing.PurchaseRequest",
                TargetId = Guid.NewGuid().ToString("D"),
                ExpectedVersion = 3,
                ActorType = AuditActorTypes.User,
                ActorId = "f023-5a-outcome-filter-test",
                PermissionCode = "purchasing.requests.approve",
                RoleCodes = ["role-a"],
                HttpStatus = StatusCodes.Status409Conflict,
                ProblemCode = "approval_state_changed",
                RefusalClass = RefusedAttemptClassesV1.BusinessRule,
                CorrelationId = correlationId,
                SourceChannel = SourceChannels.Web,
                IdempotencyKey = $"idem-{Guid.NewGuid():N}",
            });
        Assert.True(result.Recorded, result.FailureKind);
    }

    private static async Task DropDatabaseAsync(string baseConnectionString, string databaseName)
    {
        Assert.Matches("^goymen_erp_ci_[0-9a-f]{32}$", databaseName);
        SqlConnection.ClearAllPools();
        await using var master = new SqlConnection(new SqlConnectionStringBuilder(baseConnectionString)
        {
            InitialCatalog = "master",
        }.ConnectionString);
        await master.OpenAsync();
        await using var drop = master.CreateCommand();
        drop.CommandText = $"""
            IF DB_ID(@name) IS NOT NULL
            BEGIN
                ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{databaseName}];
            END;
            """;
        drop.Parameters.AddWithValue("@name", databaseName);
        await drop.ExecuteNonQueryAsync();
    }
}
