using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Work.Domain;
using GoymenERP.Modules.Work.Infrastructure.Collaboration;
using GoymenERP.Modules.Work.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace GoymenERP.Work.UnitTests;

/// <summary>WP9: note read receipts, the D7 people picker fence, the widened record kinds and the YOL-D26 disposal path. SENTETIK.</summary>
public sealed partial class WorkCollaborationServiceTests
{
    [Fact]
    public async Task A_reader_marks_a_note_read_once_the_first_time_wins_and_every_record_reader_sees_who_read_it()
    {
        var harness = new Harness();
        var note = await harness.Service(Writer, WorkPermissionManifestV1.NotesWrite)
            .AddNoteAsync("sales-order-draft", Draft, new("SENTETIK not", [Reader], null), Meta("r1"));
        Assert.Null(note.Note.ReadByMeAtUtc);
        Assert.Empty(note.Note.Reads);

        var first = await harness.Service(Reader).MarkNoteReadAsync(note.Note.Id);
        harness.Time.Now = Now.AddMinutes(5);
        var again = await harness.Service(Reader).MarkNoteReadAsync(note.Note.Id);
        Assert.Equal((note.Note.Id, Now, Now), (first.NoteId, first.ReadAtUtc, again.ReadAtUtc));

        var asReader = Assert.Single((await harness.Service(Reader).ListNotesAsync("sales-order-draft", Draft, false)).Items);
        Assert.Equal(Now, asReader.ReadByMeAtUtc);
        var asWriter = Assert.Single((await harness.Service(Writer).ListNotesAsync("sales-order-draft", Draft, false)).Items);
        Assert.Null(asWriter.ReadByMeAtUtc);
        var read = Assert.Single(asWriter.Reads);
        Assert.Equal((Reader, "Okur", Now), (read.UserId, read.DisplayName, read.ReadAtUtc));

        await using var database = harness.Database();
        Assert.Single(await database.RecordNoteReads.ToListAsync());
    }

    [Fact]
    public async Task The_author_cannot_mark_their_own_note_read_and_an_unreadable_or_unknown_note_answers_404()
    {
        var harness = new Harness();
        var note = await harness.Service(Writer, WorkPermissionManifestV1.NotesWrite)
            .AddNoteAsync("sales-order-draft", Draft, new("SENTETIK", null, null), Meta("r2"));
        var own = await Assert.ThrowsAsync<WorkCollaborationException>(() => harness.Service(Writer).MarkNoteReadAsync(note.Note.Id));
        Assert.Equal((409, WorkCollaborationProblemCodesV1.NoteOwnRead), (own.Status, own.Code));
        Assert.Equal(404, (await Assert.ThrowsAsync<WorkCollaborationException>(() => harness.Service(Outsider).MarkNoteReadAsync(note.Note.Id))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<WorkCollaborationException>(() => harness.Service(Reader).MarkNoteReadAsync(Guid.NewGuid()))).Status);
        harness.Records.Unavailable = true;
        Assert.Equal(503, (await Assert.ThrowsAsync<WorkCollaborationException>(() => harness.Service(Reader).MarkNoteReadAsync(note.Note.Id))).Status);
        await using var database = harness.Database();
        Assert.Empty(await database.RecordNoteReads.ToListAsync());
    }

