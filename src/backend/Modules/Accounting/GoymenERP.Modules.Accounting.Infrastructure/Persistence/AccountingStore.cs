using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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
    TimeProvider timeProvider,
    AccountingAmountScaleVerifier amounts) : IAccountingPeriodStore, IAccountingPostingStore, IAccountingCommandLog,
    IAccountingNormalJournalDraftStore
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
    private const string NormalDraftEntityType = "Accounting.NormalJournalDraft";
    private const int MaximumPageSize = 100;

    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    public async Task<(AccountingPeriodRecord Record, AccountingWriteOutcome Outcome)> ProposePeriodAsync(Guid periodId,
        DateOnly startsOn, DateOnly endsOn, Guid preparedByUserId, AccountingCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        RequireMetadata(metadata);
        var companyId = RequireResolvedCompany();
        var now = timeProvider.GetUtcNow();
        // Ö4: a new period is a PROPOSAL; the domain refuses an empty id, company or preparer and reversed dates.
        var period = AccountingPeriod.Propose(periodId, companyId, startsOn, endsOn, preparedByUserId, now);

        var existing = await dbContext.Periods.AsNoTracking().SingleOrDefaultAsync(row => row.Id == periodId, cancellationToken);
        if (existing is not null)
        {
            if (existing.StartsOn != startsOn || existing.EndsOn != endsOn)
            {
                throw Refused(AccountingStoreCodes.PeriodConflict, "Aynı dönem kimliği başka tarihlerle verildi.");
            }

            // R3: the same id is the same proposal; another preparer under it is not a replay of this one.
            return existing.PreparedByUserId == preparedByUserId
                ? (new AccountingPeriodRecord(RestorePeriod(existing), existing.Version), AccountingWriteOutcome.Replayed)
                : throw Refused(AccountingStoreCodes.PeriodPreparerConflict, "Aynı dönem kimliğini başka bir kişi önerdi.");
        }

        // A proposal covers its dates too: no two periods of the company overlap, whatever their status.
        if (await dbContext.Periods.AsNoTracking().AnyAsync(row => row.StartsOn <= endsOn && row.EndsOn >= startsOn,
                cancellationToken))
        {
            throw Refused(AccountingStoreCodes.PeriodOverlap, "Yeni dönem şirketin kayıtlı bir dönemiyle çakışıyor.");
        }

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
            Status = (int)AccountingPeriodStatus.Proposed,
            PreparedByUserId = period.PreparedByUserId,
            PreparedAtUtc = period.PreparedAtUtc,
            Version = 1,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });
        Audit(metadata, companyId, PeriodEntityType, period.Id.ToString("D"), "accounting.period.proposed.v1", "Propose",
            versionBefore: null, versionAfter: 1, before: "{}", after: PeriodAudit(period), "Muhasebe dönemi açılış için önerildi");
        var record = new AccountingPeriodRecord(period, 1);
        Complete(metadata, companyId, record);
        await SaveAsync(cancellationToken);
        return (record, AccountingWriteOutcome.Recorded);
    }

    public async Task<AccountingPeriodRecord> ApprovePeriodAsync(Guid periodId, long expectedVersion, Guid approvedByUserId,
        AccountingCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        RequireMetadata(metadata);
        var companyId = RequireResolvedCompany();
        var row = await dbContext.Periods.SingleOrDefaultAsync(candidate => candidate.Id == periodId, cancellationToken)
            ?? throw Refused(AccountingStoreCodes.PeriodNotFound, "Bu şirketin böyle bir dönemi yok.");
        RequireVersion(row, expectedVersion);
        var period = RestorePeriod(row);
        var before = PeriodAudit(period);
        // Ö4: the domain refuses the preparer and anything that is not a proposal.
        period.Approve(approvedByUserId, timeProvider.GetUtcNow());

        var versionBefore = row.Version;
        row.Status = (int)AccountingPeriodStatus.Open;
        row.ApprovedByUserId = period.ApprovedByUserId;
        row.ApprovedAtUtc = period.ApprovedAtUtc;
        row.Version = checked(versionBefore + 1);
        row.UpdatedAtUtc = period.ApprovedAtUtc!.Value;
        // Ö4 in the audit record itself: who prepared the period and who (someone else) approved it.
        var approval = JsonSerializer.Serialize(new
        {
            rule = "O4",
            preparedBy = period.PreparedByUserId,
            approvedBy = period.ApprovedByUserId,
        }, Json);
        Audit(metadata, companyId, PeriodEntityType, period.Id.ToString("D"), "accounting.period.approved.v1", "Approve",
            versionBefore, row.Version, before, PeriodAudit(period), "Muhasebe dönemi açılışı onaylandı", approval);
        var record = new AccountingPeriodRecord(period, row.Version);
        Complete(metadata, companyId, record);
        await SaveAsync(cancellationToken);
        return record;
    }

    public async Task<AccountingPeriodRecord> ClosePeriodAsync(Guid periodId, long expectedVersion, Guid closedByUserId,
        AccountingCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        RequireMetadata(metadata);
        var companyId = RequireResolvedCompany();
        var row = await dbContext.Periods.SingleOrDefaultAsync(candidate => candidate.Id == periodId, cancellationToken)
            ?? throw Refused(AccountingStoreCodes.PeriodNotFound, "Bu şirketin böyle bir dönemi yok.");
        RequireVersion(row, expectedVersion);
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
        var record = new AccountingPeriodRecord(period, row.Version);
        Complete(metadata, companyId, record);
        await SaveAsync(cancellationToken);
        return record;
    }

    public async Task<AccountingPeriodRecord?> FindPeriodAsync(Guid periodId, CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        var row = await dbContext.Periods.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == periodId, cancellationToken);
        return row is null ? null : new AccountingPeriodRecord(RestorePeriod(row), row.Version);
    }

    public async Task<AccountingPeriodRecord?> FindPeriodCoveringAsync(DateOnly on, CancellationToken cancellationToken = default)
    {
        // The company is the ambient one (the tenant filter); another company's period is not visible here.
        RequireResolvedCompany();
        // One company's periods never overlap, so SingleOrDefault is the right shape: two rows covering one
        // date would be a stored contradiction and must fail loudly rather than pick one.
        var row = await dbContext.Periods.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.StartsOn <= on && candidate.EndsOn >= on, cancellationToken);
        return row is null ? null : new AccountingPeriodRecord(RestorePeriod(row), row.Version);
    }

    public async Task<AccountingPage<AccountingPeriodRecord>> ListPeriodsAsync(int pageSize, string? cursor,
        CancellationToken cancellationToken = default)
    {
        var companyId = RequireResolvedCompany();
        RequirePageSize(pageSize);
        var query = dbContext.Periods.AsNoTracking();
        if (cursor is not null)
        {
            // Periods of one company never overlap, so the start date alone orders them.
            var startsOn = ListCursor.DecodePeriod(cursor, companyId);
            query = query.Where(row => row.StartsOn < startsOn);
        }

        var rows = await query.OrderByDescending(row => row.StartsOn).Take(pageSize + 1).ToListAsync(cancellationToken);
        var page = rows.Take(pageSize).ToArray();
        return new AccountingPage<AccountingPeriodRecord>(
            [.. page.Select(row => new AccountingPeriodRecord(RestorePeriod(row), row.Version))],
            rows.Count > pageSize ? ListCursor.EncodePeriod(companyId, page[^1].StartsOn) : null);
    }

    public async Task<PostingOutcome> ReceiveAsync(PostingRequest request, Guid periodId, AccountingCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        // Null parts are caller errors, not rules: refused before anything else (SİPARİŞ #294/#298 P3-1).
        if (request.Source is null || request.Dates is null || request.Lines is null || request.Lines.Any(line => line is null))
        {
            throw new ArgumentException("A posting request needs its source, dates and proposed lines.", nameof(request));
        }

        RequireMetadata(metadata);
        RequireCompany(request.Source.CompanyId);
        RequireDelivery(request.Consumer, request.MessageId);
        RequireFits(request.Source.PostingPurpose, request.PostingRuleVersion,
            request.Lines.Where(line => line.Amount is not null).Select(line => line.Amount!.Value));

        var requestHash = ReceiveHash(request);
        var replay = await ReplayAsync(request.Consumer, request.MessageId, requestHash, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        // Ö2 (ACC-V0): every known amount at the currency's approved scale, before anything is decided.
        await VerifyAmountsAsync(request.Source.CompanyId, request.CurrencyId, request.Dates.AccountingDate,
            request.Lines.Where(line => line.Amount is not null).Select(line => line.Amount!.Value), cancellationToken);

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
        AddDelivery(request.Consumer, request.MessageId, requestHash, outcome, metadata);
        await SaveAsync(cancellationToken);
        return outcome;
    }

    public async Task<(NormalJournalDraftRecordV1 Record, AccountingWriteOutcome Outcome)> CreateNormalDraftAsync(
        NormalJournalDraftV1 draft,
        Func<CancellationToken, Task> freshAuthorityFence,
        AccountingCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(freshAuthorityFence);
        RequireMetadata(metadata);
        RequireCompany(draft.CompanyId);
        RequireNormalDraft(draft);
        var hash = NormalDraftHash(draft);
        var existing = await dbContext.NormalJournalDrafts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == draft.Id, cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.ContentHash, hash, StringComparison.Ordinal))
            {
                throw Refused(NormalJournalDraftStoreCodesV1.ContentConflict,
                    "Aynı taslak kimliği başka değişmez içerikle kaydedilmiş.");
            }

            var replay = await RestoreNormalDraftAsync(existing, cancellationToken);
            await freshAuthorityFence(cancellationToken);
            return (replay, AccountingWriteOutcome.Replayed);
        }

        var now = timeProvider.GetUtcNow();
        var row = new NormalJournalDraftRow
        {
            Id = draft.Id,
            CompanyId = draft.CompanyId,
            Status = (int)NormalJournalDraftStatusV1.Draft,
            Version = draft.Version,
            ContentHash = hash,
            PreparedByUserId = draft.PreparedByUserId,
            PreparedAtUtc = now,
            Consumer = draft.Request.Consumer,
            MessageId = draft.Request.MessageId,
            SourceId = draft.Request.Source.SourceId,
            PostingPurpose = draft.Request.Source.PostingPurpose,
            PostingRuleVersion = draft.Request.PostingRuleVersion,
            CurrencyId = draft.Request.CurrencyId,
            TransactionDate = draft.Request.Dates.TransactionDate,
            AccountingDate = draft.Request.Dates.AccountingDate,
            ValueDate = draft.Request.Dates.ValueDate,
            RecordedAtUtc = draft.Request.Dates.RecordedAtUtc,
            AccountPlanSourceVersion = draft.Rule.Mirror.SourceVersion,
            AccountPlanSourceSha256 = draft.Rule.Mirror.SourceSha256.ToUpperInvariant(),
            AccountPlanApprovedByUserId = draft.Rule.Mirror.ApprovedByUserId,
            AccountPlanApprovedAtUtc = draft.Rule.Mirror.ApprovedAtUtc,
            PostingRuleApprovedByUserId = draft.Rule.ApprovedByUserId,
            PostingRuleApprovedAtUtc = draft.Rule.ApprovedAtUtc,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        dbContext.NormalJournalDrafts.Add(row);
        var lineRows = draft.Request.Lines.Select((line, index) => new NormalJournalDraftLineRow
        {
            CompanyId = draft.CompanyId,
            DraftId = draft.Id,
            LineNumber = index + 1,
            MappingKey = line.MappingKey,
            Side = (int)line.Side,
            AccountId = line.Account!.Value.AccountId,
            Amount = line.Amount!.Value,
        }).ToArray();
        dbContext.NormalJournalDraftLines.AddRange(lineRows);

        var record = RestoreNormalDraft(row, lineRows);
        // The existing-row/line lookup above is the final awaited owner read. Revalidate immediately
        // before the synchronous audit/command additions and their one atomic EF save.
        await freshAuthorityFence(cancellationToken);
        Audit(metadata, draft.CompanyId, NormalDraftEntityType, draft.Id.ToString("D"),
            "accounting.normal-journal-draft.created.v1", "Create", null, draft.Version, "{}",
            NormalDraftAudit(record), "Normal fiş taslağı onaylı plan ve kural snapshot'ına bağlı hazırlandı");
        Complete(metadata, draft.CompanyId, record);
        await SaveAsync(cancellationToken);
        return (record, AccountingWriteOutcome.Recorded);
    }

    public async Task<NormalJournalDraftRecordV1?> FindNormalDraftAsync(
        Guid draftId,
        CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        var row = await dbContext.NormalJournalDrafts.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == draftId, cancellationToken);
        return row is null ? null : await RestoreNormalDraftAsync(row, cancellationToken);
    }

    public async Task<NormalJournalDraftRecordV1> ApproveAndPostNormalDraftAsync(
        Guid draftId,
        long expectedVersion,
        Guid periodId,
        Guid approvedByUserId,
        Func<CancellationToken, Task> freshAuthorityFence,
        AccountingCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(freshAuthorityFence);
        RequireMetadata(metadata);
        var companyId = RequireResolvedCompany();
        if (draftId == Guid.Empty || periodId == Guid.Empty || expectedVersion <= 0 || approvedByUserId == Guid.Empty)
        {
            throw new ArgumentException("Taslak, sürüm, dönem ve onaylayan kimliği zorunludur.", nameof(draftId));
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var row = await dbContext.NormalJournalDrafts
                .SingleOrDefaultAsync(candidate => candidate.Id == draftId, cancellationToken)
                ?? throw Refused(NormalJournalDraftStoreCodesV1.NotFound, "Bu şirketin böyle bir normal fiş taslağı yok.");
            if (row.Version != expectedVersion)
            {
                throw new AccountingVersionConflictException(expectedVersion, row.Version);
            }

            if (row.Status != (int)NormalJournalDraftStatusV1.Draft)
            {
                throw Refused(NormalJournalDraftStoreCodesV1.NotPending, "Normal fiş taslağı artık onay beklemiyor.");
            }

            var lineRows = await dbContext.NormalJournalDraftLines
                .Where(line => line.DraftId == draftId)
                .OrderBy(line => line.LineNumber)
                .ToListAsync(cancellationToken);
            var before = RestoreNormalDraft(row, lineRows);

            // This follows the draft/version reads. ReceiveAsync then performs the real scale,
            // period and ledger reads while every write remains inside this transaction.
            await freshAuthorityFence(cancellationToken);
            var postingMetadata = metadata with { Completion = null };
            var outcome = await ReceiveAsync(before.Request, periodId, postingMetadata, cancellationToken);
            if (outcome.Kind != PostingOutcomeKind.Posted || outcome.Journal is null || outcome.Journal.Id != draftId)
            {
                throw Refused(NormalJournalDraftStoreCodesV1.PostingConflict,
                    "Taslak mevcut defter gerçeklerinden yeni ve tek bir fiş olarak kaydedilemedi.");
            }

            // ReceiveAsync's save is still uncommitted. This second fence is after every awaited
            // scale/period/ledger read and immediately before approval, command and final commit.
            await freshAuthorityFence(cancellationToken);
            var now = timeProvider.GetUtcNow();
            var versionBefore = row.Version;
            row.Status = (int)NormalJournalDraftStatusV1.Approved;
            row.ApprovedByUserId = approvedByUserId;
            row.ApprovedAtUtc = now;
            row.PeriodId = periodId;
            row.PostedJournalId = outcome.Journal.Id;
            row.Version = checked(versionBefore + 1);
            row.UpdatedAtUtc = now;

            var record = RestoreNormalDraft(row, lineRows);
            var approvalContext = JsonSerializer.Serialize(new
            {
                policy = "ACC-V1-normal-self-approval-with-narrow-authority",
                preparedBy = row.PreparedByUserId,
                approvedBy = approvedByUserId,
                selfApproval = row.PreparedByUserId == approvedByUserId,
                periodId,
                journalId = outcome.Journal.Id,
            }, Json);
            Audit(metadata, companyId, NormalDraftEntityType, draftId.ToString("D"),
                "accounting.normal-journal-draft.approved.v1", "Approve", versionBefore, row.Version,
                NormalDraftAudit(before), NormalDraftAudit(record), "Normal fiş taslağı onaylandı ve mevcut defter yolunda kaydedildi",
                approvalContext);
            Complete(metadata, companyId, record);
            await SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return record;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            dbContext.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<PostingOutcome> ReverseAsync(string consumer, string messageId, Guid reversedJournalId,
        Guid reversalJournalId, PostingDates dates, Guid periodId, AccountingCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dates);
        RequireMetadata(metadata);
        var companyId = RequireResolvedCompany();
        RequireDelivery(consumer, messageId);

        var requestHash = ReverseHash(reversedJournalId, dates);
        var replay = await ReplayAsync(consumer, messageId, requestHash, cancellationToken);
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

        AddDelivery(consumer, messageId, requestHash, outcome, metadata);
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
        if (correctionLines.Any(line => line is null))
        {
            throw new ArgumentException("A journal line cannot be null.", nameof(lines));
        }

        RequireFits(purpose: null, postingRuleVersion,
            correctionLines.SelectMany(line => new[] { line.Debit, line.Credit }));

        var requestHash = CorrectHash(correctionId, correctedJournalId, postingRuleVersion, dates, correctionLines);
        var replay = await ReplayAsync(consumer, messageId, requestHash, cancellationToken);
        if (replay is not null)
        {
            return replay;
        }

        var (periodRow, period) = await LoadPeriodAsync(periodId, cancellationToken);
        var rows = await dbContext.Journals.AsNoTracking()
            .Where(row => row.Id == correctedJournalId || row.CorrectionId == correctionId)
            .ToListAsync(cancellationToken);
        if (rows.FirstOrDefault(row => row.Id == correctedJournalId) is { } corrected)
        {
            // Ö2: a correction is posted in the corrected journal's currency; its amounts at that scale.
            await VerifyAmountsAsync(companyId, corrected.CurrencyId, dates.AccountingDate,
                correctionLines.SelectMany(line => new[] { line.Debit, line.Credit }).Where(amount => amount != 0m), cancellationToken);
        }

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

        AddDelivery(consumer, messageId, requestHash, outcome, metadata);
        await SaveAsync(cancellationToken);
        return outcome;
    }

    public async Task<Journal?> FindJournalAsync(Guid journalId, CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        var rows = await dbContext.Journals.AsNoTracking().Where(row => row.Id == journalId).ToListAsync(cancellationToken);
        return rows.Count == 0 ? null : (await RestoreJournalsAsync(rows, cancellationToken)).Single();
    }

    public async Task<AccountingJournalRecord?> ReadJournalAsync(Guid journalId, CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        var rows = await dbContext.Journals.AsNoTracking().Where(row => row.Id == journalId).ToListAsync(cancellationToken);
        return rows.Count == 0
            ? null
            : new AccountingJournalRecord((await RestoreJournalsAsync(rows, cancellationToken)).Single(), rows[0].PeriodId,
                rows[0].StoredAtUtc);
    }

    public async Task<AccountingPage<AccountingJournalRecord>> ListJournalsAsync(Guid? periodId, int pageSize, string? cursor,
        CancellationToken cancellationToken = default)
    {
        var companyId = RequireResolvedCompany();
        RequirePageSize(pageSize);
        var query = dbContext.Journals.AsNoTracking();
        if (periodId is { } period)
        {
            query = query.Where(row => row.PeriodId == period);
        }

        if (cursor is not null)
        {
            var (accountingDate, storedAtUtc, id) = ListCursor.DecodeJournal(cursor, companyId, periodId);
            query = query.Where(row => row.AccountingDate < accountingDate
                || (row.AccountingDate == accountingDate && (row.StoredAtUtc < storedAtUtc
                    || (row.StoredAtUtc == storedAtUtc && row.Id.CompareTo(id) < 0))));
        }

        var rows = await query
            .OrderByDescending(row => row.AccountingDate).ThenByDescending(row => row.StoredAtUtc).ThenByDescending(row => row.Id)
            .Take(pageSize + 1)
            .ToListAsync(cancellationToken);
        var page = rows.Take(pageSize).ToArray();
        var journals = await RestoreJournalsAsync(page, cancellationToken);
        var byId = journals.ToDictionary(journal => journal.Id);
        return new AccountingPage<AccountingJournalRecord>(
            [.. page.Select(row => new AccountingJournalRecord(byId[row.Id], row.PeriodId, row.StoredAtUtc))],
            rows.Count > pageSize
                ? ListCursor.EncodeJournal(companyId, periodId, page[^1].AccountingDate, page[^1].StoredAtUtc, page[^1].Id)
                : null);
    }

    public async Task<AccountingCommandRecord?> FindCommandAsync(string commandName, string idempotencyKeyHash,
        CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        var row = await dbContext.Commands.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.CommandName == commandName && candidate.IdempotencyKeyHash == idempotencyKeyHash,
            cancellationToken);
        return row is null
            ? null
            : new AccountingCommandRecord(row.CommandName, row.IdempotencyKeyHash, row.ActorId, row.PayloadHash,
                row.ResultJson, row.CorrelationId);
    }

    public async Task RecordCommandAsync(AccountingCommandRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        AddCommand(RequireResolvedCompany(), record);
        await SaveAsync(cancellationToken);
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

    /// <summary>
    /// A stored delivery of the same (consumer, message id) answers what it recorded — but only for
    /// the same message. The same id with other content is refused with
    /// <see cref="AccountingStoreCodes.DeliveryContentConflict"/> instead of silently getting the
    /// first answer (ORG #298 P3-1; the Inventory movement-id and Idempotency request-hash pattern).
    /// Every delivery carries its hash (NOT NULL), so no replay escapes this check.
    /// </summary>
    private async Task<PostingOutcome?> ReplayAsync(string consumer, string messageId, string requestHash,
        CancellationToken cancellationToken)
    {
        var delivery = await dbContext.Deliveries.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Consumer == consumer && row.MessageId == messageId, cancellationToken);
        if (delivery is null)
        {
            return null;
        }

        if (!string.Equals(delivery.RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw Refused(AccountingStoreCodes.DeliveryContentConflict,
                "Aynı mesaj kimliği başka bir içerikle geldi; ilk cevap bu içeriğe verilmez.");
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

    /// <summary>
    /// ADR-011 §6: the completed command's record joins the business change and its audit event in the
    /// same save. Without a completion (a caller outside the HTTP surface) nothing is added.
    /// </summary>
    private void Complete(AccountingCommandMetadata metadata, Guid companyId, object result)
    {
        if (metadata.Completion is { } completion)
        {
            AddCommand(companyId, completion(result));
        }
    }

    private void AddCommand(Guid companyId, AccountingCommandRecord record) =>
        dbContext.Commands.Add(new AccountingCommandRow
        {
            CompanyId = companyId,
            CommandName = record.CommandName,
            IdempotencyKeyHash = record.IdempotencyKeyHash,
            ActorId = record.ActorId,
            PayloadHash = record.RequestHash,
            ResultJson = record.ResultJson,
            CorrelationId = record.OriginalCorrelationId,
            RecordedAtUtc = timeProvider.GetUtcNow(),
        });

    /// <summary>The caller's version is the stored one, or the write is a terminal version conflict (§7).</summary>
    private static void RequireVersion(PeriodRow row, long expectedVersion)
    {
        if (row.Version != expectedVersion)
        {
            throw new AccountingVersionConflictException(expectedVersion, row.Version);
        }
    }

    private async Task VerifyAmountsAsync(Guid companyId, Guid currencyId, DateOnly accountingDate, IEnumerable<decimal> values,
        CancellationToken cancellationToken) =>
        await amounts.VerifyAsync(companyId, currencyId, accountingDate, values, cancellationToken);

    private static void RequirePageSize(int pageSize)
    {
        if (pageSize is < 1 or > MaximumPageSize)
        {
            throw Refused(AccountingStoreCodes.ListRequestInvalid, "Sayfa boyutu 1 ile 100 arasında olmalıdır.");
        }
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

    private void AddDelivery(string consumer, string messageId, string requestHash, PostingOutcome outcome,
        AccountingCommandMetadata metadata)
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
            RequestHash = requestHash,
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
        string operation, long? versionBefore, long? versionAfter, string before, string after, string reason,
        string? approvalContext = null)
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
            ApprovalContextJson: approvalContext,
            AuditAiInvolvement.None,
            AiContextJson: null,
            metadata.SourceChannel,
            metadata.ClientIpAddress,
            metadata.UserAgent,
            metadata.CorrelationId,
            metadata.CausationId,
            metadata.IdempotencyKey));
    }

    /// <summary>
    /// The canonical content of one message: operation and business facts. Delivery details a
    /// redelivery may change are left out: the journal id it proposes and the recording instant (the
    /// same rule as the correction content, ACC-2). Lines are compared as an ordered-by-value list,
    /// so their order does not matter but a repeated line does.
    /// </summary>
    internal static string ReceiveHash(PostingRequest request) => Hash(new
    {
        operation = "receive",
        sourceId = request.Source.SourceId,
        purpose = request.Source.PostingPurpose,
        ruleVersion = request.PostingRuleVersion,
        currencyId = request.CurrencyId,
        dates = BusinessDates(request.Dates),
        lines = request.Lines
            .Select(line => new
            {
                key = line.MappingKey,
                account = line.Account?.AccountId,
                side = (int)line.Side,
                amount = line.Amount is { } value ? Canonical(value) : null,
            })
            .OrderBy(line => line.key, StringComparer.Ordinal)
            .ThenBy(line => line.account)
            .ThenBy(line => line.side)
            .ThenBy(line => line.amount, StringComparer.Ordinal),
    });

    internal static string ReverseHash(Guid reversedJournalId, PostingDates dates) => Hash(new
    {
        operation = "reverse",
        reversedJournalId,
        dates = BusinessDates(dates),
    });

    internal static string CorrectHash(Guid correctionId, Guid correctedJournalId, string postingRuleVersion,
        PostingDates dates, IEnumerable<JournalLine> lines) => Hash(new
    {
        operation = "correct",
        correctionId,
        correctedJournalId,
        ruleVersion = postingRuleVersion,
        dates = BusinessDates(dates),
        lines = lines
            .Select(line => new
            {
                account = line.Account.AccountId,
                debit = Canonical(line.Debit),
                credit = Canonical(line.Credit),
            })
            .OrderBy(line => line.account)
            .ThenBy(line => line.debit, StringComparer.Ordinal)
            .ThenBy(line => line.credit, StringComparer.Ordinal),
    });

    private static string NormalDraftHash(NormalJournalDraftV1 draft) => Hash(new
    {
        operation = "normal-journal-draft",
        draft.Id,
        draft.CompanyId,
        draft.Version,
        draft.PreparedByUserId,
        mirror = new
        {
            draft.Rule.Mirror.SourceVersion,
            sha256 = draft.Rule.Mirror.SourceSha256.ToUpperInvariant(),
            draft.Rule.Mirror.ApprovedByUserId,
            approvedAt = draft.Rule.Mirror.ApprovedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
        },
        rule = new
        {
            draft.Rule.Version,
            draft.Rule.ApprovedByUserId,
            approvedAt = draft.Rule.ApprovedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
        },
        request = new
        {
            draft.Request.Consumer,
            draft.Request.MessageId,
            draft.Request.JournalId,
            draft.Request.Source.SourceId,
            draft.Request.Source.PostingPurpose,
            draft.Request.PostingRuleVersion,
            draft.Request.CurrencyId,
            dates = new
            {
                business = BusinessDates(draft.Request.Dates),
                recordedAt = draft.Request.Dates.RecordedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            },
            lines = draft.Request.Lines.Select(line => new
                {
                    line.MappingKey,
                    accountId = line.Account!.Value.AccountId,
                    side = (int)line.Side,
                    amount = Canonical(line.Amount!.Value),
                })
                .OrderBy(line => line.MappingKey, StringComparer.Ordinal)
                .ThenBy(line => line.accountId)
                .ThenBy(line => line.side)
                .ThenBy(line => line.amount, StringComparer.Ordinal),
        },
    });

    private static object BusinessDates(PostingDates dates) => new
    {
        transaction = dates.TransactionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        accounting = dates.AccountingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        value = dates.ValueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
    };

    /// <summary>A decimal without its trailing zeros: 5, 5.0 and 5.00 are the same amount (as in ACC-2's content rule).</summary>
    private static string Canonical(decimal value) =>
        (value / 1.0000000000000000000000000000m).ToString(CultureInfo.InvariantCulture);

    private static string Hash(object canonical) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(canonical, Json)))).ToLowerInvariant();

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
        AccountingPeriod.Restore(row.Id, row.CompanyId, row.StartsOn, row.EndsOn, row.PreparedByUserId, row.PreparedAtUtc,
            row.ApprovedByUserId, row.ApprovedAtUtc, row.ClosedByUserId, row.ClosedAtUtc);

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
        period.PreparedByUserId,
        period.PreparedAtUtc,
        period.ApprovedByUserId,
        period.ApprovedAtUtc,
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

    private static string NormalDraftAudit(NormalJournalDraftRecordV1 record) => JsonSerializer.Serialize(new
    {
        record.Id,
        status = record.Status.ToString(),
        record.Version,
        record.PreparedByUserId,
        record.PreparedAtUtc,
        record.ApprovedByUserId,
        record.ApprovedAtUtc,
        record.PeriodId,
        record.PostedJournalId,
        source = new { record.Request.Source.SourceId, record.Request.Source.PostingPurpose },
        record.Request.PostingRuleVersion,
        record.AccountPlanSourceVersion,
        accountPlanSha256 = record.AccountPlanSourceSha256,
        lines = record.Request.Lines.Count,
    }, Json);

    private void RequireNormalDraft(NormalJournalDraftV1 draft)
    {
        RequireDelivery(draft.Request.Consumer, draft.Request.MessageId);
        RequireFits(draft.Request.Source.PostingPurpose, draft.Request.PostingRuleVersion,
            draft.Request.Lines.Select(line => line.Amount!.Value));
        if (draft.Version != 1 || draft.Rule.Mirror.CompanyId != draft.CompanyId
            || !string.Equals(draft.Rule.PostingPurpose, draft.Request.Source.PostingPurpose, StringComparison.Ordinal)
            || !string.Equals(draft.Rule.Version, draft.Request.PostingRuleVersion, StringComparison.Ordinal)
            || draft.Request.Lines.Count < 2
            || draft.Request.Lines.Any(line => line.MappingKey.Length > IdentityMaximumLength))
        {
            throw Refused(NormalJournalDraftStoreCodesV1.ContentConflict,
                "Normal fiş taslağı onaylı plan/kural snapshot'ı ve kaynak içeriğiyle tutarlı değil.");
        }

        if (draft.Rule.Mirror.SourceVersion.Length > IdentityMaximumLength)
        {
            throw Refused(AccountingStoreCodes.ValueDoesNotFit, "Hesap planı sürümü kayıt alanına sığmıyor.");
        }
    }

    private async Task<NormalJournalDraftRecordV1> RestoreNormalDraftAsync(
        NormalJournalDraftRow row,
        CancellationToken cancellationToken)
    {
        var lines = await dbContext.NormalJournalDraftLines.AsNoTracking()
            .Where(line => line.DraftId == row.Id)
            .OrderBy(line => line.LineNumber)
            .ToListAsync(cancellationToken);
        return RestoreNormalDraft(row, lines);
    }

    private static NormalJournalDraftRecordV1 RestoreNormalDraft(
        NormalJournalDraftRow row,
        IReadOnlyList<NormalJournalDraftLineRow> lines)
    {
        if (lines.Count < 2 || lines.Select(line => line.LineNumber).Where((number, index) => number != index + 1).Any())
        {
            throw new InvalidOperationException("Kayıtlı normal fiş taslağının satır sırası geçersiz.");
        }

        var request = new PostingRequest(
            row.Consumer,
            row.MessageId,
            row.Id,
            new SourceReference(row.CompanyId, row.SourceId, row.PostingPurpose),
            row.PostingRuleVersion,
            row.CurrencyId,
            new PostingDates(row.TransactionDate, row.AccountingDate, row.ValueDate, row.RecordedAtUtc),
            Array.AsReadOnly(lines.Select(line => new ProposedLine(
                line.MappingKey,
                new AccountRef(row.CompanyId, line.AccountId),
                (PostingSide)line.Side,
                line.Amount)).ToArray()));
        return new NormalJournalDraftRecordV1(
            row.Id,
            row.CompanyId,
            row.Version,
            (NormalJournalDraftStatusV1)row.Status,
            row.PreparedByUserId,
            row.PreparedAtUtc,
            row.ApprovedByUserId,
            row.ApprovedAtUtc,
            row.PeriodId,
            row.PostedJournalId,
            row.AccountPlanSourceVersion,
            row.AccountPlanSourceSha256,
            row.AccountPlanApprovedByUserId,
            row.AccountPlanApprovedAtUtc,
            row.PostingRuleApprovedByUserId,
            row.PostingRuleApprovedAtUtc,
            request);
    }

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

    /// <summary>
    /// An opaque keyset cursor bound to the company, the list's ordering and (for journals) its period
    /// filter; any other cursor is refused (<see cref="AccountingStoreCodes.ListRequestInvalid"/>), never
    /// read as a position in another list.
    /// </summary>
    internal static class ListCursor
    {
        private const string PeriodOrdering = "periods:startsOn desc";
        private const string JournalOrdering = "journals:accountingDate desc, storedAtUtc desc, id desc";

        public static string EncodePeriod(Guid companyId, DateOnly startsOn) =>
            Encode(new Payload(companyId, PeriodOrdering, null, startsOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), null, null));

        public static DateOnly DecodePeriod(string cursor, Guid companyId)
        {
            var payload = Decode(cursor, companyId, PeriodOrdering, periodId: null);
            return DateOnly.TryParseExact(payload.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date
                : throw Invalid();
        }

        public static string EncodeJournal(Guid companyId, Guid? periodId, DateOnly accountingDate, DateTimeOffset storedAtUtc, Guid id) =>
            Encode(new Payload(companyId, JournalOrdering, periodId,
                accountingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                storedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture), id));

        public static (DateOnly AccountingDate, DateTimeOffset StoredAtUtc, Guid Id) DecodeJournal(string cursor, Guid companyId,
            Guid? periodId)
        {
            var payload = Decode(cursor, companyId, JournalOrdering, periodId);
            if (!DateOnly.TryParseExact(payload.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                || !DateTimeOffset.TryParseExact(payload.At, "O", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at)
                || payload.Id is not { } id || id == Guid.Empty)
            {
                throw Invalid();
            }

            return (date, at, id);
        }

        private static string Encode(Payload payload) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, Json)))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static Payload Decode(string cursor, Guid companyId, string ordering, Guid? periodId)
        {
            Payload? payload;
            try
            {
                var padded = cursor.Replace('-', '+').Replace('_', '/');
                padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
                payload = JsonSerializer.Deserialize<Payload>(Encoding.UTF8.GetString(Convert.FromBase64String(padded)), Json);
            }
            catch (Exception exception) when (exception is FormatException or JsonException or ArgumentException)
            {
                throw Invalid(exception);
            }

            return payload is not null && payload.Company == companyId && payload.PeriodId == periodId
                && string.Equals(payload.Ordering, ordering, StringComparison.Ordinal)
                ? payload
                : throw Invalid();
        }

        private static AccountingStoreException Invalid(Exception? inner = null) =>
            Refused(AccountingStoreCodes.ListRequestInvalid, "Liste imleci bu şirketin ve bu listenin imleci değil.", inner);

        private sealed record Payload(Guid Company, string Ordering, Guid? PeriodId, string? Date, string? At, Guid? Id);
    }
}
