using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Transfers;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence;

public sealed partial class LogisticsTransferOutgoingDocumentStore
{
    public async Task<TransferOutgoingDocumentForTransferV1?> ReadByTransferAsync(Guid transferId, CancellationToken cancellationToken = default)
    {
        var companyId = RequireCompany();
        if (transferId == Guid.Empty) return null;
        var row = await dbContext.TransferOutgoingDocuments.AsNoTracking().SingleOrDefaultAsync(x => x.TransferId == transferId, cancellationToken);
        if (row is null) return null;
        var lines = await dbContext.TransferOutgoingDocumentLines.AsNoTracking().Where(x => x.DocumentId == row.DocumentId)
            .OrderBy(x => x.Ordinal).ToArrayAsync(cancellationToken);
        var departure = await dbContext.TransferDepartureStates.AsNoTracking().SingleOrDefaultAsync(x => x.TransferId == transferId, cancellationToken);
        if (RequireCompany() != companyId) throw Refused(LogisticsStoreCodes.CompanyContextMismatch, "Şirket bağlamı değişti.");
        var item = Restore(row, lines);
        if (departure is not null && (departure.CompanyId != companyId || departure.DocumentId != row.DocumentId || departure.Version < 1
            || departure.Association is not (1 or 2))) throw Refused("logistics_transfer_departure_unavailable", "Çıkış belgesi bağlantısı okunamıyor.");
        return new(new(item.CompanyId, item.DocumentId, item.TransferId, item.OrderedTransferLineIds, item.EDispatchNumber,
            item.Status.ToString(), item.Version, item.CreatedBy, item.CreatedAtUtc, item.EffectiveBy, item.EffectiveAtUtc,
            item.CancelledBy, item.CancelledAtUtc, item.CancellationReason), departure is null
                ? LogisticsTransferEDispatchAssociationV1.Unknown : (LogisticsTransferEDispatchAssociationV1)departure.Association,
            departure?.Version);
    }

    private async Task StageInitialAssociationAsync(TransferOutgoingDocument document,
        LogisticsTransferEDispatchAssociationV1 association, LogisticsWriteEnvelope envelope,
        Func<CancellationToken, Task> fence, CancellationToken token)
    {
        if (association is not (LogisticsTransferEDispatchAssociationV1.NotLinked or LogisticsTransferEDispatchAssociationV1.Linked))
            throw Refused("logistics_transfer_departure_unavailable", "E-İrsaliye bağlantı türü açıkça belirtilmelidir.");
        var plan = transfers is null ? null : await transfers.ReadAsync(document.CompanyId, document.TransferId, token);
        await fence(token);
        if (plan is null || plan.CompanyId != document.CompanyId || plan.TransferId != document.TransferId
            || plan.SourceLocationId == Guid.Empty || plan.Version < 1 || plan.Status != "Planned" || plan.DispatchedAtUtc is not null
            || !plan.OrderedLineIds.SequenceEqual(document.OrderedTransferLineIds))
            throw Refused("logistics_transfer_departure_unavailable", "Gerçek transfer planı doğrulanamadı.");
        if (association == LogisticsTransferEDispatchAssociationV1.NotLinked && document.EDispatchNumber is not null)
            throw Refused("logistics_transfer_departure_unavailable", "Bağlantısız belge için e-İrsaliye numarası verilemez.");
        LogisticsTransferEDispatchEvidenceV1? officialProof = null;
        if (association == LogisticsTransferEDispatchAssociationV1.Linked)
        {
            officialProof = officialDocuments is null ? null : await officialDocuments.ReadAsync(document.CompanyId, document.TransferId, token);
            await fence(token);
            if (!LogisticsTransferDepartureProof.IsValid(officialProof, document.CompanyId, document.TransferId))
                throw Refused("logistics_transfer_departure_unavailable", "Gerçek e-İrsaliye sahibi kanıtı doğrulanamadı.");
        }
        var now = timeProvider.GetUtcNow();
        dbContext.TransferDepartureStates.Add(new TransferDepartureStateRow { CompanyId = document.CompanyId,
            TransferId = document.TransferId, DocumentId = document.DocumentId, SourceLocationId = plan.SourceLocationId,
            Association = (int)association, OfficialDocumentId = officialProof?.OfficialDocumentId,
            InitialOfficialDocumentVersion = officialProof?.Version, OfficialLinkedAtUtc = officialProof?.LinkedAtUtc,
            Version = 1, CreatedBy = envelope.ActorId, CreatedAtUtc = now });
        dbContext.TransferDepartureEvents.Add(new TransferDepartureEventRow { CompanyId = document.CompanyId,
            TransferId = document.TransferId, Version = 1, Kind = "AssociationRecorded", OccurredAtUtc = now,
            RecordedBy = envelope.ActorId, RecordedAtUtc = now, Reason = "Yeni transfer çıkış belgesinin açık bağlantı kaydı",
            CorrelationId = envelope.CorrelationId });
        auditEvents.Enqueue(new AuditEventDraft(document.CompanyId, "logistics.transfer-departure.association-recorded.v1",
            "Logistics.TransferDeparture", document.TransferId.ToString("D"), "RecordAssociation", null, 1,
            AuditActorTypes.User, envelope.ActorId, envelope.PermissionCode, envelope.RoleCodes, "{}",
            System.Text.Json.JsonSerializer.Serialize(new { document.TransferId, document.DocumentId, plan.SourceLocationId, association,
                officialDocumentId = officialProof?.OfficialDocumentId, officialVersion = officialProof?.Version,
                officialLinkedAtUtc = officialProof?.LinkedAtUtc, version = 1 }, Json),
            "Yeni transfer çıkış belgesi için e-İrsaliye bağlantı sahipliği kaydedildi", null,
            AuditAiInvolvement.None, null, envelope.SourceChannel, envelope.ClientIpAddress, envelope.UserAgent,
            envelope.CorrelationId, envelope.CausationId, envelope.IdempotencyKey));
    }
}

internal static class LogisticsTransferDepartureProof
{
    internal static bool IsValid(LogisticsTransferEDispatchEvidenceV1? proof, Guid companyId, Guid transferId) =>
        proof is { IsLinked: true, Version: > 0, OfficialDocumentId: var documentId, LinkedAtUtc: { } linkedAt,
            IsSentToGib: { } sent }
        && documentId != Guid.Empty && proof.CompanyId == companyId && proof.TransferId == transferId
        && linkedAt != default && linkedAt.Offset == TimeSpan.Zero
        && (sent ? proof.SentAtUtc is { } sentAt && sentAt.Offset == TimeSpan.Zero && sentAt >= linkedAt : proof.SentAtUtc is null)
        && (proof.CancelConfirmationVersion is null or > 0);
}
