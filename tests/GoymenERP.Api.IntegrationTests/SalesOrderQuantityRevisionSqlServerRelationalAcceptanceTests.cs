using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Api.Security;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using LogisticsQuantity = GoymenERP.BuildingBlocks.Quantities.Quantity<GoymenERP.Modules.Logistics.Domain.Quantities.LogisticsQuantityRefusals>;
using LogisticsQuantityUnit = GoymenERP.BuildingBlocks.Quantities.QuantityUnit;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// DEC-SAL-002 / AT-023 against a REAL SQL Server and the REAL host composition: an approved order's quantity revision
/// through the real route is ONE save — the archived Rev.0 (its approval facts, the line as approved and the reason), the
/// header at Rev.1 back in Preparing with version 5, the line's new quantity, the attempt and the audit event with
/// SourceChannel Web; while Rev.1 waits the line port answers the LAST APPROVED Rev.0 for physical shipping (SAT-D103:
/// its quantity, the cap — an increase is not shipped before its approval, SAT-D104); the existing submit/approve routes
/// approve Rev.1 and the port then answers the NEW quantity at Rev.1; the revisions route still shows Rev.0 with its reason.
/// SAT-D104 / SAT-D111: a loading instruction of the order (Logistics, the real owner port) no longer blocks the revision; a
/// decrease below the shipped (or, fail-closed, the loaded) quantity is refused unrecorded; a pending decrease caps shipping at
/// the requested quantity and a rejection restores the last approved one; a stale version is terminal; another company sees
/// nothing; the migration refuses to roll back over a recorded reason (51057). The draft is stored and approved through the real Sales store from ISOLATED SYNTHETIC owner snapshots (the
/// AT-024 / DEC-SAL-005 precedent); every grant is a fresh synthetic role. Needs the central registration of
/// <c>sales.orders.quantity.write</c> (until then the route answers 503 by design). SENTETIK / NOT_ENV.
/// </summary>
public sealed class SalesOrderQuantityRevisionSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly DateOnly Day = new(2026, 9, 19);
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateTimeOffset T0 = new(2026, 9, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid KgUnit = SettingsUnitIdentitiesV1.Kilogram;
    private static readonly Guid Currency = Guid.Parse("9c3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7e01");
    private static readonly Guid MoneyProfile = Guid.Parse("9c3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7e02");
    private static readonly Guid KgProfile = Guid.Parse("9c3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7e03");
    private static readonly Guid Product = Guid.Parse("9c3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7e04");

    private static readonly SalesDraftStateAudit Submitted = new SalesDraftStateAudit("sales.order-draft.submitted.v1", "Submit", "SENTETIK onaya gönderildi");
    private static readonly SalesDraftStateAudit Approved = new SalesDraftStateAudit("sales.order-draft.approved.v1", "Approve", "SENTETIK onaylandı");
    private static readonly SalesDraftStateAudit Revised = new SalesDraftStateAudit("sales.order-draft.quantity-revised.v1", "ReviseQuantity", "SENTETIK miktar revizyonu");
    private static readonly LogisticsMutationAudit Issued = new("logistics.loading-instruction.issued.v1", "Issue", "SENTETIK yayımlandı");
    private static readonly LogisticsMutationAudit LoadedAudit = new("logistics.loading-instruction.load-recorded.v1", "RecordLoad", "SENTETIK yüklendi");
    private static readonly LogisticsMutationAudit ShippedAudit = new("logistics.loading-instruction.shipment-recorded.v1", "RecordShipment", "SENTETIK sevk edildi");

    [SqlServerFact]
    public async Task An_approved_orders_quantity_revision_archives_rev0_with_its_reason_in_one_save_and_rev1_is_approved_through_the_real_routes_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k", "6");
        var orderLine = Assert.Single(draft.Lines);
        var subject = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.DraftsCreate,
            SalesPermissionManifestV1.OrdersApprove, SalesPermissionManifestV1.OrdersQuantityWrite, SalesPermissionManifestV1.OrdersPriceRead);
        using var client = run.Client(subject, CompanyA);
        var draftPath = $"{SalesEndpoints.DraftsPath}/{draft.Id:D}";
        Assert.Equal(SalesOrderLineReferenceOutcomeV1.Approved, (await run.LineAsync(CompanyA, draft.Id, orderLine.Id)).Outcome);

        using (var revised = await client.SendAsync(Revise($"{draftPath}/quantity-revision", "SENTETIK-quantity-1", "4", "  SENTETIK müşteri 7.5 kg istedi ",
                   orderLine.Id, "7.5")))
        {
            Assert.Equal(HttpStatusCode.OK, revised.StatusCode);
            Assert.False(revised.Headers.Contains("Idempotency-Replayed"));
            var body = (await revised.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail");
            Assert.Equal(("Preparing", "5", "Rev.1", "7.5"), (body.GetProperty("draft").GetProperty("approvalState").GetString(),
                body.GetProperty("draft").GetProperty("version").GetString(), body.GetProperty("draft").GetProperty("revision").GetString(),
                body.GetProperty("lines")[0].GetProperty("quantity").GetString()));
        }

        // ONE save, as SQL Server holds it: the header, the archived Rev.0 and its line as approved, the line's new quantity, the
        // attempt and the audit event (SourceChannel Web, version 4 -> 5).
        Assert.Equal((1, 1, 5L, (int)InternalApprovalState.Preparing), (
            await run.ScalarAsync<int>("SELECT [Revision] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftRevisions] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT [ApprovalState] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id))));
        Assert.Equal(("SENTETIK müşteri 7.5 kg istedi", "SENTETIK-approver", "6", "7.5"), (
            await run.ScalarAsync<string>("SELECT [Reason] FROM [sales].[DraftRevisions] WHERE [DraftId] = @id AND [Revision] = 0", ("@id", draft.Id)),
            await run.ScalarAsync<string>("SELECT [ApprovedBy] FROM [sales].[DraftRevisions] WHERE [DraftId] = @id AND [Revision] = 0", ("@id", draft.Id)),
            await run.ScalarAsync<string>("SELECT [QuantityText] FROM [sales].[DraftRevisionLines] WHERE [DraftId] = @id AND [Revision] = 0", ("@id", draft.Id)),
            await run.ScalarAsync<string>("SELECT [QuantityText] FROM [sales].[DraftLines] WHERE [DraftId] = @id", ("@id", draft.Id))));
        Assert.Equal(7.5m, await run.ScalarAsync<decimal>("SELECT [Quantity] FROM [sales].[DraftLines] WHERE [DraftId] = @id", ("@id", draft.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = N'sales.order-draft.quantity-revised.v1' AND [EntityId] = @id "
            + "AND [SourceChannel] = N'Web' AND [EntityVersionBefore] = 4 AND [EntityVersionAfter] = 5",
            ("@id", draft.Id.ToString("D"))));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [CommandName] = N'sales.order-draft.revise-quantity.v1' AND [Outcome] = 1 AND [DraftId] = @id",
            ("@id", draft.Id)));

        // SAT-D103 / SAT-D104: while Rev.1 waits for approval shipping goes on under Rev.0 — its quantity is the cap; the
        // increase to 7.5 reaches no consumer before its approval.
        var pending = await run.LineAsync(CompanyA, draft.Id, orderLine.Id);
        Assert.Equal((SalesOrderLineReferenceOutcomeV1.Approved, 0, "6", "6", SalesOrderLineRevisionBasisV1.LastApprovedRevision, "Preparing"),
            (pending.Outcome, pending.Line!.Revision, pending.Line.QuantityText, pending.Line.ShippingCapQuantityText, pending.Line.RevisionBasis,
                pending.Line.PendingApprovalState));
        var listed = Assert.Single(await run.ApprovedLinesAsync(CompanyA), item => item.OrderId == draft.Id);
        Assert.Equal((0, "6", "6"), (listed.Revision, listed.QuantityText, listed.ShippingCapQuantityText));

        // The same key and content: the stored result replayed — no second archive, event or attempt.
        var afterRevision = await run.EffectsAsync(draft.Id);
        using (var replayed = await client.SendAsync(Revise($"{draftPath}/quantity-revision", "SENTETIK-quantity-1", "4", "  SENTETIK müşteri 7.5 kg istedi ",
                   orderLine.Id, "7.5")))
        {
            Assert.Equal(HttpStatusCode.OK, replayed.StatusCode);
            Assert.Equal("true", Assert.Single(replayed.Headers.GetValues("Idempotency-Replayed")));
        }

        Assert.Equal(afterRevision, await run.EffectsAsync(draft.Id));

        // The existing flow approves Rev.1 through the real routes.
        using (var submitted = await client.SendAsync(State($"{draftPath}/submission", "SENTETIK-submit-rev1", "5")))
        {
            Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        }

        using (var approved = await client.SendAsync(State($"{draftPath}/approval", "SENTETIK-approve-rev1", "6")))
        {
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        }

        // Close and reopen: Rev.1 approved with the new quantity; Rev.0 archived with its reason and its approved quantity.
        var detail = await run.JsonAsync(client, draftPath);
        Assert.Equal(("Approved", "Rev.1", "7", "7.5"), (detail.GetProperty("draft").GetProperty("approvalState").GetString(),
            detail.GetProperty("draft").GetProperty("revision").GetString(), detail.GetProperty("draft").GetProperty("version").GetString(),
            detail.GetProperty("lines")[0].GetProperty("quantity").GetString()));
        var rev0 = Assert.Single((await run.JsonAsync(client, $"{draftPath}/revisions")).GetProperty("items").EnumerateArray());
        Assert.Equal(("Rev.0", "SENTETIK müşteri 7.5 kg istedi", "6", "SENTETIK-approver"), (rev0.GetProperty("revision").GetString(),
            rev0.GetProperty("reason").GetString(), rev0.GetProperty("lines")[0].GetProperty("quantity").GetString(), rev0.GetProperty("approvedBy").GetString()));

        // Downstream reads the APPROVED revision: Rev.1 and the new quantity.
        var answer = await run.LineAsync(CompanyA, draft.Id, orderLine.Id);
        Assert.Equal((SalesOrderLineReferenceOutcomeV1.Approved, "7.5", 1, SalesOrderLineRevisionBasisV1.CurrentRevision, "7.5"),
            (answer.Outcome, answer.Line!.QuantityText, answer.Line.Revision, answer.Line.RevisionBasis, answer.Line.ShippingCapQuantityText));
    }

    [SqlServerFact]
    public async Task A_loading_instruction_no_longer_blocks_a_decrease_which_never_goes_below_the_shipped_or_loaded_quantity_and_caps_shipping_until_the_decision_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k", "6");
        var orderLine = Assert.Single(draft.Lines);
        // Real Logistics records: 6 kg instructed under Rev.0, 5 loaded, 4 shipped.
        await run.ShippedAsync(CompanyA, draft, "k", loaded: 5m, shipped: 4m);
        var subject = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.DraftsCreate,
            SalesPermissionManifestV1.OrdersApprove, SalesPermissionManifestV1.OrdersQuantityWrite);
        using var client = run.Client(subject, CompanyA);
        var draftPath = $"{SalesEndpoints.DraftsPath}/{draft.Id:D}";
        var path = $"{draftPath}/quantity-revision";

        // Below the shipped 4 and below the loaded 5: refused on the line, unrecorded (the floor moves with Logistics).
        foreach (var (key, quantity, code) in new[]
                 {
                     ("SENTETIK-below-shipped", "3", SalesRuleCodes.RevisionBelowShipped),
                     ("SENTETIK-below-loaded", "4.5", SalesRuleCodes.RevisionBelowLoaded),
                 })
        {
            using var refused = await client.SendAsync(Revise(path, key, "4", "neden", orderLine.Id, quantity));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
            var error = (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors")[0];
            Assert.Equal(("lines[0].quantity", code), (error.GetProperty("field").GetString(), error.GetProperty("code").GetString()));
        }

        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [CommandName] = N'sales.order-draft.revise-quantity.v1' AND [DraftId] = @id",
            ("@id", draft.Id)));

        // At the loaded quantity: recorded although the instruction stands (SAT-D104 replaces the old block); Sales released
        // nothing of Logistics.
        using (var revised = await client.SendAsync(Revise(path, "SENTETIK-down", "4", "SENTETIK müşteri 5 kg istedi", orderLine.Id, "5")))
        {
            Assert.Equal(HttpStatusCode.OK, revised.StatusCode);
        }

        Assert.Equal((1, 1, 5L), (
            await run.ScalarAsync<int>("SELECT [Revision] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [logistics].[LoadingInstructionLines] WHERE [SourceOrderId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id))));

        // SAT-D111: the pending decrease caps shipping at min(6, 5) = 5; the identity stays Rev.0's.
        await run.AssertShippingAsync(CompanyA, draft.Id, orderLine.Id, "Preparing", "5", 5);
        // The loading facts still read although the instruction (6, made under Rev.0) stands above the requested 5.
        var progress = (await run.JsonAsync(client, $"{draftPath}/loading-progress")).GetProperty("lines")[0];
        Assert.Equal(("5", "6", "5", "4"), (progress.GetProperty("ordered").GetString(), progress.GetProperty("instructed").GetString(),
            progress.GetProperty("loaded").GetString(), progress.GetProperty("shipped").GetString()));

        using (var submitted = await client.SendAsync(State($"{draftPath}/submission", "SENTETIK-down-submit", "5")))
        {
            Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        }

        await run.AssertShippingAsync(CompanyA, draft.Id, orderLine.Id, "PendingApproval", "5", 6);
        using (var rejected = await client.SendAsync(State($"{draftPath}/rejection", "SENTETIK-down-reject", "6")))
        {
            Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        }

        // Rejected: shipping goes on up to the last approved 6 again; invoicing opens at Rev.0's prices.
        await run.AssertShippingAsync(CompanyA, draft.Id, orderLine.Id, "Rejected", "6", 7);
        var invoicing = (await run.JsonAsync(client, $"{draftPath}/status")).GetProperty("invoicing");
        Assert.Equal(("NoSource", "Open", "Rev.0"), (invoicing.GetProperty("state").GetString(), invoicing.GetProperty("gate").GetString(),
            invoicing.GetProperty("priceBasisRevision").GetString()));

        // A stale version is still the key's terminal 409, recorded and replayed.
        using (var stale = await client.SendAsync(Revise(path, "SENTETIK-stale", "3", "neden", orderLine.Id, "5.5")))
        {
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            Assert.Equal(SalesDraftProblemCodesV1.VersionConflict, (await stale.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }

        using (var replayed = await client.SendAsync(Revise(path, "SENTETIK-stale", "3", "neden", orderLine.Id, "5.5")))
        {
            Assert.Equal(HttpStatusCode.Conflict, replayed.StatusCode);
            Assert.Equal("true", Assert.Single(replayed.Headers.GetValues("Idempotency-Replayed")));
        }
    }

    [SqlServerFact]
    public async Task A_price_revision_keeps_shipping_on_the_last_approved_revision_while_invoicing_waits_and_a_rejection_keeps_the_old_price_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k", "6");
        var orderLine = Assert.Single(draft.Lines);
        var subject = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.DraftsCreate,
            SalesPermissionManifestV1.OrdersApprove, SalesPermissionManifestV1.OrdersPriceWrite, SalesPermissionManifestV1.OrdersPriceRead,
            SalesPermissionManifestV1.OrdersDiscountExceptionApprove);
        using var client = run.Client(subject, CompanyA);
        var draftPath = $"{SalesEndpoints.DraftsPath}/{draft.Id:D}";
        Assert.Equal("Open", (await run.JsonAsync(client, $"{draftPath}/status")).GetProperty("invoicing").GetProperty("gate").GetString());

        // A manual price on the approved order: Rev.1 back in preparation (K3 commercial revision through the real route).
        using (var revised = await client.SendAsync(Post($"{draftPath}/commercial-revision", "SENTETIK-price",
                   new SalesDraftCommercialRevisionRequest("4", [new SalesDraftLineCommercialV1(orderLine.Id, "100", KgUnit)]))))
        {
            Assert.Equal(HttpStatusCode.OK, revised.StatusCode);
        }

        // Shipping goes on under Rev.0 (the port carries no price member at all); invoicing waits for the decision.
        await run.AssertShippingAsync(CompanyA, draft.Id, orderLine.Id, "Preparing", "6", 5);
        var waiting = (await run.JsonAsync(client, $"{draftPath}/status")).GetProperty("invoicing");
        Assert.Equal(("NoSource", "AwaitingReapproval", JsonValueKind.Null), (waiting.GetProperty("state").GetString(),
            waiting.GetProperty("gate").GetString(), waiting.GetProperty("priceBasisRevision").ValueKind));

        // SAT-D43/D45: the manual price is approved (self-approvable without a list price), submitted and REJECTED.
        using (var approved = await client.SendAsync(Post($"{draftPath}/discount-exception-approval", "SENTETIK-discount",
                   new SalesDraftDiscountExceptionApprovalRequest("5", [new SalesDraftDiscountExceptionApprovalLineV1(orderLine.Id, "SENTETIK liste dışı fiyat")]))))
        {
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        }

        using (var submitted = await client.SendAsync(State($"{draftPath}/submission", "SENTETIK-price-submit", "6")))
        {
            Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        }

        await run.AssertShippingAsync(CompanyA, draft.Id, orderLine.Id, "PendingApproval", "6", 7);
        using (var rejected = await client.SendAsync(State($"{draftPath}/rejection", "SENTETIK-price-reject", "7")))
        {
            Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        }

        // Rejected: shipping still under Rev.0 and the invoice uses Rev.0's (last approved) prices.
        await run.AssertShippingAsync(CompanyA, draft.Id, orderLine.Id, "Rejected", "6", 8);
        var open = (await run.JsonAsync(client, $"{draftPath}/status")).GetProperty("invoicing");
        Assert.Equal(("Open", "Rev.0"), (open.GetProperty("gate").GetString(), open.GetProperty("priceBasisRevision").GetString()));

        // Resubmitted and approved: every consumer moves to Rev.1, the invoice to the new price.
        using (var resubmitted = await client.SendAsync(State($"{draftPath}/submission", "SENTETIK-price-resubmit", "8")))
        {
            Assert.Equal(HttpStatusCode.OK, resubmitted.StatusCode);
        }

        using (var approvedRev1 = await client.SendAsync(State($"{draftPath}/approval", "SENTETIK-price-approve", "9")))
        {
            Assert.Equal(HttpStatusCode.OK, approvedRev1.StatusCode);
        }

        var current = await run.LineAsync(CompanyA, draft.Id, orderLine.Id);
        Assert.Equal((1, SalesOrderLineRevisionBasisV1.CurrentRevision, (string?)null), (current.Line!.Revision, current.Line.RevisionBasis,
            current.Line.PendingApprovalState));
        Assert.Equal("Rev.1", (await run.JsonAsync(client, $"{draftPath}/status")).GetProperty("invoicing").GetProperty("priceBasisRevision").GetString());
    }

    [SqlServerFact]
    public async Task Another_company_cannot_revise_or_read_the_order_and_an_ungranted_actor_is_refused_before_anything_is_read_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k", "6");
        var orderLine = Assert.Single(draft.Lines);
        var path = $"{SalesEndpoints.DraftsPath}/{draft.Id:D}/quantity-revision";

        var subjectOfB = await run.GrantAsync(TestUsers.CompanyB, CompanyB, SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersQuantityWrite);
        using (var clientOfB = run.Client(subjectOfB, CompanyB))
        {
            using var refused = await clientOfB.SendAsync(Revise(path, "SENTETIK-b", "4", "neden", orderLine.Id, "7"));
            Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await clientOfB.GetAsync($"{SalesEndpoints.DraftsPath}/{draft.Id:D}/revisions")).StatusCode);
        }

        // Company A's reader without the quantity name: 403, nothing recorded.
        var reader = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsRead);
        using (var client = run.Client(reader, CompanyA))
        {
            using var forbidden = await client.SendAsync(Revise(path, "SENTETIK-a", "4", "neden", orderLine.Id, "7"));
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [CommandName] = N'sales.order-draft.revise-quantity.v1'"));
        Assert.Equal((0, 4L), (await run.ScalarAsync<int>("SELECT [Revision] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id))));
    }

    [SqlServerFact]
    public async Task The_revision_reason_migration_refuses_to_roll_back_while_any_reason_exists()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k", "6");
        var lineId = Assert.Single(draft.Lines).Id;
        await using (var scope = run.SalesStore(CompanyA))
        {
            await scope.Store.ReviseDraftQuantityAsync(run.SalesEnvelope("k-quantity"), Hash("k-quantity"), SalesDraftCommands.ReviseQuantityV1, draft.Id,
                (d, now) => d.ReviseQuantities(4, [new LineQuantityInput(lineId, "8", 8m)], "SENTETIK-reviser", "SENTETIK neden", now), Revised,
                (_, _) => Task.CompletedTask);
        }

        await using var context = run.SalesContext(CompanyA);
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        var revisionReasons = Array.FindIndex(applied,
            migration => migration.EndsWith("_AddRevisionReasons", StringComparison.Ordinal));
        Assert.True(revisionReasons > 0);
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(applied[revisionReasons]);
        Assert.Equal(applied[..(revisionReasons + 1)], await context.Database.GetAppliedMigrationsAsync());
        var error = await Assert.ThrowsAsync<SqlException>(() => migrator.MigrateAsync(applied[revisionReasons - 1]));
        Assert.Equal(51057, error.Number);
        Assert.Equal(applied[..(revisionReasons + 1)], await context.Database.GetAppliedMigrationsAsync());
        await context.Database.MigrateAsync();
        Assert.Equal(applied, await context.Database.GetAppliedMigrationsAsync());
        // The rebuilt order carries its archived Rev.0 with the reason (the store's restore reads it back through the domain).
        await using var reader = run.SalesStore(CompanyA);
        var reread = await reader.Store.FindDraftAsync(draft.Id);
        Assert.Equal((1, "SENTETIK neden", "6", "8"), (reread!.Revision, reread.ArchivedRevisions[0].Reason, reread.ArchivedRevisions[0].Lines[0].QuantityText,
            reread.Lines[0].QuantityText));
    }

    // ---- helpers --------------------------------------------------------------------------------

    private static HttpRequestMessage Revise(string path, string key, string expectedVersion, string reason, Guid lineId, string quantity)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(
                new SalesDraftQuantityRevisionRequest(expectedVersion, reason, [new SalesDraftLineQuantityV1(lineId, quantity)]), SalesDraftJson.Options),
                Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.Add("X-Correlation-Id", $"SENTETIK-{Guid.NewGuid():N}");
        return request;
    }

    private static HttpRequestMessage Post<T>(string path, string key, T body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, SalesDraftJson.Options), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.Add("X-Correlation-Id", $"SENTETIK-{Guid.NewGuid():N}");
        return request;
    }

    private static HttpRequestMessage State(string path, string key, string expectedVersion)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(new SalesDraftStateCommandRequest(expectedVersion), SalesDraftJson.Options),
                Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.Add("X-Correlation-Id", $"SENTETIK-{Guid.NewGuid():N}");
        return request;
    }

    private static LogisticsQuantity Kg(decimal value) => LogisticsQuantity.Of(value, LogisticsQuantityUnit.Kilogram);

    private static string Hash(string seed) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));

    /// <summary>A one-line draft (the given kg quantity, no slices) from SYNTHETIC owner snapshots in the approved kg unit: version 2.</summary>
    private static SalesOrderDraft Draft(Guid company, string documentNo, string quantity)
    {
        var header = new SalesDraftHeader(Guid.NewGuid(), Guid.NewGuid(), Currency, Day, Day.AddDays(10));
        var money = new MoneyProfileSnapshot(company, Currency, SalesOrderDraft.MoneyOperation, MoneyProfile, "3", Day, true, From, null, 2);
        var draft = SalesOrderDraft.Create(Guid.NewGuid(), company, documentNo, header, money, T0);
        var unit = new UnitProfileSnapshot(company, KgUnit, "kg", "weight", KgProfile, "4", true, 3, 3, true, From, null);
        var binding = new UnitBindingSnapshot(company, KgUnit, KgProfile, "4");
        var product = new ProductSnapshot(Product, "7", "GDC-001", "Sentetik bant", Day, binding, binding, binding);
        draft.AddLine(draft.Version, new SalesDraftLineInput(Guid.NewGuid(), product, unit, quantity,
            decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture), Day.AddDays(3)), T0);
        return draft;
    }

    /// <summary>(sales attempts, audit events, archived revisions, draft version) as SQL Server holds them.</summary>
    private sealed record Effects(int SalesAttempts, int AuditEvents, int Revisions, long DraftVersion);

    private sealed class SalesScope(SalesDbContext dbContext, SalesStore store) : IAsyncDisposable
    {
        public ISalesOrderDraftStore Store => store;

        public ValueTask DisposeAsync() => dbContext.DisposeAsync();
    }

    private sealed class LogisticsScope(LogisticsDbContext dbContext, LogisticsStore store) : IAsyncDisposable
    {
        public ILogisticsInstructionStore Store => store;

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
        public string Correlation { get; } = $"sales-dec-sal-002-{Guid.NewGuid():N}";

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
        public async Task<SalesOrderDraft> ApprovedDraftAsync(Guid company, string keyPrefix, string quantity)
        {
            await using var scope = SalesStore(company);
            var draft = (await scope.Store.StoreDraftAsync(SalesEnvelope($"{keyPrefix}-create"), Hash($"{keyPrefix}-create"),
                documentNo => Draft(company, documentNo, quantity))).Draft;
            await scope.Store.UpdateDraftStateAsync(SalesEnvelope($"{keyPrefix}-submit"), Hash($"{keyPrefix}-submit"), SalesDraftCommands.SubmitV1, draft.Id,
                (d, now) => d.Submit(2, Actor, now), Submitted);
            var approved = await scope.Store.UpdateDraftStateAsync(SalesEnvelope($"{keyPrefix}-approve", actor: "SENTETIK-approver"), Hash($"{keyPrefix}-approve"),
                SalesDraftCommands.ApproveV1, draft.Id, (d, now) => d.Approve(3, "SENTETIK-approver", now), Approved);
            return approved!.Draft;
        }

        /// <summary>
        /// An instruction of <paramref name="company"/> for the draft's one line (6 kg instructed) created, issued, loaded
        /// <paramref name="loaded"/> kg and shipped <paramref name="shipped"/> kg through the real Logistics store.
        /// </summary>
        public async Task ShippedAsync(Guid company, SalesOrderDraft draft, string keyPrefix, decimal loaded, decimal shipped)
        {
            var orderLine = Assert.Single(draft.Lines);
            await using var scope = LogisticsStore(company);
            var stored = (await scope.Store.StoreInstructionAsync(LogisticsEnvelope($"{keyPrefix}-l-create"), Hash($"{keyPrefix}-l-create"), Day, () =>
            {
                var instruction = LoadingInstruction.Create(Guid.NewGuid(), company, draft.Header.PartyId, draft.Header.CompanyAccountId, T0);
                instruction.AddLine(instruction.Version, Guid.NewGuid(), new SourceOrderLineRef(draft.Id, orderLine.Id, draft.Revision),
                    orderLine.Product.ProductId, KgUnit, null, Kg(6m), T0);
                return instruction;
            })).Instruction.Instruction;
            var lineId = Assert.Single(stored.Lines).Id;
            var issued = (await scope.Store.MutateInstructionAsync(LogisticsEnvelope($"{keyPrefix}-l-issue"), Hash($"{keyPrefix}-l-issue"),
                LogisticsCommands.IssueV1, stored.Id, (instruction, now) => instruction.Issue(stored.Version, now), Issued))!.Instruction.Instruction;
            var loadedInstruction = (await scope.Store.MutateInstructionAsync(LogisticsEnvelope($"{keyPrefix}-l-load"), Hash($"{keyPrefix}-l-load"),
                LogisticsCommands.RecordLoadV1, stored.Id, (instruction, now) => instruction.RecordLoad(issued.Version, Guid.NewGuid(), lineId, Kg(loaded),
                    MeasurementKind.Measured, now), LoadedAudit))!.Instruction.Instruction;
            await scope.Store.MutateInstructionAsync(LogisticsEnvelope($"{keyPrefix}-l-ship"), Hash($"{keyPrefix}-l-ship"),
                LogisticsCommands.RecordShipmentV1, stored.Id, (instruction, now) => instruction.MarkShipped(loadedInstruction.Version, Guid.NewGuid(), lineId,
                    Kg(shipped), now), ShippedAudit);
        }

        /// <summary>
        /// SAT-D103: the Sales line port answers the LAST APPROVED Rev.0 (6 kg) with <paramref name="cap"/> and the pending state,
        /// on the direct read, the loading read (the loading limit) and the picker list.
        /// </summary>
        public async Task AssertShippingAsync(Guid company, Guid orderId, Guid lineId, string pendingState, string cap, long version)
        {
            await using var scope = SalesStore(company);
            var context = new CompanyContext();
            context.Set(company);
            var port = new SalesOrderLineReferencePort(scope.Store, context);
            var answer = await port.GetApprovedLineForLoadingAsync(orderId, lineId, Day);
            Assert.Equal((SalesOrderLineReferenceOutcomeV1.Approved, 0, "6", cap, SalesOrderLineRevisionBasisV1.LastApprovedRevision, pendingState, version),
                (answer.Outcome, answer.Line!.Revision, answer.Line.QuantityText, answer.Line.ShippingCapQuantityText, answer.Line.RevisionBasis,
                    answer.Line.PendingApprovalState, answer.Line.Version));
            Assert.Equal((cap, SalesOrderLineLoadingLimitSourcesV1.ReapprovalShippingCap, version),
                (answer.Line.LoadingLimit!.MaximumAggregateQuantityText, answer.Line.LoadingLimit.SourceOwner, answer.Line.LoadingLimit.SalesOrderVersion));
            var listed = Assert.Single((await port.ListApprovedLinesAsync(100, null)).Items, item => item.OrderId == orderId);
            Assert.Equal((0, "6", cap, pendingState), (listed.Revision, listed.QuantityText, listed.ShippingCapQuantityText, listed.PendingApprovalState));
        }

        public async Task<JsonElement> JsonAsync(HttpClient client, string path)
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }

        public async Task<Effects> EffectsAsync(Guid draftId) => new Effects(
            await ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftAttempts]"),
            await ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents]"),
            await ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftRevisions]"),
            await ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draftId)));

        /// <summary>The Sales line port (what Inventory and Logistics ask) over the real store of the company.</summary>
        public async Task<SalesOrderLineReferenceAnswerV1> LineAsync(Guid company, Guid orderId, Guid lineId)
        {
            await using var scope = SalesStore(company);
            var context = new CompanyContext();
            context.Set(company);
            return await new SalesOrderLineReferencePort(scope.Store, context).GetApprovedLineAsync(orderId, lineId);
        }

        /// <summary>The approved lines a consumer's picker is offered (the Logistics LOG-4b list), over the real store.</summary>
        public async Task<IReadOnlyList<SalesApprovedLineReferenceV1>> ApprovedLinesAsync(Guid company)
        {
            await using var scope = SalesStore(company);
            var context = new CompanyContext();
            context.Set(company);
            return (await new SalesOrderLineReferencePort(scope.Store, context).ListApprovedLinesAsync(100, null)).Items;
        }

        public SalesDraftWriteEnvelope SalesEnvelope(string key, string? actor = null) =>
            new SalesDraftWriteEnvelope(actor ?? Actor, "SENTETIK-sales-permission", ["SENTETIK-SALES"], Correlation, null, key, "Web", "127.0.0.1", "SENTETIK-agent");

        public LogisticsWriteEnvelope LogisticsEnvelope(string key) =>
            new LogisticsWriteEnvelope(Actor, "SENTETIK-logistics-permission", ["SENTETIK-LOGISTICS"], Correlation, null, key, "Web", "127.0.0.1", "SENTETIK-agent");

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
                .AddInterceptors(new GoymenERP.Modules.Audit.Infrastructure.Persistence.AuditSaveChangesInterceptor(collector, companyContext)).Options,
                companyContext);
            return new SalesScope(dbContext, new SalesStore(dbContext, companyContext, collector, TimeProvider.System));
        }

        public LogisticsScope LogisticsStore(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var collector = new AuditEventCollector();
            var dbContext = new LogisticsDbContext(new DbContextOptionsBuilder<LogisticsDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", LogisticsDbContext.Schema))
                .AddInterceptors(new GoymenERP.Modules.Audit.Infrastructure.Persistence.AuditSaveChangesInterceptor(collector, companyContext)).Options,
                companyContext);
            return new LogisticsScope(dbContext, new LogisticsStore(dbContext, companyContext, collector, TimeProvider.System));
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
                dbContext.Users.Add(new UserAccount(actor, "Synthetic Sales revision user", null));
                dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer, developmentUser));
            }
            else
            {
                actor = link.UserId;
            }

            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(roleId, $"DEC-SAL-002-{roleId:N}", "Synthetic Sales revision role", true);
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
