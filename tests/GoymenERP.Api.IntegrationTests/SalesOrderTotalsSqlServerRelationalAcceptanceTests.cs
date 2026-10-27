using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
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
/// API-03 / SAT-D11 against a REAL SQL Server and the REAL host composition: <c>GET /api/v1/sales/drafts/{id}/totals</c> answers
/// the one calculator's amounts from the lines and money profile snapshot the real Sales store persisted (exact text,
/// <c>PricingJson</c> adjustments included): per line quantity × exact net, rounded once half away from zero to the profile's
/// scale; the document as the sum of the lines. No VAT master exists, so every tax is <c>NoSource</c> and no VAT, gross or
/// payable amount is shown. The read writes nothing; it needs the draft read AND the price read (403 with the draft read
/// alone); another company's draft is 404; an unpriced line leaves the document <c>Incomplete</c> with no partial sum.
/// <para>
/// The drafts are stored and priced through the real Sales store from ISOLATED SYNTHETIC owner snapshots (the API-08 precedent).
/// SENTETIK / NOT_ENV.
/// </para>
/// </summary>
public sealed class SalesOrderTotalsSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly DateOnly Day = new(2026, 10, 27);
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateTimeOffset T0 = new(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid KgUnit = SettingsUnitIdentitiesV1.Kilogram;
    private static readonly Guid Currency = Guid.Parse("7c3b0a21-4e5d-4f6a-8b7c-9d0e1f2a3b01");
    private static readonly Guid MoneyProfile = Guid.Parse("7c3b0a21-4e5d-4f6a-8b7c-9d0e1f2a3b02");
    private static readonly Guid KgProfile = Guid.Parse("7c3b0a21-4e5d-4f6a-8b7c-9d0e1f2a3b03");
    private static readonly Guid Product = Guid.Parse("7c3b0a21-4e5d-4f6a-8b7c-9d0e1f2a3b04");

    private static readonly SalesDraftStateAudit Priced = new(
        "sales.order-draft.commercial-revised.v1", "ReviseCommercial", "SENTETIK fiyatlandı");

    [SqlServerFact]
    public async Task Totals_are_the_calculators_amounts_of_the_stored_priced_lines_need_the_price_read_and_write_nothing_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.PricedDraftAsync(CompanyA, "k",
            ("1.25", new CommercialDraftFields("12.50", KgUnit, "10", SurchargePercent: "2")),
            ("2.25", new CommercialDraftFields("100", KgUnit, null, DiscountAmount: "0.50")));
        var (first, second) = (draft.Lines[0].Id, draft.Lines[1].Id);
        var path = $"{SalesEndpoints.DraftsPath}/{draft.Id:D}/totals";

        // The draft read alone does not disclose prices: 403, nothing of the totals on the wire.
        var subject = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsRead);
        using (var readerOnly = run.Client(subject, CompanyA))
        using (var forbidden = await readerOnly.GetAsync(path))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            Assert.True(forbidden.Headers.CacheControl is { Private: true, NoStore: true });
            Assert.DoesNotContain("netAmount", await forbidden.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersPriceRead);
        using var client = run.Client(subject, CompanyA);
        var before = await run.EffectsAsync(draft.Id);
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl is { Private: true, NoStore: true });
        var totals = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal((draft.Version.ToString(CultureInfo.InvariantCulture), "Rev.0", Currency.ToString("D"), 2, "NoSource"),
            (totals.GetProperty("draftVersion").GetString(), totals.GetProperty("revision").GetString(),
                totals.GetProperty("currencyId").GetString(), totals.GetProperty("amountScale").GetInt32(),
                totals.GetProperty("taxState").GetString()));
        var lines = totals.GetProperty("lines").EnumerateArray().ToList();
        // 1.25 kg × (12.50 − 10 % + 2 %) = 1.25 × 11.475 = 14.34375 → 14.34; 2.25 kg × 99.5 = 223.875 → 223.88.
        Assert.Equal((first.ToString("D"), "1.25", "Computed", "11.475", "14.34", "NoSource"), Line(lines[0]));
        Assert.Equal((second.ToString("D"), "2.25", "Computed", "99.5", "223.88", "NoSource"), Line(lines[1]));
        foreach (var line in lines)
        {
            foreach (var member in new[] { "vatPercent", "vatAmount", "withholdingShare", "withheldVatAmount", "grossAmount", "payableAmount" })
            {
                Assert.Equal(JsonValueKind.Null, line.GetProperty(member).ValueKind);
            }
        }

        var document = totals.GetProperty("totals");
        Assert.Equal(("Computed", 0, 0, "238.22", "NoSource"), (document.GetProperty("state").GetString(),
            document.GetProperty("unpricedLineCount").GetInt32(), document.GetProperty("priceUnitNotOrderUnitLineCount").GetInt32(),
            document.GetProperty("netAmount").GetString(), document.GetProperty("taxState").GetString()));
        foreach (var member in new[] { "vatAmount", "withheldVatAmount", "grossAmount", "payableAmount" })
        {
            Assert.Equal(JsonValueKind.Null, document.GetProperty(member).ValueKind);
        }

        // Read-only: no attempt, audit event or version effect.
        Assert.Equal(before, await run.EffectsAsync(draft.Id));
    }

    [SqlServerFact]
    public async Task Another_companys_draft_is_404_and_an_unpriced_line_leaves_the_document_incomplete_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.PricedDraftAsync(CompanyA, "u",
            ("3", new CommercialDraftFields("7.25", KgUnit, null)),
            ("4.5", CommercialDraftFields.None));
        var path = $"{SalesEndpoints.DraftsPath}/{draft.Id:D}/totals";

        // Company B with its own draft and price reads: A's draft does not exist for it (404, never 403).
        var readerOfB = await run.GrantAsync(TestUsers.CompanyB, CompanyB, SalesPermissionManifestV1.DraftsRead,
            SalesPermissionManifestV1.OrdersPriceRead);
        using (var clientOfB = run.Client(readerOfB, CompanyB))
        using (var missing = await clientOfB.GetAsync(path))
        {
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            Assert.True(missing.Headers.CacheControl is { Private: true, NoStore: true });
        }

        var readerOfA = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsRead,
            SalesPermissionManifestV1.OrdersPriceRead);
        using var clientOfA = run.Client(readerOfA, CompanyA);
        using var response = await clientOfA.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var totals = await response.Content.ReadFromJsonAsync<JsonElement>();
        var lines = totals.GetProperty("lines").EnumerateArray().ToList();
        Assert.Equal((draft.Lines[0].Id.ToString("D"), "3", "Computed", "7.25", "21.75", "NoSource"), Line(lines[0]));
        Assert.Equal((draft.Lines[1].Id.ToString("D"), "4.5", "Unpriced", null, null, "NotComputed"), Line(lines[1]));
        var document = totals.GetProperty("totals");
        Assert.Equal(("Incomplete", 1, JsonValueKind.Null, "NotComputed"), (document.GetProperty("state").GetString(),
            document.GetProperty("unpricedLineCount").GetInt32(), document.GetProperty("netAmount").ValueKind,
            document.GetProperty("taxState").GetString()));
    }

    // ---- helpers --------------------------------------------------------------------------------

    private static (string? LineId, string? Quantity, string? State, string? NetUnitPrice, string? NetAmount, string? TaxState) Line(
        JsonElement line) =>
        (line.GetProperty("lineId").GetString(), line.GetProperty("quantity").GetString(), line.GetProperty("state").GetString(),
            line.GetProperty("netUnitPrice").GetString(), line.GetProperty("netAmount").GetString(), line.GetProperty("taxState").GetString());

    private static string Hash(string seed) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));

    /// <summary>A draft with one kg line per quantity on the business day from SYNTHETIC owner snapshots (money profile scale 2).</summary>
    private static SalesOrderDraft Draft(Guid company, string documentNo, IReadOnlyList<string> quantities)
    {
        var header = new SalesDraftHeader(Guid.NewGuid(), Guid.NewGuid(), Currency, Day, Day.AddDays(10));
        var money = new MoneyProfileSnapshot(company, Currency, SalesOrderDraft.MoneyOperation, MoneyProfile, "3", Day, true, From, null, 2);
        var draft = SalesOrderDraft.Create(Guid.NewGuid(), company, documentNo, header, money, T0);
        var unit = new UnitProfileSnapshot(company, KgUnit, "kg", "weight", KgProfile, "4", true, 3, 3, true, From, null);
        var binding = new UnitBindingSnapshot(company, KgUnit, KgProfile, "4");
        var product = new ProductSnapshot(Product, "7", "GDC-001", "Sentetik bant", Day, binding, binding, binding);
        foreach (var quantity in quantities)
        {
            draft.AddLine(draft.Version, new SalesDraftLineInput(Guid.NewGuid(), product, unit, quantity,
                decimal.Parse(quantity, CultureInfo.InvariantCulture), Day.AddDays(3)), T0);
        }

        return draft;
    }

    /// <summary>(sales attempts, audit events, draft version) as SQL Server holds them.</summary>
    private sealed record Effects(int SalesAttempts, int AuditEvents, long DraftVersion);

    private sealed class SalesScope(SalesDbContext dbContext, SalesStore store) : IAsyncDisposable
    {
        public ISalesOrderDraftStore Store => store;

        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
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
        public string Actor { get; } = "SENTETIK-preparer-" + Guid.NewGuid().ToString("N");
        public string Correlation { get; } = $"sales-api-03-{Guid.NewGuid():N}";

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
        /// A stored draft of the company with one kg line per entry, then ONE commercial revision through the real Sales store
        /// giving every line its commercial fields (an unpriced entry stays unpriced): the draft as stored after it.
        /// </summary>
        public async Task<SalesOrderDraft> PricedDraftAsync(Guid company, string keyPrefix,
            params (string Quantity, CommercialDraftFields Commercial)[] lines)
        {
            await using var scope = SalesStore(company);
            var draft = (await scope.Store.StoreDraftAsync(SalesEnvelope($"{keyPrefix}-create"), Hash($"{keyPrefix}-create"),
                documentNo => Draft(company, documentNo, [.. lines.Select(line => line.Quantity)]))).Draft;
            var inputs = draft.Lines.Select((line, index) => new LineCommercialInput(line.Id, lines[index].Commercial)).ToArray();
            var priced = await scope.Store.ReviseDraftCommercialAsync(SalesEnvelope($"{keyPrefix}-price"), Hash($"{keyPrefix}-price"),
                SalesDraftCommands.ReviseCommercialV1, draft.Id,
                (target, now) => target.ReviseCommercial(target.Version, inputs, Actor, now), Priced, _ => Task.CompletedTask);
            return priced!.Draft;
        }

        public async Task<Effects> EffectsAsync(Guid draftId) => new(
            await ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftAttempts]"),
            await ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents]"),
            await ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draftId)));

        public SalesDraftWriteEnvelope SalesEnvelope(string key) =>
            new(Actor, "SENTETIK-sales-permission", ["SENTETIK-SALES"], Correlation, null, key, "Web", "127.0.0.1", "SENTETIK-agent");

        public SalesScope SalesStore(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var collector = new AuditEventCollector();
            var dbContext = new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext)).Options, companyContext);
            return new SalesScope(dbContext, new SalesStore(dbContext, companyContext, collector, TimeProvider.System));
        }

        public HttpClient Client(string subject, Guid company)
        {
            var client = Factory.CreateClientFor(subject, company);
            client.DefaultRequestHeaders.Remove("Idempotency-Key");
            client.DefaultRequestHeaders.Remove("X-Correlation-Id");
            return client;
        }

        /// <summary>A fresh synthetic role of the given names for the bootstrap subject in one company (company-wide scope).</summary>
        public async Task<string> GrantAsync(string developmentUser, Guid company, params string[] permissions)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var link = await dbContext.ExternalIdentities.SingleOrDefaultAsync(x => x.Issuer == BootstrapIdentityOptions.Issuer && x.Subject == developmentUser);
            Guid actor;
            if (link is null)
            {
                actor = Guid.NewGuid();
                dbContext.Users.Add(new UserAccount(actor, "Synthetic Sales totals user", null));
                dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer, developmentUser));
            }
            else
            {
                actor = link.UserId;
            }

            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(roleId, $"API-03-{roleId:N}", "Synthetic Sales totals role", true);
            foreach (var permission in permissions)
            {
                role.AddPermission(permission);
            }

            var assignment = new RoleAssignment(Guid.NewGuid(), actor, roleId, company, DateTimeOffset.UtcNow.AddMinutes(-1), null, approvedByUserId: null);
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
    }
}
