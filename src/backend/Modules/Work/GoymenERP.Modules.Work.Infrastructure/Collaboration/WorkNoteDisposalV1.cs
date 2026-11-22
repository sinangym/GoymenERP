using System.Data;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Work.Domain;
using GoymenERP.Modules.Work.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace GoymenERP.Modules.Work.Infrastructure.Collaboration;

/// <summary>
/// YOL-D26 note disposal path. Two closed gates, both fail-closed: (1) <c>Work:NoteDisposal:Enabled</c> must be exactly
/// <c>true</c> (absent today — turning it on is the record disposal policy's activation, a human gate), and (2) the record's
/// owner must answer <see cref="WorkRecordDisposalOutcomeV1.Disposed"/> through <see cref="IWorkRecordDisposalPortV1"/> (no
/// owner does yet; the host port answers Unavailable). Only then is the record's whole note family deleted in one
/// transaction with one system audit event that carries counts, never note text. Tasks are not touched (YOL-D19 keeps them
/// on their own terms; their disposal is not part of YOL-D26).
/// </summary>
public sealed class WorkNoteDisposalV1(
    WorkDbContext database,
    ICompanyContext company,
    IConfiguration configuration,
    IWorkRecordDisposalPortV1 owners,
    IAuditEventCollector audit) : IWorkNoteDisposalV1
{
    private static readonly JsonSerializerOptions AuditJson = new(JsonSerializerDefaults.Web);

    public static bool IsEnabled(IConfiguration configuration) =>
        string.Equals(configuration[WorkNoteRetentionV1.DisposalEnabledKey], "true", StringComparison.OrdinalIgnoreCase);

    public async Task<WorkNoteDisposalResultV1> DisposeRecordNotesAsync(WorkRecordRefV1 record, string correlationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (!IsEnabled(configuration)) throw new WorkNoteDisposalException(WorkNoteRetentionV1.DisposalClosed);
        if (!company.IsResolved || company.CompanyId == Guid.Empty || record.RecordId == Guid.Empty
            || !WorkRecordTypesV1.IsKnown((int)record.Type) || string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > 128)
            throw new InvalidOperationException("Note disposal needs the resolved company, a known record and a correlation id.");
        var companyId = company.CompanyId;
        if (await owners.CheckDisposedAsync(companyId, record, cancellationToken) != WorkRecordDisposalOutcomeV1.Disposed)
            throw new WorkNoteDisposalException(WorkNoteRetentionV1.RecordDisposalNotConfirmed);

        var notes = await database.RecordNotes.Where(row => row.CompanyId == companyId && row.RecordType == (int)record.Type
            && row.RecordId == record.RecordId).ToListAsync(cancellationToken);
        var noteIds = notes.Select(row => row.Id).ToArray();
        var mentions = await database.RecordNoteMentions.Where(row => row.CompanyId == companyId && noteIds.Contains(row.NoteId)).ToListAsync(cancellationToken);
        var passivations = await database.RecordNotePassivations.Where(row => row.CompanyId == companyId && noteIds.Contains(row.NoteId)).ToListAsync(cancellationToken);
        var reads = await database.RecordNoteReads.Where(row => row.CompanyId == companyId && noteIds.Contains(row.NoteId)).ToListAsync(cancellationToken);
        var notifications = await database.InboxNotifications.Where(row => row.CompanyId == companyId
            && row.Kind == (int)WorkInboxNotificationKindV1.Mention && noteIds.Contains(row.SourceId)).ToListAsync(cancellationToken);
        var notificationIds = notifications.Select(row => row.Id).ToArray();
        var notificationReads = await database.InboxNotificationReads.Where(row => row.CompanyId == companyId
            && notificationIds.Contains(row.NotificationId)).ToListAsync(cancellationToken);
        var digestItems = await database.InboxDigestItems.Where(row => row.CompanyId == companyId
            && notificationIds.Contains(row.NotificationId)).ToListAsync(cancellationToken);

        // Children before parents (every foreign key is Restrict); corrections point at earlier notes of the same record,
        // so the notes go newest first.
        database.InboxDigestItems.RemoveRange(digestItems);
        database.InboxNotificationReads.RemoveRange(notificationReads);
        database.InboxNotifications.RemoveRange(notifications);
        database.RecordNoteReads.RemoveRange(reads);
        database.RecordNotePassivations.RemoveRange(passivations);
        database.RecordNoteMentions.RemoveRange(mentions);
        audit.Enqueue(new AuditEventDraft(companyId, "work.notes.disposed.v1", "Work.RecordNotes",
            WorkRecordTypesV1.Wire(record.Type) + ":" + record.RecordId.ToString("D"), "DisposeRecordNotes", null, null,
            AuditActorTypes.System, "work.note-disposal", "work.note-disposal", [], "{}",
            JsonSerializer.Serialize(new { notes = notes.Count, notifications = notifications.Count }, AuditJson),
            "Kayıt imha edildiği için kayda bağlı notlar imha edildi (YOL-D26).", null, AuditAiInvolvement.None, null, SourceChannels.System,
            null, null, correlationId, null, "work.note-disposal:" + correlationId));

        using var window = database.OpenNoteDisposalWindow();
        try
        {
            if (database.Database.IsRelational())
            {
                await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                await database.SaveChangesAsync(cancellationToken);
                foreach (var note in notes.OrderByDescending(row => row.CreatedAtUtc).ThenByDescending(row => row.Id))
                {
                    database.RecordNotes.Remove(note);
                    await database.SaveChangesAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
            }
            else
            {
                database.RecordNotes.RemoveRange(notes);
                await database.SaveChangesAsync(cancellationToken);
            }
        }
        finally
        {
            database.ChangeTracker.Clear();
        }

        return new WorkNoteDisposalResultV1(companyId, WorkRecordTypesV1.Wire(record.Type), record.RecordId, notes.Count, notifications.Count);
    }
}
