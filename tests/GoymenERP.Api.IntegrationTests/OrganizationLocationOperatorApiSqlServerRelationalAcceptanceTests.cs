using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Api.Contracts;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Organization.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// The accepted host path for an external location operator: the HTTP command keeps Organization's
/// existing location-write authority while the host adapter asks Counterparty's exact, company-scoped
/// supplier source. The relation is persisted once and both Organization reads return the same PartyId.
/// Synthetic fixture rows live only in this isolated database; this is not a production seed or grant.
/// </summary>
public sealed class OrganizationLocationOperatorApiSqlServerRelationalAcceptanceTests
{
    private const string Root = "/api/v1/organization";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Guid EligibleParty = Guid.Parse("74000000-0000-4000-8000-000000000001");
    private static readonly Guid InactiveParty = Guid.Parse("74000000-0000-4000-8000-000000000002");

    [SqlServerFact]
    public async Task Host_adapter_assigns_exact_supplier_and_reads_the_relation_without_duplicate_effects()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
        var databaseName = $"GoymenERP_Organization_LocationOperatorApi_{Guid.NewGuid():N}";
        await using var factory = new SqlServerRelationalApiFactory(
            baseConnectionString,
            applyTestCompanyBTopologyTemplates: false,
            databaseName: databaseName);

