using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

/// <summary>
/// The Sales draft store of the server-resolved company (SALES-P2). One call is one save: the
/// header, the lines with their owner snapshots, the delivery slices, the attempt record, the
/// number counter and the audit event commit together or not at all.
/// <list type="bullet">
/// <item><b>Company:</b> a draft of any other company than the resolved context is refused before
/// anything is read (<see cref="SalesDraftStoreCodes.CompanyContextMismatch"/>); every read runs
/// under the tenant filter, so another company's draft is null, never 403.</item>
/// <item><b>Replay (ADR-011 §2–§4, R3):</b> (company, <see cref="SalesDraftCommands.CreateV1"/>,
/// uppercase SHA-256 of the trimmed idempotency key) is the attempt identity; the raw key is never
/// stored. The same key with the same canonical request hash and the same actor answers the first
/// stored draft; another hash or another actor is <see cref="SalesDraftStoreCodes.SameKeyPayloadMismatch"/>
/// — one definite refusal that discloses nothing of the stored result nor whose key it is; a key of
/// another company is invisible. Authority is the caller's
/// check before the replay is read (§5). The key must still be canonical text (no surrounding
/// whitespace) so that the hash the caller reasons about is the hash stored.</item>
/// <item><b>Number:</b> the company's counter row gives the technical document number under a
/// concurrency token; the number is display text, not an official document (Ö8), and no gap-free
/// guarantee is made — a lost race leaves its number unused.</item>
/// <item><b>Race:</b> two attempts with the same key that both pass the lookup collide on the
/// attempt key; the loser gets <see cref="SalesDraftStoreCodes.ConcurrentChange"/>, nothing of it
/// is stored, and its retry answers from the winner.</item>
/// <item><b>Domain decides:</b> the caller builds the draft through the domain with the owners'
/// snapshots (Product eligibility, Settings unit and money profiles) already in hand; the store
/// validates nothing about them again and stores only what the domain accepted. A stored draft is
/// rebuilt through the same domain rules, so a corrupt row is refused, not repaired.</item>
/// </list>
/// </summary>
public sealed class SalesStore(
    SalesDbContext dbContext,
    ICompanyContext companyContext,
    IAuditEventCollector auditEvents,
    TimeProvider timeProvider) : ISalesOrderDraftStore
{
    private const string DraftEntityType = "Sales.OrderDraft";
    private const int ActorMaximumLength = 128;
    private const int KeyMaximumLength = 200;
    private const int DefaultPageSize = 50;
    private const int MaximumPageSize = 100;
    private const int AttemptOutcomeRecorded = 1;
    private const int AttemptOutcomeRefused = 2;

    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    public async Task<SalesDraftStoreResult> StoreDraftAsync(SalesDraftWriteEnvelope envelope, string requestHash,
        Func<string, SalesOrderDraft> build, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(build);
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        var companyId = RequireResolvedCompany();

        var keyHash = IdempotencyKeyHash(envelope.IdempotencyKey);
        var actorId = envelope.ActorId.Trim();
        var attempt = await dbContext.DraftAttempts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CommandName == SalesDraftCommands.CreateV1 && row.IdempotencyKeyHash == keyHash,
                cancellationToken);
        if (attempt is not null)
        {
            // ADR-011 §3: the stored result is disclosed to the same content of the same actor only;
            // another actor gets exactly the answer another content gets, so nothing is disclosed.
            if (!string.Equals(attempt.ActorId, actorId, StringComparison.Ordinal)
                || !string.Equals(attempt.RequestHash, requestHash, StringComparison.Ordinal))
            {
                throw Refused(SalesDraftStoreCodes.SameKeyPayloadMismatch,
                    "Aynı Idempotency-Key başka bir istekle geldi; ilk cevap bu isteğe verilmez.");
            }

            if (attempt.Outcome != AttemptOutcomeRecorded || attempt.DraftId is not { } draftId)
            {
                // ADR-011 §7: the key holds a recorded terminal refusal; the caller replays it through FindReplayAsync.
                throw Refused(SalesDraftStoreCodes.ConcurrentChange, "Bu anahtar kayıtlı bir terminal reddi taşıyor; tekrar aynı reddi alır.");
            }

            var stored = await FindDraftAsync(draftId, cancellationToken)
                ?? throw new InvalidOperationException("Kayıtlı deneme kaydının taslağı bulunamadı.");
            return new SalesDraftStoreResult(stored, SalesDraftWriteOutcome.Replayed, attempt.CorrelationId);
        }

        var now = timeProvider.GetUtcNow();
        var counter = await dbContext.DraftNumbers.SingleOrDefaultAsync(cancellationToken);
        long number;
        if (counter is null)
        {
            number = 1;
            dbContext.DraftNumbers.Add(new DraftNumberRow { CompanyId = companyId, Next = 2, Version = 1, UpdatedAtUtc = now });
        }
        else
        {
            number = counter.Next;
            counter.Next = checked(counter.Next + 1);
            counter.Version = checked(counter.Version + 1);
            counter.UpdatedAtUtc = now;
        }

        var draft = build(DocumentNo(now, number)) ?? throw new ArgumentException("Taslak kurulmadı.", nameof(build));
        RequireCompany(draft.CompanyId);
        draft.RequireStorable();
        if (await dbContext.Drafts.AsNoTracking().AnyAsync(row => row.Id == draft.Id, cancellationToken))
        {
            throw Refused(SalesDraftStoreCodes.ConcurrentChange, "Bu taslak kimliği şirketin başka bir taslağında kullanılıyor.");
        }

        AddDraft(draft, envelope, now);
        // D2: the aggregate returned from the create carries its creator the way every later read does (the row's CreatedBy IS
        // the envelope's actor, written by SalesDraftRows.Add just above).
        draft.RestoreCreator(envelope.ActorId);
        dbContext.DraftAttempts.Add(new DraftAttemptRow
        {
            CompanyId = companyId,
            CommandName = SalesDraftCommands.CreateV1,
            IdempotencyKeyHash = keyHash,
            RequestHash = requestHash,
            ActorId = actorId,
            Outcome = AttemptOutcomeRecorded,
            DraftId = draft.Id,
            CorrelationId = envelope.CorrelationId.Trim(),
            RecordedAtUtc = now,
        });
        Audit(envelope, companyId, draft, now);
        await SaveAsync(cancellationToken);
        return new SalesDraftStoreResult(draft, SalesDraftWriteOutcome.Recorded, envelope.CorrelationId.Trim());
    }

    public async Task<SalesDraftReplay?> FindReplayAsync(SalesDraftWriteEnvelope envelope, string requestHash,
        CancellationToken cancellationToken = default, string commandName = SalesDraftCommands.CreateV1)
    {
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        RequireCommand(commandName);
        RequireResolvedCompany();
        var keyHash = IdempotencyKeyHash(envelope.IdempotencyKey);
        var actorId = envelope.ActorId.Trim();
        var attempt = await dbContext.DraftAttempts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CommandName == commandName && row.IdempotencyKeyHash == keyHash,
                cancellationToken);
        if (attempt is null)
        {
            return null;
        }

        if (!string.Equals(attempt.ActorId, actorId, StringComparison.Ordinal)
            || !string.Equals(attempt.RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw Refused(SalesDraftStoreCodes.SameKeyPayloadMismatch,
                "Aynı Idempotency-Key başka bir istekle geldi; ilk cevap bu isteğe verilmez.");
        }

        if (attempt.Outcome == AttemptOutcomeRecorded && attempt.DraftId is { } draftId)
        {
            var stored = await FindDraftAsync(draftId, cancellationToken)
                ?? throw new InvalidOperationException("Kayıtlı deneme kaydının taslağı bulunamadı.");
            return new SalesDraftReplay(stored, null, attempt.CorrelationId);
        }

        var refusal = attempt.RefusalJson is null ? null : JsonSerializer.Deserialize<SalesDraftTerminalRefusal>(attempt.RefusalJson, Json);
        if (refusal is null || refusal.Status is < 400 or > 599 || string.IsNullOrWhiteSpace(refusal.Code) || refusal.Errors is null)
        {
            throw new InvalidOperationException("Kayıtlı terminal ret okunamadı.");
        }

        return new SalesDraftReplay(null, refusal, attempt.CorrelationId);
    }

    public async Task RecordRefusalAsync(SalesDraftWriteEnvelope envelope, string requestHash, SalesDraftTerminalRefusal refusal,
        CancellationToken cancellationToken = default, string commandName = SalesDraftCommands.CreateV1, Guid? draftId = null)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        RequireCommand(commandName);
        var companyId = RequireResolvedCompany();
        if (refusal.Status is < 400 or > 599 || string.IsNullOrWhiteSpace(refusal.Code) || refusal.Errors is null
            || refusal.Errors.Any(error => error is null || string.IsNullOrWhiteSpace(error.Field) || string.IsNullOrWhiteSpace(error.Code)))
        {
            throw new ArgumentException("Terminal ret bir durum, bir kod ve tam alan hataları taşır.", nameof(refusal));
        }

        var keyHash = IdempotencyKeyHash(envelope.IdempotencyKey);
        if (await dbContext.DraftAttempts.AsNoTracking()
                .AnyAsync(row => row.CommandName == commandName && row.IdempotencyKeyHash == keyHash, cancellationToken))
        {
            throw Refused(SalesDraftStoreCodes.ConcurrentChange, "Bu anahtar zaten bir deneme taşıyor; tekrar onu alır.");
        }

        dbContext.DraftAttempts.Add(new DraftAttemptRow
        {
            CompanyId = companyId,
            CommandName = commandName,
            IdempotencyKeyHash = keyHash,
            RequestHash = requestHash,
            ActorId = envelope.ActorId.Trim(),
            Outcome = AttemptOutcomeRefused,
            DraftId = draftId,
            RefusalJson = JsonSerializer.Serialize(refusal, Json),
            CorrelationId = envelope.CorrelationId.Trim(),
            RecordedAtUtc = timeProvider.GetUtcNow(),
        });
        await SaveAsync(cancellationToken);
    }

    public async Task<SalesDraftStateResult?> UpdateDraftStateAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
        Guid draftId, Action<SalesOrderDraft, DateTimeOffset> mutate, SalesDraftStateAudit audit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        ArgumentNullException.ThrowIfNull(audit);
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        RequireCommand(commandName);
        if (!SalesDraftCommands.IsStateCommand(commandName))
        {
            throw new ArgumentException("Yalnız onay ekseni komutları bir taslağın durumunu değiştirir.", nameof(commandName));
        }

        if (string.IsNullOrWhiteSpace(audit.EventType) || string.IsNullOrWhiteSpace(audit.Operation) || string.IsNullOrWhiteSpace(audit.Reason))
        {
            throw new ArgumentException("Durum komutunun audit olayı tür, işlem ve gerekçe ister.", nameof(audit));
        }

        var companyId = RequireResolvedCompany();
        var keyHash = IdempotencyKeyHash(envelope.IdempotencyKey);
        var actorId = envelope.ActorId.Trim();
        var attempt = await dbContext.DraftAttempts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CommandName == commandName && row.IdempotencyKeyHash == keyHash, cancellationToken);
        if (attempt is not null)
        {
            // ADR-011 §3/§4: the same content of the same actor answers the stored record as it is now; anything else is one 409.
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

        // The header is tracked (its version is the concurrency token of this write); the lines are read as before.
        var row = await dbContext.Drafts.SingleOrDefaultAsync(candidate => candidate.Id == draftId, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var draft = (await RestoreAsync([row], cancellationToken)).Single();
        var before = new { state = draft.ApprovalState.ToString(), version = draft.Version, approval = draft.Approval };
        var versionBefore = draft.Version;
        var now = timeProvider.GetUtcNow();
        // The domain decides: the expected version, the allowed transition and the reason bound. A
        // refusal leaves the tracked row untouched (nothing below has run) and is the caller's to answer.
        mutate(draft, now);
        if (draft.Version != versionBefore + 1 || draft.Id != row.Id || draft.CompanyId != companyId)
        {
            throw new InvalidOperationException("Onay geçişi tam olarak bir sürüm ilerletir ve taslağı değiştirmez.");
        }

        row.ApprovalState = (int)draft.ApprovalState;
        row.SubmittedBy = draft.Approval.SubmittedBy;
        row.SubmittedAtUtc = draft.Approval.SubmittedAtUtc;
        row.DecidedBy = draft.Approval.DecidedBy;
        row.DecidedAtUtc = draft.Approval.DecidedAtUtc;
        row.RejectionReason = draft.Approval.RejectionReason;
        row.Version = draft.Version;
        row.UpdatedAtUtc = draft.UpdatedAtUtc;
        dbContext.DraftAttempts.Add(new DraftAttemptRow
        {
            CompanyId = companyId,
            CommandName = commandName,
            IdempotencyKeyHash = keyHash,
            RequestHash = requestHash,
            ActorId = actorId,
            Outcome = AttemptOutcomeRecorded,
            DraftId = draft.Id,
            CorrelationId = envelope.CorrelationId.Trim(),
            RecordedAtUtc = now,
        });
        auditEvents.Enqueue(new AuditEventDraft(
            companyId,
            audit.EventType,
            DraftEntityType,
            draft.Id.ToString("D"),
            audit.Operation,
            EntityVersionBefore: versionBefore,
            EntityVersionAfter: draft.Version,
            AuditActorTypes.User,
            envelope.ActorId,
            envelope.PermissionCode,
            envelope.RoleCodes,
            JsonSerializer.Serialize(before, Json),
            JsonSerializer.Serialize(new { state = draft.ApprovalState.ToString(), version = draft.Version, approval = draft.Approval }, Json),
            audit.Reason,
            ApprovalContextJson: null,
            AuditAiInvolvement.None,
            AiContextJson: null,
            envelope.SourceChannel,
            envelope.ClientIpAddress,
            envelope.UserAgent,
            envelope.CorrelationId,
            envelope.CausationId,
            envelope.IdempotencyKey));
        await SaveAsync(cancellationToken);
        return new SalesDraftStateResult(draft, SalesDraftWriteOutcome.Recorded, envelope.CorrelationId.Trim());
    }

    /// <summary>
    /// K3 Head B: the commercial revision, one save. The same key rules as a state command; the header AND
    /// the line rows are tracked; the domain decides (expected version, state, every line, the rules, the
    /// no-change refusal) and returns the archived approved snapshot when it made one, whose rows are written
    /// beside the header's revision/approval axis and the lines' commercial columns — together with the
    /// attempt record and the audit event, or nothing at all.
    /// </summary>
    public Task<SalesDraftStateResult?> ReviseDraftCommercialAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
        Guid draftId, Func<SalesOrderDraft, DateTimeOffset, ApprovedRevisionSnapshot?> mutate, SalesDraftStateAudit audit,
        Func<CancellationToken, Task> beforeWrite, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(beforeWrite);
        RequireCommand(commandName);
        if (commandName is not (SalesDraftCommands.ReviseCommercialV1 or SalesDraftCommands.ApprovePriceFloorExceptionsV1))
        {
            throw new ArgumentException("Yalnız fiyat revizyonu komutu satırların ticari alanlarını değiştirir.", nameof(commandName));
        }

        return ReviseLinesAsync(envelope, requestHash, commandName, draftId, mutate, audit,
            (_, writeCancellationToken) => beforeWrite(writeCancellationToken), Commercial, cancellationToken);
    }

    /// <summary>
    /// DEC-SAL-002: the quantity revision, one save — the commercial revision's shape (the same core below). The header AND
    /// the line rows are tracked; <paramref name="beforeWrite"/> receives the draft as read (the caller's fences, the domain's
    /// preconditions and the owners' links); the domain decides and returns the archived approved snapshot (with its reason)
    /// when it made one; the lines' quantity columns change beside the header — no slice, schedule or commercial value moves
    /// (checked).
    /// </summary>
    public Task<SalesDraftStateResult?> ReviseDraftQuantityAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
        Guid draftId, Func<SalesOrderDraft, DateTimeOffset, ApprovedRevisionSnapshot?> mutate, SalesDraftStateAudit audit,
        Func<SalesOrderDraft, CancellationToken, Task> beforeWrite, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(beforeWrite);
        RequireCommand(commandName);
        if (commandName != SalesDraftCommands.ReviseQuantityV1)
        {
            throw new ArgumentException("Yalnız miktar revizyonu komutu satırların miktarını değiştirir.", nameof(commandName));
        }

        return ReviseLinesAsync(envelope, requestHash, commandName, draftId, mutate, audit, beforeWrite, Quantities, cancellationToken);
    }

    /// <summary>
    /// The one core of both line revisions: the key rules of a state command, the tracked header and lines, the caller's
    /// last word, the domain's mutation, the archived rows, the header's axis and revision, the line columns the command
    /// owns, the attempt and the audit event — one save, or nothing at all.
    /// </summary>
    private async Task<SalesDraftStateResult?> ReviseLinesAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
        Guid draftId, Func<SalesOrderDraft, DateTimeOffset, ApprovedRevisionSnapshot?> mutate, SalesDraftStateAudit audit,
        Func<SalesOrderDraft, CancellationToken, Task> beforeWrite, Func<SalesOrderDraft, object> picture,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        ArgumentNullException.ThrowIfNull(audit);
        RequireEnvelope(envelope);
        RequireHash(requestHash);

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

        // The line rows are tracked here so the command's own columns can change in this save (the guard admits only those).
        var lineRows = await dbContext.DraftLines.Where(line => line.DraftId == draftId).OrderBy(line => line.LineNo).ToListAsync(cancellationToken);
        var draft = (await RestoreAsync([row], cancellationToken)).Single();
        await beforeWrite(draft, cancellationToken);
        var before = picture(draft);
        var versionBefore = draft.Version;
        var commercialBefore = draft.Lines.Select(line => line.Commercial).ToArray();
        var quantitiesBefore = draft.Lines.Select(line => line.QuantityText).ToArray();
        var now = timeProvider.GetUtcNow();
        var archived = mutate(draft, now);
        if (draft.Version != versionBefore + 1 || draft.Id != row.Id || draft.CompanyId != companyId)
        {
            throw new InvalidOperationException("Satır revizyonu tam olarak bir sürüm ilerletir ve taslağı değiştirmez.");
        }

        // Each command moves only its own columns: a price revision never a quantity, a quantity revision never a price.
        var quantityCommand = commandName == SalesDraftCommands.ReviseQuantityV1;
        if (quantityCommand ? !draft.Lines.Select(line => line.Commercial).SequenceEqual(commercialBefore)
                : !draft.Lines.Select(line => line.QuantityText).SequenceEqual(quantitiesBefore, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Satır revizyonu yalnız kendi komutunun alanlarını değiştirir.");
        }

        if (archived is not null)
        {
            // The approved revision as it stood, written BEFORE the header advances in the same save.
            dbContext.DraftRevisions.Add(new DraftRevisionRow
            {
                CompanyId = companyId,
                DraftId = draft.Id,
                Revision = archived.Revision,
                SubmittedBy = archived.Approval.SubmittedBy ?? throw new InvalidOperationException("Onaylı revizyonun göndereni yok."),
                SubmittedAtUtc = archived.Approval.SubmittedAtUtc ?? throw new InvalidOperationException("Onaylı revizyonun gönderim zamanı yok."),
                ApprovedBy = archived.Approval.DecidedBy ?? throw new InvalidOperationException("Onaylı revizyonun onaylayanı yok."),
                ApprovedAtUtc = archived.Approval.DecidedAtUtc ?? throw new InvalidOperationException("Onaylı revizyonun onay zamanı yok."),
                MoneyProfileId = archived.MoneyProfileId,
                MoneyProfileVersion = archived.MoneyProfileVersion,
                MoneyScale = archived.MoneyScale,
                ArchivedAtUtc = archived.ArchivedAtUtc,
                ArchivedBy = archived.ArchivedBy,
                CorrelationId = envelope.CorrelationId.Trim(),
                Reason = archived.Reason,
            });
            foreach (var line in archived.Lines)
            {
                dbContext.DraftRevisionLines.Add(new DraftRevisionLineRow
                {
                    CompanyId = companyId,
                    DraftId = draft.Id,
                    Revision = archived.Revision,
                    LineNo = line.LineNo,
                    LineId = line.LineId,
                    ProductId = line.ProductId,
                    ProductVersion = line.ProductVersion,
                    OrderUnitId = line.OrderUnitId,
                    QuantityText = line.QuantityText,
                    UnitPrice = line.Commercial.UnitPrice,
                    PriceUnitId = line.Commercial.PriceUnitId,
                    DiscountPercent = line.Commercial.DiscountPercent,
                    PricingJson = SalesLinePricingJson.Serialize(line.Commercial),
                });
            }
        }

        row.Revision = draft.Revision;
        row.ApprovalState = (int)draft.ApprovalState;
        row.SubmittedBy = draft.Approval.SubmittedBy;
        row.SubmittedAtUtc = draft.Approval.SubmittedAtUtc;
        row.DecidedBy = draft.Approval.DecidedBy;
        row.DecidedAtUtc = draft.Approval.DecidedAtUtc;
        row.RejectionReason = draft.Approval.RejectionReason;
        row.Version = draft.Version;
        row.UpdatedAtUtc = draft.UpdatedAtUtc;
        foreach (var lineRow in lineRows)
        {
            var line = draft.Lines.Single(candidate => candidate.LineNo == lineRow.LineNo);
            if (quantityCommand)
            {
                // Assigned only when the value moved: a value-equal line keeps its stored text and stays unmodified.
                if (lineRow.Quantity != line.Quantity.Value || !string.Equals(lineRow.QuantityText, line.QuantityText, StringComparison.Ordinal))
                {
                    lineRow.Quantity = line.Quantity.Value;
                    lineRow.QuantityText = line.QuantityText;
                }
            }
            else
            {
                lineRow.UnitPrice = line.Commercial.UnitPrice;
                lineRow.PriceUnitId = line.Commercial.PriceUnitId;
                lineRow.DiscountPercent = line.Commercial.DiscountPercent;
                lineRow.PricingJson = SalesLinePricingJson.Serialize(line.Commercial);
            }
        }

        dbContext.DraftAttempts.Add(new DraftAttemptRow
        {
            CompanyId = companyId,
            CommandName = commandName,
            IdempotencyKeyHash = keyHash,
            RequestHash = requestHash,
            ActorId = actorId,
            Outcome = AttemptOutcomeRecorded,
            DraftId = draft.Id,
            CorrelationId = envelope.CorrelationId.Trim(),
            RecordedAtUtc = now,
        });
        auditEvents.Enqueue(new AuditEventDraft(
            companyId,
            audit.EventType,
            DraftEntityType,
            draft.Id.ToString("D"),
            audit.Operation,
            EntityVersionBefore: versionBefore,
            EntityVersionAfter: draft.Version,
            AuditActorTypes.User,
            envelope.ActorId,
            envelope.PermissionCode,
            envelope.RoleCodes,
            JsonSerializer.Serialize(before, Json),
            JsonSerializer.Serialize(picture(draft), Json),
            audit.Reason,
            ApprovalContextJson: null,
            AuditAiInvolvement.None,
            AiContextJson: null,
            envelope.SourceChannel,
            envelope.ClientIpAddress,
            envelope.UserAgent,
            envelope.CorrelationId,
            envelope.CausationId,
            envelope.IdempotencyKey));
        await SaveAsync(cancellationToken);
        return new SalesDraftStateResult(draft, SalesDraftWriteOutcome.Recorded, envelope.CorrelationId.Trim());
    }

    /// <summary>
    /// DEC-SAL-003: the delivery-schedule update, one save — the commercial revision's key rules and shape. The header is
    /// tracked (only its version and UpdatedAtUtc move; the guard admits nothing else); the domain decides and returns the
    /// recorded schedule, which is APPENDED (one schedule row + one date row per line and per existing slice) together with
    /// the attempt record and the audit event carrying the dates before and after — or nothing at all. No existing line or
    /// slice row is touched: the dates as created stay as schedule 0.
    /// </summary>
    public async Task<SalesDraftStateResult?> RescheduleDeliveryAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
        Guid draftId, Func<SalesOrderDraft, DateTimeOffset, DeliveryScheduleSnapshot> mutate, SalesDraftStateAudit audit,
        Func<CancellationToken, Task> beforeWrite, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(beforeWrite);
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        RequireCommand(commandName);
        if (commandName != SalesDraftCommands.RescheduleDeliveryV1)
        {
            throw new ArgumentException("Yalnız termin güncellemesi komutu taslağın termin tarihlerini değiştirir.", nameof(commandName));
        }

        if (string.IsNullOrWhiteSpace(audit.EventType) || string.IsNullOrWhiteSpace(audit.Operation) || string.IsNullOrWhiteSpace(audit.Reason))
        {
            throw new ArgumentException("Termin komutunun audit olayı tür, işlem ve gerekçe ister.", nameof(audit));
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
        await beforeWrite(cancellationToken);
        var before = Schedule(draft);
        var versionBefore = draft.Version;
        var revisionBefore = draft.Revision;
        var approvalBefore = (draft.ApprovalState, draft.Approval);
        var now = timeProvider.GetUtcNow();
        var recorded = mutate(draft, now);
        if (draft.Version != versionBefore + 1 || draft.Id != row.Id || draft.CompanyId != companyId
            || draft.Revision != revisionBefore || (draft.ApprovalState, draft.Approval) != approvalBefore
            || recorded is null || recorded.ScheduleNo != draft.DeliverySchedules.Count || recorded.ChangedBy is null
            || recorded.ChangedAtUtc is null)
        {
            throw new InvalidOperationException("Termin güncellemesi tam olarak bir sürüm ilerletir; revizyonu ve onay eksenini değiştirmez.");
        }

        dbContext.DraftDeliverySchedules.Add(new DraftDeliveryScheduleRow
        {
            CompanyId = companyId,
            DraftId = draft.Id,
            ScheduleNo = recorded.ScheduleNo,
            RequestedDeliveryDate = recorded.RequestedDeliveryDate,
            ChangedBy = recorded.ChangedBy,
            ChangedAtUtc = recorded.ChangedAtUtc.Value,
            DraftVersion = draft.Version,
            CorrelationId = envelope.CorrelationId.Trim(),
        });
        foreach (var line in recorded.Lines)
        {
            dbContext.DraftDeliveryScheduleDates.Add(new DraftDeliveryScheduleDateRow
            {
                CompanyId = companyId,
                DraftId = draft.Id,
                ScheduleNo = recorded.ScheduleNo,
                LineNo = line.LineNo,
                SliceNo = 0,
                SliceId = null,
                RequestedDeliveryDate = line.RequestedDeliveryDate,
            });
            foreach (var slice in line.Slices)
            {
                dbContext.DraftDeliveryScheduleDates.Add(new DraftDeliveryScheduleDateRow
                {
                    CompanyId = companyId,
                    DraftId = draft.Id,
                    ScheduleNo = recorded.ScheduleNo,
                    LineNo = line.LineNo,
                    SliceNo = slice.SliceNo,
                    SliceId = slice.SliceId,
                    RequestedDeliveryDate = slice.RequestedDeliveryDate,
                });
            }
        }

        row.Version = draft.Version;
        row.UpdatedAtUtc = draft.UpdatedAtUtc;
        dbContext.DraftAttempts.Add(new DraftAttemptRow
        {
            CompanyId = companyId,
            CommandName = commandName,
            IdempotencyKeyHash = keyHash,
            RequestHash = requestHash,
            ActorId = actorId,
            Outcome = AttemptOutcomeRecorded,
            DraftId = draft.Id,
            CorrelationId = envelope.CorrelationId.Trim(),
            RecordedAtUtc = now,
        });
        auditEvents.Enqueue(new AuditEventDraft(
            companyId,
            audit.EventType,
            DraftEntityType,
            draft.Id.ToString("D"),
            audit.Operation,
            EntityVersionBefore: versionBefore,
            EntityVersionAfter: draft.Version,
            AuditActorTypes.User,
            envelope.ActorId,
            envelope.PermissionCode,
            envelope.RoleCodes,
            JsonSerializer.Serialize(before, Json),
            JsonSerializer.Serialize(Schedule(draft), Json),
            audit.Reason,
            ApprovalContextJson: null,
            AuditAiInvolvement.None,
            AiContextJson: null,
            envelope.SourceChannel,
            envelope.ClientIpAddress,
            envelope.UserAgent,
            envelope.CorrelationId,
            envelope.CausationId,
            envelope.IdempotencyKey));
        await SaveAsync(cancellationToken);
        return new SalesDraftStateResult(draft, SalesDraftWriteOutcome.Recorded, envelope.CorrelationId.Trim());
    }

    /// <summary>
    /// DEC-SAL-005 / AT-007: the cancellation of an approved order, one save — the delivery schedule's key rules and shape. The
    /// header is tracked; <paramref name="beforeWrite"/> (the caller's fences and the impact read afresh from the owners) runs
    /// on the draft as read and answers the impact to keep; the domain decides (version, state, reason) and returns the facts.
    /// The cancellation row is APPENDED beside the header's lifecycle state and version, the attempt record and the audit event
    /// (the facts and the impact) — or nothing at all. No line, revision, schedule or approval column changes (checked).
    /// </summary>
    public async Task<SalesDraftStateResult?> CancelDraftAsync(SalesDraftWriteEnvelope envelope, string requestHash, string commandName,
        Guid draftId, Func<SalesOrderDraft, CancellationImpactSnapshot, DateTimeOffset, CancellationFacts> mutate, SalesDraftStateAudit audit,
        Func<SalesOrderDraft, CancellationToken, Task<CancellationImpactSnapshot>> beforeWrite, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(beforeWrite);
        RequireEnvelope(envelope);
        RequireHash(requestHash);
        RequireCommand(commandName);
        if (commandName != SalesDraftCommands.CancelV1)
        {
            throw new ArgumentException("Yalnız iptal komutu siparişin yaşam döngüsünü değiştirir.", nameof(commandName));
        }

        if (string.IsNullOrWhiteSpace(audit.EventType) || string.IsNullOrWhiteSpace(audit.Operation) || string.IsNullOrWhiteSpace(audit.Reason))
        {
            throw new ArgumentException("İptal komutunun audit olayı tür, işlem ve gerekçe ister.", nameof(audit));
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
        var impact = await beforeWrite(draft, cancellationToken)
            ?? throw new InvalidOperationException("İptal, kullanıcının gördüğü etki önizlemesi olmadan kaydedilmez.");
        var before = new { state = draft.State.ToString(), approvalState = draft.ApprovalState.ToString(), revision = draft.Revision, version = draft.Version };
        var versionBefore = draft.Version;
        var revisionBefore = draft.Revision;
        var approvalBefore = (draft.ApprovalState, draft.Approval);
        var now = timeProvider.GetUtcNow();
        var facts = mutate(draft, impact, now);
        if (draft.Version != versionBefore + 1 || draft.Id != row.Id || draft.CompanyId != companyId
            || draft.Revision != revisionBefore || (draft.ApprovalState, draft.Approval) != approvalBefore
            || draft.State != SalesDraftState.Cancelled || facts is null || draft.Cancellation != facts || facts.Impact != impact)
        {
            throw new InvalidOperationException("İptal tam olarak bir sürüm ilerletir; revizyonu, onay eksenini ve satırları değiştirmez.");
        }

        var impactJson = JsonSerializer.Serialize(new { token = facts.Impact.Token, sources = facts.Impact.Sources }, Json);
        dbContext.DraftCancellations.Add(new DraftCancellationRow
        {
            CompanyId = companyId,
            DraftId = draft.Id,
            Revision = facts.Revision,
            CancelledBy = facts.CancelledBy,
            CancelledAtUtc = facts.CancelledAtUtc,
            Reason = facts.Reason,
            DraftVersion = draft.Version,
            ImpactToken = facts.Impact.Token,
            ImpactJson = impactJson,
            CorrelationId = envelope.CorrelationId.Trim(),
        });
        row.State = (int)draft.State;
        row.Version = draft.Version;
        row.UpdatedAtUtc = draft.UpdatedAtUtc;
        dbContext.DraftAttempts.Add(new DraftAttemptRow
        {
            CompanyId = companyId,
            CommandName = commandName,
            IdempotencyKeyHash = keyHash,
            RequestHash = requestHash,
            ActorId = actorId,
            Outcome = AttemptOutcomeRecorded,
            DraftId = draft.Id,
            CorrelationId = envelope.CorrelationId.Trim(),
            RecordedAtUtc = now,
        });
        auditEvents.Enqueue(new AuditEventDraft(
            companyId,
            audit.EventType,
            DraftEntityType,
            draft.Id.ToString("D"),
            audit.Operation,
            EntityVersionBefore: versionBefore,
            EntityVersionAfter: draft.Version,
            AuditActorTypes.User,
            envelope.ActorId,
            envelope.PermissionCode,
            envelope.RoleCodes,
            JsonSerializer.Serialize(before, Json),
            JsonSerializer.Serialize(new
            {
                state = draft.State.ToString(),
                approvalState = draft.ApprovalState.ToString(),
                revision = draft.Revision,
                version = draft.Version,
                cancellation = new { facts.CancelledBy, facts.CancelledAtUtc, facts.Reason },
                impact = new { token = facts.Impact.Token, sources = facts.Impact.Sources },
            }, Json),
            audit.Reason,
            ApprovalContextJson: null,
            AuditAiInvolvement.None,
            AiContextJson: null,
            envelope.SourceChannel,
            envelope.ClientIpAddress,
            envelope.UserAgent,
            envelope.CorrelationId,
            envelope.CausationId,
            envelope.IdempotencyKey));
        await SaveAsync(cancellationToken);
        return new SalesDraftStateResult(draft, SalesDraftWriteOutcome.Recorded, envelope.CorrelationId.Trim());
    }

    /// <summary>The audit picture of a draft's delivery dates: the header's, every line's and every slice's (id, quantity, date).</summary>
    private static object Schedule(SalesOrderDraft draft) => new
    {
        schedule = draft.DeliverySchedules.Count,
        requestedDeliveryDate = draft.Header.RequestedDeliveryDate,
        lines = draft.Lines.Select(line => new
        {
            line.LineNo,
            line.RequestedDeliveryDate,
            slices = line.RequestedDeliverySlices.Select(slice => new { slice.SliceId, slice.QuantityText, slice.RequestedDeliveryDate }).ToArray(),
        }).ToArray(),
    };

    /// <summary>
    /// DEC-SAL-002: the audit picture of a draft's ordered quantities — revision, approval axis, every line's quantity text and,
    /// after an archive, the reason the last approved revision was superseded.
    /// </summary>
    private static object Quantities(SalesOrderDraft draft) => new
    {
        revision = draft.Revision,
        state = draft.ApprovalState.ToString(),
        approval = draft.Approval,
        supersededReason = draft.ArchivedRevisions.Count == 0 ? null : draft.ArchivedRevisions[^1].Reason,
        lines = draft.Lines.Select(line => new { line.LineNo, line.Id, quantity = line.QuantityText }).ToArray(),
    };

    /// <summary>The audit picture of a draft's commercial surface: revision, approval axis and every line's fields.</summary>
    private static object Commercial(SalesOrderDraft draft) => new
    {
        revision = draft.Revision,
        state = draft.ApprovalState.ToString(),
        approval = draft.Approval,
        lines = draft.Lines.Select(line => new
        {
            line.LineNo, line.Commercial.UnitPrice, line.Commercial.PriceUnitId, line.Commercial.DiscountPercent,
            // AT-018 / AT-021: the reference, the visible adjustments and the net in the audit trail.
            line.Commercial.Reference, line.Commercial.DiscountAmount, line.Commercial.SurchargePercent, line.Commercial.SurchargeAmount,
            netUnitPrice = SalesLinePriceCalculator.NetUnitPriceText(line.Commercial),
        }).ToArray(),
    };

    public async Task<SalesOrderDraft?> FindDraftAsync(Guid draftId, CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        var row = await dbContext.Drafts.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == draftId, cancellationToken);
        return row is null ? null : (await RestoreAsync([row], cancellationToken)).Single();
    }

    public async Task<SalesDraftPage> ListDraftsAsync(int pageSize, string? cursor, CancellationToken cancellationToken = default,
        InternalApprovalState? approvalState = null)
    {
        var companyId = RequireResolvedCompany();
        if (pageSize is < 1 or > MaximumPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), $"Sayfa boyutu 1–{MaximumPageSize} olmalıdır (varsayılan {DefaultPageSize}).");
        }

        var query = dbContext.Drafts.AsNoTracking();
        if (approvalState is { } state)
        {
            if (!Enum.IsDefined(state))
            {
                throw new ArgumentOutOfRangeException(nameof(approvalState));
            }

            var stateValue = (int)state;
            query = query.Where(row => row.ApprovalState == stateValue);
        }

        if (cursor is not null)
        {
            var (createdAtUtc, id) = ListCursor.Decode(cursor, companyId);
            query = query.Where(row => row.CreatedAtUtc < createdAtUtc || (row.CreatedAtUtc == createdAtUtc && row.Id.CompareTo(id) < 0));
        }

        var rows = await query
            .OrderByDescending(row => row.CreatedAtUtc).ThenByDescending(row => row.Id)
            .Take(pageSize + 1)
            .ToListAsync(cancellationToken);
        var hasMore = rows.Count > pageSize;
        var page = hasMore ? rows.Take(pageSize).ToList() : rows;
        var drafts = await RestoreAsync(page, cancellationToken);
        var last = page.LastOrDefault();
        return new SalesDraftPage(drafts, hasMore && last is not null ? ListCursor.Encode(companyId, last.CreatedAtUtc, last.Id) : null);
    }

    private void AddDraft(SalesOrderDraft draft, SalesDraftWriteEnvelope envelope, DateTimeOffset now)
    {
        // The row mapping lives in SalesDraftRows (one mapping for the store and the gated fixture store).
        SalesDraftRows.Add(dbContext, draft, envelope.ActorId, envelope.CorrelationId);
        _ = now;
    }

    /// <summary>Rebuilds stored drafts through the domain: every rule of a new draft holds again, and the stored version must be the one the rebuild reaches.</summary>
    private async Task<IReadOnlyList<SalesOrderDraft>> RestoreAsync(IReadOnlyList<DraftRow> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var ids = rows.Select(row => row.Id).ToArray();
        var lines = await dbContext.DraftLines.AsNoTracking().Where(line => ids.Contains(line.DraftId)).ToListAsync(cancellationToken);
        var slices = await dbContext.DraftDeliverySlices.AsNoTracking().Where(slice => ids.Contains(slice.DraftId)).ToListAsync(cancellationToken);
        var revisions = await dbContext.DraftRevisions.AsNoTracking().Where(revision => ids.Contains(revision.DraftId)).ToListAsync(cancellationToken);
        var revisionLines = await dbContext.DraftRevisionLines.AsNoTracking().Where(line => ids.Contains(line.DraftId)).ToListAsync(cancellationToken);
        var schedules = await dbContext.DraftDeliverySchedules.AsNoTracking().Where(schedule => ids.Contains(schedule.DraftId)).ToListAsync(cancellationToken);
        var scheduleDates = await dbContext.DraftDeliveryScheduleDates.AsNoTracking().Where(date => ids.Contains(date.DraftId)).ToListAsync(cancellationToken);
        var cancellations = await dbContext.DraftCancellations.AsNoTracking().Where(cancellation => ids.Contains(cancellation.DraftId)).ToListAsync(cancellationToken);
        var drafts = new List<SalesOrderDraft>(rows.Count);
        foreach (var row in rows)
        {
            var header = new SalesDraftHeader(row.PartyId, row.CompanyAccountId, row.CurrencyId, row.BusinessDate,
                row.RequestedDeliveryDate, row.Note,
                Reference(row.DeliveryAddressId, row.DeliveryAddressVersion), Reference(row.ContactId, row.ContactVersion),
                Reference(row.ResponsibleId, row.ResponsibleVersion), Reference(row.PaymentTermsId, row.PaymentTermsVersion),
                Reference(row.SourceStrategyId, row.SourceStrategyVersion), Reference(row.PackageId, row.PackageVersion));
            var money = new MoneyProfileSnapshot(row.CompanyId, row.CurrencyId, row.MoneyOperation, row.MoneyProfileId,
                row.MoneyProfileVersion, row.MoneyEffectiveOn, row.MoneyIsActive, row.MoneyEffectiveFrom, row.MoneyEffectiveThrough,
                row.MoneyScale);
            var draft = SalesOrderDraft.Create(row.Id, row.CompanyId, row.DocumentNo, header, money, row.CreatedAtUtc);
            foreach (var line in lines.Where(line => line.DraftId == row.Id).OrderBy(line => line.LineNo))
            {
                var lineSlices = slices.Where(slice => slice.DraftId == row.Id && slice.LineNo == line.LineNo)
                    .OrderBy(slice => slice.SliceNo)
                    .Select(slice => new DeliverySliceInput(slice.SliceId, slice.OrderUnitId, slice.QuantityText, slice.Quantity,
                        slice.RequestedDeliveryDate))
                    .ToArray();
                var restored = draft.AddLine(draft.Version, new SalesDraftLineInput(
                    line.LineId,
                    new ProductSnapshot(line.ProductId, line.ProductVersion, line.ProductCode, line.ProductName, line.ProductEffectiveOn,
                        new UnitBindingSnapshot(line.StockUnitCompanyId, line.StockUnitId, line.StockUnitProfileId, line.StockUnitProfileVersion),
                        new UnitBindingSnapshot(line.SalesUnitCompanyId, line.SalesUnitId, line.SalesUnitProfileId, line.SalesUnitProfileVersion),
                        new UnitBindingSnapshot(line.OrderUnitBindingCompanyId, line.OrderUnitBindingUnitId, line.OrderUnitBindingProfileId,
                            line.OrderUnitBindingProfileVersion)),
                    new UnitProfileSnapshot(line.OrderUnitCompanyId, line.OrderUnitId, line.OrderUnitCode, line.OrderUnitDimensionCode,
                        line.OrderUnitProfileId, line.OrderUnitProfileVersion, line.OrderUnitIsKilogram, line.OrderUnitProfileScale,
                        line.OrderUnitScale, line.OrderUnitIsActive, line.OrderUnitEffectiveFrom, line.OrderUnitEffectiveThrough),
                    line.QuantityText,
                    line.Quantity,
                    line.RequestedDeliveryDate,
                    line.Note,
                    SalesLinePricingJson.Restore(line.UnitPrice, line.PriceUnitId, line.DiscountPercent, line.PricingJson),
                    lineSlices), row.UpdatedAtUtc);
                if (restored.LineNo != line.LineNo)
                {
                    throw new InvalidOperationException("Kayıtlı satır numaraları taslağın satır sırasıyla uyuşmuyor.");
                }
            }

            // K3 Head B: the revision number and the archived approved revisions, oldest first; a history that does not fit is refused.
            // D2: the creator is restored so "one's OWN draft" is a fact of the aggregate, measured on the server.
            draft.RestoreCreator(row.CreatedBy);
            draft.RestoreRevision(row.Revision);
            foreach (var archived in revisions.Where(revision => revision.DraftId == row.Id).OrderBy(revision => revision.Revision))
            {
                draft.RestoreArchivedRevision(new ApprovedRevisionSnapshot(archived.Revision,
                    new ApprovalFacts(archived.SubmittedBy, archived.SubmittedAtUtc, archived.ApprovedBy, archived.ApprovedAtUtc, null),
                    archived.MoneyProfileId, archived.MoneyProfileVersion, archived.MoneyScale, archived.ArchivedAtUtc, archived.ArchivedBy,
                    [.. revisionLines.Where(line => line.DraftId == row.Id && line.Revision == archived.Revision).OrderBy(line => line.LineNo)
                        .Select(line => new RevisionLineSnapshot(line.LineId, line.LineNo, line.ProductId, line.ProductVersion, line.OrderUnitId,
                            line.QuantityText, SalesLinePricingJson.Restore(line.UnitPrice, line.PriceUnitId, line.DiscountPercent, line.PricingJson)))],
                    archived.Reason));
            }

            // DEC-SAL-003: every recorded delivery schedule, oldest first, BEFORE the approval axis (its version floor counts
            // them). The last one's dates become the draft's current dates; a history that does not fit is refused.
            foreach (var schedule in schedules.Where(schedule => schedule.DraftId == row.Id).OrderBy(schedule => schedule.ScheduleNo))
            {
                var dates = scheduleDates.Where(date => date.DraftId == row.Id && date.ScheduleNo == schedule.ScheduleNo).ToArray();
                draft.RestoreDeliverySchedule(new DeliveryScheduleSnapshot(schedule.ScheduleNo, schedule.RequestedDeliveryDate,
                    schedule.ChangedBy, schedule.ChangedAtUtc,
                    [.. dates.Where(date => date.SliceNo == 0).OrderBy(date => date.LineNo).Select(date => new LineScheduleSnapshot(
                        draft.Lines.SingleOrDefault(line => line.LineNo == date.LineNo)?.Id
                            ?? throw new InvalidOperationException("Kayıtlı termin satırı taslakta yok."),
                        date.LineNo,
                        date.RequestedDeliveryDate,
                        [.. dates.Where(slice => slice.LineNo == date.LineNo && slice.SliceNo >= 1).OrderBy(slice => slice.SliceNo)
                            .Select(slice => new SliceScheduleSnapshot(
                                slice.SliceId ?? throw new InvalidOperationException("Kayıtlı termin diliminin kimliği yok."),
                                slice.SliceNo,
                                draft.Lines.Single(line => line.LineNo == date.LineNo).RequestedDeliverySlices
                                    .ElementAtOrDefault(slice.SliceNo - 1)?.QuantityText
                                    ?? throw new InvalidOperationException("Kayıtlı termin dilimi satırda yok."),
                                slice.RequestedDeliveryDate ?? throw new InvalidOperationException("Kayıtlı termin diliminin tarihi yok.")))]))]));
            }

            // DEC-SAL-005: the recorded cancellation, BEFORE the approval axis (its version floor counts it as one step). The
            // header's lifecycle state and the appended row must agree: a cancelled header without its row, or a row beside a
            // Draft header, is refused, not repaired.
            var cancellation = cancellations.SingleOrDefault(candidate => candidate.DraftId == row.Id);
            if ((row.State == (int)SalesDraftState.Cancelled) != (cancellation is not null)
                || row.State is not ((int)SalesDraftState.Draft or (int)SalesDraftState.Cancelled))
            {
                throw new InvalidOperationException("Kayıtlı yaşam döngüsü durumu ile iptal kaydı uyuşmuyor.");
            }

            if (cancellation is not null)
            {
                draft.RestoreCancellation(new CancellationFacts(cancellation.Revision, cancellation.CancelledBy, cancellation.CancelledAtUtc,
                    cancellation.Reason, CancellationImpact(cancellation)));
            }

            // SALES-A1: the stored approval axis, restored without a transition; the facts must fit the state.
            draft.RestoreApproval((InternalApprovalState)row.ApprovalState,
                new ApprovalFacts(row.SubmittedBy, row.SubmittedAtUtc, row.DecidedBy, row.DecidedAtUtc, row.RejectionReason),
                row.Version, row.UpdatedAtUtc);
            if (draft.Version != row.Version)
            {
                throw new InvalidOperationException("Kayıtlı taslağın sürümü satırlarından yeniden kurulan sürümle uyuşmuyor.");
            }

            drafts.Add(draft);
        }

        return drafts;
    }

    /// <summary>The impact stored with a cancellation: its token (the row's column) and the sources of its JSON; a mismatch is refused.</summary>
    private static CancellationImpactSnapshot CancellationImpact(DraftCancellationRow row)
    {
        var stored = JsonSerializer.Deserialize<StoredImpact>(row.ImpactJson, Json);
        if (stored is null || stored.Sources is null || !string.Equals(stored.Token, row.ImpactToken, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Kayıtlı iptal etkisi okunamadı.");
        }

        return new CancellationImpactSnapshot(row.ImpactToken, [.. stored.Sources]);
    }

    private sealed record StoredImpact(string Token, IReadOnlyList<CancellationImpactEntry> Sources);

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            // Nothing of the failed write stays tracked: the caller re-reads the key afresh in this scope (ADR-011 §8).
            dbContext.ChangeTracker.Clear();
            throw Refused(SalesDraftStoreCodes.ConcurrentChange,
                "Şirketin numara sayacı ya da taslağı başka bir yazmayla değişti; hiçbir şey kaydedilmedi.", exception);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // Only unique-key violations are recognised here (the Idempotency / Inventory / Accounting pattern):
            // the attempt key, the document number, the draft id or a line/slice id lost a race. The loser's
            // rows leave the tracker so that the same scope reads the winner's result (FindReplayAsync) afresh.
            dbContext.ChangeTracker.Clear();
            throw Refused(SalesDraftStoreCodes.ConcurrentChange,
                "Kalıcı tekillik kuralı başka bir yazmanın lehine sonuçlandı; hiçbir şey kaydedilmedi.", exception);
        }
    }

    private void Audit(SalesDraftWriteEnvelope envelope, Guid companyId, SalesOrderDraft draft, DateTimeOffset now)
    {
        auditEvents.Enqueue(new AuditEventDraft(
            companyId,
            "sales.order-draft.created.v1",
            DraftEntityType,
            draft.Id.ToString("D"),
            "Create",
            EntityVersionBefore: null,
            EntityVersionAfter: draft.Version,
            AuditActorTypes.User,
            envelope.ActorId,
            envelope.PermissionCode,
            envelope.RoleCodes,
            "{}",
            DraftAudit(draft),
            "Satış sipariş taslağı oluşturuldu",
            ApprovalContextJson: null,
            AuditAiInvolvement.None,
            AiContextJson: null,
            envelope.SourceChannel,
            envelope.ClientIpAddress,
            envelope.UserAgent,
            envelope.CorrelationId,
            envelope.CausationId,
            envelope.IdempotencyKey));
        _ = now;
    }

    private Guid RequireResolvedCompany()
    {
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
        {
            throw Refused(SalesDraftStoreCodes.CompanyContextMismatch, "Aktif şirket bağlamı olmadan satış taslağı işlenmez.");
        }

        return companyContext.CompanyId;
    }

    private void RequireCompany(Guid companyId)
    {
        if (RequireResolvedCompany() != companyId)
        {
            throw Refused(SalesDraftStoreCodes.CompanyContextMismatch, "Taslak aktif şirket bağlamının şirketine ait değil.");
        }
    }

    private static void RequireEnvelope(SalesDraftWriteEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (string.IsNullOrWhiteSpace(envelope.ActorId) || string.IsNullOrWhiteSpace(envelope.PermissionCode)
            || string.IsNullOrWhiteSpace(envelope.CorrelationId) || string.IsNullOrWhiteSpace(envelope.SourceChannel)
            || envelope.RoleCodes is null)
        {
            throw new ArgumentException("Taslak yazması aktör, izin, correlation, idempotency ve kanal bilgisi ister.", nameof(envelope));
        }

        // The key is hashed (ADR-011), but only canonical text is accepted: the hash the caller reasons
        // about must be the hash stored, and "k-1 " must never silently become "k-1".
        if (string.IsNullOrWhiteSpace(envelope.IdempotencyKey)
            || !string.Equals(envelope.IdempotencyKey, envelope.IdempotencyKey.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("Idempotency-Key boş olamaz ve çevresinde boşluk taşıyamaz.", nameof(envelope));
        }

        if (envelope.IdempotencyKey.Length > KeyMaximumLength || envelope.ActorId.Trim().Length > ActorMaximumLength
            || envelope.CorrelationId.Trim().Length > ActorMaximumLength)
        {
            throw Refused(SalesDraftStoreCodes.ValueDoesNotFit, "Idempotency-Key, aktör ya da correlation değeri kayıt alanına sığmıyor.");
        }
    }

    private static void RequireCommand(string commandName)
    {
        if (commandName is not (SalesDraftCommands.CreateV1 or SalesDraftCommands.SubmitV1 or SalesDraftCommands.ApproveV1
            or SalesDraftCommands.RejectV1 or SalesDraftCommands.ReviseCommercialV1 or SalesDraftCommands.WithdrawV1
            or SalesDraftCommands.ApprovePriceFloorExceptionsV1 or SalesDraftCommands.RescheduleDeliveryV1
            or SalesDraftCommands.CancelV1 or SalesDraftCommands.ReviseQuantityV1))
        {
            throw new ArgumentException("Bilinmeyen Sales komut adı.", nameof(commandName));
        }
    }

    private static void RequireHash(string requestHash)
    {
        if (string.IsNullOrWhiteSpace(requestHash) || requestHash.Length != 64
            || requestHash.Any(character => !char.IsAsciiHexDigitLower(character)))
        {
            throw new ArgumentException("İstek hash'i 64 karakterlik küçük harfli sha256 olmalıdır.", nameof(requestHash));
        }
    }

    /// <summary>ADR-011 §2: the uppercase SHA-256 hex of the trimmed key; the raw key is never stored.</summary>
    internal static string IdempotencyKeyHash(string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key.Trim())));

    /// <summary>Technical, company-scoped display text; the counter, not the text, is the identity. Not an official document number (Ö8).</summary>
    internal static string DocumentNo(DateTimeOffset now, long number) =>
        string.Create(CultureInfo.InvariantCulture, $"SO-{now.UtcDateTime.Year}-{number:D6}");

    private static OwnerReference? Reference(Guid? id, string? version) =>
        id is { } value && version is not null ? new OwnerReference(value, version) : null;

    private static string DraftAudit(SalesOrderDraft draft) => JsonSerializer.Serialize(new
    {
        draft.Id,
        draft.DocumentNo,
        kind = draft.Kind.ToString(),
        state = draft.State.ToString(),
        approvalState = draft.ApprovalState.ToString(),
        revision = draft.Revision,
        draft.Header.PartyId,
        draft.Header.CompanyAccountId,
        draft.Header.CurrencyId,
        draft.Header.BusinessDate,
        money = new { draft.Money.Operation, draft.Money.ProfileId, draft.Money.ProfileVersion },
        lines = draft.Lines.Select(line => new
        {
            line.LineNo,
            line.Id,
            line.Product.ProductId,
            line.Product.ProductVersion,
            line.OrderUnit.UnitId,
            quantity = line.QuantityText,
            slices = line.RequestedDeliverySlices.Count,
        }),
    }, Json);

    private static SalesDraftStoreException Refused(string code, string message, Exception? inner = null) =>
        new SalesDraftStoreException(code, message, inner);

    /// <summary>
    /// The opaque list cursor (#257): the last row's ordering key, bound to the company that read it.
    /// Malformed, another company's or another ordering's cursor is <see cref="SalesDraftStoreCodes.InvalidCursor"/>.
    /// </summary>
    internal static class ListCursor
    {
        private const string Ordering = "createdAtUtc desc, id desc";

        public static string Encode(Guid companyId, DateTimeOffset createdAtUtc, Guid id) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                new Payload(companyId, Ordering, createdAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture), id), Json)))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        public static (DateTimeOffset CreatedAtUtc, Guid Id) Decode(string cursor, Guid companyId)
        {
            Payload? payload;
            try
            {
                var padded = cursor.Replace('-', '+').Replace('_', '/');
                padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
                payload = JsonSerializer.Deserialize<Payload>(Encoding.UTF8.GetString(Convert.FromBase64String(padded)), Json);
            }
            catch (Exception exception) when (exception is FormatException or JsonException)
            {
                throw Refused(SalesDraftStoreCodes.InvalidCursor, "Liste imleci çözülemedi.", exception);
            }

            if (payload is null || payload.Company != companyId || !string.Equals(payload.Ordering, Ordering, StringComparison.Ordinal)
                || payload.Id == Guid.Empty
                || !DateTimeOffset.TryParseExact(payload.CreatedAtUtc, "O", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var createdAtUtc))
            {
                throw Refused(SalesDraftStoreCodes.InvalidCursor, "Liste imleci bu şirketin ve bu sıralamanın imleci değil.");
            }

            return (createdAtUtc, payload.Id);
        }

        private sealed record Payload(Guid Company, string Ordering, string CreatedAtUtc, Guid Id);
    }
}
