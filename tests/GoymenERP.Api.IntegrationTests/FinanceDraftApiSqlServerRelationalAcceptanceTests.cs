using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// FIN-T04b against a REAL SQL Server through the REAL host: authentication, company context, the
/// Finance routes, the real <see cref="FinancePermissionPort"/> measuring fresh grants, the persisted
/// bank-account reader, the real Counterparty party port and Settings. Callers are the factory's
/// configured development users; their Finance grants are synthetic roles on the registered names,
/// created in this test's own database and never seeded. Bank accounts and drafts that need an
/// owner the API cannot create yet are written as SENTETIK rows. This is API evidence, not a user
/// outcome: no approved account exists outside the test, and T02-b decides the money answer.
/// </summary>
public sealed class FinanceDraftApiSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly Guid Currency = Guid.Parse("7e1d2c3b-4a59-4687-9a0b-1c2d3e4f5aa1");
    private static readonly string[] DraftGrants =
    [
        FinancePermissionNames.DraftSave, FinancePermissionNames.DraftRead, FinancePermissionNames.AmountRead,
        "finance.bank-account.read",
    ];

    [SqlServerFact]
    public async Task The_draft_routes_answer_only_with_fresh_finance_grants_and_only_for_the_context_company()
    {
        await using var run = await Run.StartAsync();
        var account = await run.ApprovedBankAccountAsync(CompanyA, "SENTETIK banka A");

        // Company A's read-only user holds no Finance permission (none is seeded): every route refuses.
        using (var viewer = run.Factory.CreateClientFor(TestUsers.CompanyAViewer, CompanyA))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync(FinanceEndpoints.BankAccountsPath)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync(FinanceEndpoints.ManualReceiptsPath)).StatusCode);
            var save = await viewer.PostAsync(FinanceEndpoints.DraftsPath, Body(account));
            // Denied is the platform problem, as on every route: 403 `forbidden`, not a CommandResult.
            Assert.Equal(HttpStatusCode.Forbidden, save.StatusCode);
            Assert.Equal("forbidden", (await Result(save)).GetProperty("code").GetString());
        }

        var reader = await run.GrantAsync(TestUsers.CompanyA, CompanyA, DraftGrants);
        using (var client = run.Factory.CreateClientFor(reader, CompanyA))
        {
            var banks = await client.GetAsync(FinanceEndpoints.BankAccountsPath);
            Assert.Equal(HttpStatusCode.OK, banks.StatusCode);
            Assert.True(banks.Headers.CacheControl is { Private: true, NoStore: true });
            var page = await Result(banks);
            var item = Assert.Single(page.GetProperty("items").EnumerateArray());
            Assert.Equal(account, item.GetProperty("id").GetGuid());
            Assert.True(item.GetProperty("isUsableForReceipt").GetBoolean());

            var drafts = await client.GetAsync(FinanceEndpoints.ManualReceiptsPath);
            Assert.Equal(HttpStatusCode.OK, drafts.StatusCode);
            Assert.Empty((await Result(drafts)).GetProperty("items").EnumerateArray());

            // A malformed cursor is a bad request, not an empty page.
            Assert.Equal(HttpStatusCode.BadRequest,
                (await client.GetAsync($"{FinanceEndpoints.ManualReceiptsPath}?cursor=not-a-cursor")).StatusCode);
        }

        // The same grants in company A are nothing for company B: B's reader never sees A's account.
        var readerOfB = await run.GrantAsync(TestUsers.CompanyB, CompanyB, DraftGrants);
        using (var clientOfB = run.Factory.CreateClientFor(readerOfB, CompanyB))
        {
            var banks = await clientOfB.GetAsync(FinanceEndpoints.BankAccountsPath);
            Assert.Equal(HttpStatusCode.OK, banks.StatusCode);
            Assert.Empty((await Result(banks)).GetProperty("items").EnumerateArray());
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.Commands"));
    }

    [SqlServerFact]
    public async Task A_save_reaches_its_real_owners_and_answers_their_refusal_without_storing_anything()
    {
        await using var run = await Run.StartAsync();
        var account = await run.ApprovedBankAccountAsync(CompanyA, "SENTETIK banka A");
        var saver = await run.GrantAsync(TestUsers.CompanyA, CompanyA, DraftGrants);
        using var client = run.Factory.CreateClientFor(saver, CompanyA);

        // Without an Idempotency-Key the save is not attempted.
        using (var noKey = new HttpRequestMessage(HttpMethod.Post, FinanceEndpoints.DraftsPath) { Content = Body(account) })
        {
            client.DefaultRequestHeaders.Remove("Idempotency-Key");
            Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(noKey)).StatusCode);
        }

        // With one: the real bank reader finds the approved account, the real Counterparty port knows
        // no such party account, so the draft is refused on its merits (422) and nothing is stored.
        using (var keyed = new HttpRequestMessage(HttpMethod.Post, FinanceEndpoints.DraftsPath) { Content = Body(account) })
        {
            keyed.Headers.Add("Idempotency-Key", "SENTETIK-save-1");
            var response = await client.SendAsync(keyed);
            Assert.Equal((HttpStatusCode)422, response.StatusCode);
            var result = await Result(response);
            Assert.Equal("ValidationFailed", result.GetProperty("outcome").GetString());
            Assert.Equal("party_account_ineligible",
                Assert.Single(result.GetProperty("issues").EnumerateArray()).GetProperty("code").GetString());
            Assert.False(response.Headers.Contains("Idempotency-Replayed"));
        }

        // A body outside the #258 contract (an unknown member) is refused before the service.
        using (var extra = new HttpRequestMessage(HttpMethod.Post, FinanceEndpoints.DraftsPath)
        {
            Content = new StringContent("{\"draftId\":null,\"expectedVersion\":null,\"companyId\":\"x\",\"preparation\":null}",
                Encoding.UTF8, "application/json"),
        })
        {
            extra.Headers.Add("Idempotency-Key", "SENTETIK-save-2");
            Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(extra)).StatusCode);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.ReceiptDrafts"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.Commands"));
    }

    [SqlServerFact]
    public async Task Another_companys_draft_is_not_found_and_its_cursor_reads_nothing()
    {
        await using var run = await Run.StartAsync();
        var draftOfA = await run.DraftAsync(CompanyA);
        var readerOfA = await run.GrantAsync(TestUsers.CompanyA, CompanyA, DraftGrants);
        using (var clientOfA = run.Factory.CreateClientFor(readerOfA, CompanyA))
        {
            var detail = await clientOfA.GetAsync($"{FinanceEndpoints.ManualReceiptsPath}/{draftOfA}");
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
            Assert.Equal(draftOfA, (await Result(detail)).GetProperty("source").GetProperty("id").GetGuid());
        }

        var readerOfB = await run.GrantAsync(TestUsers.CompanyB, CompanyB, DraftGrants);
        using (var clientOfB = run.Factory.CreateClientFor(readerOfB, CompanyB))
        {
            Assert.Equal(HttpStatusCode.NotFound,
                (await clientOfB.GetAsync($"{FinanceEndpoints.ManualReceiptsPath}/{draftOfA}")).StatusCode);
            var cursorOfA = DraftListCursor.Encode(CompanyA, DateTimeOffset.UtcNow, Guid.NewGuid());
            Assert.Equal(HttpStatusCode.BadRequest,
                (await clientOfB.GetAsync($"{FinanceEndpoints.ManualReceiptsPath}?cursor={Uri.EscapeDataString(cursorOfA)}")).StatusCode);
        }

        // B's user naming company A is refused before any read: no access to that company.
        using (var foreign = run.Factory.CreateClientFor(readerOfB, CompanyA))
        {
            Assert.Equal(HttpStatusCode.Forbidden,
                (await foreign.GetAsync($"{FinanceEndpoints.ManualReceiptsPath}/{draftOfA}")).StatusCode);
        }
    }

    private static StringContent Body(Guid bankAccount) =>
        new StringContent(JsonSerializer.Serialize(new DraftSaveRequest(null, null, new ReceiptPreparation(
                Guid.NewGuid(), Guid.NewGuid(), bankAccount, new MoneyText("1000", Currency),
                new ProfileReference(Guid.NewGuid().ToString("D"), "1"), new DateOnly(2026, 9, 18), null, null, null)),
            FinanceJson.CreateOptions()), Encoding.UTF8, "application/json");

    private static async Task<JsonElement> Result(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>());

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
            // Idempotent if the host already applied Finance's migrations.
            var companyContext = new CompanyContext();
            companyContext.Set(CompanyA);
            await using var context = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
                .UseSqlServer(factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema))
                .Options, companyContext);
            await context.Database.MigrateAsync();
            return run;
        }

        /// <summary>A SENTETIK approved account row (two synthetic actors, Ö4 holds in the row).</summary>
        public async Task<Guid> ApprovedBankAccountAsync(Guid company, string name)
        {
            var id = Guid.NewGuid();
            await ExecuteAsync(
                "INSERT INTO finance.BankAccounts (Id, CompanyId, CurrencyId, DisplayName, Status, Version, ProposedBy, "
                + "ProposedAtUtc, ApprovedBy, ApprovedAtUtc, UpdatedAtUtc) VALUES (@id, @c, @cur, @n, 2, 2, N'SENTETIK-proposer', "
                + "SYSDATETIMEOFFSET(), N'SENTETIK-approver', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET())",
                ("@id", id), ("@c", company), ("@cur", Currency), ("@n", name));
            return id;
        }

        /// <summary>A SENTETIK draft row of the company (its owners are synthetic identities).</summary>
        public async Task<Guid> DraftAsync(Guid company)
        {
            var id = Guid.NewGuid();
            await ExecuteAsync(
                "INSERT INTO finance.ReceiptDrafts (Id, CompanyId, Version, PartyId, CompanyAccountId, TreasuryAccountId, CurrencyId, "
                + "AmountText, ProfileId, ProfileVersion, OccurredOn, CreatedAtUtc, CreatedBy, UpdatedAtUtc, UpdatedBy) VALUES "
                + "(@id, @c, 1, NEWID(), NEWID(), NEWID(), @cur, '1000', @p, '1', '2026-09-18', SYSDATETIMEOFFSET(), "
                + "N'SENTETIK-actor', SYSDATETIMEOFFSET(), N'SENTETIK-actor')",
                ("@id", id), ("@c", company), ("@cur", Currency), ("@p", Guid.NewGuid().ToString("D")));
            return id;
        }

        /// <summary>
        /// A synthetic role with exactly these permissions in one company, assigned to a configured
        /// development user (linked to a new ERP account first if it has none). Returns the header value.
        /// </summary>
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
                dbContext.Users.Add(new UserAccount(actor, "Synthetic Finance user", null));
                dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor,
                    BootstrapIdentityOptions.Issuer, developmentUser));
            }
            else
            {
                actor = link.UserId;
            }

            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(roleId, $"FIN-T04B-{roleId:N}", "Synthetic Finance test role", true);
            foreach (var permission in permissions)
            {
                role.AddPermission(permission);
            }

            var assignment = new RoleAssignment(Guid.NewGuid(), actor, roleId, company,
                DateTimeOffset.UtcNow.AddMinutes(-1), null, approvedByUserId: null);
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
