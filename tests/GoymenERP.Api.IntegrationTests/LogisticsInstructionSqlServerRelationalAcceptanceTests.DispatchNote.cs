using System.Net;
using GoymenERP.Api.Endpoints;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// AT-055 on real SQL Server ("Aynı sevkiyat gerçekleşmesi iki kez irsaliye/faturaya bağlanamaz"): a dispatch-note DRAFT made
/// from definite shipments is one draft row plus its lines in ONE save with the attempt and the audit event; its lines are the
/// shipments' shipped quantities; the rebuild replays it at its share position; the same key answers it again without a second
/// row; a second draft naming an already linked shipment, a stale version or another company write nothing; and the DATABASE
/// itself refuses a second line for the same shipment (the unique key), even from a writer that skipped the aggregate. The
/// routes answer only with their grants and only for the context company, with the audit on the Web channel, and the Sales
/// order read lists the draft under that order. No number, finalisation or submission exists. SENTETIK.
/// </summary>
public sealed partial class LogisticsInstructionSqlServerRelationalAcceptanceTests
{
    private static readonly LogisticsMutationAudit Shipped =
        new LogisticsMutationAudit("logistics.loading-instruction.shipment-recorded.v1", "RecordShipment", "SENTETIK sevk edildi");

    private static readonly LogisticsMutationAudit Drafted =
        new LogisticsMutationAudit("logistics.loading-instruction.dispatch-note-drafted.v1", "CreateDispatchNote", "SENTETIK irsaliye taslağı");

    /// <summary>An issued two-line instruction with 5 kg loaded on each line and three definite shipments (3, 2 on line 1; 4.5 on line 2): version 8.</summary>
    private static async Task<(Guid Id, Guid First, Guid Second, Guid[] Shipments)> ShippedInstructionAsync(Run run, Owners owners)
    {
        await using var a = run.Store(owners.Company);
        var stored = (await a.Store.StoreInstructionAsync(run.Envelope("k-create"), Hash("create-1"), Day, () => Instruction(owners, lines: 2))).Instruction.Instruction;
        var id = stored.Id;
        var first = stored.Lines[0].Id;
        var second = stored.Lines[1].Id;
        Guid[] shipments = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
        await a.Store.MutateInstructionAsync(run.Envelope("k-issue"), Hash("issue-3"), LogisticsCommands.IssueV1, id, (instruction, now) => instruction.Issue(3, now), Issued);
        await a.Store.MutateInstructionAsync(run.Envelope("k-load-1"), Hash("load-4"), LogisticsCommands.RecordLoadV1, id,
            (instruction, now) => instruction.RecordLoad(4, Guid.NewGuid(), first, Kg(5m), MeasurementKind.Measured, now), Loaded);
        await a.Store.MutateInstructionAsync(run.Envelope("k-load-2"), Hash("load-5"), LogisticsCommands.RecordLoadV1, id,
            (instruction, now) => instruction.RecordLoad(5, Guid.NewGuid(), second, Kg(5m), MeasurementKind.Measured, now), Loaded);
        // The shipment share alone (its Inventory half is proven by the hosted shipment tests): the dispatch note reads the share.
        await a.Store.MutateInstructionAsync(run.Envelope("k-ship-1"), Hash("ship-6"), LogisticsCommands.RecordShipmentV1, id,
            (instruction, now) => instruction.MarkShipped(6, shipments[0], first, Kg(3m), now), Shipped);
        await a.Store.MutateInstructionAsync(run.Envelope("k-ship-2"), Hash("ship-7"), LogisticsCommands.RecordShipmentV1, id,
            (instruction, now) => instruction.MarkShipped(7, shipments[1], second, Kg(4.5m), now), Shipped);
        await a.Store.MutateInstructionAsync(run.Envelope("k-ship-3"), Hash("ship-8"), LogisticsCommands.RecordShipmentV1, id,
            (instruction, now) => instruction.MarkShipped(8, shipments[2], first, Kg(2m), now), Shipped);
        return (id, first, second, shipments);
    }

    [SqlServerFact]
    public async Task A_dispatch_note_draft_is_stored_once_with_its_shipments_quantities_rebuilt_and_a_second_link_writes_nothing()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        var (id, first, _, shipments) = await ShippedInstructionAsync(run, owners);
        var noteId = Guid.NewGuid();
        var attemptsBefore = await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingAttempts");

