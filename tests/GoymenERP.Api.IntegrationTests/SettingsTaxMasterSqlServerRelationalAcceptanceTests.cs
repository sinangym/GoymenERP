using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.ProductRuntime;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.Fixtures;
using GoymenERP.Modules.Product.Infrastructure.PriceLists;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Settings.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// MOD-03 (SAT-D73/D74/D82/D83, EK12; SAT-D93, EK13) against a REAL SQL Server: the Settings tax master through the REAL host
/// (routes, authorization, migrations, audit) — 403 without the name, an empty master is "kaynak yok", categories, dated VAT
/// and withholding records, the UBL-TR code lists, same-date replacement and withdrawal without resurrection, company isolation,
/// replay/conflict and the rollback refusal (51131); and the Product owner's tax classification feeding the Settings line tax
/// port end to end (51132). Every grant is a fresh synthetic role; every rate and code is SENTETIK.
/// </summary>
public sealed class SettingsTaxMasterSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private const string Permission = SettingsTaxMasterPermissionManifestV1.TaxMasterManage;
    private const string Root = SettingsTaxMasterEndpoints.TaxMasterPath;

    [SqlServerFact]
    public async Task Tax_master_lifecycle_with_dated_records_code_lists_isolation_replay_and_audit_on_real_sql()
    {
        await using var run = await HostRun.StartAsync();
        var (editorSubject, _) = await run.GrantAsync(TestUsers.CompanyA, CompanyA, Permission);
        var (viewerSubject, _) = await run.GrantAsync(TestUsers.CompanyAViewer, CompanyA, "sales.drafts.read");
        var (editorBSubject, _) = await run.GrantAsync(TestUsers.CompanyB, CompanyB, Permission);
        using var client = run.Client(editorSubject, CompanyA);
        using var viewer = run.Client(viewerSubject, CompanyA);
        using var clientB = run.Client(editorBSubject, CompanyB);
        var today = run.Today;

        // Without the name: 403 on the read and on a command; nothing is written.
        using (var forbidden = await viewer.GetAsync(Root))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        using (var forbidden = await viewer.SendAsync(Json(HttpMethod.Post, $"{Root}/categories", "k-forbidden",
                   new CreateTaxCategoryRequestV1(TaxCategoryKindsV1.VatClass, "GENEL", "Genel", "SENTETIK"))))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [settings].[TaxCategories]"));

        // Nothing is seeded: the empty master shows no category, no rate and both lists at version "0".
        var empty = await run.JsonAsync(client, Root);
        Assert.Equal((CompanyA, 0, 0), (empty.GetProperty("companyId").GetGuid(), empty.GetProperty("categories").GetArrayLength(),
            empty.GetProperty("rateSeries").GetArrayLength()));
        Assert.All(empty.GetProperty("codeLists").EnumerateArray(), list =>
        {
            Assert.Equal("0", list.GetProperty("version").GetString());
            Assert.Equal(JsonValueKind.Null, list.GetProperty("inForce").ValueKind);
        });

        // Categories: create, replay, another body under the key, a taken code; a reason is required.
        using (var noReason = await client.SendAsync(Json(HttpMethod.Post, $"{Root}/categories", "k-cat-1",
                   new CreateTaxCategoryRequestV1(TaxCategoryKindsV1.VatClass, "genel", "Genel", " "))))
        {
            await AssertProblemAsync(noReason, HttpStatusCode.BadRequest, TaxMasterProblemCodesV1.ReasonRequired);
        }

        var general = await run.CreateCategoryAsync(client, "k-cat-1", TaxCategoryKindsV1.VatClass, "genel", "Genel");
        using (var replay = await client.SendAsync(Json(HttpMethod.Post, $"{Root}/categories", "k-cat-1",
                   new CreateTaxCategoryRequestV1(TaxCategoryKindsV1.VatClass, "genel", "Genel", "SENTETIK kuruluş"))))
        {
            Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
            Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal(general, (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        }

        using (var other = await client.SendAsync(Json(HttpMethod.Post, $"{Root}/categories", "k-cat-1",
                   new CreateTaxCategoryRequestV1(TaxCategoryKindsV1.VatClass, "BASKA", "Başka", "SENTETIK kuruluş"))))
        {
            await AssertProblemAsync(other, HttpStatusCode.Conflict, TaxMasterProblemCodesV1.IdempotencyConflict);
        }

        using (var taken = await client.SendAsync(Json(HttpMethod.Post, $"{Root}/categories", "k-cat-taken",
                   new CreateTaxCategoryRequestV1(TaxCategoryKindsV1.VatClass, "GENEL", "Yine", "SENTETIK"))))
        {
            await AssertProblemAsync(taken, HttpStatusCode.Conflict, TaxMasterProblemCodesV1.CategoryCodeTaken);
        }

        var steel = await run.CreateCategoryAsync(client, "k-cat-2", TaxCategoryKindsV1.WithholdingClass, "DEMIR-CELIK", "Demir-çelik");
        var taxpayer = await run.CreateCategoryAsync(client, "k-cat-3", TaxCategoryKindsV1.BuyerCategory, "MUKELLEF", "KDV mükellefi");
        var consumer = await run.CreateCategoryAsync(client, "k-cat-4", TaxCategoryKindsV1.BuyerCategory, "NIHAI", "Nihai tüketici");

        // A withholding record needs its code in the withholding code list in force on its date (fail-closed).
        using (var noList = await client.SendAsync(Json(HttpMethod.Post, $"{Root}/withholding-classes/{steel:D}/rates", "k-wh-nolist",
                   new RecordWithholdingRateRequestV1("1", taxpayer, true, "50", "T1", "SENTETIK"))))
        {
            await AssertProblemAsync(noList, HttpStatusCode.UnprocessableEntity, TaxMasterProblemCodesV1.WithholdingCodeUnknown);
        }

        // The code lists start at "0"; a stale version is 409.
        await run.CommandAsync(client, HttpMethod.Post, $"{Root}/code-lists/Withholding/versions", "k-list-1",
            new PublishTaxCodeListRequestV1("0", [new TaxCodeListEntryV1("t1", "SENTETIK tevkifat kodu")], "SENTETIK liste"),
            HttpStatusCode.NoContent);
        using (var stale = await client.SendAsync(Json(HttpMethod.Post, $"{Root}/code-lists/Withholding/versions", "k-list-stale",
                   new PublishTaxCodeListRequestV1("0", [new TaxCodeListEntryV1("T2", "SENTETIK")], "SENTETIK"))))
        {
            await AssertProblemAsync(stale, HttpStatusCode.Conflict, TaxMasterProblemCodesV1.VersionConflict);
        }

        // VAT: a record on a withholding class is a kind mismatch; the VAT class takes its rate.
        using (var mismatch = await client.SendAsync(Json(HttpMethod.Post, $"{Root}/vat-classes/{steel:D}/rates", "k-vat-mismatch",
                   new RecordVatRateRequestV1("1", "20", "SENTETIK"))))
        {
            await AssertProblemAsync(mismatch, HttpStatusCode.UnprocessableEntity, TaxMasterProblemCodesV1.CategoryKindMismatch);
        }

        await run.CommandAsync(client, HttpMethod.Post, $"{Root}/vat-classes/{general:D}/rates", "k-vat-1",
            new RecordVatRateRequestV1("1", "20", "SENTETIK oran"), HttpStatusCode.NoContent);
        await run.CommandAsync(client, HttpMethod.Post, $"{Root}/vat-classes/{general:D}/rates", "k-vat-1",
            new RecordVatRateRequestV1("1", "20", "SENTETIK oran"), HttpStatusCode.NoContent, replayed: true);
        // The same rate again changes nothing (no record, no version move, no audit).
        await run.CommandAsync(client, HttpMethod.Post, $"{Root}/vat-classes/{general:D}/rates", "k-vat-same",
            new RecordVatRateRequestV1("2", "20", "SENTETIK aynı"), HttpStatusCode.NoContent);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [settings].[TaxRateRecords] WHERE [ClassId] = @id", ("@id", general)));

        using (var past = await client.SendAsync(Json(HttpMethod.Post, $"{Root}/vat-classes/{general:D}/rates", "k-vat-past",
                   new RecordVatRateRequestV1("2", "18", "SENTETIK", today.AddDays(-1)))))
        {
            await AssertProblemAsync(past, HttpStatusCode.UnprocessableEntity, TaxMasterProblemCodesV1.EffectiveFromPast);
        }

        // Withholding: applies for the taxpayer, explicitly not applied for the consumer.
        await run.CommandAsync(client, HttpMethod.Post, $"{Root}/withholding-classes/{steel:D}/rates", "k-wh-1",
            new RecordWithholdingRateRequestV1("1", taxpayer, true, "50", "T1", "SENTETIK tevkifat"), HttpStatusCode.NoContent);
        await run.CommandAsync(client, HttpMethod.Post, $"{Root}/withholding-classes/{steel:D}/rates", "k-wh-2",
            new RecordWithholdingRateRequestV1("2", consumer, false, null, null, "SENTETIK yok"), HttpStatusCode.NoContent);

        // SAT-D84 precedent: a scheduled record, replaced by another on the same date (a correction), then withdrawn — the
        // replaced one never comes back.
        var scheduledDay = today.AddDays(30);
        await run.CommandAsync(client, HttpMethod.Post, $"{Root}/vat-classes/{general:D}/rates", "k-vat-sched-1",
            new RecordVatRateRequestV1("2", "25", "SENTETIK planlı", scheduledDay), HttpStatusCode.NoContent);
        await run.CommandAsync(client, HttpMethod.Post, $"{Root}/vat-classes/{general:D}/rates", "k-vat-sched-2",
            new RecordVatRateRequestV1("3", "26", "SENTETIK düzeltme", scheduledDay), HttpStatusCode.NoContent);
        var master = await run.JsonAsync(client, Root);
        var vatSeries = master.GetProperty("rateSeries").EnumerateArray()
            .Single(series => series.GetProperty("kind").GetString() == TaxRateKindsV1.Vat);
        Assert.Equal("20", vatSeries.GetProperty("inForce").GetProperty("ratePercent").GetString());
        var scheduled = Assert.Single(vatSeries.GetProperty("scheduled").EnumerateArray());
        Assert.Equal(("26", scheduledDay.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)), (scheduled.GetProperty("ratePercent").GetString(),
            scheduled.GetProperty("effectiveFrom").GetString()));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [settings].[TaxRateRecordWithdrawals] WHERE [ReplacedByRecordId] IS NOT NULL"));
        var scheduledId = scheduled.GetProperty("recordId").GetGuid();
        var inForceId = vatSeries.GetProperty("inForce").GetProperty("recordId").GetGuid();

        using (var inEffect = await client.SendAsync(Json(HttpMethod.Post, $"{Root}/rate-records/{inForceId:D}/withdrawal",
                   "k-withdraw-in-effect", new WithdrawTaxRateRecordRequestV1("4", "SENTETIK"))))
        {
            await AssertProblemAsync(inEffect, HttpStatusCode.Conflict, TaxMasterProblemCodesV1.RecordInEffect);
        }

        await run.CommandAsync(client, HttpMethod.Post, $"{Root}/rate-records/{scheduledId:D}/withdrawal", "k-withdraw-1",
            new WithdrawTaxRateRecordRequestV1("4", "SENTETIK geri çekme"), HttpStatusCode.NoContent);
        using (var again = await client.SendAsync(Json(HttpMethod.Post, $"{Root}/rate-records/{scheduledId:D}/withdrawal",
                   "k-withdraw-2", new WithdrawTaxRateRecordRequestV1("5", "SENTETIK"))))
        {
            await AssertProblemAsync(again, HttpStatusCode.Conflict, TaxMasterProblemCodesV1.RecordWithdrawn);
        }

        master = await run.JsonAsync(client, Root);
        vatSeries = master.GetProperty("rateSeries").EnumerateArray()
            .Single(series => series.GetProperty("kind").GetString() == TaxRateKindsV1.Vat);
        Assert.Empty(vatSeries.GetProperty("scheduled").EnumerateArray());
        Assert.Equal(4, master.GetProperty("categories").GetArrayLength());

        // Company isolation: B sees an empty master and A's record is B's code-less 404.
        var masterB = await run.JsonAsync(clientB, Root);
        Assert.Equal((CompanyB, 0), (masterB.GetProperty("companyId").GetGuid(), masterB.GetProperty("categories").GetArrayLength()));
        using (var foreign = await clientB.SendAsync(Json(HttpMethod.Post, $"{Root}/rate-records/{inForceId:D}/withdrawal",
                   "k-foreign", new WithdrawTaxRateRecordRequestV1("4", "SENTETIK"))))
        {
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        }

        // The line tax port over the real store (the product's classes come from a stub here; the Product owner's real
        // classification is the second fact).
        await using (var port = run.LineTaxPort(CompanyA, new StubProductClasses(general, steel)))
        {
            var resolution = await port.Value.ResolveAsync(Guid.NewGuid(), taxpayer, today);
            Assert.Equal((TaxResolutionStatesV1.Resolved, 20m, TaxResolutionStatesV1.Resolved, 50m, "T1"),
                (resolution.VatState, resolution.VatPercent, resolution.WithholdingState, resolution.WithholdingPercent,
                    resolution.WithholdingCode));
            var notApplied = await port.Value.ResolveAsync(Guid.NewGuid(), consumer, today);
            Assert.Equal(TaxResolutionStatesV1.NotApplied, notApplied.WithholdingState);
            var exemption = await port.Value.GetCodeListAsync(TaxCodeListKindsV1.Exemption, today);
            Assert.Equal((TaxResolutionStatesV1.Unresolved, TaxResolutionReasonsV1.CodeListMissing), (exemption.State, exemption.Reason));
        }

        await using (var portB = run.LineTaxPort(CompanyB, new StubProductClasses(general, steel)))
        {
            // A's class ids mean nothing in B: "kaynak yok", never A's rate.
            var resolution = await portB.Value.ResolveAsync(Guid.NewGuid(), taxpayer, today);
            Assert.Equal((TaxResolutionStatesV1.Unresolved, TaxResolutionReasonsV1.VatClassUnknown), (resolution.VatState, resolution.VatReason));
        }

        // One audit event per real change, the whole reason in the after-image; none for the no-op.
        Assert.Equal(4, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'settings.tax-category.created.v1' AND [CompanyId] = @c",
            ("@c", CompanyA)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'settings.tax-rate.corrected.v1'"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'settings.tax-rate.withdrawn.v1' "
            + "AND JSON_VALUE([AfterJson], '$.reason') = N'SENTETIK geri çekme' AND [PermissionCode] = N'settings.tax-master.manage'"));
        Assert.Equal(4, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'settings.tax-rate.recorded.v1'"));

        // The rollback is refused while tax master data exists (forward fix, 51131); nothing is dropped.
        await using var context = run.SettingsContext(CompanyA);
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        var migration = Array.IndexOf(applied, "20261031130138_AddTaxMaster");
        Assert.True(migration > 0);
        var rollback = await Assert.ThrowsAsync<SqlException>(() => context.GetService<IMigrator>().MigrateAsync(applied[migration - 1]));
        Assert.Equal(51131, rollback.Number);
        Assert.Equal(applied, await context.Database.GetAppliedMigrationsAsync());
    }

    [SqlServerFact]
    public async Task Product_tax_classification_is_checked_with_settings_and_feeds_the_line_tax_port_on_real_sql()
    {
        await using var run = await ProductRun.StartAsync();
        var today = run.Today;
        var productId = ProductSalesCandidateFixture.ProductId;

        // Settings data is entered through the Settings owner's own context (SENTETIK values).
        Guid general, steel, taxpayer, foreignVat;
        await using (var settings = run.Settings(CompanyA))
        {
            general = settings.Category(TaxCategoryKindsV1.VatClass, "GENEL");
            steel = settings.Category(TaxCategoryKindsV1.WithholdingClass, "DEMIR-CELIK");
            taxpayer = settings.Category(TaxCategoryKindsV1.BuyerCategory, "MUKELLEF");
            settings.Rate(TaxRateKindsV1.Vat, general, null, 1, today, 10m, null);
            settings.Rate(TaxRateKindsV1.Withholding, steel, taxpayer, 1, today, 50m, "T1");
            settings.List(TaxCodeListKindsV1.Withholding, today, "T1");
            await settings.SaveAsync();
        }

        await using (var settingsB = run.Settings(CompanyB))
        {
            foreignVat = settingsB.Category(TaxCategoryKindsV1.VatClass, "GENEL");
            await settingsB.SaveAsync();
        }

        await using (var scope = run.Request(CompanyA))
        {
            // Not classified yet: version "0" and the port answers "kaynak yok".
            var none = await scope.Service.GetAsync(productId);
            Assert.Equal(("0", (Guid?)null), (none.Version, none.VatClassId));
            var unclassified = await scope.LineTaxPort.ResolveAsync(productId, taxpayer, today);
            Assert.Equal((TaxResolutionStatesV1.Unresolved, TaxResolutionReasonsV1.ProductNotClassified),
                (unclassified.VatState, unclassified.VatReason));
            var unknownProduct = await scope.LineTaxPort.ResolveAsync(Guid.NewGuid(), taxpayer, today);
            Assert.Equal(TaxResolutionReasonsV1.ProductNotFound, unknownProduct.VatReason);

            // Wrong kinds and another company's class are refused (422); nothing is stored.
            Assert.Equal((422, ProductTaxClassificationProblemCodesV1.VatClassInvalid),
                Status(await scope.Service.SetAsync(productId, new("0", steel, null, "SENTETIK"), Metadata("k-wrong-kind"))));
            Assert.Equal((422, ProductTaxClassificationProblemCodesV1.VatClassInvalid),
                Status(await scope.Service.SetAsync(productId, new("0", foreignVat, null, "SENTETIK"), Metadata("k-foreign"))));
            Assert.Equal((422, ProductTaxClassificationProblemCodesV1.WithholdingClassInvalid),
                Status(await scope.Service.SetAsync(productId, new("0", general, taxpayer, "SENTETIK"), Metadata("k-wrong-wh"))));
            Assert.Equal((400, ProductTaxClassificationProblemCodesV1.ReasonRequired),
                Status(await scope.Service.SetAsync(productId, new("0", general, null, " "), Metadata("k-no-reason"))));

            var set = await scope.Service.SetAsync(productId, new("0", general, steel, "SENTETIK sınıf"), Metadata("k-set-1"));
            Assert.Equal((204, false), (set.Answer.StatusCode, set.IsReplay));
            var replay = await scope.Service.SetAsync(productId, new("0", general, steel, "SENTETIK sınıf"), Metadata("k-set-1"));
            Assert.Equal((204, true), (replay.Answer.StatusCode, replay.IsReplay));
            Assert.Equal((409, ProductTaxClassificationProblemCodesV1.IdempotencyConflict),
                Status(await scope.Service.SetAsync(productId, new("0", general, null, "SENTETIK sınıf"), Metadata("k-set-1"))));
            Assert.Equal((409, ProductTaxClassificationProblemCodesV1.VersionConflict),
                Status(await scope.Service.SetAsync(productId, new("0", general, null, "SENTETIK"), Metadata("k-stale"))));
            var unknown = await scope.Service.SetAsync(Guid.NewGuid(), new("0", general, null, "SENTETIK"), Metadata("k-unknown"));
            Assert.Equal((404, (string?)null), (unknown.Answer.StatusCode, unknown.Answer.Code));

            var view = await scope.Service.GetAsync(productId);
            Assert.Equal(("1", (Guid?)general, (Guid?)steel), (view.Version, view.VatClassId, view.WithholdingClassId));

            // End to end: Product classification → host adapter → Settings line tax port.
            var resolved = await scope.LineTaxPort.ResolveAsync(productId, taxpayer, today);
            Assert.Equal((TaxResolutionStatesV1.Resolved, 10m, TaxResolutionStatesV1.Resolved, 50m, "T1"),
                (resolved.VatState, resolved.VatPercent, resolved.WithholdingState, resolved.WithholdingPercent, resolved.WithholdingCode));
            Assert.Equal(CompanyA, resolved.CompanyId);
            var noBuyer = await scope.LineTaxPort.ResolveAsync(productId, null, today);
            Assert.Equal((TaxResolutionStatesV1.Resolved, TaxResolutionReasonsV1.BuyerCategoryMissing),
                (noBuyer.VatState, noBuyer.WithholdingReason));
        }

        await using (var scopeB = run.Request(CompanyB))
        {
            // Company B never sees A's classification.
            Assert.Equal("0", (await scopeB.Service.GetAsync(productId)).Version);
            Assert.Equal(TaxResolutionReasonsV1.ProductNotClassified,
                (await scopeB.LineTaxPort.ResolveAsync(productId, null, today)).VatReason);
        }

        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'product.tax-classification.changed.v1' "
            + "AND JSON_VALUE([AfterJson], '$.reason') = N'SENTETIK sınıf' AND [CompanyId] = @c", ("@c", CompanyA)));

        // The rollback is refused while a classification exists (forward fix, 51132).
        await using var scopeRollback = run.Request(CompanyA);
        var context = scopeRollback.Context;
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        var migration = Array.IndexOf(applied, "20261031130728_AddProductTaxClassifications");
        Assert.True(migration > 0);
        var rollback = await Assert.ThrowsAsync<SqlException>(() => context.GetService<IMigrator>().MigrateAsync(applied[migration - 1]));
        Assert.Equal(51132, rollback.Number);
        Assert.Equal(applied, await context.Database.GetAppliedMigrationsAsync());
    }

    private static (int, string?) Status(ProductTaxClassificationResultV1 result) => (result.Answer.StatusCode, result.Answer.Code);

    private static ProductTaxClassificationCommandMetadataV1 Metadata(string key) =>
        new(CompanyA, ProductRun.ActorId.ToString("D"), $"corr-{key}", null, "Web", null, null, key);

    private static HttpRequestMessage Json(HttpMethod method, string path, string key, object body)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonSerializerOptions.Web), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.Add("X-Correlation-Id", $"tax-master-{key}");
        return request;
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(status == response.StatusCode, $"{response.StatusCode}: {body}");
        Assert.Equal(code, body.GetProperty("code").GetString());
    }

    private static DateOnly IstanbulToday() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,
        TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul")).DateTime);

    private static async Task DropDatabaseAsync(string baseConnectionString, string databaseName)
    {
        SqlConnection.ClearAllPools();
        await using var connection = new SqlConnection(baseConnectionString);
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

    private static async Task<T> ScalarAsync<T>(string connectionString, string sql, (string Name, object Value)[] parameters)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return (T)(await command.ExecuteScalarAsync())!;
    }

    private static SettingsDbContext NewSettingsContext(string connectionString, Guid company, out CompanyContext companyContext)
    {
        companyContext = new CompanyContext();
        companyContext.Set(company);
        return new SettingsDbContext(new DbContextOptionsBuilder<SettingsDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", SettingsDbContext.Schema))
            .Options, companyContext);
    }

    private sealed class StubProductClasses(Guid vatClass, Guid? withholdingClass) : ISettingsProductTaxClassSourceV1
    {
        public Task<SettingsProductTaxClassAnswerV1> GetAsync(Guid companyId, Guid productId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SettingsProductTaxClassAnswerV1(SettingsProductTaxClassOutcomeV1.Found, vatClass, withholdingClass));
    }

    private sealed class FixedActor(string userId, params Guid[] companies) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId { get; } = userId;
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>(companies);
        public bool CanAccessCompany(Guid candidate) => AllowedCompanyIds.Contains(candidate);
    }

    private sealed class Granted : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PermissionScope(true, true, new HashSet<string> { "SENTETIK-TAX" }, new HashSet<Guid>(),
                new HashSet<Guid>(), new HashSet<Guid>()));
    }

    private sealed class PortScope<T>(IAsyncDisposable owner, T value) : IAsyncDisposable
    {
        public T Value { get; } = value;

        public ValueTask DisposeAsync() => owner.DisposeAsync();
    }

    /// <summary>The REAL host over a fresh database, with Identity grants for the HTTP routes.</summary>
    private sealed class HostRun : IAsyncDisposable
    {
        private readonly string baseConnectionString;

        private HostRun(string baseConnectionString, SqlServerRelationalApiFactory factory)
        {
            this.baseConnectionString = baseConnectionString;
            Factory = factory;
        }

        public SqlServerRelationalApiFactory Factory { get; }

        public DateOnly Today { get; } = IstanbulToday();

        public static async Task<HostRun> StartAsync()
        {
            var baseConnectionString = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var run = new HostRun(baseConnectionString, new SqlServerRelationalApiFactory(baseConnectionString));
            using var client = run.Factory.CreateClient();
            using var health = await client.GetAsync("/health/live");
            health.EnsureSuccessStatusCode();
            return run;
        }

        public HttpClient Client(string subject, Guid company)
        {
            var client = Factory.CreateClientFor(subject, company);
            client.DefaultRequestHeaders.Remove("Idempotency-Key");
            client.DefaultRequestHeaders.Remove("X-Correlation-Id");
            return client;
        }

        public async Task<Guid> CreateCategoryAsync(HttpClient client, string key, string kind, string code, string name)
        {
            using var response = await client.SendAsync(Json(HttpMethod.Post, $"{Root}/categories", key,
                new CreateTaxCategoryRequestV1(kind, code, name, "SENTETIK kuruluş")));
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(response.StatusCode == HttpStatusCode.Created, $"{response.StatusCode}: {body}");
            Assert.Equal("1", body.GetProperty("version").GetString());
            return body.GetProperty("id").GetGuid();
        }

        public async Task CommandAsync(HttpClient client, HttpMethod method, string path, string key, object body,
            HttpStatusCode expected, bool replayed = false)
        {
            using var response = await client.SendAsync(Json(method, path, key, body));
            var text = await response.Content.ReadAsStringAsync();
            Assert.True(expected == response.StatusCode, $"{response.StatusCode}: {text}");
            Assert.Equal(replayed, response.Headers.Contains("Idempotency-Replayed"));
        }

        public async Task<JsonElement> JsonAsync(HttpClient client, string path)
        {
            using var response = await client.GetAsync(path);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.StatusCode}: {body}");
            return body;
        }

        public SettingsDbContext SettingsContext(Guid company) => NewSettingsContext(Factory.DatabaseConnectionString, company, out _);

        public PortScope<ISettingsLineTaxResolverV1> LineTaxPort(Guid company, ISettingsProductTaxClassSourceV1 classes)
        {
            var database = NewSettingsContext(Factory.DatabaseConnectionString, company, out var companyContext);
            return new PortScope<ISettingsLineTaxResolverV1>(database,
                new SettingsTaxMasterReader(database, companyContext, new FixedActor("SENTETIK-sales-actor", company), classes));
        }

        public async Task<(string Subject, string ActorId)> GrantAsync(string developmentUser, Guid company, params string[] permissions)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var link = await dbContext.ExternalIdentities.SingleOrDefaultAsync(
                x => x.Issuer == BootstrapIdentityOptions.Issuer && x.Subject == developmentUser);
            Guid actor;
            if (link is null)
            {
                actor = Guid.NewGuid();
                dbContext.Users.Add(new UserAccount(actor, "SENTETIK vergi düzenleyici", null));
                dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer,
                    developmentUser));
            }
            else
            {
                actor = link.UserId;
            }

            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(roleId, $"MOD-03-{roleId:N}", "SENTETIK vergi rolü", true);
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
            return (developmentUser, actor.ToString("D"));
        }

        public Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters) =>
            SettingsTaxMasterSqlServerRelationalAcceptanceTests.ScalarAsync<T>(Factory.DatabaseConnectionString, sql, parameters);

        public async ValueTask DisposeAsync()
        {
            await Factory.DisposeAsync();
            await DropDatabaseAsync(baseConnectionString, Factory.DatabaseName);
        }
    }

    /// <summary>The host with the Product runtime and price-list context enabled over a fresh database (the AT-018 harness).</summary>
    private sealed class ProductRun(SqlServerRelationalApiFactory factory, string baseConnectionString, string databaseName)
        : IAsyncDisposable
    {
        public static readonly Guid ActorId = Guid.Parse("7c000000-0000-4000-8000-0000000000a1");

        public DateOnly Today { get; } = IstanbulToday();

        public static async Task<ProductRun> StartAsync()
        {
            var baseConnection = Environment.GetEnvironmentVariable(SqlServerRelationalAcceptanceTests.ConnectionStringEnvironmentVariable)!;
            var databaseName = "goymen_erp_ci_" + Guid.NewGuid().ToString("N");
            var connection = new SqlConnectionStringBuilder(baseConnection) { InitialCatalog = databaseName }.ConnectionString;
            var factory = new SqlServerRelationalApiFactory(baseConnection, services =>
            {
                services.RemoveAll<ICurrentUser>();
                services.AddSingleton<ICurrentUser>(new FixedActor(ActorId.ToString("D"), CompanyA, CompanyB));
                services.RemoveAll<ProductRuntimeOptions>();
                services.AddSingleton(new ProductRuntimeOptions(connection, new ProductPilotIssuerBinding(ActorId, Guid.NewGuid()), new byte[32]));
                services.AddProductTechnicalCardFixtureStore();
                services.AddProductSalesCandidateFixtureStore();
                services.AddProductPriceLists(new ConfigurationBuilder()
                        .AddInMemoryCollection(new Dictionary<string, string?> { ["DB_CONNECTION_STRING"] = connection }).Build(),
                    (provider, options) => options.AddInterceptors(provider.GetRequiredService<AuditSaveChangesInterceptor>()));
            }, databaseName: databaseName);
            var run = new ProductRun(factory, baseConnection, databaseName);
            try
            {
                using var client = factory.CreateClient();
                using var health = await client.GetAsync("/health/live");
                health.EnsureSuccessStatusCode();
                await using (var setup = factory.Services.CreateAsyncScope())
                {
                    setup.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
                    await setup.ServiceProvider.GetRequiredService<IProductTechnicalCardFixtureStore>().EnsureAsync(default);
                }

                await using (var setup = factory.Services.CreateAsyncScope())
                {
                    setup.ServiceProvider.GetRequiredService<ICompanyContext>().Set(CompanyA);
                    await setup.ServiceProvider.GetRequiredService<IProductSalesCandidateFixtureStore>().EnsureAsync(default);
                }

                await factory.Services.InitializeProductPriceListsAsync();
                return run;
            }
            catch
            {
                await run.DisposeAsync();
                throw;
            }
        }

        public SettingsSetup Settings(Guid company) =>
            new(NewSettingsContext(factory.DatabaseConnectionString, company, out _), company);

        public RequestScope Request(Guid company)
        {
            var scope = factory.Services.CreateAsyncScope();
            var provider = scope.ServiceProvider;
            var companyContext = provider.GetRequiredService<ICompanyContext>();
            companyContext.Set(company);
            var actor = new FixedActor(ActorId.ToString("D"), CompanyA, CompanyB);
            var settings = NewSettingsContext(factory.DatabaseConnectionString, company, out var settingsCompany);
            var categories = new SettingsTaxMasterReader(settings, settingsCompany, actor);
            var context = provider.GetRequiredService<ProductPriceListDbContext>();
            var service = new ProductTaxClassificationService(context, companyContext, actor, new Granted(),
                provider.GetRequiredService<IAuditEventCollector>(), TimeProvider.System, categories);
            var port = new SettingsTaxMasterReader(settings, settingsCompany, actor,
                new SettingsProductTaxClassHost(service, companyContext));
            return new RequestScope(scope, settings, service, port, context);
        }

        public Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters) =>
            SettingsTaxMasterSqlServerRelationalAcceptanceTests.ScalarAsync<T>(factory.DatabaseConnectionString, sql, parameters);

        public async ValueTask DisposeAsync()
        {
            await factory.DisposeAsync();
            await DropDatabaseAsync(baseConnectionString, databaseName);
        }
    }

    private sealed class RequestScope(AsyncServiceScope scope, SettingsDbContext settings, ProductTaxClassificationService service,
        ISettingsLineTaxResolverV1 lineTaxPort, ProductPriceListDbContext context) : IAsyncDisposable
    {
        public IProductTaxClassificationServiceV1 Service => service;

        public ISettingsLineTaxResolverV1 LineTaxPort => lineTaxPort;

        public ProductPriceListDbContext Context => context;

        public async ValueTask DisposeAsync()
        {
            await settings.DisposeAsync();
            await scope.DisposeAsync();
        }
    }

    /// <summary>Settings rows entered through the Settings owner's context (the test's setup, SENTETIK values).</summary>
    private sealed class SettingsSetup(SettingsDbContext database, Guid company) : IAsyncDisposable
    {
        private const string Actor = "SENTETIK-setup";

        public Guid Category(string kind, string code)
        {
            var id = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            database.TaxCategories.Add(new TaxCategoryRow
            {
                CompanyId = company, Id = id, Kind = kind, Code = code, Name = $"SENTETIK {code}", Version = 1, RecordVersion = 1,
                CreatedAtUtc = now, CreatedBy = Actor, UpdatedAtUtc = now, UpdatedBy = Actor,
            });
            return id;
        }

        public void Rate(string kind, Guid classId, Guid? buyer, long version, DateOnly from, decimal rate, string? code) =>
            database.TaxRateRecords.Add(new TaxRateRecordRow
            {
                CompanyId = company, Id = Guid.NewGuid(), Kind = kind, ClassId = classId, BuyerCategoryId = buyer,
                RecordVersion = version, EffectiveFrom = from, Applies = true, RatePercent = rate, WithholdingCode = code,
                RecordedAtUtc = DateTimeOffset.UtcNow, RecordedBy = Actor, Reason = "SENTETIK",
            });

        public void List(string kind, DateOnly from, string code)
        {
            database.TaxCodeLists.Add(new TaxCodeListRow
            {
                CompanyId = company, Kind = kind, Version = 1, LastListVersion = 1, UpdatedAtUtc = DateTimeOffset.UtcNow, UpdatedBy = Actor,
            });
            database.TaxCodeListVersions.Add(new TaxCodeListVersionRow
            {
                CompanyId = company, Kind = kind, ListVersion = 1, EffectiveFrom = from, RecordedAtUtc = DateTimeOffset.UtcNow,
                RecordedBy = Actor, Reason = "SENTETIK",
            });
            database.TaxCodeListEntries.Add(new TaxCodeListEntryRow
            {
                CompanyId = company, Kind = kind, ListVersion = 1, Code = code, Name = "SENTETIK",
            });
        }

        public Task SaveAsync() => database.SaveChangesAsync();

        public ValueTask DisposeAsync() => database.DisposeAsync();
    }
}
