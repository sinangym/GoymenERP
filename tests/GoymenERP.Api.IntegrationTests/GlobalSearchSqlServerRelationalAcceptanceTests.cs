using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Search;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Search;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// AT-002 / ERP-000I against a REAL SQL Server and the REAL host composition: GET /api/v1/search answers, per kind, only what
/// the actor's OWN read of that kind's owner allows and only the context company's records —
/// <list type="bullet">
/// <item>a kind whose read is not held has no group at all (no hit, count or "more"): an actor with no read gets an empty
/// answer, the same shape as "no match";</item>
/// <item>Cari parties come only with an account in the context company (a party of company B alone is never found from
/// A; a party shared by A and B is found from both); sales orders come only from the context company, even where A and B
/// hold the same document number;</item>
/// <item>no price member is on the wire even for an actor who holds the Sales price read;</item>
/// <item>reading writes nothing in any module; a malformed request is 400.</item>
/// </list>
/// Product cards are group master data behind the global Product authority, which this host grants to nobody here: the
/// product kind is therefore absent for every actor (the permission-filtered path of that owner). The parties are SQL-seeded
/// SYNTHETIC rows; the sales orders are stored through the REAL Sales store from SYNTHETIC owner snapshots (the AT-024/025
/// precedent); the grants are fresh synthetic roles. SENTETIK / NOT_ENV.
/// </summary>
public sealed class GlobalSearchSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly DateOnly Day = new(2026, 9, 28);
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateTimeOffset T0 = new(2026, 9, 20, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid KgUnit = SettingsUnitIdentitiesV1.Kilogram;
    private static readonly Guid Currency = Guid.Parse("9d3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7e01");
    private static readonly Guid MoneyProfile = Guid.Parse("9d3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7e02");
    private static readonly Guid KgProfile = Guid.Parse("9d3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7e03");
    private static readonly Guid Product = Guid.Parse("9d3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7e04");
    private static readonly Guid PartyOnlyA = Guid.Parse("7a000000-0000-0000-0000-000000000001");
    private static readonly Guid PartyOnlyB = Guid.Parse("7a000000-0000-0000-0000-000000000002");
    private static readonly Guid PartyShared = Guid.Parse("7a000000-0000-0000-0000-000000000003");
    private static readonly Guid PartyOtherName = Guid.Parse("7a000000-0000-0000-0000-000000000004");
    private static readonly string[] PriceWords = ["price", "amount", "discount", "currency", "payment", "total", "cost"];
    private static readonly SalesDraftStateAudit Submitted = new("sales.order-draft.submitted.v1", "Submit", "SENTETIK onaya gönderildi");
    private static readonly SalesDraftStateAudit Approved = new("sales.order-draft.approved.v1", "Approve", "SENTETIK onaylandı");

    [SqlServerFact]
    public async Task Each_kind_answers_only_with_its_own_read_and_only_the_context_companys_records_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        await run.SeedPartiesAsync();
        var orderA = await run.ApprovedDraftAsync(CompanyA, "a", "6");
        var preparingA = await run.PreparingDraftAsync(CompanyA, "a-prep", "2");
        var orderB = await run.ApprovedDraftAsync(CompanyB, "b", "9");
        // Numbers are per company: the same text may exist in A and in B, and only the context company's record is answered.
        Assert.Equal(orderA.DocumentNo, orderB.DocumentNo);
        var before = await run.EffectsAsync();

        // No read of any kind: an empty answer, the same shape as "no match" — nothing says a kind exists.
        using (var none = run.Client(TestUsers.CompanyAViewer, CompanyA))
        {
            var answer = await run.SearchAsync(none, "zeta");
            Assert.Equal(JsonValueKind.Array, answer.GetProperty("groups").ValueKind);
            Assert.Equal(0, answer.GetProperty("groups").GetArrayLength());
            Assert.Equal(0, (await run.SearchAsync(none, "SO-")).GetProperty("groups").GetArrayLength());
        }

        // The Cari read alone: the Cari group only — the parties holding an account in A (not B's own party) — and no Sales
        // group even where orders match.
        await run.GrantAsync(TestUsers.CompanyAViewer, CompanyA, ErpPermissions.CounterpartyPartiesRead);
        using (var cariOnly = run.Client(TestUsers.CompanyAViewer, CompanyA))
        {
            var parties = await run.SearchAsync(cariOnly, "zeta");
            var group = Assert.Single(parties.GetProperty("groups").EnumerateArray());
            Assert.Equal(GlobalSearchKindsV1.CounterpartyParty, group.GetProperty("kind").GetString());
            Assert.Equal(new[] { PartyOnlyA, PartyShared }.Select(Id).Order(), Ids(group).Order());
            Assert.False(group.GetProperty("hasMore").GetBoolean());
            Assert.DoesNotContain(Id(PartyOnlyB), parties.GetRawText(), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(Id(PartyOtherName), parties.GetRawText(), StringComparison.OrdinalIgnoreCase);

            var orders = await run.SearchAsync(cariOnly, "SO-");
            Assert.Equal([GlobalSearchKindsV1.CounterpartyParty], Kinds(orders));
            Assert.Equal(0, orders.GetProperty("groups")[0].GetProperty("items").GetArrayLength());
            Assert.DoesNotContain(Id(orderA.Id), orders.GetRawText(), StringComparison.OrdinalIgnoreCase);
        }

        // The Sales read alone: the Sales group only, A's orders only (never B's same-numbered one), newest first, with the
        // owner's state name and business date.
        await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsRead);
        using (var salesOnly = run.Client(TestUsers.CompanyA, CompanyA))
        {
            var orders = await run.SearchAsync(salesOnly, "so-");
            var group = Assert.Single(orders.GetProperty("groups").EnumerateArray());
            Assert.Equal(GlobalSearchKindsV1.SalesOrder, group.GetProperty("kind").GetString());
            Assert.Equal([Id(preparingA.Id), Id(orderA.Id)], Ids(group));
            var approved = group.GetProperty("items")[1];
            Assert.Equal((orderA.DocumentNo, "Approved", "2026-09-28"),
                (approved.GetProperty("code").GetString(), approved.GetProperty("state").GetString(), approved.GetProperty("date").GetString()));
            Assert.Equal("Preparing", group.GetProperty("items")[0].GetProperty("state").GetString());
            Assert.DoesNotContain(Id(orderB.Id), orders.GetRawText(), StringComparison.OrdinalIgnoreCase);
            // A readable kind with no match answers its group with no items (as the Cari-only "SO-" search above); no Cari
            // group appears because this actor holds no Cari read.
            var noOrder = await run.SearchAsync(salesOnly, "zeta");
            Assert.Equal([GlobalSearchKindsV1.SalesOrder], Kinds(noOrder));
            Assert.Equal(0, noOrder.GetProperty("groups")[0].GetProperty("items").GetArrayLength());
        }

        // Company B's reader with both reads: B's own party and the shared one, B's order only.
        await run.GrantAsync(TestUsers.CompanyB, CompanyB, ErpPermissions.CounterpartyPartiesRead, SalesPermissionManifestV1.DraftsRead);
        using (var readerB = run.Client(TestUsers.CompanyB, CompanyB))
        {
            var parties = await run.SearchAsync(readerB, "zeta");
            Assert.Equal(new[] { PartyOnlyB, PartyShared }.Select(Id).Order(), Ids(parties.GetProperty("groups")[0]).Order());
            var orders = await run.SearchAsync(readerB, "SO-");
            var sales = orders.GetProperty("groups").EnumerateArray().Single(g => g.GetProperty("kind").GetString() == GlobalSearchKindsV1.SalesOrder);
            Assert.Equal([Id(orderB.Id)], Ids(sales));
        }

        // Product cards: no actor here holds the global Product authority, so the kind never appears.
        using (var groupAdmin = run.Client(TestUsers.GroupAdmin, CompanyA))
        {
            Assert.DoesNotContain(GlobalSearchKindsV1.ProductItem, Kinds(await run.SearchAsync(groupAdmin, "zeta")));
        }

        // Reading wrote nothing in any module.
        Assert.Equal(before, await run.EffectsAsync());
    }

    [SqlServerFact]
    public async Task No_price_leaks_even_with_the_price_read_the_bound_holds_and_a_malformed_request_is_refused_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        await run.SeedPartiesAsync();
        var orders = new List<SalesOrderDraft>();
        for (var index = 0; index <= GlobalSearchApiV1.PerKindLimit; index++)
        {
            orders.Add(await run.ApprovedDraftAsync(CompanyA, $"p{index}", "5"));
        }

        await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersPriceRead,
            ErpPermissions.CounterpartyPartiesRead);
        using var client = run.Client(TestUsers.CompanyA, CompanyA);

        // Six matching orders: five answered (newest first) and "more" — no total anywhere.
        using var response = await client.GetAsync($"{GlobalSearchApiV1.Path}?q=SO-");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);
        var answer = await response.Content.ReadFromJsonAsync<JsonElement>();
        var sales = answer.GetProperty("groups").EnumerateArray().Single(g => g.GetProperty("kind").GetString() == GlobalSearchKindsV1.SalesOrder);
        Assert.Equal(orders.AsEnumerable().Reverse().Take(GlobalSearchApiV1.PerKindLimit).Select(order => Id(order.Id)), Ids(sales));
        Assert.True(sales.GetProperty("hasMore").GetBoolean());
        foreach (var item in sales.GetProperty("items").EnumerateArray())
        {
            Assert.DoesNotContain(item.EnumerateObject(), member => PriceWords.Any(word => member.Name.Contains(word, StringComparison.OrdinalIgnoreCase)));
        }

        Assert.DoesNotContain(answer.EnumerateObject(), member => member.Name.Contains("count", StringComparison.OrdinalIgnoreCase));
        foreach (var bad in new[] { "?q=S", "?q=SO-&kind=sales.order", "?q=SO-&q=SO-", "", $"?q={new string('x', GlobalSearchApiV1.MaximumTermLength + 1)}" })
        {
            using var refused = await client.GetAsync($"{GlobalSearchApiV1.Path}{bad}");
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        }
    }

    // ---- helpers --------------------------------------------------------------------------------

    private static string Id(Guid id) => id.ToString("D");

    private static IEnumerable<string> Ids(JsonElement group) =>
        group.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString()!).ToArray();

    private static string[] Kinds(JsonElement answer) =>
        [.. answer.GetProperty("groups").EnumerateArray().Select(group => group.GetProperty("kind").GetString()!)];

    private static string Hash(string seed) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));

    /// <summary>A one-line draft (the given kg quantity) from SYNTHETIC owner snapshots in the approved kg unit, created at <paramref name="at"/>.</summary>
    private static SalesOrderDraft Draft(Guid company, string documentNo, string quantity, DateTimeOffset at)
    {
        var header = new SalesDraftHeader(Guid.NewGuid(), Guid.NewGuid(), Currency, Day, Day.AddDays(10));
        var money = new MoneyProfileSnapshot(company, Currency, SalesOrderDraft.MoneyOperation, MoneyProfile, "3", Day, true, From, null, 2);
        var draft = SalesOrderDraft.Create(Guid.NewGuid(), company, documentNo, header, money, at);
        var unit = new UnitProfileSnapshot(company, KgUnit, "kg", "weight", KgProfile, "4", true, 3, 3, true, From, null);
        var binding = new UnitBindingSnapshot(company, KgUnit, KgProfile, "4");
        var product = new ProductSnapshot(Product, "7", "GSR-001", "Sentetik arama bandı", Day, binding, binding, binding);
        draft.AddLine(draft.Version, new SalesDraftLineInput(Guid.NewGuid(), product, unit, quantity,
            decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture), Day.AddDays(3)), at);
        return draft;
    }

    private sealed record Effects(int SalesAttempts, int AuditEvents, long PartyVersions, long AccountVersions);

    private sealed class Run : IAsyncDisposable
    {
        private readonly string baseConnectionString;
        private int draftSequence;

        private Run(string baseConnectionString, SqlServerRelationalApiFactory factory)
        {
            this.baseConnectionString = baseConnectionString;
            Factory = factory;
        }

        public SqlServerRelationalApiFactory Factory { get; }
        public string Actor { get; } = "SENTETIK-search-" + Guid.NewGuid().ToString("N");
        public string Correlation { get; } = $"at-002-{Guid.NewGuid():N}";

        public static async Task<Run> StartAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var factory = new SqlServerRelationalApiFactory(baseConnectionString);
            var run = new Run(baseConnectionString, factory);
            using var client = factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            return run;
        }

        /// <summary>
        /// SYNTHETIC Cari parties: one with an account in A only, one in B only, one shared by A and B (all named "Zeta …"),
        /// and one in A whose name does not match.
        /// </summary>
        public async Task SeedPartiesAsync()
        {
            await ExecuteAsync(
                """
                INSERT INTO [counterparty].[Parties] ([Id], [Code], [Kind], [LegalName], [TradeName], [IsActive], [Version], [CreatedAtUtc], [UpdatedAtUtc])
                VALUES (@OnlyA, N'CAR-910001', N'LegalEntity', N'Zeta Çelik A', NULL, 1, 1, @Now, @Now),
                       (@OnlyB, N'CAR-910002', N'LegalEntity', N'Zeta Çelik B', NULL, 1, 1, @Now, @Now),
                       (@Shared, N'CAR-910003', N'LegalEntity', N'Ortak Sac', N'Zeta Ortak', 1, 1, @Now, @Now),
                       (@Other, N'CAR-910004', N'LegalEntity', N'Başka Cari', NULL, 1, 1, @Now, @Now);
                INSERT INTO [counterparty].[CompanyPartyAccounts] ([Id], [CompanyId], [PartyId], [Roles], [ExternalIntegrationCode], [IsActive], [Version], [CreatedAtUtc], [UpdatedAtUtc])
                VALUES (NEWID(), @A, @OnlyA, @Customer, N'SENTETIK-1', 1, 1, @Now, @Now),
                       (NEWID(), @B, @OnlyB, @Customer, N'SENTETIK-2', 1, 1, @Now, @Now),
                       (NEWID(), @A, @Shared, @Customer, N'SENTETIK-3', 1, 1, @Now, @Now),
                       (NEWID(), @B, @Shared, @Customer, N'SENTETIK-4', 1, 1, @Now, @Now),
                       (NEWID(), @A, @Other, @Customer, N'SENTETIK-5', 1, 1, @Now, @Now);
                """,
                ("@OnlyA", PartyOnlyA), ("@OnlyB", PartyOnlyB), ("@Shared", PartyShared), ("@Other", PartyOtherName),
                ("@A", CompanyA), ("@B", CompanyB), ("@Customer", (int)PartyRole.Customer), ("@Now", T0));
        }

        /// <summary>A stored draft of the company, submitted and approved, through the real Sales store.</summary>
        public async Task<SalesOrderDraft> ApprovedDraftAsync(Guid company, string keyPrefix, string quantity)
        {
            var draft = await PreparingDraftAsync(company, keyPrefix, quantity);
            await using var scope = SalesStore(company);
            var submitted = await scope.Store.UpdateDraftStateAsync(SalesEnvelope($"{keyPrefix}-submit"), Hash($"{keyPrefix}-submit"),
                SalesDraftCommands.SubmitV1, draft.Id, (d, now) => d.Submit(draft.Version, Actor, now), Submitted);
            var approved = await scope.Store.UpdateDraftStateAsync(SalesEnvelope($"{keyPrefix}-approve", actor: "SENTETIK-approver"),
                Hash($"{keyPrefix}-approve"), SalesDraftCommands.ApproveV1, draft.Id,
                (d, now) => d.Approve(submitted!.Draft.Version, "SENTETIK-approver", now), Approved);
            return approved!.Draft;
        }

        /// <summary>A stored draft of the company, still being prepared, through the real Sales store.</summary>
        public async Task<SalesOrderDraft> PreparingDraftAsync(Guid company, string keyPrefix, string quantity)
        {
            await using var scope = SalesStore(company);
            // Each draft one minute after the previous: "newest first" is then a fact of the data, never an id tie-break.
            var at = T0.AddMinutes(Interlocked.Increment(ref draftSequence));
            return (await scope.Store.StoreDraftAsync(SalesEnvelope($"{keyPrefix}-create"), Hash($"{keyPrefix}-create"),
                documentNo => Draft(company, documentNo, quantity, at))).Draft;
        }

        public async Task<JsonElement> SearchAsync(HttpClient client, string term)
        {
            using var response = await client.GetAsync($"{GlobalSearchApiV1.Path}?q={Uri.EscapeDataString(term)}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }

        public async Task<Effects> EffectsAsync() => new(
            await ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftAttempts]"),
            await ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents]"),
            await ScalarAsync<long>("SELECT ISNULL(SUM([Version]), 0) FROM [counterparty].[Parties]"),
            await ScalarAsync<long>("SELECT ISNULL(SUM([Version]), 0) FROM [counterparty].[CompanyPartyAccounts]"));

        public HttpClient Client(string subject, Guid company)
        {
            var client = Factory.CreateClientFor(subject, company);
            client.DefaultRequestHeaders.Remove("Idempotency-Key");
            client.DefaultRequestHeaders.Remove("X-Correlation-Id");
            return client;
        }

        /// <summary>A fresh synthetic role of the given names for the bootstrap subject in one company (company-wide scope).</summary>
        public async Task GrantAsync(string developmentUser, Guid company, params string[] permissions)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var link = await dbContext.ExternalIdentities.SingleOrDefaultAsync(x => x.Issuer == BootstrapIdentityOptions.Issuer && x.Subject == developmentUser);
            Guid actor;
            if (link is null)
            {
                actor = Guid.NewGuid();
                dbContext.Users.Add(new UserAccount(actor, "Synthetic search user", null));
                dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer, developmentUser));
            }
            else
            {
                actor = link.UserId;
            }

            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(roleId, $"AT-002-{roleId:N}", "Synthetic global search role", true);
            foreach (var permission in permissions)
            {
                role.AddPermission(permission);
            }

            var assignment = new RoleAssignment(Guid.NewGuid(), actor, roleId, company, DateTimeOffset.UtcNow.AddMinutes(-1), null, approvedByUserId: null);
            assignment.AddScope(AccessScopeType.Company, company);
            dbContext.Roles.Add(role);
            dbContext.RoleAssignments.Add(assignment);
            await dbContext.SaveChangesAsync();
        }

        private SalesDraftWriteEnvelope SalesEnvelope(string key, string? actor = null) =>
            new SalesDraftWriteEnvelope(actor ?? Actor, "SENTETIK-sales-permission", ["SENTETIK-SALES"], Correlation, null, key, "Web", "127.0.0.1",
                "SENTETIK-agent");

        private StoreScope SalesStore(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var collector = new AuditEventCollector();
            var dbContext = new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext)).Options, companyContext);
            return new StoreScope(dbContext, new SalesStore(dbContext, companyContext, collector, TimeProvider.System));
        }

        private async Task<T> ScalarAsync<T>(string sql)
        {
            await using var connection = new SqlConnection(Factory.DatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            return (T)(await command.ExecuteScalarAsync())!;
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

    private sealed class StoreScope(DbContext dbContext, ISalesOrderDraftStore store) : IAsyncDisposable
    {
        public ISalesOrderDraftStore Store => store;

        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }
}
