using System.Net;
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
/// AT-055 lifecycle on real SQL Server (user decision 2026-09-30): approval gives the company's next SYSTEM number (series +
/// gap-free sequence) in the approval's own save, concurrency-safe across instructions; an approved note is never deleted, only
/// cancelled, and the cancellation releases its lines so the shipment can go on a new note — while the filtered unique key still
/// holds a shipment on at most ONE non-cancelled note; the e-İrsaliye number is kept apart and unique per company; a note id or an
/// e-İrsaliye number taken on ANOTHER instruction is the terminal refusal, never a <c>concurrent_change</c>; the lifecycle
/// migration's Down refuses with 51056 while lifecycle data exists; the routes answer only with their own grants. SENTETIK.
/// </summary>
public sealed partial class LogisticsInstructionSqlServerRelationalAcceptanceTests
{
    private static readonly LogisticsMutationAudit Approved =
        new("logistics.loading-instruction.dispatch-note-approved.v1", "ApproveDispatchNote", "SENTETIK irsaliye onaylandı");

    private static readonly LogisticsMutationAudit Cancelled =
        new("logistics.loading-instruction.dispatch-note-cancelled.v1", "CancelDispatchNote", "SENTETIK irsaliye iptal edildi");

    private static readonly LogisticsMutationAudit EDispatchRecorded =
        new("logistics.loading-instruction.e-dispatch-number-recorded.v1", "SetEDispatchNumber", "SENTETIK e-İrsaliye no");

    /// <summary>A shipped instruction (version 8) with one draft on the first shipment (version 9); a fresh key prefix per instruction.</summary>
    private static async Task<(Guid Id, Guid Note, Guid[] Shipments)> DraftedInstructionAsync(Run run, Owners owners)
    {
        var (id, _, _, shipments) = await ShippedInstructionAsync(run, owners, $"{Guid.NewGuid():N}-");
        var note = Guid.NewGuid();
        await using var a = run.Store(owners.Company);
        await a.Store.MutateInstructionAsync(run.Envelope($"k-note-{note:N}"), Hash($"note-{note:N}"), LogisticsCommands.CreateDispatchNoteV1, id,
            (instruction, now) => instruction.CreateDispatchNoteDraft(9, note, [shipments[0]], run.Actor, now), Drafted);
        return (id, note, shipments);
    }

    [SqlServerFact]
    public async Task A_dispatch_note_is_approved_with_the_next_company_number_cancelled_with_its_lines_released_and_its_shipment_put_on_a_new_note()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        var (id, note, shipments) = await DraftedInstructionAsync(run, owners);

        await using (var a = run.Store(CompanyA))
        {
            var approved = await a.Store.MutateInstructionAsync(run.Envelope("k-approve"), Hash("approve-10"), LogisticsCommands.ApproveDispatchNoteV1, id,
                (instruction, now) => instruction.ApproveDispatchNote(10, note, run.Actor, now), Approved);
            var numbered = Assert.Single(approved!.Instruction.Instruction.DispatchNotes);
            Assert.Equal((11L, DispatchNoteStatus.Approved, "IRS-000001"), (approved.Instruction.Instruction.Version, numbered.Status, numbered.Number!.Text));
        }

