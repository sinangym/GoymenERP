using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Api.Security;
using GoymenERP.Modules.Organization.Application.Contracts;
using Microsoft.Data.SqlClient;

namespace GoymenERP.Api.IntegrationTests;

public sealed class CompanyDirectorySqlServerRelationalAcceptanceTests
{
    private const string CompanyACode = "TBD-GOY-01";
    private const string CompanyALegalName = "Göymen Grup Şirket 1 (Teyit Bekliyor)";
    private const string CompanyBCode = "TBD-GOY-02";
    private const string CompanyBLegalName = "Göymen Grup Şirket 2 (Teyit Bekliyor)";
    private const string ForgedCompanyCode = "FORGED-FOREIGN-CODE";
    private const string ForgedCompanyLegalName = "FORGED-FOREIGN-LEGAL-NAME";

    private static readonly Guid NoAssignmentUserId = Guid.Parse(
        "20000000-0000-0000-0000-000000000099");

    private static readonly Guid NoAssignmentIdentityId = Guid.Parse(
        "21000000-0000-0000-0000-000000000099");

    [SqlServerFact]
    public async Task Directory_EnforcesAssignmentIsolationActiveFilteringAndReadOnlyBehavior()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var factory = new SqlServerRelationalApiFactory(baseConnectionString);

        try
        {
            using var healthClient = factory.CreateClient();
            using var healthResponse = await healthClient.GetAsync("/health/live");
            Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);

            await using var connection = new SqlConnection(factory.DatabaseConnectionString);
            await connection.OpenAsync();
            var baselineAuditCount = await CountAsync(
                connection,
                "SELECT COUNT(*) FROM [audit].[AuditEvents];");
            var baselineIdempotencyCount = await CountAsync(
                connection,
                "SELECT COUNT(*) FROM [idempotency].[CommandExecutions];");

            await AssertIdentityGatesAsync(factory);
            await AssertAssignmentDerivedResultsAsync(factory);
            await InsertNoAssignmentUserAsync(connection);
            await AssertNoAssignmentResultAsync(factory);
            await AssertForgedMetadataDoesNotExpandAsync(factory);
            await AssertInactiveCompanyIsFilteredAsync(factory, connection);

