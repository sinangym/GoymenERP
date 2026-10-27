using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Counterparty.Domain.Accounts;
using GoymenERP.Modules.Counterparty.Domain.Parties;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UglyToad.PdfPig;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// MOD-07 against a REAL SQL Server and the REAL host composition: <c>GET /api/v1/sales/drafts/{id}/pdf</c> of an approved,
/// priced order. The customer is created through the Cari owner's own route (no foreign table is written by this test); the
/// order is stored, submitted and approved through the real Sales store in the GDC company on a day the approved GDC/TRY
/// money profile is in force, so the currency code is the real Settings provider's answer. Grants are synthetic roles on
/// the registered names, created in this test's own database. The PDF text is read back from the bytes.
/// SENTETIK / NOT_ENV.
/// </summary>
public sealed class SalesOrderPdfSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly DateOnly Day = new(2026, 10, 27);
    private static readonly DateTimeOffset T0 = new(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid KgUnit = SettingsUnitIdentitiesV1.Kilogram;
    // The approved GDC/TRY money profile (ApprovedMoneyProfileCatalog.GdcTurkishLiraV1): scale 2, from 2026-09-18.
    private static readonly Guid GdcTryProfile = Guid.Parse("7f836492-a54f-512f-a8f4-5f556b3f0f9d");
    private static readonly Guid KgProfile = Guid.Parse("8b3c1f0e-7a6d-4e2b-9c41-5d6e7f8a9b01");
    private static readonly Guid Product = Guid.Parse("8b3c1f0e-7a6d-4e2b-9c41-5d6e7f8a9b02");

    private static readonly SalesDraftStateAudit Submitted = new("sales.order-draft.submitted.v1", "Submit", "SENTETIK onaya gönderildi");
    private static readonly SalesDraftStateAudit Approved = new("sales.order-draft.approved.v1", "Approve", "SENTETIK onaylandı");

    [SqlServerFact]
    public async Task The_order_pdf_prints_prices_only_under_the_price_read_only_in_its_own_company_and_writes_nothing_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var customer = await run.CustomerAsync("SENTETIK PDF Müşterisi A.Ş.");
        var draft = await run.ApprovedPricedDraftAsync(customer);
        var path = SalesDraftApiV1.OrderPdfPath.Replace("{id}", draft.Id.ToString("D"), StringComparison.Ordinal);
        var before = await run.EffectsAsync(draft.Id);

        // Company A's read-only user holds no Sales permission (none is seeded): 403 before anything is read.
        using (var viewer = run.Factory.CreateClientFor(TestUsers.CompanyAViewer, CompanyA))
        using (var forbidden = await viewer.GetAsync(path))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            Assert.True(forbidden.Headers.CacheControl is { Private: true, NoStore: true });
        }

        // Company B with its own draft read AND price read: A's order does not exist for it (404, never 403).
        var readerOfB = await run.GrantAsync(TestUsers.CompanyB, CompanyB,
            SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersPriceRead);
        using (var clientOfB = run.Factory.CreateClientFor(readerOfB, CompanyB))
        using (var missing = await clientOfB.GetAsync(path))
        {
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            Assert.NotEqual("application/pdf", missing.Content.Headers.ContentType?.MediaType);
        }

        // Company A's draft read without the price read: the price-less variant.
        var reader = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsRead);
        using (var client = run.Factory.CreateClientFor(reader, CompanyA))
        using (var unpriced = await client.GetAsync(path))
        {
            Assert.Equal(HttpStatusCode.OK, unpriced.StatusCode);
            Assert.Equal("application/pdf", unpriced.Content.Headers.ContentType?.MediaType);
            Assert.True(unpriced.Headers.CacheControl is { Private: true, NoStore: true });
            Assert.StartsWith("Satış_Siparişi_Fiyatsız_", unpriced.Content.Headers.ContentDisposition!.FileNameStar, StringComparison.Ordinal);
            var text = await TextAsync(unpriced);
            Assert.Contains(draft.DocumentNo, text, StringComparison.Ordinal);
            Assert.Contains($"{customer.Code} — SENTETIK PDF Müşterisi A.Ş.", text, StringComparison.Ordinal);
            Assert.Contains("SENTETIK-PDF-001", text, StringComparison.Ordinal);
            Assert.Contains("Taslak · Onaylı", text, StringComparison.Ordinal);
            Assert.Contains("Fiyat bilgileri bu belgede yer almaz.", text, StringComparison.Ordinal);
            foreach (var priceText in new[] { "12.50", "11.875", "%5", "TRY", "Birim fiyat" })
            {
                Assert.DoesNotContain(priceText, text, StringComparison.Ordinal);
            }
        }

        // The same reader once granted the price read too: the stored prices, the calculator's net and the real currency.
        await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.OrdersPriceRead);
        using (var client = run.Factory.CreateClientFor(reader, CompanyA))
        using (var priced = await client.GetAsync(path))
        {
            Assert.Equal(HttpStatusCode.OK, priced.StatusCode);
            Assert.Equal($"Satış_Siparişi_{draft.DocumentNo}_SENTETIK_PDF_Müşterisi_A.Ş_2026-10-27_v01_Onaylı.pdf",
                priced.Content.Headers.ContentDisposition!.FileNameStar);
            var text = await TextAsync(priced);
            Assert.Contains("12.50 TRY / kg", text, StringComparison.Ordinal);
            Assert.Contains("%5", text, StringComparison.Ordinal);
            Assert.Contains("11.875 TRY / kg", text, StringComparison.Ordinal);
            Assert.Contains("Sipariş toplamı, KDV ve genel toplam henüz hesaplanmıyor", text, StringComparison.Ordinal);
            // 6 kg × 11.875 = 71.25: no line amount or total is computed.
            Assert.DoesNotContain("71.25", text, StringComparison.Ordinal);
            // SAT-D56: the approved revision carries no draft watermark.
            Assert.DoesNotContain("TASLAK", text, StringComparison.Ordinal);
        }

        // A read: no attempt, audit event, vault document or version change in any module.
        Assert.Equal(before, await run.EffectsAsync(draft.Id));
    }

    private static async Task<string> TextAsync(HttpResponseMessage response)
    {
        using var pdf = PdfDocument.Open(await response.Content.ReadAsByteArrayAsync());
        return string.Concat(pdf.GetPages().Select(page => page.Text));
    }

    private static string Hash(string seed) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));

    private sealed record Customer(Guid PartyId, Guid AccountId, string Code);

    /// <summary>(sales attempts, audit events, vault documents, draft version) as SQL Server holds them.</summary>
    private sealed record Effects(int SalesAttempts, int AuditEvents, int Documents, long DraftVersion);

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
        public string Correlation { get; } = $"sales-mod-07-{Guid.NewGuid():N}";

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

        /// <summary>A customer of company A created through the Cari owner's own create route (its account in A comes with it).</summary>
        public async Task<Customer> CustomerAsync(string legalName)
        {
            using var client = Factory.CreateClientFor(TestUsers.GroupAdmin, CompanyA);
            client.DefaultRequestHeaders.Remove("Idempotency-Key");
            client.DefaultRequestHeaders.Add("Idempotency-Key", $"sales-mod-07-cari-{Guid.NewGuid():N}");
            client.DefaultRequestHeaders.Remove("X-Correlation-Id");
            client.DefaultRequestHeaders.Add("X-Correlation-Id", Correlation);
            using var response = await client.PostAsJsonAsync("/api/v1/counterparties",
                new CreateCounterparty(PartyKind.LegalEntity, legalName, null, PartyRole.Customer));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = document.RootElement;
            var account = Assert.Single(root.GetProperty("companyAccounts").EnumerateArray());
            Assert.Equal(CompanyA, account.GetProperty("companyId").GetGuid());
            return new Customer(root.GetProperty("id").GetGuid(), account.GetProperty("id").GetGuid(), root.GetProperty("code").GetString()!);
        }

        /// <summary>One priced line (6 kg at 12.50 TRY, 5 % discount), stored, submitted and approved through the real Sales store.</summary>
        public async Task<SalesOrderDraft> ApprovedPricedDraftAsync(Customer customer)
        {
            await using var scope = SalesStore(CompanyA);
            var draft = (await scope.Store.StoreDraftAsync(Envelope("pdf-create"), Hash("pdf-create"),
                documentNo => Draft(documentNo, customer))).Draft;
            await scope.Store.UpdateDraftStateAsync(Envelope("pdf-submit"), Hash("pdf-submit"), SalesDraftCommands.SubmitV1, draft.Id,
                (d, now) => d.Submit(2, Actor, now), Submitted);
            var approved = await scope.Store.UpdateDraftStateAsync(Envelope("pdf-approve", "SENTETIK-approver"), Hash("pdf-approve"),
                SalesDraftCommands.ApproveV1, draft.Id, (d, now) => d.Approve(3, "SENTETIK-approver", now), Approved);
            return approved!.Draft;
        }

        public async Task<Effects> EffectsAsync(Guid draftId) => new(
            await ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftAttempts]"),
            await ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents]"),
            await ScalarAsync<int>("SELECT COUNT(*) FROM [documents].[Documents]"),
            await ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draftId)));

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
                dbContext.Users.Add(new UserAccount(actor, "Synthetic Sales PDF user", null));
                dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer, developmentUser));
            }
            else
            {
                actor = link.UserId;
            }

            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(roleId, $"MOD-07-{roleId:N}", "Synthetic Sales PDF role", true);
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

        private SalesOrderDraft Draft(string documentNo, Customer customer)
        {
            var header = new SalesDraftHeader(customer.PartyId, customer.AccountId, SettingsCurrencyIdentitiesV1.TurkishLira, Day, Day.AddDays(7));
            var money = new MoneyProfileSnapshot(CompanyA, SettingsCurrencyIdentitiesV1.TurkishLira, SalesOrderDraft.MoneyOperation, GdcTryProfile,
                "1", Day, true, new DateOnly(2026, 9, 18), null, 2);
            var draft = SalesOrderDraft.Create(Guid.NewGuid(), CompanyA, documentNo, header, money, T0);
            var unit = new UnitProfileSnapshot(CompanyA, KgUnit, "kg", "weight", KgProfile, "1", true, 3, 3, true, new DateOnly(2026, 9, 1), null);
            var binding = new UnitBindingSnapshot(CompanyA, KgUnit, KgProfile, "1");
            var product = new ProductSnapshot(Product, "1", "SENTETIK-PDF-001", "Sentetik PDF bandı", Day, binding, binding, binding);
            draft.AddLine(draft.Version, new SalesDraftLineInput(Guid.NewGuid(), product, unit, "6", 6m, Day.AddDays(3),
                Commercial: new CommercialDraftFields("12.50", KgUnit, "5")), T0);
            return draft;
        }

        private SalesDraftWriteEnvelope Envelope(string key, string? actor = null) =>
            new(actor ?? Actor, "SENTETIK-sales-permission", ["SENTETIK-SALES"], Correlation, null, key, "Web", "127.0.0.1", "SENTETIK-agent");

        private SalesScope SalesStore(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var collector = new AuditEventCollector();
            var dbContext = new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext)).Options, companyContext);
            return new SalesScope(dbContext, new SalesStore(dbContext, companyContext, collector, TimeProvider.System));
        }

        private async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
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
    }

    private sealed class SalesScope(SalesDbContext dbContext, SalesStore store) : IAsyncDisposable
    {
        public ISalesOrderDraftStore Store => store;

        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }
}
