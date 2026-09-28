using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// DEC-FIN-016 first release through the REAL host against a REAL SQL Server: the portfolio's HTTP mapping as a client
/// sees it. The permission port (the five <c>finance.customer-instrument.*</c> names, which the central registry must
/// know), the persisted bank-account reader, Settings (the approved GDC/TRY money profile), audit and the command
/// record are real; the Counterparty party port and the open period are SENTETIK stand-ins, as in FIN-UI-1's HTTP cases.
/// Callers are the factory's development users with synthetic roles created in this test's own database. API
/// evidence, not the FIN-01 browser outcome.
/// </summary>
public sealed class FinanceCustomerInstrumentApiSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly Guid Lira = SettingsCurrencyIdentitiesV1.TurkishLira;
    private static readonly Guid Party = Guid.Parse("f1600000-0000-4000-8000-000000000001");
    private static readonly Guid PartyAccount = Guid.Parse("f1600000-0000-4000-8000-000000000002");
    private const string Clerk = TestUsers.CompanyA;
    private const string Other = TestUsers.CompanyAViewer;
    private static readonly string[] PortfolioGrants =
    [
        FinancePermissionNames.CustomerInstrumentRead, FinancePermissionNames.CustomerInstrumentAmountRead,
        FinancePermissionNames.CustomerInstrumentReceive, FinancePermissionNames.CustomerInstrumentStatusChange,
        FinancePermissionNames.CustomerInstrumentCollect, "finance.bank-account.read",
        FinancePermissionNames.DraftRead, FinancePermissionNames.AmountRead,
    ];

    [SqlServerFact]
    public async Task A_customer_cheque_is_received_reopened_and_collected_once_over_HTTP()
    {
        await using var run = await Run.StartAsync();
        var bank = await run.ApprovedBankAccountAsync(CompanyA);
        await run.GrantAsync(Clerk, CompanyA, PortfolioGrants);
        await run.GrantAsync(Other, CompanyB, FinancePermissionNames.CustomerInstrumentRead,
            FinancePermissionNames.CustomerInstrumentAmountRead);
        using var clerk = run.Client(Clerk, CompanyA);
        using var foreignCompany = run.Client(Other, CompanyB);

        Guid id;
        using (var received = await clerk.SendAsync(Post(FinanceEndpoints.CustomerInstrumentsPath, "SENTETIK-cheque-receive",
                   Guid.NewGuid().ToString("N"), Receive("SENTETIK-CEK-HTTP-1"))))
        {
            Assert.Equal(HttpStatusCode.OK, received.StatusCode);
            var resource = (await Json(received)).GetProperty("resource");
            Assert.Equal(("InPortfolio", "1"), (resource.GetProperty("status").GetString(), resource.GetProperty("version").GetString()));
            id = resource.GetProperty("id").GetGuid();
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));

        // Close and reopen: the list and the detail read it back; the detail offers the next steps.
        using (var list = await clerk.GetAsync(FinanceEndpoints.CustomerInstrumentsPath))
        {
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            var item = Assert.Single((await Json(list)).GetProperty("items").EnumerateArray());
            Assert.Equal(id, item.GetProperty("id").GetGuid());
            Assert.Equal("1000.00", item.GetProperty("amount").GetProperty("money").GetProperty("amount").GetString());
        }

        using (var detail = await clerk.GetAsync($"{FinanceEndpoints.CustomerInstrumentsPath}/{id:D}"))
        {
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
            var body = await Json(detail);
            Assert.Contains("Collect", body.GetProperty("availableActions").EnumerateArray().Select(action => action.GetString()));
            Assert.Single(body.GetProperty("movements").EnumerateArray());
        }

        using (var foreign = await foreignCompany.GetAsync($"{FinanceEndpoints.CustomerInstrumentsPath}/{id:D}"))
        {
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        }

        using (var deposited = await clerk.SendAsync(Post($"{FinanceEndpoints.CustomerInstrumentsPath}/{id:D}/status-changes",
                   "SENTETIK-cheque-deposit", Guid.NewGuid().ToString("N"),
                   new CustomerInstrumentStatusChangeRequest(id, "1", CustomerInstrumentTransition.DepositForCollection,
                       new DateOnly(2026, 9, 25), bank, null))))
        {
            Assert.Equal(HttpStatusCode.OK, deposited.StatusCode);
            Assert.Equal("AtBankForCollection", (await Json(deposited)).GetProperty("resource").GetProperty("status").GetString());
        }

        var collect = new CustomerInstrumentCollectRequest(id, "2", bank, new DateOnly(2026, 9, 26), null);
        using (var mismatch = await clerk.SendAsync(Post($"{FinanceEndpoints.CustomerInstrumentsPath}/{Guid.NewGuid():D}/collection",
                   "SENTETIK-cheque-collect-route", Guid.NewGuid().ToString("N"), collect)))
        {
            Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);
        }

        var firstCorrelation = Guid.NewGuid().ToString("N");
        Guid sourceId;
        using (var collected = await clerk.SendAsync(Post($"{FinanceEndpoints.CustomerInstrumentsPath}/{id:D}/collection",
                   "SENTETIK-cheque-collect", firstCorrelation, collect)))
        {
            Assert.Equal(HttpStatusCode.OK, collected.StatusCode);
            var resource = (await Json(collected)).GetProperty("resource");
            Assert.Equal("Collected", resource.GetProperty("status").GetString());
            sourceId = resource.GetProperty("collectionSourceId").GetGuid();
        }

        using (var replay = await clerk.SendAsync(Post($"{FinanceEndpoints.CustomerInstrumentsPath}/{id:D}/collection",
                   "SENTETIK-cheque-collect", Guid.NewGuid().ToString("N"), collect)))
        {
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal(firstCorrelation, Assert.Single(replay.Headers.GetValues("Idempotency-Original-Correlation-Id")));
        }

        using (var stale = await clerk.SendAsync(Post($"{FinanceEndpoints.CustomerInstrumentsPath}/{id:D}/collection",
                   "SENTETIK-cheque-collect-stale", Guid.NewGuid().ToString("N"), collect)))
        {
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
        Assert.Equal((3, "1000.00"), (
            await run.ScalarAsync<int>("SELECT Kind FROM finance.MoneyEvents WHERE SourceId = @s", ("@s", sourceId)),
            await run.ScalarAsync<string>("SELECT AmountText FROM finance.MoneyEvents WHERE SourceId = @s", ("@s", sourceId))));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'finance.customer-instrument.collected.v1' "
            + "AND EntityId = @i AND SourceChannel = N'Web' AND PermissionCode = N'finance.customer-instrument.collect'",
            ("@i", id.ToString("D"))));

        // The collection is an ordinary recorded receipt to F-02 (and so to F-03): its whole face value unallocated.
        using (var source = await clerk.GetAsync($"{FinanceEndpoints.ManualReceiptsPath}/{sourceId:D}"))
        {
            Assert.Equal(HttpStatusCode.OK, source.StatusCode);
            var body = await Json(source);
            Assert.Equal("Recorded", body.GetProperty("source").GetProperty("stage").GetString());
            Assert.Equal("1000.00", body.GetProperty("unallocated").GetProperty("amount").GetProperty("money")
                .GetProperty("amount").GetString());
        }

        // #497 P2-1: an actor who may read, receive and collect but not read amounts gets no amount back from any
        // answer. Checked on the parsed JSON (every amount view Redacted, no stored decimal as a whole token).
        await run.GrantAsync(Other, CompanyA, FinancePermissionNames.CustomerInstrumentRead,
            FinancePermissionNames.CustomerInstrumentReceive, FinancePermissionNames.CustomerInstrumentCollect,
            "finance.bank-account.read");
        using var blind = run.Client(Other, CompanyA);
        var answers = new List<string>();
        Guid secondId;
        using (var received = await blind.SendAsync(Post(FinanceEndpoints.CustomerInstrumentsPath, "SENTETIK-cheque-blind",
                   Guid.NewGuid().ToString("N"), Receive("SENTETIK-CEK-HTTP-2"))))
        {
            Assert.Equal(HttpStatusCode.OK, received.StatusCode);
            var body = await Json(received);
            secondId = body.GetProperty("resource").GetProperty("id").GetGuid();
            answers.Add(body.GetRawText());
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var collected = await blind.SendAsync(Post($"{FinanceEndpoints.CustomerInstrumentsPath}/{secondId:D}/collection",
                "SENTETIK-cheque-blind-collect", Guid.NewGuid().ToString("N"),
                new CustomerInstrumentCollectRequest(secondId, "1", bank, new DateOnly(2026, 9, 26), null)));
            Assert.Equal(HttpStatusCode.OK, collected.StatusCode);
            answers.Add((await Json(collected)).GetRawText());
        }

        using (var list = await blind.GetAsync(FinanceEndpoints.CustomerInstrumentsPath))
        {
            answers.Add((await Json(list)).GetRawText());
        }

        using (var detail = await blind.GetAsync($"{FinanceEndpoints.CustomerInstrumentsPath}/{secondId:D}"))
        {
            answers.Add((await Json(detail)).GetRawText());
        }

        string[] stored = ["1000.00", "1000", "1000.0"];
        Assert.All(answers, answer =>
        {
            using var document = JsonDocument.Parse(answer);
            var views = AmountViews(document.RootElement).ToList();
            Assert.NotEmpty(views);
            Assert.All(views, view =>
            {
                Assert.Equal("Redacted", view.GetProperty("disclosure").GetString());
                Assert.Equal(JsonValueKind.Null, view.GetProperty("money").ValueKind);
            });
            Assert.DoesNotContain(ScalarTokens(document.RootElement), token => stored.Contains(token, StringComparer.Ordinal));
        });
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
    }

    private static CustomerInstrumentReceiveRequest Receive(string serial) => new(
        CustomerInstrumentKind.Cheque, Party, PartyAccount, serial, "SENTETIK Keşideci A.Ş.", "SENTETIK Banka", null,
        new DateOnly(2026, 10, 15), new DateOnly(2026, 9, 18), new MoneyText("1000.00", Lira), null);

    private static HttpRequestMessage Post<T>(string path, string key, string correlation, T body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, FinanceJson.CreateOptions()), Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.Add("X-Correlation-Id", correlation);
        return request;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static IEnumerable<JsonElement> AmountViews(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("disclosure", out _) && element.TryGetProperty("money", out _))
            {
                yield return element;
            }

            foreach (var nested in element.EnumerateObject().SelectMany(property => AmountViews(property.Value)))
            {
                yield return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var nested in element.EnumerateArray().SelectMany(AmountViews))
            {
                yield return nested;
            }
        }
    }

    private static IEnumerable<string> ScalarTokens(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().SelectMany(property => ScalarTokens(property.Value)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(ScalarTokens),
        JsonValueKind.String => [element.GetString()!],
        JsonValueKind.Number => [element.GetRawText()],
        _ => [],
    };

    /// <summary>SENTETIK: one active party account of company A.</summary>
    private sealed class SyntheticParties : IFinancePartyAccountPort
    {
        public Task<ReadResult<PartyAccountEligibility>> GetAuthorizedAsync(FinanceContext context, Guid partyId,
            Guid companyAccountId, CancellationToken cancellationToken) =>
            Task.FromResult(partyId == Party && companyAccountId == PartyAccount && context.CompanyId == CompanyA
                ? new ReadResult<PartyAccountEligibility>(ReadOutcome.Found,
                    new PartyAccountEligibility(partyId, companyAccountId, CompanyA, IsActive: true, IsPartyActive: true))
                : new ReadResult<PartyAccountEligibility>(ReadOutcome.NotFound, null));
    }

    /// <summary>Explicit HTTP-mapping test input; the real period policy is FIN-T05's relational case.</summary>
    private sealed class SyntheticOpenPeriodRule : IFinanceRecordPeriodRule
    {
        public Task<RecordPeriodAnswer> CheckAsync(FinanceContext context, DateOnly occurredOn, DateOnly? valueOn,
            DateTimeOffset decidedAt, CancellationToken cancellationToken) =>
            Task.FromResult(new RecordPeriodAnswer(RecordPeriodOutcome.Open, null));
    }

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
            var factory = new SqlServerRelationalApiFactory(baseConnectionString, services =>
            {
                services.RemoveAll<IFinancePartyAccountPort>();
                services.AddScoped<IFinancePartyAccountPort, SyntheticParties>();
                services.RemoveAll<IFinanceRecordPeriodRule>();
                services.AddScoped<IFinanceRecordPeriodRule, SyntheticOpenPeriodRule>();
            });
            var run = new Run(baseConnectionString, factory);
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            var companyContext = new CompanyContext();
            companyContext.Set(CompanyA);
            await using var context = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
                .UseSqlServer(factory.DatabaseConnectionString,
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema))
                .Options, companyContext);
            await context.Database.MigrateAsync();
            return run;
        }

        public HttpClient Client(string developmentUser, Guid company)
        {
            var client = Factory.CreateClientFor(developmentUser, company);
            client.DefaultRequestHeaders.Remove("Idempotency-Key");
            client.DefaultRequestHeaders.Remove("X-Correlation-Id");
            return client;
        }

        /// <summary>A SENTETIK approved TRY account row of the company (two synthetic actors, Ö4 holds in the row).</summary>
        public async Task<Guid> ApprovedBankAccountAsync(Guid company)
        {
            var id = Guid.NewGuid();
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(
                "INSERT INTO finance.BankAccounts (Id, CompanyId, CurrencyId, DisplayName, MoneyMovementEligible, Iban, "
                + "Status, Version, ProposedBy, ProposedAtUtc, ApprovedBy, ApprovedAtUtc, UpdatedAtUtc) VALUES "
                + "(@id, @c, @cur, N'SENTETIK çek bankası', 1, N'TR330006100519786457841326', 2, 2, N'SENTETIK-proposer', "
                + "SYSDATETIMEOFFSET(), N'SENTETIK-approver', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET())",
                connection);
            command.Parameters.AddWithValue("@id", id);
            command.Parameters.AddWithValue("@c", company);
            command.Parameters.AddWithValue("@cur", Lira);
            await command.ExecuteNonQueryAsync();
            return id;
        }

        /// <summary>A synthetic role with exactly these permissions in one company, assigned to a development user.</summary>
        public async Task GrantAsync(string developmentUser, Guid company, params string[] permissions)
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
            var role = new RoleDefinition(roleId, $"FIN-016-{roleId:N}", "Synthetic Finance portfolio role", true);
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
    }
}