    [Theory]
    [InlineData(WorkPermissionManifestV1.TasksAssign)]
    [InlineData(WorkPermissionManifestV1.NotesWrite)]
    public async Task D7_the_people_picker_answers_a_holder_of_assign_or_note_write_with_id_and_display_name_only(string permission)
    {
        var harness = new Harness();
        var page = await harness.Service(Writer, permission).SearchPeopleAsync("o");
        Assert.Equal(Company, page.CompanyId);
        // The wire type itself has exactly two members: an id and a display name.
        Assert.Equal(new[] { nameof(WorkPersonSummaryV1.DisplayName), nameof(WorkPersonSummaryV1.UserId) },
            typeof(WorkPersonSummaryV1).GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(new[] { Writer, Reader, Outsider }.Order(), page.People.Select(person => person.UserId).Order());
    }

    [Theory]
    [InlineData(null)]
    [InlineData(WorkPermissionManifestV1.TasksComplete)]
    public async Task D7_the_people_picker_refuses_403_without_assign_or_note_write(string? permission)
    {
        var harness = new Harness();
        var error = await Assert.ThrowsAsync<WorkCollaborationException>(() => harness.Service(Writer, permission).SearchPeopleAsync(null));
        Assert.Equal(403, error.Status);
        Assert.Equal(401, (await Assert.ThrowsAsync<WorkCollaborationException>(() => harness.Service(null, permission).SearchPeopleAsync(null))).Status);
    }

    [Theory]
    [InlineData("sales-invoice", WorkRecordTypeV1.SalesInvoice)]
    [InlineData("loading-instruction", WorkRecordTypeV1.LoadingInstruction)]
    [InlineData("purchase-incoming-invoice", WorkRecordTypeV1.PurchaseIncomingInvoice)]
    [InlineData("manufacturing-order", WorkRecordTypeV1.ManufacturingOrder)]
    [InlineData("finance-manual-receipt", WorkRecordTypeV1.FinanceManualReceipt)]
    [InlineData("finance-manual-bank-payment", WorkRecordTypeV1.FinanceManualBankPayment)]
    [InlineData("sales-quote", WorkRecordTypeV1.SalesQuote)]
    [InlineData("purchase-goods-receipt", WorkRecordTypeV1.PurchaseGoodsReceipt)]
    public async Task The_WP9_record_kinds_take_notes_and_tasks_through_the_owner_port_like_the_first_five(string wire, WorkRecordTypeV1 type)
    {
        Assert.True(WorkRecordTypesV1.TryParse(wire, out var parsed));
        Assert.Equal(type, parsed);
        Assert.Equal(wire, WorkRecordTypesV1.Wire(type));
        var harness = new Harness();
        var note = await harness.Service(Writer, WorkPermissionManifestV1.NotesWrite).AddNoteAsync(wire, Draft, new("SENTETIK", [Reader], null), Meta("n-" + wire));
        Assert.Equal((wire, Draft), (note.Note.RecordType, note.Note.RecordId));
        var task = await harness.Service(Writer, WorkPermissionManifestV1.TasksAssign)
            .CreateTaskAsync(new("SENTETIK gorev", null, Reader, Now.AddDays(1), wire, Draft), Meta("t-" + wire));
        Assert.Equal(wire, task.Task.RecordType);
        Assert.Equal(new[] { (type, Draft) }, harness.Records.Checked.Distinct());
        Assert.Equal(404, (await Assert.ThrowsAsync<WorkCollaborationException>(() => harness.Service(Outsider).ListNotesAsync(wire, Draft, false))).Status);
    }

    [Fact]
    public void A_dispatch_note_is_not_a_record_kind_of_its_own_and_the_goods_receipt_is_YOL_D42s_thirteenth()
    {
        Assert.False(WorkRecordTypesV1.TryParse("goods-receipt", out _));
        Assert.False(WorkRecordTypesV1.TryParse("dispatch-note", out _));
        Assert.True(WorkRecordTypesV1.TryParse("purchase-goods-receipt", out var receipt));
        Assert.Equal(13, (int)receipt);
        Assert.Equal(13, WorkRecordTypesV1.WireNames.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task YOL_D26_note_disposal_is_closed_by_default_and_deletes_nothing()
    {
        var harness = new Harness();
        var note = await harness.Service(Writer, WorkPermissionManifestV1.NotesWrite).AddNoteAsync("sales-order-draft", Draft, new("SENTETIK", [Reader], null), Meta("x1"));
        foreach (var settings in new Dictionary<string, string?>[] { new(), new() { [WorkNoteRetentionV1.DisposalEnabledKey] = "false" },
                     new() { [WorkNoteRetentionV1.DisposalEnabledKey] = "yes" } })
        {
            var error = await Assert.ThrowsAsync<WorkNoteDisposalException>(() => harness.Disposal(settings, WorkRecordDisposalOutcomeV1.Disposed)
                .DisposeRecordNotesAsync(new(WorkRecordTypeV1.SalesOrderDraft, Draft), "corr-dispose"));
            Assert.Equal(WorkNoteRetentionV1.DisposalClosed, error.Code);
        }

        await using var database = harness.Database();
        Assert.Equal(note.Note.Id, Assert.Single(await database.RecordNotes.ToListAsync()).Id);
        Assert.Single(await database.InboxNotifications.ToListAsync());
    }

    [Theory]
    [InlineData(WorkRecordDisposalOutcomeV1.Unavailable)]
    [InlineData(WorkRecordDisposalOutcomeV1.NotDisposed)]
    public async Task YOL_D26_even_with_the_key_on_nothing_is_disposed_without_the_owners_disposal_confirmation(WorkRecordDisposalOutcomeV1 owner)
    {
        var harness = new Harness();
        await harness.Service(Writer, WorkPermissionManifestV1.NotesWrite).AddNoteAsync("sales-order-draft", Draft, new("SENTETIK", null, null), Meta("x2"));
        var error = await Assert.ThrowsAsync<WorkNoteDisposalException>(() => harness.Disposal(Enabled, owner)
            .DisposeRecordNotesAsync(new(WorkRecordTypeV1.SalesOrderDraft, Draft), "corr-dispose"));
        Assert.Equal(WorkNoteRetentionV1.RecordDisposalNotConfirmed, error.Code);
        await using var database = harness.Database();
        Assert.Single(await database.RecordNotes.ToListAsync());
    }

    [Fact]
    public async Task YOL_D26_an_open_path_with_the_owners_confirmation_disposes_the_whole_note_family_of_that_record_and_keeps_tasks()
    {
        var harness = new Harness();
        var first = await harness.Service(Writer, WorkPermissionManifestV1.NotesWrite).AddNoteAsync("sales-order-draft", Draft, new("SENTETIK", [Reader], null), Meta("y1"));
        await harness.Service(Writer, WorkPermissionManifestV1.NotesWrite).AddNoteAsync("sales-order-draft", Draft, new("duzeltme", null, first.Note.Id), Meta("y2"));
        await harness.Service(Writer, WorkPermissionManifestV1.NotesWrite).PassivateNoteAsync(first.Note.Id, new("eski"), Meta("y3"));
        await harness.Service(Reader).MarkNoteReadAsync(first.Note.Id);
        var mention = Assert.Single((await harness.Service(Reader).ListMyInboxAsync(null, false)).Items);
        await harness.Service(Reader).MarkReadAsync(mention.Id);
        var otherRecord = await harness.Service(Writer, WorkPermissionManifestV1.NotesWrite).AddNoteAsync("sales-quote", Draft, new("baska kayit", null, null), Meta("y4"));
        await harness.Service(Writer, WorkPermissionManifestV1.TasksAssign)
            .CreateTaskAsync(new("SENTETIK gorev", null, Reader, Now.AddDays(1), "sales-order-draft", Draft), Meta("y5"));

        var result = await harness.Disposal(Enabled, WorkRecordDisposalOutcomeV1.Disposed)
            .DisposeRecordNotesAsync(new(WorkRecordTypeV1.SalesOrderDraft, Draft), "corr-dispose");
        Assert.Equal((2, 1), (result.NotesDisposed, result.NotificationsDisposed));

        await using var database = harness.Database();
        Assert.Equal(otherRecord.Note.Id, Assert.Single(await database.RecordNotes.ToListAsync()).Id);
        Assert.Empty(await database.RecordNoteMentions.ToListAsync());
        Assert.Empty(await database.RecordNotePassivations.ToListAsync());
        Assert.Empty(await database.RecordNoteReads.ToListAsync());
        Assert.Empty(await database.InboxNotificationReads.ToListAsync());
        var remaining = Assert.Single(await database.InboxNotifications.ToListAsync());
        Assert.Equal((int)WorkInboxNotificationKindV1.TaskAssigned, remaining.Kind);
        Assert.Single(await database.Tasks.ToListAsync());
        var audit = Assert.Single(harness.Audit.Events, item => item.EventType == "work.notes.disposed.v1");
        Assert.DoesNotContain("SENTETIK", audit.AfterJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Outside_the_disposal_window_a_note_still_cannot_be_deleted()
    {
        var harness = new Harness();
        await harness.Service(Writer, WorkPermissionManifestV1.NotesWrite).AddNoteAsync("sales-order-draft", Draft, new("SENTETIK", null, null), Meta("z1"));
        await using var database = harness.Database();
        database.RecordNotes.Remove(await database.RecordNotes.SingleAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync());
    }

    private static readonly Dictionary<string, string?> Enabled = new() { [WorkNoteRetentionV1.DisposalEnabledKey] = "true" };

    private sealed class FixedDisposal(WorkRecordDisposalOutcomeV1 outcome) : IWorkRecordDisposalPortV1
    {
        public Task<WorkRecordDisposalOutcomeV1> CheckDisposedAsync(Guid companyId, WorkRecordRefV1 record, CancellationToken cancellationToken = default) =>
            Task.FromResult(companyId == Company ? outcome : WorkRecordDisposalOutcomeV1.Unavailable);
    }

    private sealed partial class Harness
    {
        public WorkNoteDisposalV1 Disposal(IReadOnlyDictionary<string, string?> settings, WorkRecordDisposalOutcomeV1 owner)
        {
            var company = new CompanyContext();
            company.Set(Company);
            return new WorkNoteDisposalV1(Database(), company, new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
                new FixedDisposal(owner), Audit);
        }
    }
}
