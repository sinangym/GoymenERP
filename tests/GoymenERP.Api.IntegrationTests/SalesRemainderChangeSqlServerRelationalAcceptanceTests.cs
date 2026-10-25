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
using Microsoft.Extensions.DependencyInjection;
using LogisticsQuantity = GoymenERP.Modules.Logistics.Domain.Quantities.Quantity;
using LogisticsQuantityUnit = GoymenERP.Modules.Logistics.Domain.Quantities.QuantityUnit;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// Partial delivery remainder change (user decision 2026-09-30, AT-024 "Kısmi teslimat sipariş kalanını doğru tutar",
/// DEC-SAL-002, DEC-SAL-005) against a REAL SQL Server and the REAL host composition: the request through the real route is
/// ONE save (the change row with the delivery owner's floor it was asked on, the header's version, the attempt and the audit
/// event) and moves nothing on the line; another actor's approval re-reads the floor from the REAL Logistics owner port and
/// applies the change in ONE save (the decision row, the line's quantity, the version, the attempt and the audit event) —
/// never below what is loaded; a rejection stores its reason and leaves the line; one pending change per order, a stale
/// version and a decided change are the key's recorded 409s; a replay adds no row; another company sees and decides nothing.
/// <para>
/// The Sales draft is stored and approved through the real Sales store from ISOLATED SYNTHETIC owner snapshots (the
/// AT-024 / DEC-SAL-002 precedent); the instruction is created, issued, loaded, shipped and delivered through the real
/// Logistics store (the delivery route is the delivery-progress acceptance's); the floor and the allocations are read
/// through the REAL Logistics and Inventory owner ports the host composes. Every grant is a fresh synthetic role; the
/// requester holds the quantity-write name only and the approver the approve name only. SENTETIK / NOT_ENV.
/// </para>
/// </summary>
public sealed class SalesRemainderChangeSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CompanyA = TestCompanyIds.CompanyA;
    private static readonly Guid CompanyB = TestCompanyIds.CompanyB;
    private static readonly DateOnly Day = new(2026, 9, 19);
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateTimeOffset T0 = new(2026, 9, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid KgUnit = SettingsUnitIdentitiesV1.Kilogram;
    private static readonly Guid Currency = Guid.Parse("9c3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7f01");
    private static readonly Guid MoneyProfile = Guid.Parse("9c3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7f02");
    private static readonly Guid KgProfile = Guid.Parse("9c3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7f03");
    private static readonly Guid Product = Guid.Parse("9c3f4e5d-6c7b-48a9-9c2d-3e4f5a6b7f04");

    private const string RequestedEvent = "sales.order-draft.remainder-change-requested.v1";
    private const string ApprovedEvent = "sales.order-draft.remainder-change-approved.v1";
    private const string RejectedEvent = "sales.order-draft.remainder-change-rejected.v1";

    private static readonly SalesDraftStateAudit Submitted = new("sales.order-draft.submitted.v1", "Submit", "SENTETIK onaya gönderildi");
    private static readonly SalesDraftStateAudit Approved = new("sales.order-draft.approved.v1", "Approve", "SENTETIK onaylandı");
    private static readonly LogisticsMutationAudit Issued = new("logistics.loading-instruction.issued.v1", "Issue", "SENTETIK yayımlandı");
    private static readonly LogisticsMutationAudit Loaded = new("logistics.loading-instruction.load-recorded.v1", "RecordLoad", "SENTETIK yüklendi");
    private static readonly LogisticsMutationAudit Shipped = new("logistics.loading-instruction.shipment-recorded.v1", "RecordShipment", "SENTETIK sevk edildi");
    private static readonly LogisticsMutationAudit Delivered = new("logistics.loading-instruction.delivery-recorded.v1", "RecordDelivery", "SENTETIK teslim edildi");

    [SqlServerFact]
    public async Task A_revised_remainder_is_requested_then_approved_by_another_actor_on_the_loaded_floor_in_one_save_each_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k", "10");
        var orderLine = Assert.Single(draft.Lines);
        await run.PartlyDeliveredAsync(CompanyA, draft, "k", loaded: 6m, shipped: 4m, delivered: 2.5m);
        using var requester = run.Client(await run.GrantAsync(TestUsers.CompanyA, CompanyA,
            SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersQuantityWrite), CompanyA);
        using var approver = run.Client(await run.GrantAsync(TestUsers.CompanyAViewer, CompanyA,
            SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersApprove), CompanyA);
        var path = $"{SalesEndpoints.DraftsPath}/{draft.Id:D}/remainder-changes";

        // The screen's read: the owner's recorded totals afresh, the loaded total as the minimum; nothing recorded yet.
        var before = await run.JsonAsync(requester, path);
        Assert.Equal(("4", SalesRemainderChangeNamesV1.Readable, 0), (before.GetProperty("draftVersion").GetString(),
            before.GetProperty("floorStatus").GetString(), before.GetProperty("items").GetArrayLength()));
        Assert.Equal(("10", SalesRemainderChangeNamesV1.Open, "6", "4", "2.5", "6"), RemainderLine(before, orderLine.Id));

        // Below the loaded floor: the rule's 422 on the quantity, not recorded (the floor may move).
        using (var belowFloor = await requester.SendAsync(Change(path, "SENTETIK-below", "4", orderLine.Id, SalesRemainderChangeNamesV1.Revise, "5.5", "neden")))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, belowFloor.StatusCode);
            var problem = await belowFloor.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(("quantity", SalesRuleCodes.RemainderBelowFloor), (problem.GetProperty("errors")[0].GetProperty("field").GetString(),
                problem.GetProperty("errors")[0].GetProperty("code").GetString()));
        }

        Assert.Equal(0, await run.AttemptsAsync("SENTETIK-below"));

        using (var requested = await requester.SendAsync(Change(path, "SENTETIK-request", "4", orderLine.Id, SalesRemainderChangeNamesV1.Revise, "8",
                   "  SENTETIK müşteri kalanı 8 kg'a indirdi ")))
        {
            Assert.Equal(HttpStatusCode.OK, requested.StatusCode);
            Assert.False(requested.Headers.Contains("Idempotency-Replayed"));
            var detail = (await requested.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail");
            Assert.Equal(("Approved", "5", "Rev.0", "10"), (detail.GetProperty("draft").GetProperty("approvalState").GetString(),
                detail.GetProperty("draft").GetProperty("version").GetString(), detail.GetProperty("draft").GetProperty("revision").GetString(),
                detail.GetProperty("lines")[0].GetProperty("quantity").GetString()));
        }

        // ONE save, as SQL Server holds it: the change row with the floor it was asked on, nothing on the line, version 4 -> 5,
        // the approval axis and the revision unmoved, the attempt and the audit event.
        Assert.Equal((1, 2, "10", "8", 8m, "SENTETIK müşteri kalanı 8 kg'a indirdi", 5L, 0), (
            await run.ScalarAsync<int>("SELECT [ChangeNo] FROM [sales].[DraftRemainderChanges] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT [Kind] FROM [sales].[DraftRemainderChanges] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<string>("SELECT [OrderedQuantityText] FROM [sales].[DraftRemainderChanges] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<string>("SELECT [RequestedQuantityText] FROM [sales].[DraftRemainderChanges] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<decimal>("SELECT [RequestedQuantity] FROM [sales].[DraftRemainderChanges] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<string>("SELECT [Reason] FROM [sales].[DraftRemainderChanges] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<long>("SELECT [DraftVersion] FROM [sales].[DraftRemainderChanges] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftRemainderDecisions] WHERE [DraftId] = @id", ("@id", draft.Id))));
        Assert.Equal(new Floor(6m, 4m, 2.5m), await run.RequestFloorAsync(draft.Id, 1));
        Assert.Equal(new Header(5L, (int)InternalApprovalState.Approved, 0, "10", 10m), await run.HeaderAsync(draft.Id));
        Assert.Equal(1, await run.AuditAsync(RequestedEvent, draft.Id, 4, 5));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [CommandName] = @c AND [Outcome] = 1 AND [DraftId] = @id",
            ("@c", SalesDraftCommands.RequestRemainderChangeV1), ("@id", draft.Id)));
        var requestedBy = await run.ScalarAsync<string>("SELECT [RequestedBy] FROM [sales].[DraftRemainderChanges] WHERE [DraftId] = @id", ("@id", draft.Id));
        Assert.False(string.IsNullOrWhiteSpace(requestedBy));

        // While pending the consumers still see the approved, untouched line.
        var pending = await run.LineAsync(CompanyA, draft.Id, orderLine.Id);
        Assert.Equal((SalesOrderLineReferenceOutcomeV1.Approved, "10", SalesRemainderChangeNamesV1.Open),
            (pending.Outcome, pending.Line!.QuantityText, pending.Line.RemainderState));

        // The approver lists it as Pending with the floor it was asked on.
        var listed = Assert.Single((await run.JsonAsync(approver, path)).GetProperty("items").EnumerateArray());
        Assert.Equal((1, SalesRemainderChangeNamesV1.Revise, SalesRemainderChangeNamesV1.Pending, "10", "8", requestedBy, "6", "4", "2.5"),
            (listed.GetProperty("changeNo").GetInt32(), listed.GetProperty("kind").GetString(), listed.GetProperty("state").GetString(),
                listed.GetProperty("orderedQuantity").GetString(), listed.GetProperty("requestedQuantity").GetString(),
                listed.GetProperty("requestedBy").GetString(), listed.GetProperty("loadedAtRequest").GetString(),
                listed.GetProperty("shippedAtRequest").GetString(), listed.GetProperty("deliveredAtRequest").GetString()));
        Assert.Equal(JsonValueKind.Null, listed.GetProperty("decidedBy").ValueKind);

        // The requester holds no approve name: 403, nothing recorded.
        using (var own = await requester.SendAsync(Decision($"{path}/1/approval", "SENTETIK-own-approval", "5")))
        {
            Assert.Equal(HttpStatusCode.Forbidden, own.StatusCode);
        }

        Assert.Equal(0, await run.AttemptsAsync("SENTETIK-own-approval"));

        // Another actor approves: the floor is read afresh from the real Logistics port and the line moves to 8 in one save.
        using (var approved = await approver.SendAsync(Decision($"{path}/1/approval", "SENTETIK-approve", "5")))
        {
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
            var detail = (await approved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail");
            Assert.Equal(("Approved", "6", "Rev.0", "8"), (detail.GetProperty("draft").GetProperty("approvalState").GetString(),
                detail.GetProperty("draft").GetProperty("version").GetString(), detail.GetProperty("draft").GetProperty("revision").GetString(),
                detail.GetProperty("lines")[0].GetProperty("quantity").GetString()));
        }

        Assert.Equal(new Header(6L, (int)InternalApprovalState.Approved, 0, "8", 8m), await run.HeaderAsync(draft.Id));
        Assert.Equal((2, "8", 8m, 6L), (
            await run.ScalarAsync<int>("SELECT [Outcome] FROM [sales].[DraftRemainderDecisions] WHERE [DraftId] = @id AND [ChangeNo] = 1", ("@id", draft.Id)),
            await run.ScalarAsync<string>("SELECT [ResultQuantityText] FROM [sales].[DraftRemainderDecisions] WHERE [DraftId] = @id AND [ChangeNo] = 1", ("@id", draft.Id)),
            await run.ScalarAsync<decimal>("SELECT [ResultQuantity] FROM [sales].[DraftRemainderDecisions] WHERE [DraftId] = @id AND [ChangeNo] = 1", ("@id", draft.Id)),
            await run.ScalarAsync<long>("SELECT [DraftVersion] FROM [sales].[DraftRemainderDecisions] WHERE [DraftId] = @id AND [ChangeNo] = 1", ("@id", draft.Id))));
        Assert.Equal(new Floor(6m, 4m, 2.5m), await run.DecisionFloorAsync(draft.Id, 1));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [sales].[DraftRemainderDecisions] WHERE [DraftId] = @id AND [Reason] IS NULL", ("@id", draft.Id)));
        var decidedBy = await run.ScalarAsync<string>("SELECT [DecidedBy] FROM [sales].[DraftRemainderDecisions] WHERE [DraftId] = @id", ("@id", draft.Id));
        Assert.False(string.IsNullOrWhiteSpace(decidedBy));
        Assert.NotEqual(requestedBy, decidedBy);
        Assert.Equal(1, await run.AuditAsync(ApprovedEvent, draft.Id, 5, 6));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [CommandName] = @c AND [Outcome] = 1 AND [DraftId] = @id",
            ("@c", SalesDraftCommands.ApproveRemainderChangeV1), ("@id", draft.Id)));
        // Sales moved nothing of Logistics: the loaded/shipped/delivered facts stay the owner's.
        Assert.Equal((6m, 4m, 2.5m), (
            await run.ScalarAsync<decimal>("SELECT [Loaded] FROM [logistics].[LoadingInstructionLines] WHERE [SourceOrderId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<decimal>("SELECT [Shipped] FROM [logistics].[LoadingInstructionLines] WHERE [SourceOrderId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<decimal>("SELECT [Delivered] FROM [logistics].[LoadingInstructionLines] WHERE [SourceOrderId] = @id", ("@id", draft.Id))));

        // Reopened through a fresh store: the restored order carries the Revised remainder and the decided change.
        await using (var scope = run.SalesStore(CompanyA))
        {
            var reread = await scope.Store.FindDraftAsync(draft.Id);
            var change = Assert.Single(reread!.RemainderChanges);
            Assert.Equal((LineRemainderState.Revised, "8", RemainderChangeState.Approved, 6L, InternalApprovalState.Approved, 0),
                (reread.Lines[0].Remainder, reread.Lines[0].QuantityText, change.State, reread.Version, reread.ApprovalState, reread.Revision));
        }

        // Downstream reads the capped quantity at the same revision; the screen shows the decision.
        var answer = await run.LineAsync(CompanyA, draft.Id, orderLine.Id);
        Assert.Equal((SalesOrderLineReferenceOutcomeV1.Approved, "8", SalesRemainderChangeNamesV1.Revised, 0),
            (answer.Outcome, answer.Line!.QuantityText, answer.Line.RemainderState, answer.Line.Revision));
        var after = await run.JsonAsync(approver, path);
        Assert.Equal(("8", SalesRemainderChangeNamesV1.Revised, "6", "4", "2.5", "6"), RemainderLine(after, orderLine.Id));
        var decided = Assert.Single(after.GetProperty("items").EnumerateArray());
        Assert.Equal((SalesRemainderChangeNamesV1.Approved, decidedBy, "8", "6"), (decided.GetProperty("state").GetString(),
            decided.GetProperty("decidedBy").GetString(), decided.GetProperty("resultQuantity").GetString(),
            decided.GetProperty("loadedAtDecision").GetString()));
    }

    [SqlServerFact]
    public async Task A_cancelled_remainder_ends_the_line_at_the_floor_read_afresh_at_the_approval_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k", "10");
        var orderLine = Assert.Single(draft.Lines);
        var (instructionId, instructionLineId, instructionVersion) =
            await run.PartlyDeliveredAsync(CompanyA, draft, "k", loaded: 5m, shipped: 4m, delivered: 2.5m);
        using var requester = run.Client(await run.GrantAsync(TestUsers.CompanyA, CompanyA,
            SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersQuantityWrite), CompanyA);
        using var approver = run.Client(await run.GrantAsync(TestUsers.CompanyAViewer, CompanyA,
            SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersApprove), CompanyA);
        var path = $"{SalesEndpoints.DraftsPath}/{draft.Id:D}/remainder-changes";

        using (var requested = await requester.SendAsync(Change(path, "SENTETIK-cancel", "4", orderLine.Id, SalesRemainderChangeNamesV1.Cancel, null,
                   "SENTETIK müşteri kalanı istemiyor")))
        {
            Assert.Equal(HttpStatusCode.OK, requested.StatusCode);
        }

        Assert.Equal(new Floor(5m, 4m, 2.5m), await run.RequestFloorAsync(draft.Id, 1));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [sales].[DraftRemainderChanges] WHERE [DraftId] = @id AND [Kind] = 1 AND [RequestedQuantity] IS NULL AND [RequestedQuantityText] IS NULL",
            ("@id", draft.Id)));
        Assert.Equal(new Header(5L, (int)InternalApprovalState.Approved, 0, "10", 10m), await run.HeaderAsync(draft.Id));

        // Logistics loads one more kilogram before the decision: the approval must keep it (the floor is read again).
        await run.LoadMoreAsync(CompanyA, instructionId, instructionLineId, instructionVersion, "k", 1m);

        using (var approved = await approver.SendAsync(Decision($"{path}/1/approval", "SENTETIK-approve-cancel", "5")))
        {
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
            Assert.Equal("6", (await approved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetProperty("lines")[0]
                .GetProperty("quantity").GetString());
        }

        Assert.Equal(new Header(6L, (int)InternalApprovalState.Approved, 0, "6", 6m), await run.HeaderAsync(draft.Id));
        Assert.Equal(new Floor(6m, 4m, 2.5m), await run.DecisionFloorAsync(draft.Id, 1));
        Assert.Equal(("6", 6m), (
            await run.ScalarAsync<string>("SELECT [ResultQuantityText] FROM [sales].[DraftRemainderDecisions] WHERE [DraftId] = @id AND [ChangeNo] = 1", ("@id", draft.Id)),
            await run.ScalarAsync<decimal>("SELECT [ResultQuantity] FROM [sales].[DraftRemainderDecisions] WHERE [DraftId] = @id AND [ChangeNo] = 1", ("@id", draft.Id))));
        Assert.Equal(1, await run.AuditAsync(ApprovedEvent, draft.Id, 5, 6));

        // The cancelled line is still the approved order's line at its new quantity, but is no longer offered for loading.
        var answer = await run.LineAsync(CompanyA, draft.Id, orderLine.Id);
        Assert.Equal((SalesOrderLineReferenceOutcomeV1.Approved, "6", SalesRemainderChangeNamesV1.Cancelled),
            (answer.Outcome, answer.Line!.QuantityText, answer.Line.RemainderState));
        Assert.DoesNotContain(await run.ApprovedLinesAsync(CompanyA), item => item.OrderId == draft.Id);
        Assert.Equal(("6", SalesRemainderChangeNamesV1.Cancelled, "6", "4", "2.5", "6"), RemainderLine(await run.JsonAsync(approver, path), orderLine.Id));

        // A cancelled remainder does not reopen: a further request is the key's recorded state conflict.
        using (var reopened = await requester.SendAsync(Change(path, "SENTETIK-reopen", "6", orderLine.Id, SalesRemainderChangeNamesV1.Revise, "7", "neden")))
        {
            Assert.Equal(HttpStatusCode.Conflict, reopened.StatusCode);
            Assert.Equal(SalesDraftProblemCodesV1.StateConflict, (await reopened.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftRemainderChanges] WHERE [DraftId] = @id", ("@id", draft.Id)));
    }

    [SqlServerFact]
    public async Task A_rejected_remainder_change_stores_its_reason_and_leaves_the_order_quantity_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k", "10");
        var orderLine = Assert.Single(draft.Lines);
        await run.PartlyDeliveredAsync(CompanyA, draft, "k", loaded: 6m, shipped: 4m, delivered: 2.5m);
        using var requester = run.Client(await run.GrantAsync(TestUsers.CompanyA, CompanyA,
            SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersQuantityWrite), CompanyA);
        using var approver = run.Client(await run.GrantAsync(TestUsers.CompanyAViewer, CompanyA,
            SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersApprove), CompanyA);
        var path = $"{SalesEndpoints.DraftsPath}/{draft.Id:D}/remainder-changes";

        using (var requested = await requester.SendAsync(Change(path, "SENTETIK-request", "4", orderLine.Id, SalesRemainderChangeNamesV1.Revise, "7",
                   "SENTETIK kalan 7 kg")))
        {
            Assert.Equal(HttpStatusCode.OK, requested.StatusCode);
        }

        using (var rejected = await approver.SendAsync(Decision($"{path}/1/rejection", "SENTETIK-reject", "5", "  SENTETIK müşteri sözleşme miktarında ısrarlı ")))
        {
            Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
            var detail = (await rejected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail");
            Assert.Equal(("6", "10"), (detail.GetProperty("draft").GetProperty("version").GetString(),
                detail.GetProperty("lines")[0].GetProperty("quantity").GetString()));
        }

        // ONE save: the decision row (reason trimmed, no result, no floor), the version, the attempt and the audit event;
        // the line keeps its quantity.
        Assert.Equal(new Header(6L, (int)InternalApprovalState.Approved, 0, "10", 10m), await run.HeaderAsync(draft.Id));
        Assert.Equal((3, "SENTETIK müşteri sözleşme miktarında ısrarlı", 6L), (
            await run.ScalarAsync<int>("SELECT [Outcome] FROM [sales].[DraftRemainderDecisions] WHERE [DraftId] = @id AND [ChangeNo] = 1", ("@id", draft.Id)),
            await run.ScalarAsync<string>("SELECT [Reason] FROM [sales].[DraftRemainderDecisions] WHERE [DraftId] = @id AND [ChangeNo] = 1", ("@id", draft.Id)),
            await run.ScalarAsync<long>("SELECT [DraftVersion] FROM [sales].[DraftRemainderDecisions] WHERE [DraftId] = @id AND [ChangeNo] = 1", ("@id", draft.Id))));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [sales].[DraftRemainderDecisions] WHERE [DraftId] = @id AND [ResultQuantity] IS NULL AND [ResultQuantityText] IS NULL "
            + "AND [LoadedQuantity] IS NULL AND [ShippedQuantity] IS NULL AND [DeliveredQuantity] IS NULL", ("@id", draft.Id)));
        Assert.Equal(1, await run.AuditAsync(RejectedEvent, draft.Id, 5, 6));
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = @e AND [EntityId] = @id", ("@e", ApprovedEvent), ("@id", draft.Id.ToString("D"))));

        var answer = await run.LineAsync(CompanyA, draft.Id, orderLine.Id);
        Assert.Equal(("10", SalesRemainderChangeNamesV1.Open), (answer.Line!.QuantityText, answer.Line.RemainderState));
        var read = await run.JsonAsync(requester, path);
        Assert.Equal(("10", SalesRemainderChangeNamesV1.Open, "6", "4", "2.5", "6"), RemainderLine(read, orderLine.Id));
        var item = Assert.Single(read.GetProperty("items").EnumerateArray());
        Assert.Equal((SalesRemainderChangeNamesV1.Rejected, "SENTETIK müşteri sözleşme miktarında ısrarlı"),
            (item.GetProperty("state").GetString(), item.GetProperty("decisionReason").GetString()));
        Assert.Equal((JsonValueKind.Null, JsonValueKind.Null), (item.GetProperty("resultQuantity").ValueKind, item.GetProperty("loadedAtDecision").ValueKind));

        // After the rejection the order is free for a new request: change 2, against version 6.
        using (var again = await requester.SendAsync(Change(path, "SENTETIK-request-2", "6", orderLine.Id, SalesRemainderChangeNamesV1.Revise, "9", "SENTETIK yeni talep")))
        {
            Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        }

        Assert.Equal((2, 7L), (
            await run.ScalarAsync<int>("SELECT MAX([ChangeNo]) FROM [sales].[DraftRemainderChanges] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draft.Id))));
    }

    [SqlServerFact]
    public async Task One_pending_change_per_order_stale_versions_and_a_decided_change_are_terminal_409s_and_a_replay_adds_no_row_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k", "10");
        var orderLine = Assert.Single(draft.Lines);
        await run.PartlyDeliveredAsync(CompanyA, draft, "k", loaded: 6m, shipped: 4m, delivered: 2.5m);
        using var requester = run.Client(await run.GrantAsync(TestUsers.CompanyA, CompanyA,
            SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersQuantityWrite), CompanyA);
        using var approver = run.Client(await run.GrantAsync(TestUsers.CompanyAViewer, CompanyA,
            SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersApprove), CompanyA);
        var path = $"{SalesEndpoints.DraftsPath}/{draft.Id:D}/remainder-changes";

        var firstCorrelation = $"SENTETIK-{Guid.NewGuid():N}";
        using (var requested = await requester.SendAsync(Change(path, "SENTETIK-request", "4", orderLine.Id, SalesRemainderChangeNamesV1.Revise, "8",
                   "SENTETIK kalan", firstCorrelation)))
        {
            Assert.Equal(HttpStatusCode.OK, requested.StatusCode);
        }

        // The same key and content: the stored result again — no second change row, attempt, audit event or version step.
        var afterRequest = await run.EffectsAsync(draft.Id);
        using (var replayed = await requester.SendAsync(Change(path, "SENTETIK-request", "4", orderLine.Id, SalesRemainderChangeNamesV1.Revise, "8", "SENTETIK kalan")))
        {
            Assert.Equal(HttpStatusCode.OK, replayed.StatusCode);
            Assert.Equal("true", Assert.Single(replayed.Headers.GetValues("Idempotency-Replayed")));
            Assert.Equal(firstCorrelation, Assert.Single(replayed.Headers.GetValues("Idempotency-Original-Correlation-Id")));
            Assert.Equal("5", (await replayed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetProperty("draft")
                .GetProperty("version").GetString());
        }

        Assert.Equal(afterRequest, await run.EffectsAsync(draft.Id));
        Assert.Equal((1, 1, 0, 5L), (afterRequest.AuditEvents, afterRequest.Changes, afterRequest.Decisions, afterRequest.DraftVersion));

        // The same key with other content is refused; nothing is written.
        using (var mismatch = await requester.SendAsync(Change(path, "SENTETIK-request", "4", orderLine.Id, SalesRemainderChangeNamesV1.Revise, "9", "SENTETIK kalan")))
        {
            Assert.Equal(HttpStatusCode.Conflict, mismatch.StatusCode);
            Assert.Equal(SalesDraftProblemCodesV1.SameKeyPayloadMismatch, (await mismatch.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }

        Assert.Equal(afterRequest, await run.EffectsAsync(draft.Id));

        // A second request while one is pending: the key's recorded 409 state conflict, replayed as such.
        using (var second = await requester.SendAsync(Change(path, "SENTETIK-second", "5", orderLine.Id, SalesRemainderChangeNamesV1.Cancel, null, "SENTETIK ikinci")))
        {
            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
            var problem = await second.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal((SalesDraftProblemCodesV1.StateConflict, "remainder"), (problem.GetProperty("code").GetString(),
                problem.GetProperty("errors")[0].GetProperty("field").GetString()));
            Assert.False(second.Headers.Contains("Idempotency-Replayed"));
        }

        using (var secondReplayed = await requester.SendAsync(Change(path, "SENTETIK-second", "5", orderLine.Id, SalesRemainderChangeNamesV1.Cancel, null, "SENTETIK ikinci")))
        {
            Assert.Equal(HttpStatusCode.Conflict, secondReplayed.StatusCode);
            Assert.Equal("true", Assert.Single(secondReplayed.Headers.GetValues("Idempotency-Replayed")));
        }

        // A stale request and a stale decision: the documented 409 version conflict, recorded for their keys.
        using (var staleRequest = await requester.SendAsync(Change(path, "SENTETIK-stale-request", "4", orderLine.Id, SalesRemainderChangeNamesV1.Revise, "7", "SENTETIK")))
        {
            Assert.Equal(HttpStatusCode.Conflict, staleRequest.StatusCode);
            Assert.Equal(SalesDraftProblemCodesV1.VersionConflict, (await staleRequest.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }

        using (var staleDecision = await approver.SendAsync(Decision($"{path}/1/approval", "SENTETIK-stale-approval", "4")))
        {
            Assert.Equal(HttpStatusCode.Conflict, staleDecision.StatusCode);
            Assert.Equal(SalesDraftProblemCodesV1.VersionConflict, (await staleDecision.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }

        // A change number that is not THE pending change: 409 state conflict.
        using (var unknown = await approver.SendAsync(Decision($"{path}/2/approval", "SENTETIK-unknown-change", "5")))
        {
            Assert.Equal(HttpStatusCode.Conflict, unknown.StatusCode);
            Assert.Equal(SalesDraftProblemCodesV1.StateConflict, (await unknown.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }

        // Every refusal is a recorded terminal attempt (Outcome 2); nothing else moved: one pending change, the line at 10, version 5.
        Assert.Equal(4, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [Outcome] = 2 AND [DraftId] = @id", ("@id", draft.Id)));
        Assert.Equal((1, 0), (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftRemainderChanges] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftRemainderDecisions] WHERE [DraftId] = @id", ("@id", draft.Id))));
        Assert.Equal(new Header(5L, (int)InternalApprovalState.Approved, 0, "10", 10m), await run.HeaderAsync(draft.Id));

        // The approval itself, then its replay (no second decision) and a second decision on the decided change (terminal).
        using (var approved = await approver.SendAsync(Decision($"{path}/1/approval", "SENTETIK-approve", "5")))
        {
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        }

        var afterApproval = await run.EffectsAsync(draft.Id);
        using (var approvalReplayed = await approver.SendAsync(Decision($"{path}/1/approval", "SENTETIK-approve", "5")))
        {
            Assert.Equal(HttpStatusCode.OK, approvalReplayed.StatusCode);
            Assert.Equal("true", Assert.Single(approvalReplayed.Headers.GetValues("Idempotency-Replayed")));
        }

        Assert.Equal(afterApproval, await run.EffectsAsync(draft.Id));

        using (var decidedAgain = await approver.SendAsync(Decision($"{path}/1/rejection", "SENTETIK-reject-decided", "6", "SENTETIK geç ret")))
        {
            Assert.Equal(HttpStatusCode.Conflict, decidedAgain.StatusCode);
            Assert.Equal(SalesDraftProblemCodesV1.StateConflict, (await decidedAgain.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }

        Assert.Equal((1, 2), (
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftRemainderDecisions] WHERE [DraftId] = @id", ("@id", draft.Id)),
            await run.ScalarAsync<int>("SELECT [Outcome] FROM [sales].[DraftRemainderDecisions] WHERE [DraftId] = @id AND [ChangeNo] = 1", ("@id", draft.Id))));
        Assert.Equal(new Header(6L, (int)InternalApprovalState.Approved, 0, "8", 8m), await run.HeaderAsync(draft.Id));
    }

    [SqlServerFact]
    public async Task Another_company_cannot_read_request_or_decide_the_change_and_an_ungranted_actor_is_refused_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var draft = await run.ApprovedDraftAsync(CompanyA, "k", "10");
        var orderLine = Assert.Single(draft.Lines);
        await run.PartlyDeliveredAsync(CompanyA, draft, "k", loaded: 6m, shipped: 4m, delivered: 2.5m);
        using (var requester = run.Client(await run.GrantAsync(TestUsers.CompanyA, CompanyA,
                   SalesPermissionManifestV1.DraftsRead, SalesPermissionManifestV1.OrdersQuantityWrite), CompanyA))
        {
            using var requested = await requester.SendAsync(Change($"{SalesEndpoints.DraftsPath}/{draft.Id:D}/remainder-changes", "SENTETIK-request", "4",
                orderLine.Id, SalesRemainderChangeNamesV1.Revise, "8", "SENTETIK kalan"));
            Assert.Equal(HttpStatusCode.OK, requested.StatusCode);
        }

        var path = $"{SalesEndpoints.DraftsPath}/{draft.Id:D}/remainder-changes";
        var afterRequest = await run.EffectsAsync(draft.Id);

        // Company B with every name of the feature: A's order does not exist for it (404, never 403) and nothing is recorded.
        var subjectOfB = await run.GrantAsync(TestUsers.CompanyB, CompanyB, SalesPermissionManifestV1.DraftsRead,
            SalesPermissionManifestV1.OrdersQuantityWrite, SalesPermissionManifestV1.OrdersApprove);
        using (var clientOfB = run.Client(subjectOfB, CompanyB))
        {
            Assert.Equal(HttpStatusCode.NotFound, (await clientOfB.GetAsync(path)).StatusCode);
            using (var request = await clientOfB.SendAsync(Change(path, "SENTETIK-b-request", "5", orderLine.Id, SalesRemainderChangeNamesV1.Cancel, null, "SENTETIK b")))
            {
                Assert.Equal(HttpStatusCode.NotFound, request.StatusCode);
            }

            using (var approval = await clientOfB.SendAsync(Decision($"{path}/1/approval", "SENTETIK-b-approval", "5")))
            {
                Assert.Equal(HttpStatusCode.NotFound, approval.StatusCode);
            }

            using (var rejection = await clientOfB.SendAsync(Decision($"{path}/1/rejection", "SENTETIK-b-rejection", "5", "SENTETIK b")))
            {
                Assert.Equal(HttpStatusCode.NotFound, rejection.StatusCode);
            }
        }

        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [CompanyId] = @c", ("@c", CompanyB)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftRemainderChanges] WHERE [CompanyId] = @c", ("@c", CompanyB)));
        Assert.Equal(afterRequest, await run.EffectsAsync(draft.Id));

        // Company A's reader without the approve or quantity names: 403 before anything is read, nothing recorded.
        using (var reader = run.Client(await run.GrantAsync(TestUsers.CompanyAViewer, CompanyA, SalesPermissionManifestV1.DraftsRead), CompanyA))
        {
            using (var approval = await reader.SendAsync(Decision($"{path}/1/approval", "SENTETIK-reader-approval", "5")))
            {
                Assert.Equal(HttpStatusCode.Forbidden, approval.StatusCode);
            }

            using (var request = await reader.SendAsync(Change(path, "SENTETIK-reader-request", "5", orderLine.Id, SalesRemainderChangeNamesV1.Cancel, null, "SENTETIK")))
            {
                Assert.Equal(HttpStatusCode.Forbidden, request.StatusCode);
            }

            // The draft read alone lists A's own pending change.
            var item = Assert.Single((await run.JsonAsync(reader, path)).GetProperty("items").EnumerateArray());
            Assert.Equal(SalesRemainderChangeNamesV1.Pending, item.GetProperty("state").GetString());
        }

        Assert.Equal(afterRequest, await run.EffectsAsync(draft.Id));
        // The owner port under company B answers nothing of A's loading: the floor is per company.
        await using (var b = run.LogisticsContext(CompanyB))
        {
            Assert.Empty((await b.Port.ReadOrderLoadingAsync(draft.Id)).Lines);
        }
    }

    // ---- helpers --------------------------------------------------------------------------------

    private static (string Quantity, string Remainder, string? Loaded, string? Shipped, string? Delivered, string? Minimum) RemainderLine(
        JsonElement read, Guid lineId)
    {
        var line = Assert.Single(read.GetProperty("lines").EnumerateArray());
        Assert.Equal(lineId.ToString("D"), line.GetProperty("lineId").GetString());
        return (line.GetProperty("quantity").GetString()!, line.GetProperty("remainder").GetString()!, line.GetProperty("loaded").GetString(),
            line.GetProperty("shipped").GetString(), line.GetProperty("delivered").GetString(), line.GetProperty("minimum").GetString());
    }

    private static HttpRequestMessage Change(string path, string key, string expectedVersion, Guid lineId, string kind, string? quantity, string reason,
        string? correlation = null) =>
        Command(path, key, JsonSerializer.Serialize(new SalesDraftRemainderChangeRequest(expectedVersion, lineId, kind, quantity, reason), SalesDraftJson.Options),
            correlation);

    private static HttpRequestMessage Decision(string path, string key, string expectedVersion, string? reason = null) =>
        Command(path, key, JsonSerializer.Serialize(new SalesDraftRemainderDecisionRequest(expectedVersion, reason), SalesDraftJson.Options), null);

    private static HttpRequestMessage Command(string path, string key, string json, string? correlation)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.Add("X-Correlation-Id", correlation ?? $"SENTETIK-{Guid.NewGuid():N}");
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

    /// <summary>The owner's floor as a remainder row holds it (loaded, shipped, delivered).</summary>
    private sealed record Floor(decimal Loaded, decimal Shipped, decimal Delivered);

    /// <summary>(version, approval state, revision, line quantity text, line quantity) of the one-line draft as SQL Server holds it.</summary>
    private sealed record Header(long Version, int ApprovalState, int Revision, string QuantityText, decimal Quantity);

    /// <summary>(the draft's sales attempts, its remainder audit events, change rows, decision rows and version) as SQL Server holds them.</summary>
    private sealed record Effects(int SalesAttempts, int AuditEvents, int Changes, int Decisions, long DraftVersion);

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

    private sealed class LogisticsRead(LogisticsDbContext context, CompanyContext company) : IAsyncDisposable
    {
        public ISalesOrderLoadingReadPortV1 Port { get; } = new LogisticsSalesOrderLoadingReadPort(context, company);

        public ValueTask DisposeAsync() => context.DisposeAsync();
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
        public string Correlation { get; } = $"sales-remainder-{Guid.NewGuid():N}";

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
        /// An instruction of <paramref name="company"/> for the draft's one line (6 kg instructed), created, issued, loaded
        /// <paramref name="loaded"/> kg, shipped <paramref name="shipped"/> kg and delivered <paramref name="delivered"/> kg
        /// through the real Logistics store: its id, line and version.
        /// </summary>
        public async Task<(Guid InstructionId, Guid LineId, long Version)> PartlyDeliveredAsync(Guid company, SalesOrderDraft draft, string keyPrefix,
            decimal loaded, decimal shipped, decimal delivered)
        {
            var orderLine = Assert.Single(draft.Lines);
            var shipmentId = Guid.NewGuid();
            await using var scope = LogisticsStore(company);
            var stored = (await scope.Store.StoreInstructionAsync(LogisticsEnvelope($"{keyPrefix}-l-create"), Hash($"{keyPrefix}-l-create"), Day, () =>
            {
                var instruction = LoadingInstruction.Create(Guid.NewGuid(), company, draft.Header.PartyId, draft.Header.CompanyAccountId, T0);
                instruction.AddLine(instruction.Version, Guid.NewGuid(), new SourceOrderLineRef(draft.Id, orderLine.Id, draft.Revision),
                    orderLine.Product.ProductId, KgUnit, null, Kg(6m), T0);
                return instruction;
            })).Instruction.Instruction;
            var lineId = Assert.Single(stored.Lines).Id;
            var id = stored.Id;
            var issued = (await scope.Store.MutateInstructionAsync(LogisticsEnvelope($"{keyPrefix}-l-issue"), Hash($"{keyPrefix}-l-issue"),
                LogisticsCommands.IssueV1, id, (instruction, now) => instruction.Issue(stored.Version, now), Issued))!.Instruction.Instruction;
            var loadedInstruction = (await scope.Store.MutateInstructionAsync(LogisticsEnvelope($"{keyPrefix}-l-load"), Hash($"{keyPrefix}-l-load"),
                LogisticsCommands.RecordLoadV1, id, (instruction, now) => instruction.RecordLoad(issued.Version, Guid.NewGuid(), lineId, Kg(loaded),
                    MeasurementKind.Measured, now), Loaded))!.Instruction.Instruction;
            var shippedInstruction = (await scope.Store.MutateInstructionAsync(LogisticsEnvelope($"{keyPrefix}-l-ship"), Hash($"{keyPrefix}-l-ship"),
                LogisticsCommands.RecordShipmentV1, id, (instruction, now) => instruction.MarkShipped(loadedInstruction.Version, shipmentId, lineId, Kg(shipped), now),
                Shipped))!.Instruction.Instruction;
            var deliveredInstruction = (await scope.Store.MutateInstructionAsync(LogisticsEnvelope($"{keyPrefix}-l-deliver"), Hash($"{keyPrefix}-l-deliver"),
                LogisticsCommands.RecordDeliveryV1, id, (instruction, now) => instruction.MarkDelivered(shippedInstruction.Version, Guid.NewGuid(), lineId, shipmentId,
                    Kg(delivered), now), Delivered))!.Instruction.Instruction;
            return (id, lineId, deliveredInstruction.Version);
        }

        /// <summary>One more load of <paramref name="kg"/> on the instruction line through the real Logistics store.</summary>
        public async Task LoadMoreAsync(Guid company, Guid instructionId, Guid lineId, long version, string keyPrefix, decimal kg)
        {
            await using var scope = LogisticsStore(company);
            await scope.Store.MutateInstructionAsync(LogisticsEnvelope($"{keyPrefix}-l-load-more"), Hash($"{keyPrefix}-l-load-more"),
                LogisticsCommands.RecordLoadV1, instructionId, (instruction, now) => instruction.RecordLoad(version, Guid.NewGuid(), lineId, Kg(kg),
                    MeasurementKind.Measured, now), Loaded);
        }

        public async Task<JsonElement> JsonAsync(HttpClient client, string path)
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }

        public async Task<Effects> EffectsAsync(Guid draftId) => new(
            await ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [DraftId] = @id", ("@id", draftId)),
            await ScalarAsync<int>("SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EntityId] = @id AND [EventType] LIKE N'sales.order-draft.remainder-change-%'",
                ("@id", draftId.ToString("D"))),
            await ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftRemainderChanges] WHERE [DraftId] = @id", ("@id", draftId)),
            await ScalarAsync<int>("SELECT COUNT(*) FROM [sales].[DraftRemainderDecisions] WHERE [DraftId] = @id", ("@id", draftId)),
            await ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draftId)));

        public async Task<Header> HeaderAsync(Guid draftId) => new(
            await ScalarAsync<long>("SELECT [Version] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draftId)),
            await ScalarAsync<int>("SELECT [ApprovalState] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draftId)),
            await ScalarAsync<int>("SELECT [Revision] FROM [sales].[Drafts] WHERE [Id] = @id", ("@id", draftId)),
            await ScalarAsync<string>("SELECT [QuantityText] FROM [sales].[DraftLines] WHERE [DraftId] = @id", ("@id", draftId)),
            await ScalarAsync<decimal>("SELECT [Quantity] FROM [sales].[DraftLines] WHERE [DraftId] = @id", ("@id", draftId)));

        public async Task<Floor> RequestFloorAsync(Guid draftId, int changeNo) => new(
            await ScalarAsync<decimal>("SELECT [LoadedQuantity] FROM [sales].[DraftRemainderChanges] WHERE [DraftId] = @id AND [ChangeNo] = @n", ("@id", draftId), ("@n", changeNo)),
            await ScalarAsync<decimal>("SELECT [ShippedQuantity] FROM [sales].[DraftRemainderChanges] WHERE [DraftId] = @id AND [ChangeNo] = @n", ("@id", draftId), ("@n", changeNo)),
            await ScalarAsync<decimal>("SELECT [DeliveredQuantity] FROM [sales].[DraftRemainderChanges] WHERE [DraftId] = @id AND [ChangeNo] = @n", ("@id", draftId), ("@n", changeNo)));

        public async Task<Floor> DecisionFloorAsync(Guid draftId, int changeNo) => new(
            await ScalarAsync<decimal>("SELECT [LoadedQuantity] FROM [sales].[DraftRemainderDecisions] WHERE [DraftId] = @id AND [ChangeNo] = @n", ("@id", draftId), ("@n", changeNo)),
            await ScalarAsync<decimal>("SELECT [ShippedQuantity] FROM [sales].[DraftRemainderDecisions] WHERE [DraftId] = @id AND [ChangeNo] = @n", ("@id", draftId), ("@n", changeNo)),
            await ScalarAsync<decimal>("SELECT [DeliveredQuantity] FROM [sales].[DraftRemainderDecisions] WHERE [DraftId] = @id AND [ChangeNo] = @n", ("@id", draftId), ("@n", changeNo)));

        /// <summary>The audit events of the type for the draft, from the version to the version, written through the Web channel.</summary>
        public Task<int> AuditAsync(string eventType, Guid draftId, long versionBefore, long versionAfter) => ScalarAsync<int>(
            "SELECT COUNT(*) FROM [audit].[AuditEvents] WHERE [EventType] = @e AND [EntityId] = @id AND [SourceChannel] = N'Web' "
            + "AND [EntityVersionBefore] = @before AND [EntityVersionAfter] = @after",
            ("@e", eventType), ("@id", draftId.ToString("D")), ("@before", versionBefore), ("@after", versionAfter));

        /// <summary>The attempts recorded for the idempotency key, whatever the command.</summary>
        public Task<int> AttemptsAsync(string key) => ScalarAsync<int>(
            "SELECT COUNT(*) FROM [sales].[DraftAttempts] WHERE [IdempotencyKeyHash] = @h", ("@h", GoymenERP.Modules.Sales.Infrastructure.Persistence.SalesStore.IdempotencyKeyHash(key)));

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
            new(actor ?? Actor, "SENTETIK-sales-permission", ["SENTETIK-SALES"], Correlation, null, key, "Web", "127.0.0.1", "SENTETIK-agent");

        public LogisticsWriteEnvelope LogisticsEnvelope(string key) =>
            new(Actor, "SENTETIK-logistics-permission", ["SENTETIK-LOGISTICS"], Correlation, null, key, "Web", "127.0.0.1", "SENTETIK-agent");

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

        public LogisticsScope LogisticsStore(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            var collector = new AuditEventCollector();
            var dbContext = new LogisticsDbContext(new DbContextOptionsBuilder<LogisticsDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", LogisticsDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(collector, companyContext)).Options, companyContext);
            return new LogisticsScope(dbContext, new LogisticsStore(dbContext, companyContext, collector, TimeProvider.System));
        }

        public LogisticsRead LogisticsContext(Guid company)
        {
            var companyContext = new CompanyContext();
            companyContext.Set(company);
            return new LogisticsRead(new LogisticsDbContext(new DbContextOptionsBuilder<LogisticsDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", LogisticsDbContext.Schema))
                .Options, companyContext), companyContext);
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
                dbContext.Users.Add(new UserAccount(actor, "Synthetic Sales remainder user", null));
                dbContext.ExternalIdentities.Add(new ExternalIdentityLink(Guid.NewGuid(), actor, BootstrapIdentityOptions.Issuer, developmentUser));
            }
            else
            {
                actor = link.UserId;
            }

            var roleId = Guid.NewGuid();
            var role = new RoleDefinition(roleId, $"SAL-REMAINDER-{roleId:N}", "Synthetic Sales remainder role", true);
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