        try
        {
            using (var health = await factory.CreateClient().GetAsync("/health/live"))
            {
                health.EnsureSuccessStatusCode();
            }

            await GrantAsync(factory, TestUsers.NoAssignment, TestCompanyIds.CompanyA,
            [
                ErpPermissions.OrganizationLocationsRead,
                OrganizationPermissionManifestV1.SitesWrite,
                OrganizationPermissionManifestV1.LocationsWrite,
            ]);
            await SeedCounterpartiesAsync(factory.DatabaseConnectionString);

            using var editor = CreateClient(factory, TestUsers.NoAssignment, TestCompanyIds.CompanyA);
            var externalSite = await CreateAsync<OrganizationCreatedRecord>(
                editor,
                HttpMethod.Post,
                $"{Root}/sites",
                new CreateSite("SYN-HAR-9", "Sentetik Harici Depo", "ExternalWarehouse", "Operatör kabulü"));
            var externalLocation = await CreateAsync<OrganizationCreatedRecord>(
                editor,
                HttpMethod.Post,
                $"{Root}/locations",
                new CreateLocation(externalSite.Id, "SYN-HAR-9A", "Sentetik Harici Alan", "Operatör kabulü"));

            var key = Guid.NewGuid().ToString("N");
            var command = new SetLocationOperator(
                EligibleParty,
                externalLocation.Version,
                "Yetkili tedarikçi harici depo operatörü olarak atandı");
            using (var response = await SendAsync(
                       editor,
                       HttpMethod.Put,
                       $"{Root}/locations/{externalLocation.Id}/operator",
                       command,
                       key))
            {
                Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
                Assert.False(response.Headers.Contains("Idempotency-Replayed"));
                Assert.Empty(await response.Content.ReadAsByteArrayAsync());
            }

            using (var reopened = CreateClient(factory, TestUsers.NoAssignment, TestCompanyIds.CompanyA))
            {
                var detail = await reopened.GetFromJsonAsync<LocationSummary>(
                    $"{Root}/locations/{externalLocation.Id}", Json);
                Assert.NotNull(detail);
                Assert.Equal(EligibleParty, detail.OperatorPartyId);
                Assert.Equal(2, detail.Version);

                var page = await reopened.GetFromJsonAsync<PageResult<LocationSummary>>(
                    $"{Root}/locations?page=1&pageSize=100", Json);
                var row = Assert.Single(page!.Items, item => item.Id == externalLocation.Id);
                Assert.Equal(EligibleParty, row.OperatorPartyId);
            }

            var effectsAfterFirst = await ReadEffectsAsync(factory, externalLocation.Id);
            using (var replay = await SendAsync(
                       editor,
                       HttpMethod.Put,
                       $"{Root}/locations/{externalLocation.Id}/operator",
                       command,
                       key))
            {
                Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
                Assert.True(replay.Headers.Contains("Idempotency-Replayed"));
            }
            Assert.Equal(effectsAfterFirst, await ReadEffectsAsync(factory, externalLocation.Id));

            using (var rejected = await SendAsync(
                       editor,
                       HttpMethod.Put,
                       $"{Root}/locations/{externalLocation.Id}/operator",
                       command with
                       {
                           OperatorPartyId = InactiveParty,
                           ExpectedVersion = 2,
                           Reason = "Pasif cari reddedilir",
                       },
                       Guid.NewGuid().ToString("N")))
            {
                Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
                using var body = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
                Assert.Equal(
                    OrganizationProblemCodesV1.LocationOperatorIneligible,
                    body.RootElement.GetProperty("code").GetString());
            }

            using (var missing = await SendAsync(
                       editor,
                       HttpMethod.Put,
                       $"{Root}/locations/{Guid.NewGuid()}/operator",
                       command,
                       Guid.NewGuid().ToString("N")))
            {
                Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
                using var body = JsonDocument.Parse(await missing.Content.ReadAsStringAsync());
                Assert.False(body.RootElement.TryGetProperty("code", out _));
            }

            using var noGrant = CreateClient(factory, TestUsers.Unprovisioned, TestCompanyIds.CompanyA);
            using var forbidden = await SendAsync(
                noGrant,
                HttpMethod.Put,
                $"{Root}/locations/{externalLocation.Id}/operator",
                command with { ExpectedVersion = 2 },
                Guid.NewGuid().ToString("N"));
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            Assert.Equal(effectsAfterFirst with { CommandExecutions = effectsAfterFirst.CommandExecutions + 2 },
                await ReadEffectsAsync(factory, externalLocation.Id));
        }
        finally
        {
            await DropDatabaseAsync(baseConnectionString, databaseName);
        }
    }

    private static HttpClient CreateClient(
        SqlServerRelationalApiFactory factory,
        string user,
        Guid company)
    {
        var client = factory.CreateClientFor(user, company);
        client.DefaultRequestHeaders.Remove(CounterpartyClientContract.HeaderName);
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        return client;
    }

    private static async Task<T> CreateAsync<T>(
        HttpClient client,
        HttpMethod method,
        string path,
        object body)
    {
        using var response = await SendAsync(
            client,
            method,
            path,
            body,
            Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return Assert.IsType<T>(await response.Content.ReadFromJsonAsync<T>(Json));
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        object body,
        string idempotencyKey)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body, body.GetType(), options: Json),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        request.Headers.Add("X-Correlation-Id", Guid.NewGuid().ToString("N"));
        return await client.SendAsync(request);
    }

    private static async Task SeedCounterpartiesAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO [counterparty].[Parties]
                ([Id], [Code], [Kind], [LegalName], [TradeName], [IsActive], [Version], [CreatedAtUtc], [UpdatedAtUtc])
            VALUES
                (@Eligible, N'CAR-993001', N'LegalEntity', N'Uygun Sentetik Tedarikçi', NULL, 1, 1, @Now, @Now),
                (@Inactive, N'CAR-993002', N'LegalEntity', N'Pasif Sentetik Tedarikçi', NULL, 0, 1, @Now, @Now);

            INSERT INTO [counterparty].[CompanyPartyAccounts]
                ([Id], [CompanyId], [PartyId], [Roles], [ExternalIntegrationCode], [IsActive], [Version], [CreatedAtUtc], [UpdatedAtUtc])
            VALUES
                (NEWID(), @Company, @Eligible, @Supplier, NULL, 1, 1, @Now, @Now),
                (NEWID(), @Company, @Inactive, @Supplier, NULL, 1, 1, @Now, @Now);
            """;
        command.Parameters.AddWithValue("@Eligible", EligibleParty);
        command.Parameters.AddWithValue("@Inactive", InactiveParty);
        command.Parameters.AddWithValue("@Company", TestCompanyIds.CompanyA);
        command.Parameters.AddWithValue("@Supplier", (int)PartyRole.Supplier);
        command.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow);
        Assert.Equal(4, await command.ExecuteNonQueryAsync());
    }

    private static async Task<Effects> ReadEffectsAsync(
        SqlServerRelationalApiFactory factory,
        Guid locationId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<GoymenERP.BuildingBlocks.Multitenancy.ICompanyContext>()
            .Set(TestCompanyIds.CompanyA);
        var db = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
        var location = await db.Locations.SingleAsync(row => row.Id == locationId);
        return new Effects(
            location.Version,
            location.OperatorPartyId,
            await db.CommandExecutions.CountAsync(),
            await db.Set<GoymenERP.Modules.Audit.Domain.Events.AuditEvent>().CountAsync());
    }

    private static async Task GrantAsync(
        SqlServerRelationalApiFactory factory,
        string developmentUser,
        Guid company,
        IReadOnlyCollection<string> permissions)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var link = await db.ExternalIdentities.SingleOrDefaultAsync(
            row => row.Issuer == BootstrapIdentityOptions.Issuer && row.Subject == developmentUser);
        Guid actor;
        if (link is null)
        {
            actor = Guid.NewGuid();
            db.Users.Add(new UserAccount(actor, "Synthetic location operator editor", null));
            db.ExternalIdentities.Add(new ExternalIdentityLink(
                Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer, developmentUser));
        }
        else
        {
            actor = link.UserId;
        }

        var roleId = Guid.NewGuid();
        var role = new RoleDefinition(roleId, $"ORG-OPERATOR-{roleId:N}", "Synthetic operator role", true);
        foreach (var permission in permissions)
        {
            role.AddPermission(permission);
        }
        var assignment = new RoleAssignment(
            Guid.NewGuid(), actor, roleId, company, DateTimeOffset.UtcNow.AddMinutes(-1), null, null);
        assignment.AddScope(AccessScopeType.Company, company);
        db.Roles.Add(role);
        db.RoleAssignments.Add(assignment);
        await db.SaveChangesAsync();
    }

    private static async Task DropDatabaseAsync(string baseConnectionString, string databaseName)
    {
        var builder = new SqlConnectionStringBuilder(baseConnectionString) { InitialCatalog = "master" };
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

    private sealed record Effects(
        long LocationVersion,
        Guid? OperatorPartyId,
        int CommandExecutions,
        int AuditEvents);
}
