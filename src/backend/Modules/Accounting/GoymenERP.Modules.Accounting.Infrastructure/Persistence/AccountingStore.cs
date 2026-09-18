using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain;
using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Accounting.Domain.Periods;
using GoymenERP.Modules.Accounting.Domain.Posting;
using GoymenERP.Modules.Audit.Application.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

/// <summary>
/// The Accounting store of the server-resolved company (ACC-3). Every write is one save: the
/// journal and its lines, the queue entries, the delivery record, the period version and the audit
/// events commit together or not at all.
/// <list type="bullet">
/// <item><b>Company:</b> a request of any other company than the resolved context is refused before
/// anything is read; every read runs under the tenant filter.</item>
/// <item><b>Domain decides:</b> each call rebuilds the part of the ledger it needs from stored facts
/// (<see cref="SourcePostingLedger.FromStored"/>) and asks the domain; only what the domain decided
/// is stored. The outcomes are the domain's.</item>
/// <item><b>Inbox (R4):</b> (company, consumer, message id) is a primary key. A stored delivery is
/// answered from what it recorded, before the ledger is asked.</item>
/// <item><b>Uniqueness under a race:</b> one original per fact, one reversal per journal, one
/// journal per correction id and one delivery per message are unique indexes. A racing writer that
/// loses gets <see cref="AccountingStoreCodes.ConcurrentChange"/>, nothing of it is stored, and the
/// same request retried answers from the winner.</item>
/// <item><b>Period guard (Ö5):</b> every posting bumps its period's version and a close bumps it too,
/// so a close racing a posting cannot both commit.</item>
/// <item><b>Audit:</b> ADR-009 envelope from <see cref="AccountingCommandMetadata"/>, in the same save.
/// A delivery that only answers from stored facts (already posted, already reversed) stores its
/// delivery record and no audit event.</item>
/// </list>
/// </summary>
public sealed class AccountingStore(
    AccountingDbContext dbContext,
    ICompanyContext companyContext,
    IAuditEventCollector auditEvents,
    TimeProvider timeProvider) : IAccountingPeriodStore, IAccountingPostingStore
{
    private const int Open = 1;
    private const int Resolved = 2;
    private const int IdentityMaximumLength = 128;
    private const int MessageIdMaximumLength = 200;
    private const int AmountScaleMaximum = 18;
    private const string PeriodEntityType = "Accounting.Period";
    private const string JournalEntityType = "Accounting.Journal";
    private const string ExceptionEntityType = "Accounting.PostingException";
    private const string PendingEntityType = "Accounting.PendingValuation";

    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    public async Task<AccountingWriteOutcome> OpenPeriodAsync(Guid periodId, DateOnly startsOn, DateOnly endsOn,
        AccountingCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        RequireMetadata(metadata);
        var companyId = RequireResolvedCompany();
        var period = new AccountingPeriod(periodId, companyId, startsOn, endsOn);

        var existing = await dbContext.Periods.AsNoTracking().SingleOrDefaultAsync(row => row.Id == periodId, cancellationToken);
        if (existing is not null)
        {
            return existing.StartsOn == startsOn && existing.EndsOn == endsOn
                ? AccountingWriteOutcome.Replayed
                : throw Refused(AccountingStoreCodes.PeriodConflict, "Aynı dönem kimliği başka tarihlerle verildi.");
        }

        if (await dbContext.Periods.AsNoTracking().AnyAsync(row => row.StartsOn <= endsOn && row.EndsOn >= startsOn,
                cancellationToken))
        {
            throw Refused(AccountingStoreCodes.PeriodOverlap, "Yeni dönem şirketin kayıtlı bir dönemiyle çakışıyor.");
        }

        var now = timeProvider.GetUtcNow();
        var calendar = await dbContext.PeriodCalendars.SingleOrDefaultAsync(cancellationToken);
        if (calendar is null)
        {
            dbContext.PeriodCalendars.Add(new PeriodCalendarRow { CompanyId = companyId, Version = 1, UpdatedAtUtc = now });
        }
        else
        {
            calendar.Version = checked(calendar.Version + 1);
            calendar.UpdatedAtUtc = now;
        }

        dbContext.Periods.Add(new PeriodRow
        {
            Id = period.Id,
            CompanyId = companyId,
            StartsOn = period.StartsOn,
            EndsOn = period.EndsOn,
            Status = (int)AccountingPeriodStatus.Open,
            Version = 1,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });
        Audit(metadata, companyId, PeriodEntityType, period.Id.ToString("D"), "accounting.period.opened.v1", "Open",
            versionBefore: null, versionAfter: 1, before: "{}", after: PeriodAudit(period), "Muhasebe dönemi açıldı");
        await SaveAsync(cancellationToken);
        return AccountingWriteOutcome.Recorded;
    }

    public async Task ClosePeriodAsync(Guid periodId, Guid closedByUserId, AccountingCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        RequireMetadata(metadata);
        var companyId = RequireResolvedCompany();
        var row = await dbContext.Periods.SingleOrDefaultAsync(candidate => candidate.Id == periodId, cancellationToken)
            ?? throw Refused(AccountingStoreCodes.PeriodNotFound, "Bu şirketin böyle bir dönemi yok.");
        var period = RestorePeriod(row);
        var before = PeriodAudit(period);
        period.Close(closedByUserId, timeProvider.GetUtcNow());

        var versionBefore = row.Version;
        row.Status = (int)AccountingPeriodStatus.Closed;
        row.ClosedByUserId = period.ClosedByUserId;
        row.ClosedAtUtc = period.ClosedAtUtc;
        row.Version = checked(versionBefore + 1);
        row.UpdatedAtUtc = period.ClosedAtUtc!.Value;
        Audit(metadata, companyId, PeriodEntityType, period.Id.ToString("D"), "accounting.period.closed.v1", "Close",
            versionBefore, row.Version, before, PeriodAudit(period), "Muhasebe dönemi kapandı");
        await SaveAsync(cancellationToken);
    }

    public async Task<AccountingPeriod?> FindPeriodAsync(Guid periodId, CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        var row = await dbContext.Periods.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == periodId, cancellationToken);
        return row is null ? null : RestorePeriod(row);
    }

    public async Task<PostingOutcome> ReceiveAsync(PostingRequest request, Guid periodId, AccountingCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireMetadata(metadata);
        RequireCompany(request.Source.CompanyId);
        RequireDelivery(request.Consumer, request.MessageId);
        RequireFits(request.Source.PostingPurpose, request.PostingRuleVersion,
            request.Lines.Where(line => line is not null && line.Amount is not null).Select(line => line.Amount!.Value));

        var replay = await ReplayAsync(request.Consumer, request.MessageId, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        var (periodRow, period) = await LoadPeriodAsync(periodId, cancellationToken);
        var sourceId = request.Source.SourceId;
        var purpose = request.Source.PostingPurpose;
        var originalRows = await dbContext.Journals.AsNoTracking()
            .Where(row => row.Kind == (int)JournalKind.Original && row.SourceId == sourceId && row.PostingPurpose == purpose)
            .ToListAsync(cancellationToken);
        var exceptionRow = await dbContext.PostingExceptions
            .SingleOrDefaultAsync(row => row.SourceId == sourceId && row.PostingPurpose == purpose, cancellationToken);
        var pendingRow = await dbContext.PendingValuations
            .SingleOrDefaultAsync(row => row.SourceId == sourceId && row.PostingPurpose == purpose, cancellationToken);

        var ledger = SourcePostingLedger.FromStored(
            request.Source.CompanyId,
            await RestoreJournalsAsync(originalRows, cancellationToken),
            [],
            exceptionRow is { Status: Open } ? [ToException(exceptionRow)] : [],
            pendingRow is { Status: Open } ? [ToPending(pendingRow)] : []);

        // The domain decides; nothing below runs for a refused request.
        var outcome = ledger.Receive(request, period);

        if (outcome.Kind == PostingOutcomeKind.Posted)
        {
            await AddJournalAsync(outcome.Journal!, periodRow, correctionId: null, metadata, cancellationToken);
        }

        SyncException(exceptionRow, ledger.FindOpenException(sourceId, purpose), request.Source, metadata);
        SyncPending(pendingRow, ledger.FindPendingValuation(sourceId, purpose), request.Source, metadata);
        AddDelivery(request.Consumer, request.MessageId, outcome, metadata);
        await SaveAsync(cancellationToken);
        return outcome;
    }

    public async Task<PostingOutcome> ReverseAsync(string consumer, string messageId, Guid reversedJournalId,
        Guid reversalJournalId, PostingDates dates, Guid periodId, AccountingCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dates);
        RequireMetadata(metadata);
        var companyId = RequireResolvedCompany();
        RequireDelivery(consumer, messageId);

        var replay = await ReplayAsync(consumer, messageId, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        var (periodRow, period) = await LoadPeriodAsync(periodId, cancellationToken);
        var rows = await dbContext.Journals.AsNoTracking()
            .Where(row => row.Id == reversedJournalId
                || (row.Kind == (int)JournalKind.Reversal && row.LinkedJournalId == reversedJournalId))
            .ToListAsync(cancellationToken);
        var ledger = SourcePostingLedger.FromStored(companyId, await RestoreJournalsAsync(rows, cancellationToken), [], [], []);

        var outcome = ledger.Reverse(consumer, messageId, reversedJournalId, reversalJournalId, dates, period);

        if (outcome.Kind == PostingOutcomeKind.Posted)
        {
            await AddJournalAsync(outcome.Journal!, periodRow, correctionId: null, metadata, cancellationToken);
        }

        AddDelivery(consumer, messageId, outcome, metadata);
        await SaveAsync(cancellationToken);
        return outcome;
    }

    public async Task<PostingOutcome> CorrectAsync(string consumer, string messageId, Guid correctionId,
        Guid correctedJournalId, Guid correctionJournalId, string postingRuleVersion, PostingDates dates,
        IEnumerable<JournalLine> lines, Guid periodId, AccountingCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dates);
        ArgumentNullException.ThrowIfNull(lines);
        RequireMetadata(metadata);
        var companyId = RequireResolvedCompany();
        RequireDelivery(consumer, messageId);
        var correctionLines = lines.ToArray();
        RequireFits(purpose: null, postingRuleVersion,
            correctionLines.Where(line => line is not null).SelectMany(line => new[] { line.Debit, line.Credit }));

        var replay = await ReplayAsync(consumer, messageId, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        var (periodRow, period) = await LoadPeriodAsync(periodId, cancellationToken);
        var rows = await dbContext.Journals.AsNoTracking()
            .Where(row => row.Id == correctedJournalId || row.CorrectionId == correctionId)
            .ToListAsync(cancellationToken);
        var corrections = rows
            .Where(row => row.CorrectionId == correctionId && row.Kind == (int)JournalKind.Correction)
            .Select(row => new StoredCorrection(correctionId, row.Id))
            .ToArray();
        var ledger = SourcePostingLedger.FromStored(companyId, await RestoreJournalsAsync(rows, cancellationToken),
            corrections, [], []);

        var outcome = ledger.Correct(consumer, messageId, correctionId, correctedJournalId, correctionJournalId,
            postingRuleVersion, dates, correctionLines, period);

        if (outcome.Kind == PostingOutcomeKind.Posted)
        {
            await AddJournalAsync(outcome.Journal!, periodRow, correctionId, metadata, cancellationToken);
        }

        AddDelivery(consumer, messageId, outcome, metadata);
        await SaveAsync(cancellationToken);
        return outcome;
    }

    public async Task<Journal?> FindJournalAsync(Guid journalId, CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        var rows = await dbContext.Journals.AsNoTracking().Where(row => row.Id == journalId).ToListAsync(cancellationToken);
        return rows.Count == 0 ? null : (await RestoreJournalsAsync(rows, cancellationToken)).Single();
    }

    public async Task<IReadOnlyList<PostingException>> ReadOpenExceptionsAsync(CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        var rows = await dbContext.PostingExceptions.AsNoTracking().Where(row => row.Status == Open)
            .OrderBy(row => row.SourceId).ThenBy(row => row.PostingPurpose)
            .ToListAsync(cancellationToken);
        return [.. rows.Select(ToException)];
    }

    public async Task<IReadOnlyList<PendingValuation>> ReadPendingValuationsAsync(CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        var rows = await dbContext.PendingValuations.AsNoTracking().Where(row => row.Status == Open)
            .OrderBy(row => row.SourceId).ThenBy(row => row.PostingPurpose)
            .ToListAsync(cancellationToken);
        return [.. rows.Select(ToPending)];
    }

    private async Task<PostingOutcome?> ReplayAsync(string consumer, string messageId, CancellationToken cancellationToken)
    {
        var delivery = await dbContext.Deliveries.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Consumer == consumer && row.MessageId == messageId, cancellationToken);
        if (delivery is null)
        {
            return null;
        }

        Journal? journal = null;
        if (delivery.JournalId is { } journalId)
        {
            var rows = await dbContext.Journals.AsNoTracking().Where(row => row.Id == journalId).ToListAsync(cancellationToken);
            journal = (await RestoreJournalsAsync(rows, cancellationToken)).Single();
        }

        var entry = delivery.QueueEntryJson is null ? null : JsonSerializer.Deserialize<QueueEntrySnapshot>(delivery.QueueEntryJson, Json)
            ?? throw new InvalidOperationException("Kayıtlı teslimatın kuyruk kaydı okunamadı.");
        return new PostingOutcome(
            PostingOutcomeKind.DuplicateMessage,
            Journal: journal,
            Exception: entry is { MissingMappingKeys: not null } ? entry.ToException(delivery.CompanyId) : null,
            Pending: entry is { LinesAwaitingAmount: not null } ? entry.ToPending(delivery.CompanyId) : null);
    }

    private async Task<(PeriodRow Row, AccountingPeriod Period)> LoadPeriodAsync(Guid periodId, CancellationToken cancellationToken)
    {
        var row = await dbContext.Periods.SingleOrDefaultAsync(candidate => candidate.Id == periodId, cancellationToken)
            ?? throw Refused(AccountingStoreCodes.PeriodNotFound, "Bu şirketin böyle bir dönemi yok.");
        return (row, RestorePeriod(row));
    }

    private async Task<IReadOnlyList<Journal>> RestoreJournalsAsync(IReadOnlyList<JournalRow> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var ids = rows.Select(row => row.Id).Distinct().ToArray();
        var lines = await dbContext.JournalLines.AsNoTracking().Where(line => ids.Contains(line.JournalId))
            .ToListAsync(cancellationToken);
        return [.. rows.DistinctBy(row => row.Id).Select(row => Journal.Restore(
            row.Id,
            (JournalKind)row.Kind,
            new SourceReference(row.CompanyId, row.SourceId, row.PostingPurpose),
            row.PostingRuleVersion,
            row.CurrencyId,
            new PostingDates(row.TransactionDate, row.AccountingDate, row.ValueDate, row.RecordedAtUtc),
            lines.Where(line => line.JournalId == row.Id).OrderBy(line => line.LineNumber)
                .Select(line => new JournalLine(new AccountRef(line.CompanyId, line.AccountId), line.Debit, line.Credit)),
            row.LinkedJournalId))];
    }

    private async Task AddJournalAsync(Journal journal, PeriodRow periodRow, Guid? correctionId,
        AccountingCommandMetadata metadata, CancellationToken cancellationToken)
    {
        if (await dbContext.Journals.AsNoTracking().AnyAsync(row => row.Id == journal.Id, cancellationToken))
        {
            throw Refused(AccountingStoreCodes.JournalIdTaken, "Bu fiş kimliği şirketin başka bir fişinde kullanılıyor.");
        }

        var now = timeProvider.GetUtcNow();
        dbContext.Journals.Add(new JournalRow
        {
            Id = journal.Id,
            CompanyId = journal.CompanyId,
            Kind = (int)journal.Kind,
            SourceId = journal.Source.SourceId,
            PostingPurpose = journal.Source.PostingPurpose,
            PostingRuleVersion = journal.PostingRuleVersion,
            CurrencyId = journal.CurrencyId,
            TransactionDate = journal.Dates.TransactionDate,
            AccountingDate = journal.Dates.AccountingDate,
            ValueDate = journal.Dates.ValueDate,
            RecordedAtUtc = journal.Dates.RecordedAtUtc,
            LinkedJournalId = journal.LinkedJournalId,
            CorrectionId = correctionId,
            PeriodId = periodRow.Id,
            StoredAtUtc = now,
            StoredBy = metadata.ActorId.Trim(),
            CorrelationId = metadata.CorrelationId.Trim(),
        });
        var number = 0;
        foreach (var line in journal.Lines)
        {
            dbContext.JournalLines.Add(new JournalLineRow
            {
                CompanyId = journal.CompanyId,
                JournalId = journal.Id,
                LineNumber = ++number,
                AccountId = line.Account.AccountId,
                Debit = line.Debit,
                Credit = line.Credit,
            });
        }

        // The period is the posting's concurrency token: a close racing this posting cannot also commit.
        periodRow.Version = checked(periodRow.Version + 1);
        periodRow.UpdatedAtUtc = now;
        Audit(metadata, journal.CompanyId, JournalEntityType, journal.Id.ToString("D"), "accounting.journal.posted.v1",
            "Post", versionBefore: null, versionAfter: 1, before: "{}", after: JournalAudit(journal, correctionId),
            $"Muhasebe fişi ({journal.Kind})");
    }

    private void SyncException(PostingExceptionRow? row, PostingException? entry, SourceReference source,
        AccountingCommandMetadata metadata)
    {
        var now = timeProvider.GetUtcNow();
        if (entry is null)
        {
            if (row is { Status: Open })
            {
                var before = row.Version;
                row.Status = Resolved;
                row.Version = checked(before + 1);
                row.UpdatedAtUtc = now;
                Audit(metadata, source.CompanyId, ExceptionEntityType, source.SourceId.ToString("D"),
                    "accounting.posting-exception.resolved.v1", "Resolve", before, row.Version, ExceptionAudit(row),
                    ExceptionAudit(row), "Eşleme istisnası kapandı");
            }

            return;
        }

        var keys = JsonSerializer.Serialize(entry.MissingMappingKeys, Json);
        if (row is null)
        {
            var added = new PostingExceptionRow
            {
                CompanyId = source.CompanyId,
                SourceId = source.SourceId,
                PostingPurpose = source.PostingPurpose,
                Status = Open,
                PostingRuleVersion = entry.PostingRuleVersion,
                MissingMappingKeysJson = keys,
                FirstMessageId = entry.FirstMessageId,
                Version = 1,
                UpdatedAtUtc = now,
            };
            dbContext.PostingExceptions.Add(added);
            Audit(metadata, source.CompanyId, ExceptionEntityType, source.SourceId.ToString("D"),
                "accounting.posting-exception.queued.v1", "Queue", versionBefore: null, versionAfter: 1, before: "{}",
                ExceptionAudit(added), "Eşleme istisnası kuyruğa alındı");
            return;
        }

        if (row.Status == Open && row.PostingRuleVersion == entry.PostingRuleVersion
            && row.MissingMappingKeysJson == keys && row.FirstMessageId == entry.FirstMessageId)
        {
            return;
        }

        var previous = ExceptionAudit(row);
        var versionBefore = row.Version;
        row.Status = Open;
        row.PostingRuleVersion = entry.PostingRuleVersion;
        row.MissingMappingKeysJson = keys;
        row.FirstMessageId = entry.FirstMessageId;
        row.Version = checked(versionBefore + 1);
        row.UpdatedAtUtc = now;
        Audit(metadata, source.CompanyId, ExceptionEntityType, source.SourceId.ToString("D"),
            "accounting.posting-exception.updated.v1", "Update", versionBefore, row.Version, previous, ExceptionAudit(row),
            "Eşleme istisnası güncellendi");
    }

    private void SyncPending(PendingValuationRow? row, PendingValuation? entry, SourceReference source,
        AccountingCommandMetadata metadata)
    {
        var now = timeProvider.GetUtcNow();
        if (entry is null)
        {
            if (row is { Status: Open })
            {
                var before = row.Version;
                row.Status = Resolved;
                row.Version = checked(before + 1);
                row.UpdatedAtUtc = now;
                Audit(metadata, source.CompanyId, PendingEntityType, source.SourceId.ToString("D"),
                    "accounting.pending-valuation.resolved.v1", "Resolve", before, row.Version, PendingAudit(row),
                    PendingAudit(row), "Değerleme bekleyen kayıt kapandı");
            }

            return;
        }

        if (row is null)
        {
            var added = new PendingValuationRow
            {
                CompanyId = source.CompanyId,
                SourceId = source.SourceId,
                PostingPurpose = source.PostingPurpose,
                Status = Open,
                PostingRuleVersion = entry.PostingRuleVersion,
                LinesAwaitingAmount = entry.LinesAwaitingAmount,
                FirstMessageId = entry.FirstMessageId,
                Version = 1,
                UpdatedAtUtc = now,
            };
            dbContext.PendingValuations.Add(added);
            Audit(metadata, source.CompanyId, PendingEntityType, source.SourceId.ToString("D"),
                "accounting.pending-valuation.queued.v1", "Queue", versionBefore: null, versionAfter: 1, before: "{}",
                PendingAudit(added), "Değerleme bekleyen kayıt açıldı");
            return;
        }

        if (row.Status == Open && row.PostingRuleVersion == entry.PostingRuleVersion
            && row.LinesAwaitingAmount == entry.LinesAwaitingAmount && row.FirstMessageId == entry.FirstMessageId)
        {
            return;
        }

        var previous = PendingAudit(row);
        var versionBefore = row.Version;
        row.Status = Open;
        row.PostingRuleVersion = entry.PostingRuleVersion;
        row.LinesAwaitingAmount = entry.LinesAwaitingAmount;
        row.FirstMessageId = entry.FirstMessageId;
        row.Version = checked(versionBefore + 1);
        row.UpdatedAtUtc = now;
        Audit(metadata, source.CompanyId, PendingEntityType, source.SourceId.ToString("D"),
            "accounting.pending-valuation.updated.v1", "Update", versionBefore, row.Version, previous, PendingAudit(row),
            "Değerleme bekleyen kayıt güncellendi");
    }

    private void AddDelivery(string consumer, string messageId, PostingOutcome outcome, AccountingCommandMetadata metadata)
    {
        QueueEntrySnapshot? entry = outcome.Kind switch
        {
            PostingOutcomeKind.ExceptionQueued => QueueEntrySnapshot.Of(outcome.Exception!),
            PostingOutcomeKind.WaitingValuation => QueueEntrySnapshot.Of(outcome.Pending!),
            _ => null,
        };
        dbContext.Deliveries.Add(new DeliveryRow
        {
            CompanyId = RequireResolvedCompany(),
            Consumer = consumer,
            MessageId = messageId,
            OutcomeKind = (int)outcome.Kind,
            JournalId = outcome.Journal?.Id,
            QueueEntryJson = entry is null ? null : JsonSerializer.Serialize(entry, Json),
            RecordedAtUtc = timeProvider.GetUtcNow(),
            CorrelationId = metadata.CorrelationId.Trim(),
        });
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw Refused(AccountingStoreCodes.ConcurrentChange,
                "Aynı dönem, olgu ya da kuyruk kaydı başka bir yazmayla değişti; hiçbir şey kaydedilmedi.", exception);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw Refused(AccountingStoreCodes.ConcurrentChange,
                "Kalıcı tekillik kuralı başka bir yazmanın lehine sonuçlandı; hiçbir şey kaydedilmedi.", exception);
        }
    }

    private void Audit(AccountingCommandMetadata metadata, Guid companyId, string entityType, string entityId, string eventType,
        string operation, long? versionBefore, long? versionAfter, string before, string after, string reason)
    {
        auditEvents.Enqueue(new AuditEventDraft(
            companyId,
            eventType,
            entityType,
            entityId,
            operation,
            versionBefore,
            versionAfter,
            AuditActorTypes.User,
            metadata.ActorId,
            metadata.PermissionCode,
            metadata.RoleCodes,
            before,
            after,
            reason,
            ApprovalContextJson: null,
            AuditAiInvolvement.None,
            AiContextJson: null,
            metadata.SourceChannel,
            metadata.ClientIpAddress,
            metadata.UserAgent,
            metadata.CorrelationId,
            metadata.CausationId,
            metadata.IdempotencyKey));
    }

    private Guid RequireResolvedCompany()
    {
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
        {
            throw Refused(AccountingStoreCodes.CompanyContextMismatch, "Aktif şirket bağlamı olmadan muhasebe kaydı işlenmez.");
        }

        return companyContext.CompanyId;
    }

    private void RequireCompany(Guid companyId)
    {
        if (RequireResolvedCompany() != companyId)
        {
            throw Refused(AccountingStoreCodes.CompanyContextMismatch, "Kayıt aktif şirket bağlamının şirketine ait değil.");
        }
    }

    /// <summary>
    /// The domain's identity-text rule, checked BEFORE the inbox lookup: SQL Server's equality
    /// ignores trailing spaces even under a binary collation, so "m-1 " would otherwise find the
    /// stored delivery of "m-1" and be answered as its replay instead of being refused.
    /// </summary>
    private static void RequireDelivery(string consumer, string messageId)
    {
        if (!IsCanonical(consumer) || !IsCanonical(messageId))
        {
            throw new AccountingDomainException(AccountingErrorCodes.InvalidReference,
                "A delivery needs a consumer and a message id without surrounding whitespace.");
        }

        if (consumer.Length > IdentityMaximumLength || messageId.Length > MessageIdMaximumLength)
        {
            throw Refused(AccountingStoreCodes.ValueDoesNotFit, "Tüketici ya da mesaj kimliği kayıt alanına sığmıyor.");
        }
    }

    private static bool IsCanonical(string? value) =>
        !string.IsNullOrWhiteSpace(value) && string.Equals(value, value.Trim(), StringComparison.Ordinal);

    private static void RequireFits(string? purpose, string? postingRuleVersion, IEnumerable<decimal> amounts)
    {
        // Not a money policy: the stored columns hold 18 decimals and 20 integer digits; a value that
        // does not fit is refused rather than rounded by the database. No scale is chosen here.
        if ((purpose?.Length ?? 0) > IdentityMaximumLength || (postingRuleVersion?.Length ?? 0) > IdentityMaximumLength
            || amounts.Any(amount => Scale(amount) > AmountScaleMaximum || Math.Abs(amount) >= 100_000_000_000_000_000_000m))
        {
            throw Refused(AccountingStoreCodes.ValueDoesNotFit, "Bir metin ya da tutar kayıt alanına sığmıyor; kesilmez, yuvarlanmaz.");
        }
    }

    private static void RequireMetadata(AccountingCommandMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (string.IsNullOrWhiteSpace(metadata.ActorId) || string.IsNullOrWhiteSpace(metadata.PermissionCode)
            || string.IsNullOrWhiteSpace(metadata.CorrelationId) || string.IsNullOrWhiteSpace(metadata.IdempotencyKey)
            || string.IsNullOrWhiteSpace(metadata.SourceChannel) || metadata.RoleCodes is null)
        {
            throw new ArgumentException("Muhasebe yazması aktör, izin, correlation, idempotency ve kanal bilgisi ister.", nameof(metadata));
        }

        if (metadata.ActorId.Trim().Length > IdentityMaximumLength || metadata.CorrelationId.Trim().Length > IdentityMaximumLength)
        {
            throw Refused(AccountingStoreCodes.ValueDoesNotFit, "Aktör ya da correlation değeri kayıt alanına sığmıyor.");
        }
    }

    private static int Scale(decimal value) => (decimal.GetBits(value)[3] >> 16) & 0xFF;

    private static AccountingPeriod RestorePeriod(PeriodRow row) =>
        AccountingPeriod.Restore(row.Id, row.CompanyId, row.StartsOn, row.EndsOn, row.ClosedByUserId, row.ClosedAtUtc);

    private static PostingException ToException(PostingExceptionRow row) =>
        new PostingException(new SourceReference(row.CompanyId, row.SourceId, row.PostingPurpose), row.PostingRuleVersion,
            Array.AsReadOnly(JsonSerializer.Deserialize<string[]>(row.MissingMappingKeysJson, Json)
                ?? throw new InvalidOperationException("Kayıtlı istisnanın eşleme anahtarları okunamadı.")),
            row.FirstMessageId);

    private static PendingValuation ToPending(PendingValuationRow row) =>
        new PendingValuation(new SourceReference(row.CompanyId, row.SourceId, row.PostingPurpose), row.PostingRuleVersion,
            row.LinesAwaitingAmount, row.FirstMessageId);

    private static string PeriodAudit(AccountingPeriod period) => JsonSerializer.Serialize(new
    {
        period.Id,
        period.StartsOn,
        period.EndsOn,
        status = period.Status.ToString(),
        period.ClosedByUserId,
        period.ClosedAtUtc,
    }, Json);

    private static string JournalAudit(Journal journal, Guid? correctionId) => JsonSerializer.Serialize(new
    {
        journal.Id,
        kind = journal.Kind.ToString(),
        source = new { journal.Source.SourceId, journal.Source.PostingPurpose },
        journal.PostingRuleVersion,
        journal.CurrencyId,
        journal.Dates.TransactionDate,
        journal.Dates.AccountingDate,
        journal.Dates.ValueDate,
        journal.LinkedJournalId,
        correctionId,
        lines = journal.Lines.Count,
    }, Json);

    private static string ExceptionAudit(PostingExceptionRow row) => JsonSerializer.Serialize(new
    {
        row.SourceId,
        row.PostingPurpose,
        status = row.Status == Open ? "Open" : "Resolved",
        row.PostingRuleVersion,
        missingMappingKeys = row.MissingMappingKeysJson,
        row.FirstMessageId,
    }, Json);

    private static string PendingAudit(PendingValuationRow row) => JsonSerializer.Serialize(new
    {
        row.SourceId,
        row.PostingPurpose,
        status = row.Status == Open ? "Open" : "Resolved",
        row.PostingRuleVersion,
        row.LinesAwaitingAmount,
        row.FirstMessageId,
    }, Json);

    private static AccountingStoreException Refused(string code, string message, Exception? inner = null) =>
        new AccountingStoreException(code, message, inner);

    /// <summary>The queue entry a delivery answered with, kept so a replay returns it unchanged.</summary>
    private sealed record QueueEntrySnapshot(
        Guid SourceId,
        string PostingPurpose,
        string PostingRuleVersion,
        string FirstMessageId,
        string[]? MissingMappingKeys,
        int? LinesAwaitingAmount)
    {
        public static QueueEntrySnapshot Of(PostingException exception) =>
            new QueueEntrySnapshot(exception.Source.SourceId, exception.Source.PostingPurpose, exception.PostingRuleVersion,
                exception.FirstMessageId, [.. exception.MissingMappingKeys], LinesAwaitingAmount: null);

        public static QueueEntrySnapshot Of(PendingValuation pending) =>
            new QueueEntrySnapshot(pending.Source.SourceId, pending.Source.PostingPurpose, pending.PostingRuleVersion,
                pending.FirstMessageId, MissingMappingKeys: null, pending.LinesAwaitingAmount);

        public PostingException ToException(Guid companyId) =>
            new PostingException(new SourceReference(companyId, SourceId, PostingPurpose), PostingRuleVersion,
                Array.AsReadOnly(MissingMappingKeys!), FirstMessageId);

        public PendingValuation ToPending(Guid companyId) =>
            new PendingValuation(new SourceReference(companyId, SourceId, PostingPurpose), PostingRuleVersion,
                LinesAwaitingAmount!.Value, FirstMessageId);
    }
}
