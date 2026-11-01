using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

/// <summary>
/// API-04 (DEC-SAL-002, DEC-SAL-008): the header revision's one save — the quantity revision's key rules (ADR-011 §3/§4/§7/§8),
/// the tracked header, the caller's last word, the domain's mutation, the archived approved revision with its header, the
/// header's revision / approval axis and its revisable columns, the attempt and the audit event. No line, slice or schedule row
/// is tracked or written.
/// </summary>
public sealed partial class SalesStore
{
    public async Task<SalesDraftStateResult?> ReviseDraftHeaderAsync(SalesDraftWriteEnvelope envelope, string requestHash,
        string commandName, Guid draftId, Func<SalesOrderDraft, SalesHeaderRevisionInput, DateTimeOffset, ApprovedRevisionSnapshot?> mutate,
        SalesDraftStateAudit audit, string revisionReason, Func<SalesOrderDraft, CancellationToken, Task<SalesHeaderRevisionInput>> beforeWrite,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        ArgumentNullException.ThrowIfNull(revisionReason);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(beforeWrite);
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        RequireCommand(commandName);
        if (commandName != SalesDraftCommands.ReviseHeaderV1)
        {
            throw new ArgumentException("Yalnız başlık revizyonu komutu siparişin başlığını değiştirir.", nameof(commandName));
        }

        if (string.IsNullOrWhiteSpace(audit.EventType) || string.IsNullOrWhiteSpace(audit.Operation) || string.IsNullOrWhiteSpace(audit.Reason))
        {
            throw new ArgumentException("Revizyon komutunun audit olayı tür, işlem ve gerekçe ister.", nameof(audit));
        }

        var companyId = RequireResolvedCompany();
        var keyHash = IdempotencyKeyHash(envelope.IdempotencyKey);
        var actorId = envelope.ActorId.Trim();
        var attempt = await dbContext.DraftAttempts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CommandName == commandName && row.IdempotencyKeyHash == keyHash, cancellationToken);
        if (attempt is not null)
        {
            if (!string.Equals(attempt.ActorId, actorId, StringComparison.Ordinal)
                || !string.Equals(attempt.RequestHash, requestHash, StringComparison.Ordinal))
            {
                throw Refused(SalesDraftStoreCodes.SameKeyPayloadMismatch,
                    "Aynı Idempotency-Key başka bir istekle geldi; ilk cevap bu isteğe verilmez.");
            }

            if (attempt.Outcome != AttemptOutcomeRecorded || attempt.DraftId is not { } recordedId)
            {
                throw Refused(SalesDraftStoreCodes.ConcurrentChange, "Bu anahtar kayıtlı bir terminal reddi taşıyor; tekrar aynı reddi alır.");
            }

            var stored = await FindDraftAsync(recordedId, cancellationToken)
                ?? throw new InvalidOperationException("Kayıtlı deneme kaydının taslağı bulunamadı.");
            return new SalesDraftStateResult(stored, SalesDraftWriteOutcome.Replayed, attempt.CorrelationId);
        }

        if (draftId == Guid.Empty)
        {
            return null;
        }