        await using (var a = run.Store(CompanyA))
        {
            var drafted = await a.Store.MutateInstructionAsync(run.Envelope("k-note"), Hash("note-9"), LogisticsCommands.CreateDispatchNoteV1, id,
                (instruction, now) => instruction.CreateDispatchNoteDraft(9, noteId, [shipments[2], shipments[0]], run.Actor, now), Drafted);
            var note = Assert.Single(drafted!.Instruction.Instruction.DispatchNotes);
            Assert.Equal((10L, noteId, 5), (drafted.Instruction.Instruction.Version, note.DispatchNoteId, note.SharesBefore));
        }

        // One draft row, its two lines (the shipments' quantities, in recording order), one recorded attempt, one audit event.
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.DispatchNotes WHERE InstructionId = @id", ("@id", id)));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.DispatchNoteLines WHERE DispatchNoteId = @note", ("@note", noteId)));
        Assert.Equal(3m, await run.ScalarAsync<decimal>("SELECT Quantity FROM logistics.DispatchNoteLines WHERE DispatchNoteId = @note AND [LineNo] = 1", ("@note", noteId)));
        Assert.Equal(2m, await run.ScalarAsync<decimal>("SELECT Quantity FROM logistics.DispatchNoteLines WHERE DispatchNoteId = @note AND [LineNo] = 2", ("@note", noteId)));
        Assert.Equal(owners.Order, await run.ScalarAsync<Guid>("SELECT TOP 1 SourceOrderId FROM logistics.DispatchNoteLines WHERE DispatchNoteId = @note", ("@note", noteId)));
        Assert.Equal(attemptsBefore + 1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingAttempts"));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId = @id AND EventType = N'logistics.loading-instruction.dispatch-note-drafted.v1' "
            + "AND SourceChannel = N'Web' AND AfterJson LIKE N'%\"dispatchNotes\":[[]{%'", ("@id", id.ToString("D"))));
        // A document books nothing: the shipped totals are the shares', unchanged.
        Assert.Equal(5m, await run.ScalarAsync<decimal>("SELECT Shipped FROM logistics.LoadingInstructionLines WHERE LineId = @line", ("@line", first)));

        await using (var a = run.Store(CompanyA))
        {
            // A delivery after the draft is still a delivery; the rebuild replays the draft at its share position.
            await a.Store.MutateInstructionAsync(run.Envelope("k-deliver"), Hash("deliver-10"), LogisticsCommands.RecordDeliveryV1, id,
                (instruction, now) => instruction.MarkDelivered(10, Guid.NewGuid(), first, shipments[0], Kg(1m), now),
                new LogisticsMutationAudit("logistics.loading-instruction.delivery-recorded.v1", "RecordDelivery", "SENTETIK teslim"));
            var rebuilt = (await a.Store.FindInstructionAsync(id))!.Instruction;
            var note = Assert.Single(rebuilt.DispatchNotes);
            Assert.Equal((11L, run.Actor, 3m), (rebuilt.Version, note.CreatedBy, note.Lines[0].Quantity.Value));
            Assert.Equal(noteId, rebuilt.DispatchNoteOf(shipments[2]));
            Assert.Null(rebuilt.DispatchNoteOf(shipments[1]));

            // The same key: the stored answer, never a second draft.
            var replay = await a.Store.MutateInstructionAsync(run.Envelope("k-note", correlation: "SENTETIK-retry"), Hash("note-9"),
                LogisticsCommands.CreateDispatchNoteV1, id, (_, _) => throw new InvalidOperationException("a replay never mutates"), Drafted);
            Assert.Equal((LogisticsWriteOutcome.Replayed, run.Correlation), (replay!.Outcome, replay.OriginalCorrelationId));

            // AT-055: another draft naming an already linked shipment (beside a free one) and a stale version write nothing.
            var again = await Assert.ThrowsAsync<LogisticsRuleException>(() => a.Store.MutateInstructionAsync(run.Envelope("k-note-2"), Hash("note-11"),
                LogisticsCommands.CreateDispatchNoteV1, id, (instruction, now) => instruction.CreateDispatchNoteDraft(11, Guid.NewGuid(),
                    [shipments[1], shipments[0]], run.Actor, now), Drafted));
            Assert.Equal(LogisticsRuleCodes.ShipmentAlreadyDispatched, again.Code);
            await Assert.ThrowsAsync<LogisticsVersionConflictException>(() => a.Store.MutateInstructionAsync(run.Envelope("k-note-3"), Hash("note-10"),
                LogisticsCommands.CreateDispatchNoteV1, id, (instruction, now) => instruction.CreateDispatchNoteDraft(10, Guid.NewGuid(),
                    [shipments[1]], run.Actor, now), Drafted));

            // The free shipment goes on its own draft.
            await a.Store.MutateInstructionAsync(run.Envelope("k-note-4"), Hash("note-11b"), LogisticsCommands.CreateDispatchNoteV1, id,
                (instruction, now) => instruction.CreateDispatchNoteDraft(11, Guid.NewGuid(), [shipments[1]], run.Actor, now), Drafted);

            // The Sales order read: both drafts, each with only this order's lines.
            var forOrder = await a.Store.ListSalesOrderDispatchNotesAsync(owners.Order, 101);
            Assert.Equal([2, 1], forOrder.Select(item => item.Lines.Count).ToArray());
            Assert.Empty(await a.Store.ListSalesOrderDispatchNotesAsync(Guid.NewGuid(), 101));
        }

        await using (var b = run.Store(CompanyB))
        {
            Assert.Null(await b.Store.FindInstructionAsync(id));
            Assert.Null(await b.Store.MutateInstructionAsync(run.Envelope("k-b-note"), Hash("note-b"), LogisticsCommands.CreateDispatchNoteV1, id,
                (instruction, now) => instruction.CreateDispatchNoteDraft(12, Guid.NewGuid(), [shipments[1]], run.Actor, now), Drafted));
            Assert.Empty(await b.Store.ListSalesOrderDispatchNotesAsync(owners.Order, 101));
        }

        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.DispatchNotes"));
        Assert.Equal(3, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.DispatchNoteLines"));
        Assert.Equal(12L, await run.ScalarAsync<long>("SELECT Version FROM logistics.LoadingInstructions WHERE Id = @id", ("@id", id)));
    }

    [SqlServerFact]
    public async Task The_database_refuses_a_second_dispatch_note_line_for_the_same_shipment_and_a_line_without_its_draft()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        var (id, first, _, shipments) = await ShippedInstructionAsync(run, owners);
        var noteA = Guid.NewGuid();
        var noteB = Guid.NewGuid();

        const string InsertNote = "INSERT INTO [logistics].[DispatchNotes] ([CompanyId], [InstructionId], [DispatchNoteId], [NoteNo], [Status], [SharesBefore], "
            + "[CreatedBy], [CreatedAtUtc]) VALUES (@company, @id, @note, @no, @status, 5, N'SENTETIK', SYSDATETIMEOFFSET())";
        const string InsertLine = "INSERT INTO [logistics].[DispatchNoteLines] ([CompanyId], [InstructionId], [DispatchNoteId], [LineNo], [ShipmentId], "
            + "[InstructionLineId], [SourceOrderId], [SourceOrderLineId], [ProductId], [UnitId], [Quantity]) "
            + "VALUES (@company, @id, @note, @line, @shipment, @instructionLine, @order, NEWID(), NEWID(), NEWID(), @quantity)";
        async Task<int> TryAsync(string sql, params (string Name, object Value)[] parameters)
        {
            try
            {
                await run.ExecuteAsync(sql, parameters);
                return 0;
            }
            catch (SqlException exception)
            {
                return exception.Number;
            }
        }

        Task<int> Note(Guid note, int no, int status = 1) =>
            TryAsync(InsertNote, ("@company", CompanyA), ("@id", id), ("@note", note), ("@no", no), ("@status", status));
        Task<int> Line(Guid note, int line, Guid shipment, decimal quantity = 3m) =>
            TryAsync(InsertLine, ("@company", CompanyA), ("@id", id), ("@note", note), ("@line", line), ("@shipment", shipment),
                ("@instructionLine", first), ("@order", owners.Order), ("@quantity", quantity));

        // Only the Draft state exists (547); a line needs its draft (547, the foreign key); a zero quantity is refused (547).
        Assert.Equal(547, await Note(noteA, 1, status: 2));
        Assert.Equal(547, await Line(noteA, 1, shipments[0]));
        Assert.Equal(0, await Note(noteA, 1));
        Assert.Equal(0, await Note(noteB, 2));
        Assert.Equal(547, await Line(noteA, 1, shipments[0], quantity: 0m));

        // AT-055 in the schema: the same shipment on a second line — of another draft or of the same one — is the unique key (2601).
        Assert.Equal(0, await Line(noteA, 1, shipments[0]));
        Assert.Equal(2601, await Line(noteB, 1, shipments[0]));
        Assert.Equal(2601, await Line(noteA, 2, shipments[0]));
        Assert.Equal(0, await Line(noteB, 1, shipments[2]));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.DispatchNoteLines WHERE ShipmentId = @shipment", ("@shipment", shipments[0])));
    }

    [SqlServerFact]
    public async Task The_dispatch_note_migration_rolls_back_empty_tables_and_refuses_with_51056_while_any_draft_exists()
    {
        await using var run = await Run.StartAsync();
        var companyContext = new CompanyContext();
        companyContext.Set(CompanyA);
        var options = new DbContextOptionsBuilder<LogisticsDbContext>()
            .UseSqlServer(run.Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", LogisticsDbContext.Schema))
            .Options;
        const string TablesExist = "SELECT COUNT(*) FROM sys.tables WHERE [name] IN (N'DispatchNotes', N'DispatchNoteLines') AND SCHEMA_NAME([schema_id]) = N'logistics'";

        await using (var context = new LogisticsDbContext(options, companyContext))
        {
            var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
            Assert.EndsWith("_AddDispatchNoteDrafts", applied[^1], StringComparison.Ordinal);
            await context.GetService<IMigrator>().MigrateAsync(applied[^2]);
            Assert.Equal(0, await run.ScalarAsync<int>(TablesExist));
            await context.Database.MigrateAsync();
            Assert.Equal(2, await run.ScalarAsync<int>(TablesExist));
        }

        var owners = Owners.For(CompanyA);
        var (id, _, _, shipments) = await ShippedInstructionAsync(run, owners);
        await using (var a = run.Store(CompanyA))
        {
            await a.Store.MutateInstructionAsync(run.Envelope("k-note"), Hash("note-9"), LogisticsCommands.CreateDispatchNoteV1, id,
                (instruction, now) => instruction.CreateDispatchNoteDraft(9, Guid.NewGuid(), [shipments[0]], run.Actor, now), Drafted);
        }

        await using (var context = new LogisticsDbContext(options, companyContext))
        {
            var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
            var error = await Assert.ThrowsAsync<SqlException>(() => context.GetService<IMigrator>().MigrateAsync(applied[^2]));
            Assert.Equal(51056, error.Number);
            Assert.Equal(applied, await context.Database.GetAppliedMigrationsAsync());
        }

        Assert.Equal(2, await run.ScalarAsync<int>(TablesExist));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.DispatchNoteLines WHERE InstructionId = @id", ("@id", id)));
    }

    [SqlServerFact]
    public async Task The_dispatch_note_routes_answer_only_with_their_grants_and_only_for_the_context_company()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        var (id, _, _, shipments) = await ShippedInstructionAsync(run, owners);
        var body = $$"""{"expectedVersion":"9","dispatchNoteId":"{{Guid.NewGuid():D}}","shipmentIds":["{{shipments[0]:D}}"]}""";

        // Reading the instruction (or recording shipments) is not the dispatch-note grant: the fence refuses before the store.
        var reader = await run.GrantAsync(TestUsers.CompanyA, CompanyA, LogisticsPermissionManifestV1.InstructionsRead, LogisticsPermissionManifestV1.ShipmentsRecord);
        using (var readOnly = run.Factory.CreateClientFor(reader, CompanyA))
        {
            readOnly.DefaultRequestHeaders.Remove("Idempotency-Key");
            readOnly.DefaultRequestHeaders.Remove("X-Correlation-Id");
            Assert.Equal(HttpStatusCode.Forbidden, (await readOnly.SendAsync(Command($"{id:D}/dispatch-notes", "SENTETIK-note-reader", body))).StatusCode);
        }

        var preparer = await run.GrantAsync(TestUsers.CompanyA, CompanyA, LogisticsPermissionManifestV1.InstructionsRead, LogisticsPermissionManifestV1.DispatchNotesCreate);
        using (var granted = run.Factory.CreateClientFor(preparer, CompanyA))
        {
            granted.DefaultRequestHeaders.Remove("Idempotency-Key");
            granted.DefaultRequestHeaders.Remove("X-Correlation-Id");
            using var drafted = await granted.SendAsync(Command($"{id:D}/dispatch-notes", "SENTETIK-note", body));
            Assert.Equal(HttpStatusCode.OK, drafted.StatusCode);
            var text = await drafted.Content.ReadAsStringAsync();
            Assert.Contains("\"status\":\"Draft\"", text, StringComparison.Ordinal);
            Assert.Contains($"\"shipmentId\":\"{shipments[0]:D}\"", text, StringComparison.Ordinal);
            Assert.Contains("\"quantity\":\"3\"", text, StringComparison.Ordinal);
            using var replay = await granted.SendAsync(Command($"{id:D}/dispatch-notes", "SENTETIK-note", body));
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotency-Replayed")));

            // Close and reopen: the instruction shows the draft with its line.
            using var detail = await granted.GetAsync($"{LogisticsEndpoints.InstructionsPath}/{id:D}");
            Assert.Contains($"\"shipmentId\":\"{shipments[0]:D}\"", await detail.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            // AT-055: a second draft for the same shipment (another key) is the recorded terminal 409; the stale version is a 409 too.
            var secondBody = $$"""{"expectedVersion":"10","dispatchNoteId":"{{Guid.NewGuid():D}}","shipmentIds":["{{shipments[0]:D}}"]}""";
            using var second = await granted.SendAsync(Command($"{id:D}/dispatch-notes", "SENTETIK-note-2", secondBody));
            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
            Assert.Contains("\"code\":\"logistics_shipment_already_dispatched\"", await second.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            using var stale = await granted.SendAsync(Command($"{id:D}/dispatch-notes", "SENTETIK-note-3",
                $$"""{"expectedVersion":"9","dispatchNoteId":"{{Guid.NewGuid():D}}","shipmentIds":["{{shipments[1]:D}}"]}"""));
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            Assert.Contains("\"code\":\"version_conflict\"", await stale.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            // The Sales order view's link: the draft under its order, read with the instruction read.
            using var forOrder = await granted.GetAsync($"{LogisticsApiV1.SalesOrderDispatchNotesPath}?salesOrderId={owners.Order:D}");
            Assert.Equal(HttpStatusCode.OK, forOrder.StatusCode);
            var orderText = await forOrder.Content.ReadAsStringAsync();
            Assert.Contains($"\"instructionId\":\"{id:D}\"", orderText, StringComparison.Ordinal);
            Assert.Contains("\"hasMore\":false", orderText, StringComparison.Ordinal);
        }

        var preparerOfB = await run.GrantAsync(TestUsers.CompanyB, CompanyB, LogisticsPermissionManifestV1.InstructionsRead, LogisticsPermissionManifestV1.DispatchNotesCreate);
        using (var clientOfB = run.Factory.CreateClientFor(preparerOfB, CompanyB))
        {
            clientOfB.DefaultRequestHeaders.Remove("Idempotency-Key");
            clientOfB.DefaultRequestHeaders.Remove("X-Correlation-Id");
            Assert.Equal(HttpStatusCode.NotFound, (await clientOfB.SendAsync(Command($"{id:D}/dispatch-notes", "SENTETIK-note-b", body))).StatusCode);
            using var forOrderOfB = await clientOfB.GetAsync($"{LogisticsApiV1.SalesOrderDispatchNotesPath}?salesOrderId={owners.Order:D}");
            Assert.Contains("\"items\":[]", await forOrderOfB.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.DispatchNotes"));
        Assert.Equal(10L, await run.ScalarAsync<long>("SELECT Version FROM logistics.LoadingInstructions WHERE Id = @id", ("@id", id)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId = @id AND EventType = N'logistics.loading-instruction.dispatch-note-drafted.v1' "
            + "AND SourceChannel = N'Web' AND PermissionCode = N'logistics.dispatch-notes.create'", ("@id", id.ToString("D"))));
    }
}
