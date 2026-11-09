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
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// MOD-03 → Sales against a REAL SQL Server and the REAL host composition: the order's buyer tax situation (SAT-D140/D147) and every
/// line's tax snapshot (SAT-D93/D139) are persisted by the real Sales store in their JSON columns (<c>BuyerTaxJson</c>,
/// <c>TaxJson</c>) — on the order and, when an approved revision is archived (SAT-D105), on the archive (SAT-D115/D166 read it) — the
/// write guard admits exactly those columns under a commercial or header revision; <c>GET …/tax</c> answers them under the draft read
/// AND the price read (SAT-D150); <c>GET …/totals</c> computes VAT, withholding, gross and payable from them through the one
/// calculator; <c>GET /api/v1/sales/tax-options</c> answers the REAL Settings lists in force (the reviewed buyer situation catalog;
/// the exemption list empty until entered, SAT-D148); the migration refuses to roll back over a stored snapshot (51135).
/// <para>
/// The drafts are stored through the real Sales store from ISOLATED SYNTHETIC owner snapshots and SENTETIK tax snapshots (the
/// API-03 precedent); no rate here is a policy. SENTETIK / NOT_ENV.
/// </para>
/// </summary>
public sealed partial class SalesOrderTaxSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly DateOnly Day = new(2026, 11, 2);
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid KgUnit = SettingsUnitIdentitiesV1.Kilogram;
    private static readonly Guid Currency = Guid.Parse("7c3b0a21-4e5d-4f6a-8b7c-9d0e1f2a3c01");
    private static readonly Guid MoneyProfile = Guid.Parse("7c3b0a21-4e5d-4f6a-8b7c-9d0e1f2a3c02");
    private static readonly Guid KgProfile = Guid.Parse("7c3b0a21-4e5d-4f6a-8b7c-9d0e1f2a3c03");
    private static readonly Guid Product = Guid.Parse("7c3b0a21-4e5d-4f6a-8b7c-9d0e1f2a3c04");

    private static readonly SalesDraftStateAudit Submitted = new("sales.order-draft.submitted.v1", "Submit", "SENTETIK onaya gönderildi");
    private static readonly SalesDraftStateAudit Approved = new("sales.order-draft.approved.v1", "Approve", "SENTETIK onaylandı");
    private static readonly SalesDraftStateAudit Revised = new("sales.order-draft.commercial-revised.v1", "ReviseCommercial", "SENTETIK revize");
    private static readonly SalesDraftStateAudit HeaderRevised = new("sales.order-draft.header-revised.v1", "ReviseHeader", "SENTETIK başlık");

    [SqlServerFact]
    public async Task An_approved_orders_tax_change_archives_the_approved_tax_and_the_tax_and_totals_routes_answer_the_stored_snapshots_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k");
        var line = Assert.Single(draft.Lines);

        // SAT-D105: a reasoned tax change (SAT-D97) of the approved order — priced in the same commercial revision — archives Rev.0
        // WITH its tax.
        var change = new SalesLineTaxOverride(null, TaxBuyerSituationsV1.NotSubjectToWithholding, null, "SENTETIK tevkifatsız alıcı",
            "SENTETIK-reviser", T0);
        var changed = Tax() with { WithholdingState = "NotApplied", WithholdingPercent = null, WithholdingCodeState = "NotRequired",
            WithholdingCodeReason = null, BuyerAdjustment = TaxResolutionReasonsV1.BuyerNotSubjectToWithholding, Override = change };
        await using (var scope = run.SalesStore(CompanyA))
        {
            var revised = await scope.Store.ReviseDraftCommercialAsync(run.SalesEnvelope("k-tax"), Hash("k-tax"),
                SalesDraftCommands.ReviseCommercialV1, draft.Id,
                (d, now) => d.ReviseCommercial(4, [new LineCommercialInput(line.Id, new CommercialDraftFields("100", KgUnit, null))], "SENTETIK-reviser", now,
                    taxes: new Dictionary<Guid, SalesLineTaxSnapshot?> { [line.Id] = changed }), Revised, _ => Task.CompletedTask);
            Assert.Equal((1, "Preparing"), (revised!.Draft.Revision, revised.Draft.ApprovalState.ToString()));
        }

        await using (var scope = run.SalesStore(CompanyA))
        {
            var stored = (await scope.Store.FindDraftAsync(draft.Id))!;
            Assert.Equal(changed, Assert.Single(stored.Lines).Tax);
            Assert.Equal("100", stored.Lines[0].Commercial.UnitPrice);
            Assert.Equal(Tax(), Assert.Single(Assert.Single(stored.ArchivedRevisions).Lines).Tax);
            Assert.Equal(SalesBuyerTax.Create(TaxBuyerSituationsV1.Normal), stored.ArchivedRevisions[0].Header!.BuyerTax);
        }

        Assert.Equal((1, 1, 1), (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftLines] WHERE [DraftId] = @id AND JSON_VALUE([TaxJson], '$.override.reason') = N'SENTETIK tevkifatsız alıcı'", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftRevisionLines] WHERE [DraftId] = @id AND ISJSON([TaxJson]) = 1 AND JSON_QUERY([TaxJson], '$.override') IS NULL AND JSON_VALUE([TaxJson], '$.withholdingState') = N'Resolved'", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftRevisionHeaders] WHERE [DraftId] = @id AND ISJSON([BuyerTaxJson]) = 1", ("@id", draft.Id))));

        var path = $"{SalesEndpoints.DraftsPath}/{draft.Id:D}";
        // SAT-D150: the draft read alone discloses no tax (403); another company's draft is 404.
        var reader = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsRead);
        using (var readerOnly = run.Client(reader, CompanyA))
        using (var forbidden = await readerOnly.GetAsync($"{path}/tax"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        var other = await run.GrantAsync(TestUsers.CompanyB, CompanyB, SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersPriceRead);
        using (var clientOfB = run.Client(other, CompanyB))
        using (var missing = await clientOfB.GetAsync($"{path}/tax"))
        {
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }

        await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersPriceRead);
        using var client = run.Client(reader, CompanyA);
        var before = await run.EffectsAsync(draft.Id);
        using (var response = await client.GetAsync($"{path}/tax"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl is { Private: true, NoStore: true });
            var tax = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(TaxBuyerSituationsV1.Normal, tax.GetProperty("buyerTax").GetProperty("situation").GetString());
            var taxLine = Assert.Single(tax.GetProperty("lines").EnumerateArray());
            Assert.Equal(("Override", "Resolved", "20", "NotApplied", "Computed", "SENTETIK tevkifatsız alıcı"),
                (taxLine.GetProperty("origin").GetString(), taxLine.GetProperty("vatState").GetString(),
                    taxLine.GetProperty("vatPercent").GetString(), taxLine.GetProperty("withholdingState").GetString(),
                    taxLine.GetProperty("rateState").GetString(), taxLine.GetProperty("override").GetProperty("reason").GetString()));
        }

        // API-03 from the stored snapshot: 10 kg × 100 = 1000.00; VAT 20 % = 200.00; no withholding; gross = payable = 1200.00.
        using (var response = await client.GetAsync($"{path}/totals"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var document = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("totals");
            Assert.Equal(("Computed", "1000.00", "200.00", "0.00", "1200.00", "1200.00"), (document.GetProperty("taxState").GetString(),
                document.GetProperty("netAmount").GetString(), document.GetProperty("vatAmount").GetString(),
                document.GetProperty("withheldVatAmount").GetString(), document.GetProperty("grossAmount").GetString(),
                document.GetProperty("payableAmount").GetString()));
        }

        // Read-only: no attempt, audit event or version effect.
        Assert.Equal(before, await run.EffectsAsync(draft.Id));
    }

    [SqlServerFact]
    public async Task A_buyer_tax_change_moves_the_header_and_line_tax_columns_and_archives_the_approved_buyer_tax_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "h");
        var line = Assert.Single(draft.Lines);
        var buyer = SalesBuyerTax.Create(TaxBuyerSituationsV1.ExportRegistered, null, "SENTETIK-IK-1", Day);
        var zero = Tax() with
        {
            BuyerSituation = TaxBuyerSituationsV1.ExportRegistered, VatState = "ZeroRated", VatPercent = 0m,
            ExemptionCodeState = "Unresolved", ExemptionCodeReason = TaxResolutionReasonsV1.ExemptionCodeMissing,
            WithholdingState = "NotApplied", WithholdingPercent = null, WithholdingCodeState = "NotRequired", WithholdingCodeReason = null,
            BuyerVatMode = TaxBuyerSituationVatModesV1.ZeroWithExemptionCode, BuyerDocumentRequired = true,
            BuyerAdjustment = TaxResolutionReasonsV1.BuyerZeroRatedWithExemptionCode,
        };
        await using (var scope = run.SalesStore(CompanyA))
        {
            var result = await scope.Store.ReviseDraftHeaderAsync(run.SalesEnvelope("h-header"), Hash("h-header"), SalesDraftCommands.ReviseHeaderV1,
                draft.Id, (d, input, now) => d.ReviseHeader(4, input, "SENTETIK-reviser", "SENTETIK ihraç kayıtlı", now), HeaderRevised,
                "SENTETIK ihraç kayıtlı",
                (d, _) => Task.FromResult(new SalesHeaderRevisionInput(d.Header.PartyId, d.Header.CompanyAccountId, d.Header.CurrencyId,
                    null, null, null, null, d.Header.DueTerms, buyer, new Dictionary<Guid, SalesLineTaxSnapshot?> { [line.Id] = zero })));
            Assert.Equal((1, buyer, zero), (result!.Draft.Revision, result.Draft.Header.BuyerTax, Assert.Single(result.Draft.Lines).Tax));
        }

        await using (var scope = run.SalesStore(CompanyA))
        {
            var stored = (await scope.Store.FindDraftAsync(draft.Id))!;
            Assert.Equal((buyer, zero), (stored.Header.BuyerTax, Assert.Single(stored.Lines).Tax));
            var archived = Assert.Single(stored.ArchivedRevisions);
            Assert.Equal((SalesBuyerTax.Create(TaxBuyerSituationsV1.Normal), Tax()), (archived.Header!.BuyerTax, Assert.Single(archived.Lines).Tax));
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [sales].[Drafts] WHERE [Id] = @id AND JSON_VALUE([BuyerTaxJson], '$.situation') = N'ExportRegistered'", ("@id", draft.Id)));
    }

    [SqlServerFact]
    public async Task The_tax_options_are_the_real_settings_lists_and_the_migration_refuses_to_roll_back_over_a_stored_snapshot_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var subject = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsRead);
        using (var client = run.Client(subject, CompanyA))
        {
            using (var bad = await client.GetAsync($"{SalesDraftTaxApiV1.TaxOptionsPath}?on=2026-13-01"))
            {
                Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
            }

            using var response = await client.GetAsync($"{SalesDraftTaxApiV1.TaxOptionsPath}?on={Day:yyyy-MM-dd}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var options = await response.Content.ReadFromJsonAsync<JsonElement>();
            // SAT-D158: the reviewed version-0 list of the company; SAT-D148: no exemption code is entered yet ("kaynak yok").
            Assert.Equal(("Resolved", "Unresolved", TaxResolutionReasonsV1.CodeListMissing),
                (options.GetProperty("buyerSituationsState").GetString(), options.GetProperty("exemptionCodesState").GetString(),
                    options.GetProperty("exemptionCodesReason").GetString()));
            Assert.Equal(TaxBuyerSituationsV1.Catalog.Order(StringComparer.Ordinal),
                options.GetProperty("buyerSituations").EnumerateArray().Select(entry => entry.GetProperty("code").GetString()!)
                    .Order(StringComparer.Ordinal));
        }

        await run.ApprovedDraftAsync(CompanyA, "m");
        await using var context = run.SalesContext(CompanyA);
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        var tax = Array.FindIndex(applied, migration => migration.EndsWith("_AddDraftOrderTax", StringComparison.Ordinal));
        Assert.True(tax > 0);
        var refused = await Assert.ThrowsAsync<SqlException>(() => context.GetService<IMigrator>().MigrateAsync(applied[tax - 1]));
        Assert.Equal(51135, refused.Number);
        // SAT-D184: BackfillDraftBuyerTax (a no-op Down) rolled back first; AddDraftOrderTax stands.
        Assert.Equal(applied[..(tax + 1)], await context.Database.GetAppliedMigrationsAsync());
    }

    [SqlServerFact]
    public async Task A_legacy_order_without_buyer_tax_is_backfilled_normal_once_by_the_migration_and_its_lines_wait_for_a_resolve_point_on_real_sql()
    {
        // SAT-D184 (EK26): the schema as it stood before the backfill, a legacy order written then (no buyer tax, no line tax), and
        // the backfill applied: the order reads "Normal" in the store's exact JSON; its lines stay "kaynak yok" until resolved.
        await using var run = await Run.StartAsync();
        await using var context = run.SalesContext(CompanyA);
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        var backfill = Array.FindIndex(applied, migration => migration.EndsWith("_BackfillDraftBuyerTax", StringComparison.Ordinal));
        Assert.True(backfill > 0);
        var migrator = context.GetService<IMigrator>();

        // The legacy order is written through today's store (it writes every later migration's columns too, e.g. SAT-D175's
        // AddDraftSubcontractService), with no buyer tax — the state SQL Server held before the backfill — and the schema is
        // then rolled back to before the backfill (the later migrations' Downs pass over a normal order) and applied again.
        Guid legacyId;
        await using (var scope = run.SalesStore(CompanyA))
        {
            legacyId = (await scope.Store.StoreDraftAsync(run.SalesEnvelope("l-create"), Hash("l-create"),
                documentNo => Draft(CompanyA, documentNo, legacy: true))).Draft.Id;
        }

        await migrator.MigrateAsync(applied[backfill - 1]);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[Drafts] WHERE [Id] = @id AND [BuyerTaxJson] IS NULL",
            ("@id", legacyId)));
        await migrator.MigrateAsync();

        Assert.Equal(SalesTaxJson.SerializeBuyer(SalesBuyerTax.Create(TaxBuyerSituationsV1.Normal)),
            await run.ScalarAsync<string>("SELECT [BuyerTaxJson] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", legacyId)));
        await using (var scope = run.SalesStore(CompanyA))
        {
            var restored = (await scope.Store.FindDraftAsync(legacyId))!;
            Assert.Equal((SalesBuyerTax.Create(TaxBuyerSituationsV1.Normal), (SalesLineTaxSnapshot?)null),
                (restored.Header.BuyerTax, Assert.Single(restored.Lines).Tax));
        }
    }

    // ---- helpers --------------------------------------------------------------------------------

    private static string Hash(string seed) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));

    /// <summary>A SENTETIK resolved snapshot of the line's product for the normal buyer: VAT 20 %, 50 % withheld, no code entered.</summary>
    private static SalesLineTaxSnapshot Tax() =>
        new(Product, Day, TaxBuyerSituationsV1.Normal, TaxItemTypesV1.Steel, "Resolved", null, "Record",
            Guid.Parse("7c3b0a21-4e5d-4f6a-8b7c-9d0e1f2a3c05"), 1, "Resolved", 20m, "NotRequired", null, null, "Resolved", 50m,
            "Unresolved", null, TaxResolutionReasonsV1.WithholdingCodeMissing, null, 0, TaxBuyerSituationVatModesV1.Normal, false);

    /// <summary>A draft with one (unpriced) 10 kg line and its tax snapshot, for the normal buyer, from SYNTHETIC owner snapshots.</summary>
    private static SalesOrderDraft Draft(Guid company, string documentNo, bool legacy = false)
    {
        var header = new SalesDraftHeader(Guid.NewGuid(), Guid.NewGuid(), Currency, Day, Day.AddDays(10), DueTerms: TestDueTerms.PointDue30,
            BuyerTax: legacy ? null : SalesBuyerTax.Create(TaxBuyerSituationsV1.Normal));
        var money = new MoneyProfileSnapshot(company, Currency, SalesOrderDraft.MoneyOperation, MoneyProfile, "3", Day, true, From, null, 2);
        var draft = SalesOrderDraft.Create(Guid.NewGuid(), company, documentNo, header, money, T0);
        var unit = new UnitProfileSnapshot(company, KgUnit, "kg", "weight", KgProfile, "4", true, 3, 3, true, From, null);
        var binding = new UnitBindingSnapshot(company, KgUnit, KgProfile, "4");
        var product = new ProductSnapshot(Product, "7", "GDC-001", "Sentetik bant", Day, binding, binding, binding);
        draft.AddLine(draft.Version, new SalesDraftLineInput(Guid.NewGuid(), product, unit, "10", 10m, Day.AddDays(3),
            Tax: legacy ? null : Tax()), T0);
        return draft;
    }

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
        public string Correlation { get; } = $"sales-mod-03-{Guid.NewGuid():N}";

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

        /// <summary>A stored draft of the company, submitted and approved (version 4), through the real Sales store.</summary>
        public async Task<SalesOrderDraft> ApprovedDraftAsync(Guid company, string keyPrefix)
        {
            await using var scope = SalesStore(company);
            var draft = (await scope.Store.StoreDraftAsync(SalesEnvelope($"{keyPrefix}-create"), Hash($"{keyPrefix}-create"),
                documentNo => Draft(company, documentNo))).Draft;
            await scope.Store.UpdateDraftStateAsync(SalesEnvelope($"{keyPrefix}-submit"), Hash($"{keyPrefix}-submit"), SalesDraftCommands.SubmitV1,
                draft.Id, (d, now) => d.Submit(2, Actor, now), Submitted);
            var approved = await scope.Store.UpdateDraftStateAsync(SalesEnvelope($"{keyPrefix}-approve", "SENTETIK-approver"),
                Hash($"{keyPrefix}-approve"), SalesDraftCommands.ApproveV1, draft.Id, (d, now) => d.Approve(3, "SENTETIK-approver", now), Approved);
            return approved!.Draft;
        }

        public async Task<Effects> EffectsAsync(Guid draftId) => new(
            await ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftAttempts]"),
            await ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents]"),
            await ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draftId)));

        public SalesDraftWriteEnvelope SalesEnvelope(string key, string? actor = null) =>
            new(actor ?? Actor, "SENTETIK-sales-permission", ["SENTETIK-SALES"], Correlation, null, key, "Web", "127.0.0.1", "SENTETIK-agent");

        public SalesDbContext SalesContext(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            return new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SalesDbContext.Schema))
                .Options, companyContext);
        }

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
                dbContext.Users.Add(new UserAccount(actor, "Synthetic Sales tax user", null));
                dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer, developmentUser));
            }
            else
            {
                actor = link.UserId;
            }

            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(roleId, $"MOD-03-{roleId:N}", "Synthetic Sales tax role", true);
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