        // One save: the note row's approval group and number, the series row, one attempt, one audit event.
        Assert.Equal((2, "IRS", 1L), (await run.ScalarAsync<int>("SELECT Status FROM logistics.DispatchNotes WHERE DispatchNoteId = @n", ("@n", note)),
            await run.ScalarAsync<string>("SELECT SeriesCode FROM logistics.DispatchNotes WHERE DispatchNoteId = @n", ("@n", note)),
            await run.ScalarAsync<long>("SELECT SequenceNo FROM logistics.DispatchNotes WHERE DispatchNoteId = @n", ("@n", note))));
        Assert.Equal(1L, await run.ScalarAsync<long>("SELECT LastSequenceNo FROM logistics.DispatchNoteSeries WHERE CompanyId = @c AND SeriesCode = N'IRS'", ("@c", CompanyA)));
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId = @id AND EventType = N'logistics.loading-instruction.dispatch-note-approved.v1' "
            + "AND AfterJson LIKE N'%\"number\":\"IRS-000001\"%'", ("@id", id.ToString("D"))));

        // The e-İrsaliye number, kept apart from the system number.
        await using (var a = run.Store(CompanyA))
        {
            await a.Store.MutateInstructionAsync(run.Envelope("k-e"), Hash("e-11"), LogisticsCommands.SetEDispatchNumberV1, id,
                (instruction, now) => instruction.SetEDispatchNumber(11, note, "GYM2026000000001", run.Actor, now), EDispatchRecorded);
        }

        Assert.Equal("GYM2026000000001", await run.ScalarAsync<string>("SELECT EDispatchNumber FROM logistics.DispatchNotes WHERE DispatchNoteId = @n", ("@n", note)));

        await using (var a = run.Store(CompanyA))
        {
            // The shipment is held by the APPROVED note: a new draft for it writes nothing.
            var held = await Assert.ThrowsAsync<LogisticsRuleException>(() => a.Store.MutateInstructionAsync(run.Envelope("k-held"), Hash("held-12"),
                LogisticsCommands.CreateDispatchNoteV1, id, (instruction, now) => instruction.CreateDispatchNoteDraft(12, Guid.NewGuid(), [shipments[0]], run.Actor, now),
                Drafted));
            Assert.Equal(LogisticsRuleCodes.ShipmentAlreadyDispatched, held.Code);

            // Cancel: the row stays with its number and e-İrsaliye number; its line is released.
            await a.Store.MutateInstructionAsync(run.Envelope("k-cancel"), Hash("cancel-12"), LogisticsCommands.CancelDispatchNoteV1, id,
                (instruction, now) => instruction.CancelDispatchNote(12, note, run.Actor, "SENTETIK yanlış araç", now), Cancelled);
        }

        Assert.Equal(3, await run.ScalarAsync<int>("SELECT Status FROM logistics.DispatchNotes WHERE DispatchNoteId = @n", ("@n", note)));
        Assert.Equal(1L, await run.ScalarAsync<long>("SELECT SequenceNo FROM logistics.DispatchNotes WHERE DispatchNoteId = @n", ("@n", note)));
        Assert.True(await run.ScalarAsync<bool>("SELECT Released FROM logistics.DispatchNoteLines WHERE DispatchNoteId = @n", ("@n", note)));

        var correction = Guid.NewGuid();
        await using (var a = run.Store(CompanyA))
        {
            // Correction = a new draft from the released shipment, then its approval: the next number, gap-free.
            await a.Store.MutateInstructionAsync(run.Envelope("k-redraft"), Hash("redraft-13"), LogisticsCommands.CreateDispatchNoteV1, id,
                (instruction, now) => instruction.CreateDispatchNoteDraft(13, correction, [shipments[0]], run.Actor, now), Drafted);
            await a.Store.MutateInstructionAsync(run.Envelope("k-approve-2"), Hash("approve-14"), LogisticsCommands.ApproveDispatchNoteV1, id,
                (instruction, now) => instruction.ApproveDispatchNote(14, correction, run.Actor, now), Approved);

            // The rebuild replays every step in its order and reaches the stored state.
            var rebuilt = (await a.Store.FindInstructionAsync(id))!.Instruction;
            Assert.Equal((15L, 6), (rebuilt.Version, rebuilt.DispatchSteps));
            Assert.Equal([(DispatchNoteStatus.Cancelled, "IRS-000001", "GYM2026000000001"), (DispatchNoteStatus.Approved, "IRS-000002", (string?)null)],
                rebuilt.DispatchNotes.Select(item => (item.Status, item.Number!.Text, item.EDispatchNumber)).ToArray());
            Assert.Equal(correction, rebuilt.DispatchNoteOf(shipments[0]));

            // The Sales order read: every state, with its numbers.
            var forOrder = await a.Store.ListSalesOrderDispatchNotesAsync(owners.Order, 101);
            Assert.Equal([(DispatchNoteStatus.Cancelled, "IRS-000001", "GYM2026000000001"), (DispatchNoteStatus.Approved, "IRS-000002", (string?)null)],
                forOrder.Select(item => (item.Status, item.Number!.Text, item.EDispatchNumber)).ToArray());
        }

        // Two line rows for the same shipment exist — exactly one active.
        Assert.Equal((2, 1), (await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.DispatchNoteLines WHERE ShipmentId = @s", ("@s", shipments[0])),
            await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.DispatchNoteLines WHERE ShipmentId = @s AND Released = 0", ("@s", shipments[0]))));
        Assert.Equal(2L, await run.ScalarAsync<long>("SELECT LastSequenceNo FROM logistics.DispatchNoteSeries WHERE CompanyId = @c", ("@c", CompanyA)));

        // Company B: its own series, its own numbers; A's notes are invisible.
        var ownersB = Owners.For(CompanyB);
        var (idB, noteB, _) = await DraftedInstructionAsync(run, ownersB);
        await using (var b = run.Store(CompanyB))
        {
            var approvedB = await b.Store.MutateInstructionAsync(run.Envelope("k-approve-b"), Hash("approve-b"), LogisticsCommands.ApproveDispatchNoteV1, idB,
                (instruction, now) => instruction.ApproveDispatchNote(10, noteB, run.Actor, now), Approved);
            Assert.Equal("IRS-000001", approvedB!.Instruction.Instruction.DispatchNotes[0].Number!.Text);
            Assert.Empty(await b.Store.ListSalesOrderDispatchNotesAsync(owners.Order, 101));
        }
    }

    [SqlServerFact]
    public async Task Concurrent_approvals_on_two_instructions_never_share_or_skip_a_number()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        var drafts = new List<(Guid Id, Guid Note)>();
        for (var i = 0; i < 4; i++)
        {
            var (id, note, _) = await DraftedInstructionAsync(run, owners);
            drafts.Add((id, note));
        }

        async Task<object> ApproveAsync(Barrier barrier, (Guid Id, Guid Note) draft, string key)
        {
            await using var writer = run.Store(CompanyA, new BarrierInterceptor(barrier));
            try
            {
                var result = await writer.Store.MutateInstructionAsync(run.Envelope(key), Hash(key), LogisticsCommands.ApproveDispatchNoteV1, draft.Id,
                    (instruction, now) => instruction.ApproveDispatchNote(10, draft.Note, run.Actor, now), Approved);
                return result!.Instruction.Instruction.DispatchNotes[0].Number!.Text;
            }
            catch (LogisticsStoreException exception)
            {
                return exception.Code;
            }
        }

        // The company's FIRST number: both approvals read no series row and race to insert it — the key lets one through.
        var first = new Barrier(2);
        var firstResults = await Task.WhenAll(Task.Run(() => ApproveAsync(first, drafts[0], "k-race-1a")), Task.Run(() => ApproveAsync(first, drafts[1], "k-race-1b")));
        Assert.Single(firstResults, result => result is "IRS-000001");
        Assert.Single(firstResults, result => result is string code && code == LogisticsStoreCodes.ConcurrentChange);

        // An existing series: both read the same last number and race to move it — the concurrency token lets one through.
        var second = new Barrier(2);
        var secondResults = await Task.WhenAll(Task.Run(() => ApproveAsync(second, drafts[2], "k-race-2a")), Task.Run(() => ApproveAsync(second, drafts[3], "k-race-2b")));
        Assert.Single(secondResults, result => result is "IRS-000002");
        Assert.Single(secondResults, result => result is string code && code == LogisticsStoreCodes.ConcurrentChange);

        // The losers saved nothing (their notes are still drafts, their instructions at version 10, no attempt); a retry takes the next numbers.
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.DispatchNotes WHERE Status = 1"));
        foreach (var (draft, index) in drafts.Select((draft, index) => (draft, index)))
        {
            if (await run.ScalarAsync<int>("SELECT Status FROM logistics.DispatchNotes WHERE DispatchNoteId = @n", ("@n", draft.Note)) == 1)
            {
                Assert.Equal(10L, await run.ScalarAsync<long>("SELECT Version FROM logistics.LoadingInstructions WHERE Id = @id", ("@id", draft.Id)));
                await using var retry = run.Store(CompanyA);
                await retry.Store.MutateInstructionAsync(run.Envelope($"k-retry-{index}"), Hash($"retry-{index}"), LogisticsCommands.ApproveDispatchNoteV1, draft.Id,
                    (instruction, now) => instruction.ApproveDispatchNote(10, draft.Note, run.Actor, now), Approved);
            }
        }

        // Gap-free and unique: 1..4 once each, and the series at 4.
        Assert.Equal(4, await run.ScalarAsync<int>("SELECT COUNT(DISTINCT SequenceNo) FROM logistics.DispatchNotes WHERE SequenceNo BETWEEN 1 AND 4"));
        Assert.Equal(4L, await run.ScalarAsync<long>("SELECT MAX(SequenceNo) FROM logistics.DispatchNotes"));
        Assert.Equal(4L, await run.ScalarAsync<long>("SELECT LastSequenceNo FROM logistics.DispatchNoteSeries WHERE CompanyId = @c", ("@c", CompanyA)));
        Assert.Equal(4, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType = N'logistics.loading-instruction.dispatch-note-approved.v1'"));
    }

    [SqlServerFact]
    public async Task A_note_id_or_an_e_dispatch_number_taken_on_another_instruction_is_the_terminal_refusal_not_a_concurrent_change()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        var (firstId, firstNote, _) = await DraftedInstructionAsync(run, owners);
        var (secondId, _, secondShipments) = await DraftedInstructionAsync(run, owners);

        await using (var a = run.Store(CompanyA))
        {
            // #518 P2: the same note id on ANOTHER instruction is the domain's duplicate code (recorded by the service as a terminal
            // 409), refused before the save — never the unique key's concurrent_change.
            var reused = await Assert.ThrowsAsync<LogisticsRuleException>(() => a.Store.MutateInstructionAsync(run.Envelope("k-reuse"), Hash("reuse"),
                LogisticsCommands.CreateDispatchNoteV1, secondId,
                (instruction, now) => instruction.CreateDispatchNoteDraft(10, firstNote, [secondShipments[1]], run.Actor, now), Drafted));
            Assert.Equal(LogisticsRuleCodes.DuplicateDispatchNoteId, reused.Code);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.DispatchNotes WHERE DispatchNoteId = @n", ("@n", firstNote)));
        Assert.Equal(10L, await run.ScalarAsync<long>("SELECT Version FROM logistics.LoadingInstructions WHERE Id = @id", ("@id", secondId)));

        var secondNote = (await run.ScalarAsync<Guid>("SELECT DispatchNoteId FROM logistics.DispatchNotes WHERE InstructionId = @id", ("@id", secondId)));
        await using (var a = run.Store(CompanyA))
        {
            await a.Store.MutateInstructionAsync(run.Envelope("k-approve-1"), Hash("approve-1"), LogisticsCommands.ApproveDispatchNoteV1, firstId,
                (instruction, now) => instruction.ApproveDispatchNote(10, firstNote, run.Actor, now), Approved);
            await a.Store.MutateInstructionAsync(run.Envelope("k-approve-2"), Hash("approve-2"), LogisticsCommands.ApproveDispatchNoteV1, secondId,
                (instruction, now) => instruction.ApproveDispatchNote(10, secondNote, run.Actor, now), Approved);
            await a.Store.MutateInstructionAsync(run.Envelope("k-e-1"), Hash("e-1"), LogisticsCommands.SetEDispatchNumberV1, firstId,
                (instruction, now) => instruction.SetEDispatchNumber(11, firstNote, "GYM2026000000007", run.Actor, now), EDispatchRecorded);

            // Unique per company: the same e-İrsaliye number on another instruction's note is the in-use code, before the save.
            var taken = await Assert.ThrowsAsync<LogisticsRuleException>(() => a.Store.MutateInstructionAsync(run.Envelope("k-e-2"), Hash("e-2"),
                LogisticsCommands.SetEDispatchNumberV1, secondId,
                (instruction, now) => instruction.SetEDispatchNumber(11, secondNote, "GYM2026000000007", run.Actor, now), EDispatchRecorded));
            Assert.Equal(LogisticsRuleCodes.EDispatchNumberInUse, taken.Code);
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.DispatchNotes WHERE EDispatchNumber = N'GYM2026000000007'"));

        // Through the route: the reused id answers the recorded terminal 409 logistics_duplicate_dispatch_note_id, and its replay the same.
        var preparer = await run.GrantAsync(TestUsers.CompanyA, CompanyA, LogisticsPermissionManifestV1.InstructionsRead, LogisticsPermissionManifestV1.DispatchNotesCreate);
        using var client = run.Factory.CreateClientFor(preparer, CompanyA);
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Remove("X-Correlation-Id");
        var body = $$"""{"expectedVersion":"11","dispatchNoteId":"{{firstNote:D}}","shipmentIds":["{{secondShipments[1]:D}}"]}""";
        foreach (var attempt in new[] { "first", "replay" })
        {
            using var response = await client.SendAsync(Command($"{secondId:D}/dispatch-notes", "SENTETIK-reuse", body));
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Contains("\"code\":\"logistics_duplicate_dispatch_note_id\"", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            Assert.Equal(attempt == "replay", response.Headers.Contains("Idempotency-Replayed"));
        }

        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.LoadingAttempts WHERE Outcome = 2 AND RefusalJson LIKE N'%logistics_duplicate_dispatch_note_id%'"));
    }

    [SqlServerFact]
    public async Task The_database_holds_the_lifecycle_shape_and_lets_only_a_released_shipment_onto_a_new_line()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        var (id, first, _, shipments) = await ShippedInstructionAsync(run, owners);
        var noteA = Guid.NewGuid();
        var noteB = Guid.NewGuid();

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

        Task<int> Note(Guid note, int no, string extra = "", string values = "") => TryAsync(
            "INSERT INTO [logistics].[DispatchNotes] ([CompanyId], [InstructionId], [DispatchNoteId], [NoteNo], [Status], [SharesBefore], [CreatedBy], "
            + "[CreatedAtUtc], [CreatedStep]" + extra + ") VALUES (@company, @id, @note, @no, @status, 5, N'SENTETIK', SYSDATETIMEOFFSET(), @no" + values + ")",
            ("@company", CompanyA), ("@id", id), ("@note", note), ("@no", no), ("@status", extra.Length == 0 ? 1 : 2));
        Task<int> Line(Guid note, int line, Guid shipment, bool released = false) => TryAsync(
            "INSERT INTO [logistics].[DispatchNoteLines] ([CompanyId], [InstructionId], [DispatchNoteId], [LineNo], [ShipmentId], [InstructionLineId], "
            + "[SourceOrderId], [SourceOrderLineId], [ProductId], [UnitId], [Quantity], [Released]) "
            + "VALUES (@company, @id, @note, @line, @shipment, @instructionLine, @order, NEWID(), NEWID(), NEWID(), 3, @released)",
            ("@company", CompanyA), ("@id", id), ("@note", note), ("@line", line), ("@shipment", shipment), ("@instructionLine", first),
            ("@order", owners.Order), ("@released", released));

        // Approved (2) only with its whole approval group and number; a half-written group is refused (547).
        Assert.Equal(547, await Note(noteA, 1, ", [ApprovedStep]", ", 2"));
        Assert.Equal(547, await Note(noteA, 1, ", [ApprovedStep], [ApprovedBy], [ApprovedAtUtc], [SeriesCode]", ", 2, N'x', SYSDATETIMEOFFSET(), N'IRS'"));
        Assert.Equal(0, await Note(noteA, 1, ", [ApprovedStep], [ApprovedBy], [ApprovedAtUtc], [SeriesCode], [SequenceNo]",
            ", 2, N'x', SYSDATETIMEOFFSET(), N'IRS', 41"));
        // The system number is unique per company (2601).
        Assert.Equal(2601, await Note(noteB, 2, ", [ApprovedStep], [ApprovedBy], [ApprovedAtUtc], [SeriesCode], [SequenceNo]",
            ", 3, N'x', SYSDATETIMEOFFSET(), N'IRS', 41"));
        Assert.Equal(0, await Note(noteB, 2));

        // One active line per shipment: a second unreleased line is the filtered unique key (2601) ...
        Assert.Equal(0, await Line(noteA, 1, shipments[0]));
        Assert.Equal(2601, await Line(noteB, 1, shipments[0]));
        // ... a released line leaves the key (the cancelled note's), so the shipment may go on a new line — but only once.
        Assert.Equal(0, await Line(noteB, 1, shipments[1], released: true));
        Assert.Equal(0, await Line(noteB, 2, shipments[2]));
        await run.ExecuteAsync("UPDATE logistics.DispatchNoteLines SET Released = 1 WHERE DispatchNoteId = @n", ("@n", noteA));
        Assert.Equal(0, await Line(noteB, 3, shipments[0]));
        Assert.Equal(2601, await Line(noteB, 4, shipments[0]));

        // Cancelled (3) only with its cancellation group; a cancellation before the approval step is refused; the e-İrsaliye
        // number only on an approved note, 16 characters, unique per company.
        Assert.Equal(547, await TryAsync("UPDATE logistics.DispatchNotes SET Status = 3 WHERE DispatchNoteId = @n", ("@n", noteA)));
        Assert.Equal(547, await TryAsync("UPDATE logistics.DispatchNotes SET Status = 3, CancelledStep = 2, CancelledBy = N'x', CancelledAtUtc = SYSDATETIMEOFFSET(), "
            + "CancelReason = N'r' WHERE DispatchNoteId = @n", ("@n", noteA)));
        Assert.Equal(547, await TryAsync("UPDATE logistics.DispatchNotes SET EDispatchNumber = 'GYM2026000000001', EDispatchNumberStep = 3, EDispatchNumberSetBy = N'x', "
            + "EDispatchNumberSetAtUtc = SYSDATETIMEOFFSET() WHERE DispatchNoteId = @n", ("@n", noteB)));
        Assert.Equal(547, await TryAsync("UPDATE logistics.DispatchNotes SET EDispatchNumber = 'GYM20260001', EDispatchNumberStep = 3, EDispatchNumberSetBy = N'x', "
            + "EDispatchNumberSetAtUtc = SYSDATETIMEOFFSET() WHERE DispatchNoteId = @n", ("@n", noteA)));
        Assert.Equal(0, await TryAsync("UPDATE logistics.DispatchNotes SET EDispatchNumber = 'GYM2026000000001', EDispatchNumberStep = 3, EDispatchNumberSetBy = N'x', "
            + "EDispatchNumberSetAtUtc = SYSDATETIMEOFFSET() WHERE DispatchNoteId = @n", ("@n", noteA)));
        Assert.Equal(0, await TryAsync("UPDATE logistics.DispatchNotes SET Status = 3, CancelledStep = 4, CancelledBy = N'x', CancelledAtUtc = SYSDATETIMEOFFSET(), "
            + "CancelReason = N'r' WHERE DispatchNoteId = @n", ("@n", noteA)));

        // The series: a positive last number only.
        Assert.Equal(547, await TryAsync("INSERT INTO logistics.DispatchNoteSeries (CompanyId, SeriesCode, LastSequenceNo, CreatedAtUtc, UpdatedAtUtc) "
            + "VALUES (@c, N'IRS', 0, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET())", ("@c", CompanyA)));
    }

    [SqlServerFact]
    public async Task The_lifecycle_migration_rolls_back_plain_drafts_and_refuses_with_51056_while_any_approval_or_cancellation_exists()
    {
        await using var run = await Run.StartAsync();
        var companyContext = new CompanyContext();
        companyContext.Set(CompanyA);
        var options = new DbContextOptionsBuilder<LogisticsDbContext>()
            .UseSqlServer(run.Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", LogisticsDbContext.Schema))
            .Options;
        const string LifecycleExists = "SELECT COUNT(*) FROM sys.columns WHERE [name] = N'Released' AND OBJECT_NAME([object_id]) = N'DispatchNoteLines'";
        var owners = Owners.For(CompanyA);
        var (id, note, _) = await DraftedInstructionAsync(run, owners);
        string before;

        // Plain drafts are the previous schema's shape: the rollback keeps them, the old unfiltered key returns, and re-applying
        // the migration backfills each draft's creation step from its NoteNo.
        await using (var context = new LogisticsDbContext(options, companyContext))
        {
            var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
            Assert.EndsWith("_AddDispatchNoteLifecycle", applied[^1], StringComparison.Ordinal);
            before = applied[^2];
            Assert.EndsWith("_AddDispatchNoteDrafts", before, StringComparison.Ordinal);
            await context.GetService<IMigrator>().MigrateAsync(before);
            Assert.Equal(0, await run.ScalarAsync<int>(LifecycleExists));
            Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM logistics.DispatchNotes WHERE DispatchNoteId = @n", ("@n", note)));
            // The drafts' single-link key is back: unique and not filtered on Released (EF's hand-written unique index carries its
            // IS NOT NULL filter over the key columns, as the drafts' own Up created it).
            Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sys.indexes WHERE [name] = N'UX_DispatchNoteLines_ShipmentId' AND is_unique = 1 "
                + "AND ISNULL(filter_definition, N'') NOT LIKE N'%Released%'"));
            await context.Database.MigrateAsync();
            Assert.Equal(1, await run.ScalarAsync<int>(LifecycleExists));
            Assert.Equal(1, await run.ScalarAsync<int>("SELECT CreatedStep FROM logistics.DispatchNotes WHERE DispatchNoteId = @n", ("@n", note)));
            Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sys.indexes WHERE [name] = N'UX_DispatchNoteLines_ShipmentId' AND has_filter = 1 "
                + "AND filter_definition = N'([Released]=(0))'"));
        }

        // The stored draft is still rebuilt after the round trip; then it is approved — lifecycle data now exists.
        await using (var a = run.Store(CompanyA))
        {
            Assert.Single((await a.Store.FindInstructionAsync(id))!.Instruction.DispatchNotes);
            await a.Store.MutateInstructionAsync(run.Envelope("k-approve"), Hash("approve"), LogisticsCommands.ApproveDispatchNoteV1, id,
                (instruction, now) => instruction.ApproveDispatchNote(10, note, run.Actor, now), Approved);
        }

        await using (var context = new LogisticsDbContext(options, companyContext))
        {
            var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
            var error = await Assert.ThrowsAsync<SqlException>(() => context.GetService<IMigrator>().MigrateAsync(before));
            Assert.Equal(51056, error.Number);
            Assert.Equal(applied, await context.Database.GetAppliedMigrationsAsync());
        }

        Assert.Equal(1, await run.ScalarAsync<int>(LifecycleExists));
        Assert.Equal("IRS", await run.ScalarAsync<string>("SELECT SeriesCode FROM logistics.DispatchNotes WHERE DispatchNoteId = @n", ("@n", note)));
    }

    [SqlServerFact]
    public async Task The_dispatch_note_lifecycle_routes_answer_only_with_their_own_grants_and_the_sales_order_read_only_with_the_instruction_read()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        var (id, note, shipments) = await DraftedInstructionAsync(run, owners);
        HttpClient ClientFor(string user)
        {
            var client = run.Factory.CreateClientFor(user, CompanyA);
            client.DefaultRequestHeaders.Remove("Idempotency-Key");
            client.DefaultRequestHeaders.Remove("X-Correlation-Id");
            return client;
        }

        var approve = $"{id:D}/dispatch-notes/{note:D}/approve";
        var cancel = $"{id:D}/dispatch-notes/{note:D}/cancel";
        var eNumber = $"{id:D}/dispatch-notes/{note:D}/e-dispatch-number";

        // The preparer (create + read) neither approves, records the e-İrsaliye number nor cancels.
        var preparer = await run.GrantAsync(TestUsers.CompanyA, CompanyA, LogisticsPermissionManifestV1.InstructionsRead, LogisticsPermissionManifestV1.DispatchNotesCreate);
        using (var client = ClientFor(preparer))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Command(approve, "SENTETIK-approve-p", """{"expectedVersion":"10"}"""))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Command(cancel, "SENTETIK-cancel-p", """{"expectedVersion":"10","reason":"r"}"""))).StatusCode);
        }

        var approver = await run.GrantAsync(TestUsers.CompanyA, CompanyA, LogisticsPermissionManifestV1.InstructionsRead, LogisticsPermissionManifestV1.DispatchNotesApprove);
        using (var client = ClientFor(approver))
        {
            using var approved = await client.SendAsync(Command(approve, "SENTETIK-approve", """{"expectedVersion":"10"}"""));
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
            var text = await approved.Content.ReadAsStringAsync();
            Assert.Contains("\"status\":\"Approved\"", text, StringComparison.Ordinal);
            Assert.Contains("\"number\":\"IRS-000001\"", text, StringComparison.Ordinal);
            using var replay = await client.SendAsync(Command(approve, "SENTETIK-approve", """{"expectedVersion":"10"}"""));
            Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotency-Replayed")));
            using var twice = await client.SendAsync(Command(approve, "SENTETIK-approve-2", """{"expectedVersion":"11"}"""));
            Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);
            Assert.Contains("\"code\":\"logistics_dispatch_note_state\"", await twice.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            using var badNumber = await client.SendAsync(Command(eNumber, "SENTETIK-e-bad", """{"expectedVersion":"11","eDispatchNumber":"gym2026000000001"}"""));
            Assert.Equal(HttpStatusCode.BadRequest, badNumber.StatusCode);
            using var recorded = await client.SendAsync(Command(eNumber, "SENTETIK-e", """{"expectedVersion":"11","eDispatchNumber":"GYM2026000000001"}"""));
            Assert.Equal(HttpStatusCode.OK, recorded.StatusCode);
            Assert.Contains("\"eDispatchNumber\":\"GYM2026000000001\"", await recorded.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            // The approver does not cancel.
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Command(cancel, "SENTETIK-cancel-a", """{"expectedVersion":"12","reason":"r"}"""))).StatusCode);
            using var unknown = await client.SendAsync(Command($"{id:D}/dispatch-notes/{Guid.NewGuid():D}/approve", "SENTETIK-approve-unknown", """{"expectedVersion":"12"}"""));
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        }

        var canceller = await run.GrantAsync(TestUsers.CompanyA, CompanyA, LogisticsPermissionManifestV1.InstructionsRead, LogisticsPermissionManifestV1.DispatchNotesCancel,
            LogisticsPermissionManifestV1.DispatchNotesCreate);
        using (var client = ClientFor(canceller))
        {
            using var padded = await client.SendAsync(Command(cancel, "SENTETIK-cancel-pad", """{"expectedVersion":"12","reason":" r "}"""));
            Assert.Equal(HttpStatusCode.BadRequest, padded.StatusCode);
            using var cancelled = await client.SendAsync(Command(cancel, "SENTETIK-cancel", """{"expectedVersion":"12","reason":"SENTETIK yanlış araç"}"""));
            Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
            var text = await cancelled.Content.ReadAsStringAsync();
            Assert.Contains("\"status\":\"Cancelled\"", text, StringComparison.Ordinal);
            var cancelledNote = System.Text.Json.JsonDocument.Parse(text).RootElement.GetProperty("detail").GetProperty("dispatchNotes")[0];
            Assert.Equal("SENTETIK yanlış araç", cancelledNote.GetProperty("cancelReason").GetString());
            // Correction: the released shipment on a new draft.
            using var redrafted = await client.SendAsync(Command($"{id:D}/dispatch-notes", "SENTETIK-redraft",
                $$"""{"expectedVersion":"13","dispatchNoteId":"{{Guid.NewGuid():D}}","shipmentIds":["{{shipments[0]:D}}"]}"""));
            Assert.Equal(HttpStatusCode.OK, redrafted.StatusCode);
        }

        // #518 P2: the Sales order read is the instruction read — without it, 403 before any row is read.
        // Another user (the grants above all went to the company user), holding a Logistics write but not the read.
        var outsider = await run.GrantAsync(TestUsers.CompanyAViewer, CompanyA, LogisticsPermissionManifestV1.ShipmentsRecord);
        using (var client = ClientFor(outsider))
        {
            using var forbidden = await client.GetAsync($"{LogisticsApiV1.SalesOrderDispatchNotesPath}?salesOrderId={owners.Order:D}");
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            Assert.DoesNotContain(note.ToString("D"), await forbidden.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        using (var client = ClientFor(await run.GrantAsync(TestUsers.CompanyA, CompanyA, LogisticsPermissionManifestV1.InstructionsRead)))
        {
            using var forOrder = await client.GetAsync($"{LogisticsApiV1.SalesOrderDispatchNotesPath}?salesOrderId={owners.Order:D}");
            Assert.Equal(HttpStatusCode.OK, forOrder.StatusCode);
            var text = await forOrder.Content.ReadAsStringAsync();
            Assert.Contains("\"status\":\"Cancelled\"", text, StringComparison.Ordinal);
            Assert.Contains("\"number\":\"IRS-000001\"", text, StringComparison.Ordinal);
            Assert.Contains("\"status\":\"Draft\"", text, StringComparison.Ordinal);
        }

        // One audit event per lifecycle step, each under its own permission, on the Web channel.
        foreach (var (eventType, permission) in new[]
                 {
                     ("logistics.loading-instruction.dispatch-note-approved.v1", LogisticsPermissionManifestV1.DispatchNotesApprove),
                     ("logistics.loading-instruction.e-dispatch-number-recorded.v1", LogisticsPermissionManifestV1.DispatchNotesApprove),
                     ("logistics.loading-instruction.dispatch-note-cancelled.v1", LogisticsPermissionManifestV1.DispatchNotesCancel),
                 })
        {
            Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId = @id AND EventType = @type AND SourceChannel = N'Web' "
                + "AND PermissionCode = @permission", ("@id", id.ToString("D")), ("@type", eventType), ("@permission", permission)));
        }

        Assert.Equal(14L, await run.ScalarAsync<long>("SELECT Version FROM logistics.LoadingInstructions WHERE Id = @id", ("@id", id)));
    }
}
