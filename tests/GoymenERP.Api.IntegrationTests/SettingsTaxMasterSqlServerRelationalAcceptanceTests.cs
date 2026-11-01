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
using GoymenERP.Modules.Settings.Application;
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
/// MOD-03 (EK12 SAT-D83; EK13 SAT-D93; EK19 SAT-D133…D140) against a REAL SQL Server: the Settings tax master through the REAL
/// host (routes, authorization, migrations, audit) — 403 without the name, the reviewed SAT-D139 default per item type, a
/// Settings record over it (same-date replacement, withdrawal without resurrection, record in force immutable), the UBL-TR code
/// lists, the VUK 232 threshold (empty = "kaynak yok"), the buyer-situation rule, company isolation, replay/conflict, audit and
/// the rollback refusal (51131); the SAT-D158 buyer situation list (51153) and the SAT-D147 zero-rated buyer; and the Product
/// owner's SHARED item type (SAT-D149) feeding the Settings line tax port end to end, with the forward-fix move (51151/51152).
/// Every grant is a fresh synthetic role; codes and amounts are SENTETIK.
/// </summary>
public sealed class SettingsTaxMasterSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private const string Permission = SettingsTaxMasterPermissionManifestV1.TaxMasterManage;
    private const string Root = SettingsTaxMasterEndpoints.TaxMasterPath;
    private const string TaxMasterMigration = "20261031194631_AddTaxMaster";
    private const string ProductItemTypeMigration = "20261031195253_AddProductItemTypes";
    private const string ProductCardItemTypeMigration = "20261101144030_MoveProductItemTypesToProductCard";
    private const string BuyerSituationMigration = "20261101143556_AddTaxBuyerSituationLists";

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
        var steelPath = $"{Root}/item-type-defaults/{TaxItemTypesV1.Steel}";

        // Without the name: 403 on the read and on a command; nothing is written.
        using (var forbidden = await viewer.GetAsync(Root))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        using (var forbidden = await viewer.SendAsync(Json(HttpMethod.Post, steelPath, "k-forbidden",
                   new RecordItemTypeDefaultRequestV1("0", true, "20", null, false, null, null, "SENTETIK"))))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [settings].[TaxItemTypeDefaultRecords]"));

        // SAT-D139: the reviewed catalog answers every item type (version "0"); no code list and no threshold (kaynak yok).
        var initial = await run.JsonAsync(client, Root);
        Assert.Equal(CompanyA, initial.GetProperty("companyId").GetGuid());
        var initialDefaults = initial.GetProperty("itemTypeDefaults").EnumerateArray()
            .ToDictionary(series => series.GetProperty("itemType").GetString()!);
        Assert.Equal(("0", "Catalog", "20", "50", JsonValueKind.Null), (initialDefaults["Steel"].GetProperty("version").GetString(),
            initialDefaults["Steel"].GetProperty("inForce").GetProperty("source").GetString(),
            initialDefaults["Steel"].GetProperty("inForce").GetProperty("vatPercent").GetString(),
            initialDefaults["Steel"].GetProperty("inForce").GetProperty("withholdingPercent").GetString(),
            initialDefaults["Steel"].GetProperty("inForce").GetProperty("withholdingCode").ValueKind));
        Assert.False(initialDefaults["Scrap"].GetProperty("inForce").GetProperty("vatApplies").GetBoolean());
        Assert.Equal(("0", JsonValueKind.Null), (initial.GetProperty("withholdingThreshold").GetProperty("version").GetString(),
            initial.GetProperty("withholdingThreshold").GetProperty("inForce").ValueKind));
        Assert.All(initial.GetProperty("codeLists").EnumerateArray(), list =>
            Assert.Equal(JsonValueKind.Null, list.GetProperty("inForce").ValueKind));

        await using (var port = run.LineTaxPort(CompanyA))
        {
            var scrap = await port.Value.GetItemTypeDefaultAsync(TaxItemTypesV1.Scrap, TaxBuyerSituationsV1.Normal, today);
            Assert.Equal((TaxResolutionStatesV1.Exempt, TaxResolutionStatesV1.Unresolved, TaxResolutionReasonsV1.ExemptionCodeMissing),
                (scrap.VatState, scrap.ExemptionCodeState, scrap.ExemptionCodeReason));
            var steel = await port.Value.GetItemTypeDefaultAsync(TaxItemTypesV1.Steel, TaxBuyerSituationsV1.Normal, today);
            Assert.Equal((20m, 50m, TaxResolutionStatesV1.Unresolved, TaxDefaultSourcesV1.Catalog),
                (steel.VatPercent, steel.WithholdingPercent, steel.WithholdingCodeState, steel.Source));
            var finalConsumer = await port.Value.GetItemTypeDefaultAsync(TaxItemTypesV1.Steel, TaxBuyerSituationsV1.FinalConsumer, today);
            Assert.Equal((TaxResolutionStatesV1.NotApplied, TaxResolutionReasonsV1.BuyerNotSubjectToWithholding),
                (finalConsumer.WithholdingState, finalConsumer.BuyerAdjustment));
        }

        // A reason is required; a code must be in the list in force on the date (fail-closed).
        using (var noReason = await client.SendAsync(Json(HttpMethod.Post, steelPath, "k-noreason",
                   new RecordItemTypeDefaultRequestV1("0", true, "20", null, true, "50", null, " "))))
        {
            await AssertProblemAsync(noReason, HttpStatusCode.BadRequest, TaxMasterProblemCodesV1.ReasonRequired);
        }

        using (var noList = await client.SendAsync(Json(HttpMethod.Post, steelPath, "k-nolist",
                   new RecordItemTypeDefaultRequestV1("0", true, "20", null, true, "50", "T1", "SENTETIK"))))
        {
            await AssertProblemAsync(noList, HttpStatusCode.UnprocessableEntity, TaxMasterProblemCodesV1.WithholdingCodeUnknown);
        }

        await run.CommandAsync(client, HttpMethod.Post, $"{Root}/code-lists/Withholding/versions", "k-list-1",
            new PublishTaxCodeListRequestV1("0", [new TaxCodeListEntryV1("t1", "SENTETIK tevkifat kodu")], "SENTETIK liste"),
            HttpStatusCode.NoContent);
        using (var stale = await client.SendAsync(Json(HttpMethod.Post, $"{Root}/code-lists/Withholding/versions", "k-list-stale",
                   new PublishTaxCodeListRequestV1("0", [new TaxCodeListEntryV1("T2", "SENTETIK")], "SENTETIK"))))
        {
            await AssertProblemAsync(stale, HttpStatusCode.Conflict, TaxMasterProblemCodesV1.VersionConflict);
        }

        // The catalog's own values again are a no-op (no record); naming the listed code is a real change.
        await run.CommandAsync(client, HttpMethod.Post, steelPath, "k-same",
            new RecordItemTypeDefaultRequestV1("0", true, "20", null, true, "50", null, "SENTETIK aynı"), HttpStatusCode.NoContent);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [settings].[TaxItemTypeDefaultRecords]"));
        var steelBody = new RecordItemTypeDefaultRequestV1("0", true, "20", null, true, "50", "T1", "SENTETIK kod");
        await run.CommandAsync(client, HttpMethod.Post, steelPath, "k-steel-1", steelBody, HttpStatusCode.NoContent);
        await run.CommandAsync(client, HttpMethod.Post, steelPath, "k-steel-1", steelBody, HttpStatusCode.NoContent, replayed: true);
        using (var other = await client.SendAsync(Json(HttpMethod.Post, steelPath, "k-steel-1", steelBody with { VatPercent = "18" })))
        {
            await AssertProblemAsync(other, HttpStatusCode.Conflict, TaxMasterProblemCodesV1.IdempotencyConflict);
        }

        using (var past = await client.SendAsync(Json(HttpMethod.Post, steelPath, "k-past",
                   steelBody with { ExpectedVersion = "1", VatPercent = "18", EffectiveFrom = today.AddDays(-1) })))
        {
            await AssertProblemAsync(past, HttpStatusCode.UnprocessableEntity, TaxMasterProblemCodesV1.EffectiveFromPast);
        }

        using (var shape = await client.SendAsync(Json(HttpMethod.Post, steelPath, "k-shape",
                   new RecordItemTypeDefaultRequestV1("1", false, null, null, true, "50", null, "SENTETIK"))))
        {
            await AssertProblemAsync(shape, HttpStatusCode.BadRequest, TaxMasterProblemCodesV1.RequestInvalid);
        }

        // Review B P2-1: "KDV uygulanır, %0" (with or without withholding) is refused by the command and by the CHECK, so a
        // zero VAT can only be entered as "KDV yok" behind the exemption code gate.
        foreach (var (key, zero) in new[]
                 {
                     ("k-zero-1", new RecordItemTypeDefaultRequestV1("0", true, "0", null, false, null, null, "SENTETIK")),
                     ("k-zero-2", new RecordItemTypeDefaultRequestV1("0", true, "0.0000", null, true, "50", "T1", "SENTETIK")),
                 })
        {
            using var refused = await client.SendAsync(Json(HttpMethod.Post, $"{Root}/item-type-defaults/Scrap", key, zero));
            await AssertProblemAsync(refused, HttpStatusCode.BadRequest, TaxMasterProblemCodesV1.RequestInvalid);
        }

        var zeroCheck = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "UPDATE [settings].[TaxItemTypeDefaultRecords] SET [VatPercent] = 0 WHERE [VatApplies] = 1; SELECT 1"));
        Assert.Equal(547, zeroCheck.Number);
        Assert.Contains("CK_SettingsTaxItemTypeDefaultRecords_Vat", zeroCheck.Message, StringComparison.Ordinal);

        // A scheduled record, replaced by another on the same date (a correction), then withdrawn — the replaced one never
        // comes back.
        var scheduledDay = today.AddDays(30);
        await run.CommandAsync(client, HttpMethod.Post, steelPath, "k-sched-1",
            steelBody with { ExpectedVersion = "1", VatPercent = "25", Reason = "SENTETIK planlı", EffectiveFrom = scheduledDay },
            HttpStatusCode.NoContent);
        await run.CommandAsync(client, HttpMethod.Post, steelPath, "k-sched-2",
            steelBody with { ExpectedVersion = "2", VatPercent = "26", Reason = "SENTETIK düzeltme", EffectiveFrom = scheduledDay },
            HttpStatusCode.NoContent);
        var steelSeries = (await run.JsonAsync(client, Root)).GetProperty("itemTypeDefaults").EnumerateArray()
            .Single(series => series.GetProperty("itemType").GetString() == TaxItemTypesV1.Steel);
        Assert.Equal(("Record", "T1", "3"), (steelSeries.GetProperty("inForce").GetProperty("source").GetString(),
            steelSeries.GetProperty("inForce").GetProperty("withholdingCode").GetString(), steelSeries.GetProperty("version").GetString()));
        var scheduled = Assert.Single(steelSeries.GetProperty("scheduled").EnumerateArray());
        Assert.Equal("26", scheduled.GetProperty("vatPercent").GetString());
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [settings].[TaxItemTypeDefaultWithdrawals] WHERE [ReplacedByRecordId] IS NOT NULL"));
        var scheduledId = scheduled.GetProperty("recordId").GetGuid();
        var inForceId = steelSeries.GetProperty("inForce").GetProperty("recordId").GetGuid();

        using (var inEffect = await client.SendAsync(Json(HttpMethod.Post, $"{Root}/item-type-defaults/records/{inForceId:D}/withdrawal",
                   "k-withdraw-in-effect", new WithdrawTaxRecordRequestV1("3", "SENTETIK"))))
        {
            await AssertProblemAsync(inEffect, HttpStatusCode.Conflict, TaxMasterProblemCodesV1.RecordInEffect);
        }

        var catalogSteel = ApprovedTaxItemTypeDefaultCatalog.For(ApprovedUnitProfileCatalog.GdcCompanyId)
            .Single(entry => entry.ItemType == TaxItemTypesV1.Steel).RecordId;
        using (var catalog = await client.SendAsync(Json(HttpMethod.Post, $"{Root}/item-type-defaults/records/{catalogSteel:D}/withdrawal",
                   "k-withdraw-catalog", new WithdrawTaxRecordRequestV1("3", "SENTETIK"))))
        {
            Assert.Equal(HttpStatusCode.NotFound, catalog.StatusCode);
        }

        await run.CommandAsync(client, HttpMethod.Post, $"{Root}/item-type-defaults/records/{scheduledId:D}/withdrawal", "k-withdraw-1",
            new WithdrawTaxRecordRequestV1("3", "SENTETIK geri çekme"), HttpStatusCode.NoContent);
        using (var again = await client.SendAsync(Json(HttpMethod.Post, $"{Root}/item-type-defaults/records/{scheduledId:D}/withdrawal",
                   "k-withdraw-2", new WithdrawTaxRecordRequestV1("4", "SENTETIK"))))
        {
            await AssertProblemAsync(again, HttpStatusCode.Conflict, TaxMasterProblemCodesV1.RecordWithdrawn);
        }

        steelSeries = (await run.JsonAsync(client, Root)).GetProperty("itemTypeDefaults").EnumerateArray()
            .Single(series => series.GetProperty("itemType").GetString() == TaxItemTypesV1.Steel);
        Assert.Empty(steelSeries.GetProperty("scheduled").EnumerateArray());

        await using (var port = run.LineTaxPort(CompanyA))
        {
            var steel = await port.Value.GetItemTypeDefaultAsync(TaxItemTypesV1.Steel, TaxBuyerSituationsV1.Normal, scheduledDay);
            Assert.Equal((20m, TaxResolutionStatesV1.Resolved, "T1", TaxDefaultSourcesV1.Record, inForceId),
                (steel.VatPercent, steel.WithholdingCodeState, steel.WithholdingCode, steel.Source, steel.RecordId));
            // SAT-D147: an export-registered buyer is KDV %0 without withholding; no exemption list yet, so the code waits.
            var export = await port.Value.GetItemTypeDefaultAsync(TaxItemTypesV1.Steel, TaxBuyerSituationsV1.ExportRegistered, today,
                exemptionCode: "E701");
            Assert.Equal((TaxResolutionStatesV1.ZeroRated, 0m, TaxResolutionStatesV1.NotApplied, TaxResolutionStatesV1.Unresolved,
                    TaxResolutionReasonsV1.CodeListMissing, true),
                (export.VatState, export.VatPercent, export.WithholdingState, export.ExemptionCodeState, export.ExemptionCodeReason,
                    export.BuyerDocumentRequired));
        }

        // SAT-D135: the VUK 232 withholding threshold starts empty and is a dated record (SENTETIK amounts).
        await using (var port = run.LineTaxPort(CompanyA))
        {
            var none = await port.Value.GetWithholdingThresholdAsync(today);
            Assert.Equal((TaxResolutionStatesV1.Unresolved, TaxResolutionReasonsV1.WithholdingThresholdMissing), (none.State, none.Reason));
        }

        await run.CommandAsync(client, HttpMethod.Post, $"{Root}/withholding-threshold", "k-th-1",
            new RecordWithholdingThresholdRequestV1("0", "1000.50", "try", "SENTETIK sınır"), HttpStatusCode.NoContent);
        await run.CommandAsync(client, HttpMethod.Post, $"{Root}/withholding-threshold", "k-th-2",
            new RecordWithholdingThresholdRequestV1("1", "2000", "TRY", "SENTETIK planlı sınır", scheduledDay), HttpStatusCode.NoContent);
        var thresholdView = (await run.JsonAsync(client, Root)).GetProperty("withholdingThreshold");
        Assert.Equal(("2", "1000.5", "TRY"), (thresholdView.GetProperty("version").GetString(),
            thresholdView.GetProperty("inForce").GetProperty("amount").GetString(),
            thresholdView.GetProperty("inForce").GetProperty("currencyCode").GetString()));
        var scheduledThreshold = Assert.Single(thresholdView.GetProperty("scheduled").EnumerateArray());
        await run.CommandAsync(client, HttpMethod.Post,
            $"{Root}/withholding-threshold/records/{scheduledThreshold.GetProperty("recordId").GetGuid():D}/withdrawal", "k-th-withdraw",
            new WithdrawTaxRecordRequestV1("2", "SENTETIK sınır geri"), HttpStatusCode.NoContent);
        await using (var port = run.LineTaxPort(CompanyA))
        {
            var later = await port.Value.GetWithholdingThresholdAsync(scheduledDay);
            Assert.Equal((TaxResolutionStatesV1.Resolved, 1000.5m, "TRY"), (later.State, later.Amount, later.CurrencyCode));
        }

        // Company isolation: B sees its own catalog and none of A's records; A's record is B's code-less 404.
        var masterB = await run.JsonAsync(clientB, Root);
        Assert.Equal(CompanyB, masterB.GetProperty("companyId").GetGuid());
        Assert.All(masterB.GetProperty("itemTypeDefaults").EnumerateArray(), series =>
            Assert.Equal(("0", "Catalog"), (series.GetProperty("version").GetString(),
                series.GetProperty("inForce").GetProperty("source").GetString())));
        Assert.Equal(JsonValueKind.Null, masterB.GetProperty("withholdingThreshold").GetProperty("inForce").ValueKind);
        using (var foreign = await clientB.SendAsync(Json(HttpMethod.Post, $"{Root}/item-type-defaults/records/{inForceId:D}/withdrawal",
                   "k-foreign", new WithdrawTaxRecordRequestV1("4", "SENTETIK"))))
        {
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        }

        // One audit event per real change with the whole reason; none for the no-op.
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'settings.tax-item-type-default.recorded.v1' AND [CompanyId] = @c",
            ("@c", CompanyA)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'settings.tax-item-type-default.corrected.v1'"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'settings.tax-item-type-default.withdrawn.v1' "
            + "AND JSON_VALUE([AfterJson], '$.reason') = N'SENTETIK geri çekme' AND [PermissionCode] = N'settings.tax-master.manage'"));

        // The rollback is refused while tax master data exists (forward fix, 51131); nothing is dropped.
        await using var context = run.SettingsContext(CompanyA);
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        var migration = Array.IndexOf(applied, TaxMasterMigration);
        Assert.True(migration > 0);
        var rollback = await Assert.ThrowsAsync<SqlException>(() => context.GetService<IMigrator>().MigrateAsync(applied[migration - 1]));
        Assert.Equal(51131, rollback.Number);
        // EF reverts the later migrations first, each in its own transaction: the empty SAT-D158 buyer situation store
        // (AddTaxBuyerSituationLists) rolls back freely; the refused migration and everything before it stay applied.
        Assert.Equal(applied.Take(migration + 1), await context.Database.GetAppliedMigrationsAsync());
    }

    [SqlServerFact]
    public async Task Product_item_type_feeds_the_line_tax_port_with_the_item_type_default_on_real_sql()
    {
        await using var run = await ProductRun.StartAsync();
        var today = run.Today;
        var productId = ProductSalesCandidateFixture.ProductId;

        await using (var scope = run.Request(CompanyA))
        {
            // No item type yet: version "0" and the port answers "kaynak yok".
            var none = await scope.Service.GetAsync(productId);
            Assert.Equal(("0", (string?)null), (none.Version, none.ItemType));
            var unset = await scope.LineTaxPort.ResolveAsync(productId, TaxBuyerSituationsV1.Normal, today);
            Assert.Equal((TaxResolutionStatesV1.Unresolved, TaxResolutionReasonsV1.ProductItemTypeMissing), (unset.State, unset.Reason));
            Assert.Equal(TaxResolutionReasonsV1.ProductNotFound,
                (await scope.LineTaxPort.ResolveAsync(Guid.NewGuid(), TaxBuyerSituationsV1.Normal, today)).Reason);

            Assert.Equal((400, ProductItemTypeProblemCodesV1.RequestInvalid),
                Status(await scope.Service.SetAsync(productId, new("0", "Wood", "SENTETIK"), Metadata("k-bad-type"))));
            Assert.Equal((400, ProductItemTypeProblemCodesV1.ReasonRequired),
                Status(await scope.Service.SetAsync(productId, new("0", ProductItemTypesV1.Steel, " "), Metadata("k-no-reason"))));

            var set = await scope.Service.SetAsync(productId, new("0", ProductItemTypesV1.Steel, "SENTETIK tip"), Metadata("k-set-1"));
            Assert.Equal((204, false), (set.Answer.StatusCode, set.IsReplay));
            var replay = await scope.Service.SetAsync(productId, new("0", ProductItemTypesV1.Steel, "SENTETIK tip"), Metadata("k-set-1"));
            Assert.Equal((204, true), (replay.Answer.StatusCode, replay.IsReplay));
            Assert.Equal((409, ProductItemTypeProblemCodesV1.IdempotencyConflict),
                Status(await scope.Service.SetAsync(productId, new("0", ProductItemTypesV1.Scrap, "SENTETIK tip"), Metadata("k-set-1"))));
            Assert.Equal((409, ProductItemTypeProblemCodesV1.VersionConflict),
                Status(await scope.Service.SetAsync(productId, new("0", ProductItemTypesV1.Scrap, "SENTETIK"), Metadata("k-stale"))));
            var unknown = await scope.Service.SetAsync(Guid.NewGuid(), new("0", ProductItemTypesV1.Steel, "SENTETIK"), Metadata("k-unknown"));
            Assert.Equal((404, (string?)null), (unknown.Answer.StatusCode, unknown.Answer.Code));
            Assert.Equal(("1", ProductItemTypesV1.Steel), ((await scope.Service.GetAsync(productId)).Version,
                (await scope.Service.GetAsync(productId)).ItemType));

            // End to end: Product item type → host adapter → Settings line tax port → the SAT-D139 steel default.
            var resolved = await scope.LineTaxPort.ResolveAsync(productId, TaxBuyerSituationsV1.Normal, today);
            Assert.Equal((TaxResolutionStatesV1.Resolved, TaxItemTypesV1.Steel, 20m, 50m, TaxDefaultSourcesV1.Catalog, CompanyA),
                (resolved.State, resolved.ItemType, resolved.VatPercent, resolved.WithholdingPercent, resolved.Source, resolved.CompanyId));
            var notSubject = await scope.LineTaxPort.ResolveAsync(productId, TaxBuyerSituationsV1.NotSubjectToWithholding, today);
            Assert.Equal((TaxResolutionStatesV1.NotApplied, (decimal?)20m), (notSubject.WithholdingState, notSubject.VatPercent));

            // Changing the type to scrap: KDV yok with the exemption code "kaynak yok" (the invoice waits).
            Assert.Equal(204, (await scope.Service.SetAsync(productId, new("1", ProductItemTypesV1.Scrap, "SENTETIK hurda"),
                Metadata("k-set-2"))).Answer.StatusCode);
            var scrap = await scope.LineTaxPort.ResolveAsync(productId, TaxBuyerSituationsV1.Normal, today);
            Assert.Equal((TaxResolutionStatesV1.Exempt, TaxResolutionStatesV1.Unresolved), (scrap.VatState, scrap.ExemptionCodeState));
        }

        await using (var scopeB = run.Request(CompanyB))
        {
            // SAT-D149: the item type is ONE value on the shared card — company B sees the value set in A (and where).
            var shared = await scopeB.Service.GetAsync(productId);
            Assert.Equal(("2", ProductItemTypesV1.Scrap, (Guid?)CompanyA, CompanyB),
                (shared.Version, shared.ItemType, shared.UpdatedInCompanyId, shared.CompanyId));
            Assert.Equal(TaxItemTypesV1.Scrap,
                (await scopeB.LineTaxPort.ResolveAsync(productId, TaxBuyerSituationsV1.Normal, today)).ItemType);

            // A holder in B changes it for every company; the change is audited in B, the acting company.
            Assert.Equal((409, ProductItemTypeProblemCodesV1.VersionConflict), Status(await scopeB.Service.SetAsync(productId,
                new("1", ProductItemTypesV1.Steel, "SENTETIK B"), MetadataIn(CompanyB, "k-b-stale"))));
            Assert.Equal(204, (await scopeB.Service.SetAsync(productId, new("2", ProductItemTypesV1.Steel, "SENTETIK B demir"),
                MetadataIn(CompanyB, "k-b-set"))).Answer.StatusCode);
        }

        await using (var scopeA = run.Request(CompanyA))
        {
            var afterB = await scopeA.Service.GetAsync(productId);
            Assert.Equal(("3", ProductItemTypesV1.Steel, (Guid?)CompanyB), (afterB.Version, afterB.ItemType, afterB.UpdatedInCompanyId));
            Assert.Equal((TaxItemTypesV1.Steel, 20m),
                ((await scopeA.LineTaxPort.ResolveAsync(productId, TaxBuyerSituationsV1.Normal, today)).ItemType,
                    (await scopeA.LineTaxPort.ResolveAsync(productId, TaxBuyerSituationsV1.Normal, today)).VatPercent));
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [product].[ProductCardItemTypes]"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'product.item-type.changed.v1' AND [CompanyId] = @c "
            + "AND JSON_VALUE([AfterJson], '$.actingCompanyId') = @s AND JSON_VALUE([BeforeJson], '$.updatedInCompanyId') = @a",
            ("@c", CompanyB), ("@s", CompanyB.ToString("D")), ("@a", CompanyA.ToString("D"))));

        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'product.item-type.changed.v1' AND [CompanyId] = @c",
            ("@c", CompanyA)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'product.item-type.changed.v1' "
            + "AND JSON_VALUE([AfterJson], '$.reason') = N'SENTETIK tip'"));

        // The rollback is refused while a shared item type exists (forward fix, 51152).
        await using var scopeRollback = run.Request(CompanyA);
        var context = scopeRollback.Context;
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        var migration = Array.IndexOf(applied, ProductCardItemTypeMigration);
        Assert.True(migration > 0);
        var rollback = await Assert.ThrowsAsync<SqlException>(() => context.GetService<IMigrator>().MigrateAsync(applied[migration - 1]));
        Assert.Equal(51152, rollback.Number);
        Assert.Equal(applied, await context.Database.GetAppliedMigrationsAsync());
    }

    [SqlServerFact]
    public async Task Buyer_situation_list_is_dated_settings_data_whose_effect_drives_the_resolver_on_real_sql()
    {
        await using var run = await HostRun.StartAsync();
        var (editorSubject, _) = await run.GrantAsync(TestUsers.CompanyA, CompanyA, Permission);
        var (viewerSubject, _) = await run.GrantAsync(TestUsers.CompanyAViewer, CompanyA, "sales.drafts.read");
        var (editorBSubject, _) = await run.GrantAsync(TestUsers.CompanyB, CompanyB, Permission);
        using var client = run.Client(editorSubject, CompanyA);
        using var viewer = run.Client(viewerSubject, CompanyA);
        using var clientB = run.Client(editorBSubject, CompanyB);
        var today = run.Today;
        var tomorrow = today.AddDays(1);
        var path = $"{Root}/buyer-situations";
        var catalog = ApprovedTaxBuyerSituationCatalog.Entries
            .Select(entry => new TaxBuyerSituationEntryV1(entry.Code, entry.Name, entry.WithholdingApplies, entry.VatMode,
                entry.DocumentRequired))
            .ToArray();

        using (var forbidden = await viewer.GetAsync(path))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        // SAT-D158: the reviewed catalog (version "0") answers until a version is published; the main read carries it too.
        var initial = await run.JsonAsync(client, path);
        Assert.Equal(("0", "Catalog", "0", 5), (initial.GetProperty("version").GetString(),
            initial.GetProperty("inForce").GetProperty("source").GetString(),
            initial.GetProperty("inForce").GetProperty("listVersion").GetString(),
            initial.GetProperty("inForce").GetProperty("entries").GetArrayLength()));
        var export = initial.GetProperty("inForce").GetProperty("entries").EnumerateArray()
            .Single(entry => entry.GetProperty("code").GetString() == TaxBuyerSituationsV1.ExportRegistered);
        Assert.Equal((false, "ZeroWithExemptionCode", true), (export.GetProperty("withholdingApplies").GetBoolean(),
            export.GetProperty("vatMode").GetString(), export.GetProperty("documentRequired").GetBoolean()));
        Assert.Equal("0", (await run.JsonAsync(client, Root)).GetProperty("buyerSituations").GetProperty("version").GetString());

        // Validation: a KDV %0 situation keeps no withholding; a malformed code is refused; nothing is written.
        using (var zeroWithholding = await client.SendAsync(Json(HttpMethod.Post, $"{path}/versions", "k-bs-bad",
                   new PublishTaxBuyerSituationListRequestV1("0", [new TaxBuyerSituationEntryV1("Odd", "SENTETIK", true,
                       TaxBuyerSituationVatModesV1.ZeroWithExemptionCode, true)], "SENTETIK"))))
        {
            await AssertProblemAsync(zeroWithholding, HttpStatusCode.BadRequest, TaxMasterProblemCodesV1.RequestInvalid);
        }

        using (var badCode = await client.SendAsync(Json(HttpMethod.Post, $"{path}/versions", "k-bs-badcode",
                   new PublishTaxBuyerSituationListRequestV1("0", [new TaxBuyerSituationEntryV1("1x", "SENTETIK", true,
                       TaxBuyerSituationVatModesV1.Normal, false)], "SENTETIK"))))
        {
            await AssertProblemAsync(badCode, HttpStatusCode.BadRequest, TaxMasterProblemCodesV1.RequestInvalid);
        }

        // The catalog again is a no-op (no version, no audit).
        await run.CommandAsync(client, HttpMethod.Post, $"{path}/versions", "k-bs-same",
            new PublishTaxBuyerSituationListRequestV1("0", catalog, "SENTETIK aynı"), HttpStatusCode.NoContent);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [settings].[TaxBuyerSituationListVersions]"));

        // Müdür/Admin add a situation and drop one (SENTETIK); the exemption list (SENTETIK code) feeds the zero-rated ones.
        await run.CommandAsync(client, HttpMethod.Post, $"{Root}/code-lists/Exemption/versions", "k-bs-ex",
            new PublishTaxCodeListRequestV1("0", [new TaxCodeListEntryV1("E701", "SENTETIK istisna")], "SENTETIK liste"),
            HttpStatusCode.NoContent);
        var v1 = new PublishTaxBuyerSituationListRequestV1("0",
        [
            .. catalog.Where(entry => entry.Code != TaxBuyerSituationsV1.FinalConsumer),
            new TaxBuyerSituationEntryV1("DiplomaticMission", "SENTETIK diplomatik temsilcilik", false,
                TaxBuyerSituationVatModesV1.ZeroWithExemptionCode, true),
        ], "SENTETIK yeni durum");
        await run.CommandAsync(client, HttpMethod.Post, $"{path}/versions", "k-bs-1", v1, HttpStatusCode.NoContent);
        await run.CommandAsync(client, HttpMethod.Post, $"{path}/versions", "k-bs-1", v1, HttpStatusCode.NoContent, replayed: true);
        using (var stale = await client.SendAsync(Json(HttpMethod.Post, $"{path}/versions", "k-bs-stale", v1 with { Reason = "x" })))
        {
            await AssertProblemAsync(stale, HttpStatusCode.Conflict, TaxMasterProblemCodesV1.VersionConflict);
        }

        await using (var port = run.LineTaxPort(CompanyA))
        {
            var diplomatic = await port.Value.GetItemTypeDefaultAsync(TaxItemTypesV1.Steel, "DiplomaticMission", today, "e701");
            Assert.Equal((TaxResolutionStatesV1.ZeroRated, 0m, TaxResolutionStatesV1.Resolved, "E701", TaxResolutionStatesV1.NotApplied,
                    1L, true),
                (diplomatic.VatState, diplomatic.VatPercent, diplomatic.ExemptionCodeState, diplomatic.ExemptionCode,
                    diplomatic.WithholdingState, diplomatic.BuyerSituationListVersion, diplomatic.BuyerDocumentRequired));
            var exportRegistered = await port.Value.GetItemTypeDefaultAsync(TaxItemTypesV1.Scrap,
                TaxBuyerSituationsV1.ExportRegistered, today, "E701");
            Assert.Equal((TaxResolutionStatesV1.ZeroRated, TaxResolutionStatesV1.Resolved,
                    TaxResolutionReasonsV1.BuyerZeroRatedWithExemptionCode),
                (exportRegistered.VatState, exportRegistered.ExemptionCodeState, exportRegistered.BuyerAdjustment));
            // A withdrawn situation is "kaynak yok", never the normal default.
            var dropped = await port.Value.GetItemTypeDefaultAsync(TaxItemTypesV1.Steel, TaxBuyerSituationsV1.FinalConsumer, today);
            Assert.Equal((TaxResolutionStatesV1.Unresolved, TaxResolutionReasonsV1.BuyerSituationUnknown, (decimal?)null),
                (dropped.State, dropped.Reason, dropped.VatPercent));
            var list = await port.Value.GetBuyerSituationsAsync(today);
            Assert.Equal((TaxResolutionStatesV1.Resolved, 1L, 5), (list.State, list.ListVersion, list.Entries.Count));
            Assert.Equal(TaxMasterProblemCodesV1.RequestInvalid, (await Assert.ThrowsAsync<SettingsPrerequisiteException>(
                () => port.Value.GetItemTypeDefaultAsync(TaxItemTypesV1.Steel, "Not a code", today))).Code);
        }

        // A scheduled version can be withdrawn before its date; a version in force cannot.
        await run.CommandAsync(client, HttpMethod.Post, $"{path}/versions", "k-bs-2",
            new PublishTaxBuyerSituationListRequestV1("1", catalog, "SENTETIK planlı", tomorrow), HttpStatusCode.NoContent);
        var scheduled = await run.JsonAsync(client, path);
        Assert.Equal(("2", "2"), (scheduled.GetProperty("version").GetString(),
            Assert.Single(scheduled.GetProperty("scheduled").EnumerateArray()).GetProperty("listVersion").GetString()));
        using (var inEffect = await client.SendAsync(Json(HttpMethod.Post, $"{path}/versions/1/withdrawal", "k-bs-w1",
                   new WithdrawTaxCodeListRequestV1("2", "SENTETIK"))))
        {
            await AssertProblemAsync(inEffect, HttpStatusCode.Conflict, TaxMasterProblemCodesV1.RecordInEffect);
        }

        await run.CommandAsync(client, HttpMethod.Post, $"{path}/versions/2/withdrawal", "k-bs-w2",
            new WithdrawTaxCodeListRequestV1("2", "SENTETIK geri çekme"), HttpStatusCode.NoContent);
        using (var again = await client.SendAsync(Json(HttpMethod.Post, $"{path}/versions/2/withdrawal", "k-bs-w3",
                   new WithdrawTaxCodeListRequestV1("3", "SENTETIK"))))
        {
            await AssertProblemAsync(again, HttpStatusCode.Conflict, TaxMasterProblemCodesV1.RecordWithdrawn);
        }

        using (var catalogVersion = await client.SendAsync(Json(HttpMethod.Post, $"{path}/versions/0/withdrawal", "k-bs-w0",
                   new WithdrawTaxCodeListRequestV1("3", "SENTETIK"))))
        {
            await AssertProblemAsync(catalogVersion, HttpStatusCode.BadRequest, TaxMasterProblemCodesV1.RequestInvalid);
        }

        Assert.Empty((await run.JsonAsync(client, path)).GetProperty("scheduled").EnumerateArray());
        await using (var port = run.LineTaxPort(CompanyA))
        {
            Assert.Equal(TaxResolutionReasonsV1.BuyerSituationUnknown, (await port.Value.GetItemTypeDefaultAsync(TaxItemTypesV1.Steel,
                TaxBuyerSituationsV1.FinalConsumer, tomorrow)).Reason);
        }

        // Company isolation: B keeps its own catalog; A's version is B's code-less 404.
        var listB = await run.JsonAsync(clientB, path);
        Assert.Equal(("0", "Catalog", CompanyB), (listB.GetProperty("version").GetString(),
            listB.GetProperty("inForce").GetProperty("source").GetString(), listB.GetProperty("companyId").GetGuid()));
        using (var foreign = await clientB.SendAsync(Json(HttpMethod.Post, $"{path}/versions/1/withdrawal", "k-bs-foreign",
                   new WithdrawTaxCodeListRequestV1("1", "SENTETIK"))))
        {
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        }

        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'settings.tax-buyer-situation-list.published.v1' "
            + "AND [CompanyId] = @c AND [PermissionCode] = N'settings.tax-master.manage'", ("@c", CompanyA)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'settings.tax-buyer-situation-list.withdrawn.v1' "
            + "AND JSON_VALUE([AfterJson], '$.reason') = N'SENTETIK geri çekme'"));

        // The CHECK holds the SAT-D147 rule in the database as well.
        var check = await Assert.ThrowsAsync<SqlException>(() => run.ScalarAsync<int>(
            "INSERT INTO [settings].[TaxBuyerSituationEntries] ([CompanyId], [ListVersion], [Code], [Name], [WithholdingApplies], "
            + "[VatMode], [DocumentRequired]) VALUES (@c, 1, 'Odd', N'SENTETIK', 1, 'ZeroWithExemptionCode', 1); SELECT 0",
            ("@c", CompanyA)));
        Assert.Equal(547, check.Number);

        // The rollback is refused while a buyer situation list exists (forward fix, 51153); nothing is dropped.
        await using var context = run.SettingsContext(CompanyA);
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        var migration = Array.IndexOf(applied, BuyerSituationMigration);
        Assert.True(migration > 0);
        var rollback = await Assert.ThrowsAsync<SqlException>(() => context.GetService<IMigrator>().MigrateAsync(applied[migration - 1]));
        Assert.Equal(51153, rollback.Number);
        Assert.Equal(applied.Take(migration + 1), await context.Database.GetAppliedMigrationsAsync());
    }

    [SqlServerFact]
    public async Task Item_type_move_to_the_shared_card_refuses_disagreeing_companies_and_moves_agreeing_ones_on_real_sql()
    {
        await using var run = await ProductRun.StartAsync();
        var productId = ProductSalesCandidateFixture.ProductId;
        await using var scope = run.Request(CompanyA);
        var migrator = scope.Context.GetService<IMigrator>();

        // Down on an empty shared store restores the (empty) company-scoped table.
        await migrator.MigrateAsync(ProductItemTypeMigration);
        Assert.DoesNotContain(ProductCardItemTypeMigration, await scope.Context.Database.GetAppliedMigrationsAsync());
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [product].[ProductItemTypes]"));

        // Pre-move state (the PR #866 shape, written as that migration's rows): the same product typed differently in A and B.
        await run.ScalarAsync<int>(
            "INSERT INTO [product].[ProductItemTypes] ([CompanyId], [ProductId], [ItemType], [Version], [UpdatedAtUtc], [UpdatedBy]) "
            + "VALUES (@a, @p, 'Steel', 2, '2026-10-31T10:00:00+00:00', N'SENTETIK-a'), "
            + "(@b, @p, 'Scrap', 1, '2026-10-31T11:00:00+00:00', N'SENTETIK-b'); SELECT 0",
            ("@a", CompanyA), ("@b", CompanyB), ("@p", productId));
        var refused = await Assert.ThrowsAsync<SqlException>(() => migrator.MigrateAsync());
        Assert.Equal(51151, refused.Number);
        Assert.DoesNotContain(ProductCardItemTypeMigration, await scope.Context.Database.GetAppliedMigrationsAsync());
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [product].[ProductItemTypes]"));

        // Once the companies agree, the value moves (version 1; the latest change's actor and company) and the old table goes.
        await run.ScalarAsync<int>(
            "UPDATE [product].[ProductItemTypes] SET [ItemType] = 'Steel' WHERE [CompanyId] = @b; SELECT 0", ("@b", CompanyB));
        await migrator.MigrateAsync();
        Assert.Contains(ProductCardItemTypeMigration, await scope.Context.Database.GetAppliedMigrationsAsync());
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sys.tables WHERE [name] = N'ProductItemTypes'"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [product].[ProductCardItemTypes] WHERE [ProductId] = @p AND [ItemType] = 'Steel' AND [Version] = 1 "
            + "AND [UpdatedBy] = N'SENTETIK-b' AND [UpdatedInCompanyId] = @b", ("@p", productId), ("@b", CompanyB)));

        await using var reader = run.Request(CompanyA);
        var moved = await reader.Service.GetAsync(productId);
        Assert.Equal(("1", ProductItemTypesV1.Steel, (Guid?)CompanyB), (moved.Version, moved.ItemType, moved.UpdatedInCompanyId));

        // With a shared value stored, the move is not rolled back (forward fix, 51152).
        var rollback = await Assert.ThrowsAsync<SqlException>(() => migrator.MigrateAsync(ProductItemTypeMigration));
        Assert.Equal(51152, rollback.Number);
        Assert.Contains(ProductCardItemTypeMigration, await scope.Context.Database.GetAppliedMigrationsAsync());
    }

    private static (int, string?) Status(ProductItemTypeResultV1 result) => (result.Answer.StatusCode, result.Answer.Code);

    private static ProductItemTypeCommandMetadataV1 Metadata(string key) => MetadataIn(CompanyA, key);

    private static ProductItemTypeCommandMetadataV1 MetadataIn(Guid company, string key) =>
        new(company, ProductRun.ActorId.ToString("D"), $"corr-{key}", null, "Web", null, null, key);

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

        public PortScope<ISettingsLineTaxResolverV1> LineTaxPort(Guid company)
        {
            var database = NewSettingsContext(Factory.DatabaseConnectionString, company, out var companyContext);
            return new PortScope<ISettingsLineTaxResolverV1>(database,
                new SettingsTaxMasterReader(database, companyContext, new FixedActor("SENTETIK-sales-actor", company)));
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

        public RequestScope Request(Guid company)
        {
            var scope = factory.Services.CreateAsyncScope();
            var provider = scope.ServiceProvider;
            var companyContext = provider.GetRequiredService<ICompanyContext>();
            companyContext.Set(company);
            var actor = new FixedActor(ActorId.ToString("D"), CompanyA, CompanyB);
            var settings = NewSettingsContext(factory.DatabaseConnectionString, company, out var settingsCompany);
            var context = provider.GetRequiredService<ProductPriceListDbContext>();
            var service = new ProductItemTypeService(context, companyContext, actor, new Granted(),
                provider.GetRequiredService<IAuditEventCollector>(), TimeProvider.System);
            var port = new SettingsTaxMasterReader(settings, settingsCompany, actor,
                new SettingsProductItemTypeHost(service, companyContext));
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

    private sealed class RequestScope(AsyncServiceScope scope, SettingsDbContext settings, ProductItemTypeService service,
        ISettingsLineTaxResolverV1 lineTaxPort, ProductPriceListDbContext context) : IAsyncDisposable
    {
        public IProductItemTypeServiceV1 Service => service;

        public ISettingsLineTaxResolverV1 LineTaxPort => lineTaxPort;

        public ProductPriceListDbContext Context => context;

        public async ValueTask DisposeAsync()
        {
            await settings.DisposeAsync();
            await scope.DisposeAsync();
        }
    }
}
