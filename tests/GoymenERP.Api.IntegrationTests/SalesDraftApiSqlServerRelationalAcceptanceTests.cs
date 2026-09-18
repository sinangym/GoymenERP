using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// The Sales API slice against a REAL SQL Server through the REAL host: authentication, company
/// context, the three draft routes, the draft service measuring fresh grants through the central
/// authority, and the host's real owners (the Counterparty party read, the approved Settings
/// provider, the Product eligibility runtime as composed). Callers are the factory's development
/// users; their Sales grants are synthetic roles on the registered names, created in this test's own
/// database and never seeded. This is API evidence, not a user outcome: it measures HONESTLY what a
/// create answers today with the real owners — the first owner that cannot answer refuses, and
/// nothing is stored. Every identity is SENTETIK / NOT_ENV.
/// </summary>
public sealed class SalesDraftApiSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly string[] DraftGrants = [SalesPermissionManifestV1.DraftsCreate, SalesPermissionManifestV1.DraftsRead];

    [SqlServerFact]
    public async Task The_draft_routes_answer_only_with_fresh_sales_grants_and_only_for_the_context_company()
    {
        await using var run = await Run.StartAsync();

        // Company A's read-only user holds no Sales permission (none is seeded): every route refuses, code-less.
        using (var viewer = run.Factory.CreateClientFor(TestUsers.CompanyAViewer, CompanyA))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync(SalesEndpoints.DraftsPath)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync($"{SalesEndpoints.DraftsPath}/{Guid.NewGuid()}")).StatusCode);
            using var save = new HttpRequestMessage(HttpMethod.Post, SalesEndpoints.DraftsPath) { Content = Body() };
            save.Headers.Add("Idempotency-Key", "SENTETIK-viewer-1");
            Assert.Equal(HttpStatusCode.Forbidden, (await viewer.SendAsync(save)).StatusCode);
        }

        var reader = await run.GrantAsync(TestUsers.CompanyA, CompanyA, DraftGrants);
        using (var client = run.Factory.CreateClientFor(reader, CompanyA))
        {
            var list = await client.GetAsync(SalesEndpoints.DraftsPath);
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            Assert.True(list.Headers.CacheControl is { Private: true, NoStore: true });
            var page = await Result(list);
            Assert.Empty(page.GetProperty("items").EnumerateArray());
            Assert.Equal(JsonValueKind.Null, page.GetProperty("nextCursor").ValueKind);

            // A malformed cursor and an out-of-range page size are bad requests with their codes.
            var cursor = await client.GetAsync($"{SalesEndpoints.DraftsPath}?cursor=not-a-cursor");
            Assert.Equal(HttpStatusCode.BadRequest, cursor.StatusCode);
            Assert.Equal(SalesDraftProblemCodesV1.InvalidCursor, (await Result(cursor)).GetProperty("code").GetString());
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"{SalesEndpoints.DraftsPath}?pageSize=101")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{SalesEndpoints.DraftsPath}/{Guid.NewGuid()}")).StatusCode);
        }

        // The same grants in company B are nothing for company A: B's reader naming A is refused before any read.
        var readerOfB = await run.GrantAsync(TestUsers.CompanyB, CompanyB, DraftGrants);
        using (var foreign = run.Factory.CreateClientFor(readerOfB, CompanyA))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await foreign.GetAsync(SalesEndpoints.DraftsPath)).StatusCode);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftAttempts"));
    }

    [SqlServerFact]
    public async Task A_create_reaches_its_real_owners_in_order_and_answers_the_first_refusal_without_storing_anything()
    {
        await using var run = await Run.StartAsync();
        var saver = await run.GrantAsync(TestUsers.CompanyA, CompanyA, DraftGrants);
        using var client = run.Factory.CreateClientFor(saver, CompanyA);

        // Without an Idempotency-Key the save is not attempted; a non-canonical or repeated key neither.
        using (var noKey = new HttpRequestMessage(HttpMethod.Post, SalesEndpoints.DraftsPath) { Content = Body() })
        {
            var response = await client.SendAsync(noKey);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(SalesDraftProblemCodesV1.CommandMetadataInvalid, (await Result(response)).GetProperty("code").GetString());
        }

        using (var padded = new HttpRequestMessage(HttpMethod.Post, SalesEndpoints.DraftsPath) { Content = Body() })
        {
            padded.Headers.Add("Idempotency-Key", " SENTETIK-1 ");
            Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(padded)).StatusCode);
        }

        // A body outside the strict #257 shape (an unknown member) is refused before the service.
        using (var extra = new HttpRequestMessage(HttpMethod.Post, SalesEndpoints.DraftsPath)
        {
            Content = new StringContent("{\"kind\":\"Order\",\"companyId\":\"x\"}", Encoding.UTF8, "application/json"),
        })
        {
            extra.Headers.Add("Idempotency-Key", "SENTETIK-shape");
            var response = await client.SendAsync(extra);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("validation_failed", (await Result(response)).GetProperty("code").GetString());
        }

        // The card's fail-closed deltas answer before any owner: a commercial field, an owner reference.
        using (var priced = new HttpRequestMessage(HttpMethod.Post, SalesEndpoints.DraftsPath) { Content = Body(unitPrice: "12.50") })
        {
            priced.Headers.Add("Idempotency-Key", "SENTETIK-priced");
            var response = await client.SendAsync(priced);
            Assert.Equal((HttpStatusCode)422, response.StatusCode);
            var problem = await Result(response);
            Assert.Equal(SalesDraftProblemCodesV1.CommercialNotAvailable, problem.GetProperty("code").GetString());
            Assert.Equal("lines[0].unitPrice", problem.GetProperty("errors")[0].GetProperty("field").GetString());
        }

        // With a well-formed request the first REAL owner answers: the Counterparty party read knows no
        // such party in this company, so the customer is refused (409 account_state) — today's honest
        // behaviour with the real providers; the money profile and the Product runtime are never reached.
        using (var keyed = new HttpRequestMessage(HttpMethod.Post, SalesEndpoints.DraftsPath) { Content = Body() })
        {
            keyed.Headers.Add("Idempotency-Key", "SENTETIK-save-1");
            var response = await client.SendAsync(keyed);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var problem = await Result(response);
            Assert.Equal(SalesDraftProblemCodesV1.AccountState, problem.GetProperty("code").GetString());
            Assert.Contains(problem.GetProperty("errors").EnumerateArray(), error => error.GetProperty("field").GetString() == "partyId");
            Assert.False(response.Headers.Contains("Idempotency-Replayed"));
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.Drafts"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.DraftAttempts"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType = N'Sales.OrderDraft'"));
    }

    [SqlServerFact]
    public async Task A_stored_draft_is_read_by_its_company_only_and_its_cursor_binds_the_company()
    {
        await using var run = await Run.StartAsync();
        var draftOfA = await run.StoredDraftAsync(CompanyA);
        var readerOfA = await run.GrantAsync(TestUsers.CompanyA, CompanyA, DraftGrants);
        using (var clientOfA = run.Factory.CreateClientFor(readerOfA, CompanyA))
        {
            var detail = await clientOfA.GetAsync($"{SalesEndpoints.DraftsPath}/{draftOfA}");
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
            var body = await Result(detail);
            Assert.Equal(draftOfA, body.GetProperty("draft").GetProperty("id").GetGuid());
            Assert.Equal(CompanyA, body.GetProperty("draft").GetProperty("companyId").GetGuid());
            Assert.Equal("Rev.0", body.GetProperty("draft").GetProperty("revision").GetString());
            Assert.Single(body.GetProperty("lines").EnumerateArray());

            var list = await Result(await clientOfA.GetAsync(SalesEndpoints.DraftsPath));
            Assert.Equal(draftOfA, Assert.Single(list.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        }

        var readerOfB = await run.GrantAsync(TestUsers.CompanyB, CompanyB, DraftGrants);
        using (var clientOfB = run.Factory.CreateClientFor(readerOfB, CompanyB))
        {
            Assert.Equal(HttpStatusCode.NotFound, (await clientOfB.GetAsync($"{SalesEndpoints.DraftsPath}/{draftOfA}")).StatusCode);
            Assert.Empty((await Result(await clientOfB.GetAsync(SalesEndpoints.DraftsPath))).GetProperty("items").EnumerateArray());
            var cursorOfA = SalesStore.ListCursor.Encode(CompanyA, DateTimeOffset.UtcNow, Guid.NewGuid());
            var foreign = await clientOfB.GetAsync($"{SalesEndpoints.DraftsPath}?cursor={Uri.EscapeDataString(cursorOfA)}");
            Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);
            Assert.Equal(SalesDraftProblemCodesV1.InvalidCursor, (await Result(foreign)).GetProperty("code").GetString());
        }
    }

    private static StringContent Body(string? unitPrice = null)
    {
        var line = new SalesOrderDraftLineRequest(Guid.NewGuid(), Guid.NewGuid(), "1", Guid.NewGuid(), "1.25", unitPrice,
            unitPrice is null ? null : Guid.NewGuid());
        var request = new CreateSalesOrderDraftRequest("Order", Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 9, 18), Guid.NewGuid(), [line]);
        return new StringContent(JsonSerializer.Serialize(request, SalesDraftJson.Options), Encoding.UTF8, "application/json");
    }

    private static async Task<JsonElement> Result(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private sealed class Run : IAsyncDisposable
    {
        private readonly string baseConnectionString;

        private Run(string baseConnectionString, SqlServerRelationalApiFactory factory)
        {
            this.baseConnectionString = baseConnectionString;
            Factory = factory;
        }

        public SqlServerRelationalApiFactory Factory { get; }

        public static async Task<Run> StartAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(
                SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(baseConnectionString);
            var run = new Run(baseConnectionString, factory);
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            // Idempotent if the host already applied Sales's migrations.
            var companyContext = new CompanyContext();
            companyContext.Set(CompanyA);
            await using var context = new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>()
                .UseSqlServer(factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
                .Options, companyContext);
            await context.Database.MigrateAsync();
            return run;
        }

        /// <summary>A SENTETIK stored draft of the company with one line, written as rows (its owners are synthetic identities).</summary>
        public async Task<Guid> StoredDraftAsync(Guid company)
        {
            var id = Guid.NewGuid();
            var unit = Guid.NewGuid();
            var profile = Guid.NewGuid();
            await ExecuteAsync(
                "INSERT INTO sales.Drafts (Id, CompanyId, DocumentNo, Kind, State, Revision, Version, PartyId, CompanyAccountId, CurrencyId, "
                + "BusinessDate, MoneyOperation, MoneyProfileId, MoneyProfileVersion, MoneyEffectiveOn, MoneyIsActive, MoneyEffectiveFrom, "
                + "MoneyScale, CreatedAtUtc, UpdatedAtUtc, CreatedBy, CorrelationId) VALUES (@id, @c, N'SO-2026-000001', 1, 1, 0, 2, NEWID(), "
                + "NEWID(), NEWID(), '2026-09-18', N'sales.order-draft.v1', NEWID(), N'3', '2026-09-18', 1, '2026-09-01', 2, "
                + "SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'SENTETIK-actor', N'SENTETIK-correlation')",
                ("@id", id), ("@c", company));
            await ExecuteAsync(
                "INSERT INTO sales.DraftLines (CompanyId, DraftId, [LineNo], LineId, ProductId, ProductVersion, ProductCode, ProductName, "
                + "ProductEffectiveOn, StockUnitCompanyId, StockUnitId, StockUnitProfileId, StockUnitProfileVersion, SalesUnitCompanyId, SalesUnitId, "
                + "SalesUnitProfileId, SalesUnitProfileVersion, OrderUnitBindingCompanyId, OrderUnitBindingUnitId, OrderUnitBindingProfileId, "
                + "OrderUnitBindingProfileVersion, OrderUnitCompanyId, OrderUnitId, OrderUnitCode, OrderUnitDimensionCode, OrderUnitProfileId, "
                + "OrderUnitProfileVersion, OrderUnitIsKilogram, OrderUnitProfileScale, OrderUnitScale, OrderUnitIsActive, OrderUnitEffectiveFrom, "
                + "Quantity, QuantityText) VALUES (@c, @id, 1, NEWID(), NEWID(), N'7', N'GDC-001', N'Sentetik bant', '2026-09-18', "
                + "@c, @u, @p, N'4', @c, @u, @p, N'4', @c, @u, @p, N'4', @c, @u, N'kg', N'weight', @p, N'4', 1, 3, 3, 1, '2026-09-01', 1.250, N'1.25')",
                ("@id", id), ("@c", company), ("@u", unit), ("@p", profile));
            return id;
        }

        /// <summary>A synthetic role with exactly these permissions in one company, assigned to a configured development user.</summary>
        public async Task<string> GrantAsync(string developmentUser, Guid company, params string[] permissions)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var link = await dbContext.ExternalIdentities.SingleOrDefaultAsync(
                x => x.Issuer == BootstrapIdentityOptions.Issuer && x.Subject == developmentUser);
            Guid actor;
            if (link is null)
            {
                actor = Guid.NewGuid();
                dbContext.Users.Add(new UserAccount(actor, "Synthetic Sales user", null));
                dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer, developmentUser));
            }
            else
            {
                actor = link.UserId;
            }

            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(roleId, $"SALES-API-{roleId:N}", "Synthetic Sales test role", true);
            foreach (var permission in permissions)
            {
                role.AddPermission(permission);
            }

            var assignment = new RoleAssignment(Guid.NewGuid(), actor, roleId, company, DateTimeOffset.UtcNow.AddMinutes(-1), null,
                approvedByUserId: null);
            assignment.AddScope(AccessScopeType.Company, company);
            dbContext.Roles.Add(role);
            dbContext.RoleAssignments.Add(assignment);
            await dbContext.SaveChangesAsync();
            return developmentUser;
        }

        public async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            return (T)(await command.ExecuteScalarAsync())!;
        }

        public async ValueTask DisposeAsync()
        {
            await Factory.DisposeAsync();
            SqlConnection.ClearAllPools();
            await using var connection = new SqlConnection(baseConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                IF DB_ID(N'{Factory.DatabaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{Factory.DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{Factory.DatabaseName}];
                END;
                """;
            await command.ExecuteNonQueryAsync();
        }

        private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            await command.ExecuteNonQueryAsync();
        }
    }
}