            Assert.Equal(
                baselineAuditCount,
                await CountAsync(connection, "SELECT COUNT(*) FROM [audit].[AuditEvents];"));
            Assert.Equal(
                baselineIdempotencyCount,
                await CountAsync(
                    connection,
                    "SELECT COUNT(*) FROM [idempotency].[CommandExecutions];"));
        }
        finally
        {
            await factory.DisposeAsync();
            await DropDatabaseAsync(baseConnectionString, factory.DatabaseName);
        }
    }

    private static async Task AssertIdentityGatesAsync(SqlServerRelationalApiFactory factory)
    {
        using var anonymousClient = factory.CreateClient();
        using var anonymousResponse = await anonymousClient.GetAsync(
            "/api/v1/organization/companies");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var unprovisionedClient = factory.CreateClientFor(
            TestUsers.Unprovisioned,
            TestCompanyIds.CompanyA);
        using var unprovisionedResponse = await unprovisionedClient.GetAsync(
            "/api/v1/organization/companies");
        Assert.Equal(HttpStatusCode.Forbidden, unprovisionedResponse.StatusCode);
    }

    private static async Task AssertAssignmentDerivedResultsAsync(
        SqlServerRelationalApiFactory factory)
    {
        using var companyAClient = factory.CreateClientFor(
            TestUsers.CompanyA,
            TestCompanyIds.CompanyA);
        using var companyBClient = factory.CreateClientFor(
            TestUsers.CompanyB,
            TestCompanyIds.CompanyB);
        using var groupClient = factory.CreateClientFor(
            TestUsers.GroupAdmin,
            TestCompanyIds.CompanyA);

        using var companyAResponse = await companyAClient.GetAsync(
            "/api/v1/organization/companies");
        using var companyBResponse = await companyBClient.GetAsync(
            "/api/v1/organization/companies");
        using var groupResponse = await groupClient.GetAsync(
            "/api/v1/organization/companies");

        Assert.Equal(HttpStatusCode.OK, companyAResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, companyBResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, groupResponse.StatusCode);
        AssertPrivateNoStore(companyAResponse);
        AssertPrivateNoStore(companyBResponse);
        AssertPrivateNoStore(groupResponse);

        AssertCompanyA(Assert.Single(await ReadCompaniesAsync(companyAResponse)));
        AssertCompanyB(Assert.Single(await ReadCompaniesAsync(companyBResponse)));
        var groupCompanies = await ReadCompaniesAsync(groupResponse);
        Assert.Equal(
            new[] { TestCompanyIds.CompanyA, TestCompanyIds.CompanyB },
            groupCompanies.Select(x => x.Id));
        AssertCompanyA(groupCompanies[0]);
        AssertCompanyB(groupCompanies[1]);
        Assert.Equal(
            groupCompanies.Select(x => x.Code).OrderBy(x => x, StringComparer.Ordinal),
            groupCompanies.Select(x => x.Code));
    }

    private static async Task AssertNoAssignmentResultAsync(
        SqlServerRelationalApiFactory factory)
    {
        using var client = factory.CreateClientFor(TestUsers.NoAssignment, TestCompanyIds.CompanyA);
        using var response = await client.GetAsync("/api/v1/organization/companies");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertPrivateNoStore(response);
        Assert.Empty(await ReadCompaniesAsync(response));
    }

    private static async Task AssertForgedMetadataDoesNotExpandAsync(
        SqlServerRelationalApiFactory factory)
    {
        using var queryClient = factory.CreateClientFor(
            TestUsers.CompanyA,
            TestCompanyIds.CompanyA);
        queryClient.DefaultRequestHeaders.Add(
            "X-Allowed-Company-Ids",
            TestCompanyIds.CompanyB.ToString());
        queryClient.DefaultRequestHeaders.Add("X-Company-Code", ForgedCompanyCode);
        queryClient.DefaultRequestHeaders.Add("X-Company-Legal-Name", ForgedCompanyLegalName);
        using var queryResponse = await queryClient.GetAsync(
            $"/api/v1/organization/companies?companyId={TestCompanyIds.CompanyB}"
            + $"&companyCode={ForgedCompanyCode}&legalName={ForgedCompanyLegalName}");
        var queryBody = await queryResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, queryResponse.StatusCode);
        AssertCompanyA(Assert.Single(Assert.IsType<List<CompanySummary>>(
            await queryResponse.Content.ReadFromJsonAsync<List<CompanySummary>>())));
        AssertNoForeignOrForgedMetadata(queryBody);

        using var foreignContextClient = factory.CreateClientFor(
            TestUsers.CompanyA,
            TestCompanyIds.CompanyB);
        using var foreignContextResponse = await foreignContextClient.GetAsync(
            "/api/v1/organization/companies");
        var foreignContextBody = await foreignContextResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, foreignContextResponse.StatusCode);
        AssertCompanyA(Assert.Single(Assert.IsType<List<CompanySummary>>(
            await foreignContextResponse.Content.ReadFromJsonAsync<List<CompanySummary>>())));
        AssertNoForeignOrForgedMetadata(foreignContextBody);
    }

    private static async Task AssertInactiveCompanyIsFilteredAsync(
        SqlServerRelationalApiFactory factory,
        SqlConnection connection)
    {
        await ExecuteAsync(
            connection,
            "UPDATE [organization].[Companies] SET [IsActive] = 0 WHERE [Id] = @CompanyId;",
            new SqlParameter("@CompanyId", TestCompanyIds.CompanyB));

        using var groupClient = factory.CreateClientFor(
            TestUsers.GroupAdmin,
            TestCompanyIds.CompanyA);
        using var companyBClient = factory.CreateClientFor(
            TestUsers.CompanyB,
            TestCompanyIds.CompanyB);
        using var groupResponse = await groupClient.GetAsync(
            "/api/v1/organization/companies");
        using var companyBResponse = await companyBClient.GetAsync(
            "/api/v1/organization/companies");

        Assert.Equal(HttpStatusCode.OK, groupResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, companyBResponse.StatusCode);
        AssertPrivateNoStore(groupResponse);
        AssertPrivateNoStore(companyBResponse);
        AssertCompanyA(Assert.Single(await ReadCompaniesAsync(groupResponse)));
        Assert.Empty(await ReadCompaniesAsync(companyBResponse));
    }

    private static async Task InsertNoAssignmentUserAsync(SqlConnection connection)
    {
        await ExecuteAsync(
            connection,
            """
            INSERT INTO [identity].[Users]
                ([Id], [DisplayName], [Email], [IsActive], [CreatedAtUtc], [UpdatedAtUtc], [Version])
            VALUES
                (@UserId, N'Atamasız ERP SQL Test Kullanıcısı', NULL, 1,
                 SYSUTCDATETIME(), SYSUTCDATETIME(), 1);

            INSERT INTO [identity].[ExternalIdentities]
                ([Id], [UserId], [Issuer], [Subject], [CreatedAtUtc])
            VALUES
                (@IdentityId, @UserId, @Issuer, @Subject, SYSUTCDATETIME());
            """,
            new SqlParameter("@UserId", NoAssignmentUserId),
            new SqlParameter("@IdentityId", NoAssignmentIdentityId),
            new SqlParameter("@Issuer", BootstrapIdentityOptions.Issuer),
            new SqlParameter("@Subject", TestUsers.NoAssignment));
    }

    private static async Task<List<CompanySummary>> ReadCompaniesAsync(
        HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(payload);
        foreach (var company in document.RootElement.EnumerateArray())
        {
            Assert.Equal(
                ["id", "code", "legalName", "isPlaceholder"],
                company.EnumerateObject().Select(property => property.Name).ToArray());
        }

        return Assert.IsType<List<CompanySummary>>(
            JsonSerializer.Deserialize<List<CompanySummary>>(
                payload,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private static void AssertPrivateNoStore(HttpResponseMessage response)
    {
        Assert.NotNull(response.Headers.CacheControl);
        Assert.True(response.Headers.CacheControl.Private);
        Assert.True(response.Headers.CacheControl.NoStore);
    }

    private static void AssertCompanyA(CompanySummary company)
    {
        AssertCompany(
            company,
            TestCompanyIds.CompanyA,
            CompanyACode,
            CompanyALegalName,
            isPlaceholder: true);
    }

    private static void AssertCompanyB(CompanySummary company)
    {
        AssertCompany(
            company,
            TestCompanyIds.CompanyB,
            CompanyBCode,
            CompanyBLegalName,
            isPlaceholder: true);
    }

    private static void AssertCompany(
        CompanySummary company,
        Guid id,
        string code,
        string legalName,
        bool isPlaceholder)
    {
        Assert.Equal(id, company.Id);
        Assert.Equal(code, company.Code);
        Assert.Equal(legalName, company.LegalName);
        Assert.Equal(isPlaceholder, company.IsPlaceholder);
    }

    private static void AssertNoForeignOrForgedMetadata(string body)
    {
        Assert.DoesNotContain(
            TestCompanyIds.CompanyB.ToString(),
            body,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(CompanyBCode, body, StringComparison.Ordinal);
        Assert.DoesNotContain(CompanyBLegalName, body, StringComparison.Ordinal);
        Assert.DoesNotContain(ForgedCompanyCode, body, StringComparison.Ordinal);
        Assert.DoesNotContain(ForgedCompanyLegalName, body, StringComparison.Ordinal);
    }

    private static async Task<int> CountAsync(SqlConnection connection, string commandText)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        return Assert.IsType<int>(await command.ExecuteScalarAsync());
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
}