        var row = await dbContext.Drafts.SingleOrDefaultAsync(candidate => candidate.Id == draftId, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var draft = (await RestoreAsync([row], cancellationToken)).Single();
        var input = await beforeWrite(draft, cancellationToken)
            ?? throw new InvalidOperationException("Başlık revizyonu girdisi boş olamaz.");
        var before = HeaderPicture(draft, reason: null, supersededReason: null);
        var versionBefore = draft.Version;
        var revisionBefore = draft.Revision;
        var linesBefore = draft.Lines.Select(line => (line.QuantityText, line.Commercial)).ToArray();
        var now = timeProvider.GetUtcNow();
        var archived = mutate(draft, input, now);
        if (draft.Version != versionBefore + 1 || draft.Id != row.Id || draft.CompanyId != companyId)
        {
            throw new InvalidOperationException("Başlık revizyonu tam olarak bir sürüm ilerletir ve taslağı değiştirmez.");
        }

        // The header revision moves the header only: never a line's quantity or commercial value.
        if (!draft.Lines.Select(line => (line.QuantityText, line.Commercial)).SequenceEqual(linesBefore)
            || (archived is null) != (draft.Revision == revisionBefore))
        {
            throw new InvalidOperationException("Başlık revizyonu yalnız başlığı değiştirir.");
        }

        if (archived is not null)
        {
            AddArchiveRows(companyId, draft, archived, envelope);
        }

        var header = draft.Header;
        var money = draft.Money;
        row.Revision = draft.Revision;
        row.ApprovalState = (int)draft.ApprovalState;
        row.SubmittedBy = draft.Approval.SubmittedBy;
        row.SubmittedAtUtc = draft.Approval.SubmittedAtUtc;
        row.DecidedBy = draft.Approval.DecidedBy;
        row.DecidedAtUtc = draft.Approval.DecidedAtUtc;
        row.RejectionReason = draft.Approval.RejectionReason;
        row.Version = draft.Version;
        row.UpdatedAtUtc = draft.UpdatedAtUtc;
        // Assigned only when the value moved, so an unchanged column stays unmodified (the write guard sees only real changes).
        Assign(row.PartyId, header.PartyId, value => row.PartyId = value);
        Assign(row.CompanyAccountId, header.CompanyAccountId, value => row.CompanyAccountId = value);
        Assign(row.CurrencyId, header.CurrencyId, value => row.CurrencyId = value);
        Assign(row.DeliveryAddressId, header.DeliveryAddress?.Id, value => row.DeliveryAddressId = value);
        Assign(row.DeliveryAddressVersion, header.DeliveryAddress?.ExpectedVersion, value => row.DeliveryAddressVersion = value);
        Assign(row.ContactId, header.Contact?.Id, value => row.ContactId = value);
        Assign(row.ContactVersion, header.Contact?.ExpectedVersion, value => row.ContactVersion = value);
        Assign(row.SalesOwnerUserId, header.SalesOwnerUserId, value => row.SalesOwnerUserId = value);
        Assign(row.DueTermsJson, SalesDueTermsJson.Serialize(header.DueTerms), value => row.DueTermsJson = value);
        Assign(row.MoneyOperation, money.Operation, value => row.MoneyOperation = value);
        Assign(row.MoneyProfileId, money.ProfileId, value => row.MoneyProfileId = value);
        Assign(row.MoneyProfileVersion, money.ProfileVersion, value => row.MoneyProfileVersion = value);
        Assign(row.MoneyEffectiveOn, money.EffectiveOn, value => row.MoneyEffectiveOn = value);
        Assign(row.MoneyIsActive, money.IsActive, value => row.MoneyIsActive = value);
        Assign(row.MoneyEffectiveFrom, money.EffectiveFrom, value => row.MoneyEffectiveFrom = value);
        Assign(row.MoneyEffectiveThrough, money.EffectiveThrough, value => row.MoneyEffectiveThrough = value);
        Assign(row.MoneyScale, money.Scale, value => row.MoneyScale = value);

        try
        {
            // The domain accepted the reason above (required, trimmed, at most 4000): the "after" picture keeps it whole.
            AddAttemptAndAudit(companyId, keyHash, actorId, commandName, requestHash, envelope, draft, null, now, audit, versionBefore,
                before, HeaderPicture(draft, revisionReason.Trim(), archived?.Reason));
            await SaveAsync(cancellationToken);
        }
        catch (ArgumentException exception)
        {
            // The audit record's own envelope (not the caller's input) was refused: nothing was stored, and it is a store
            // failure (503), never the service's field-less 400 for an invalid argument.
            dbContext.ChangeTracker.Clear();
            throw Refused(SalesDraftStoreCodes.AuditRecordInvalid, "Başlık revizyonunun audit kaydı oluşturulamadı; hiçbir şey kaydedilmedi.", exception);
        }
        return new SalesDraftStateResult(draft, SalesDraftWriteOutcome.Recorded, envelope.CorrelationId.Trim());
    }

    private static void Assign<T>(T current, T next, Action<T> set)
    {
        if (!EqualityComparer<T>.Default.Equals(current, next))
        {
            set(next);
        }
    }

    /// <summary>
    /// API-04: the audit picture of a draft's revisable header — revision, approval axis, customer, currency with its money
    /// profile, delivery references, sales owner and due terms. The "after" picture carries THIS revision's reason (SAT-D126:
    /// recorded on every header revision, in place or not) and, only when THIS revision archived the approved one, the
    /// archive's reason — never an earlier archive's (no stale attribution on an in-place change).
    /// </summary>
    private static object HeaderPicture(SalesOrderDraft draft, string? reason, string? supersededReason) => new
    {
        revision = draft.Revision,
        state = draft.ApprovalState.ToString(),
        approval = draft.Approval,
        reason,
        supersededReason,
        partyId = draft.Header.PartyId,
        companyAccountId = draft.Header.CompanyAccountId,
        currencyId = draft.Header.CurrencyId,
        moneyProfile = new { draft.Money.ProfileId, draft.Money.ProfileVersion, draft.Money.Scale },
        deliveryAddress = draft.Header.DeliveryAddress,
        contact = draft.Header.Contact,
        salesOwnerUserId = draft.Header.SalesOwnerUserId,
        dueTerms = SalesDraftHeaderApiV1.ToWire(draft.Header.DueTerms),
    };
}
