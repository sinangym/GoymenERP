using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Domain;
using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Accounting.Domain.Periods;
using GoymenERP.Modules.Accounting.Domain.Posting;

namespace GoymenERP.Modules.Accounting.Application;

// ACC-V0 (card approved 2026-09-19): the authorized Accounting surface — periods (propose, approve by
// someone else, close), the journal read (M-04, read-only) and the two queues (M-01). No voucher is
// created, approved or reversed here (ACC-V1 waits on NEEDS-BUSINESS 5740501573 / 5740501655 and
// OPEN-009). Amounts travel as lossless invariant text, as the other modules' money and mass reads do.

public sealed record ProposePeriodRequestV1(Guid PeriodId, DateOnly StartsOn, DateOnly EndsOn);

public sealed record PeriodCommandRequestV1(long ExpectedVersion);

/// <summary>
/// One period as the caller may see it. <see cref="IsOwnProposal"/> is true when the caller prepared it:
/// the screen then offers no approval (Ö4), and the server refuses one anyway.
/// </summary>
public sealed record PeriodViewV1(
    Guid PeriodId, Guid CompanyId, DateOnly StartsOn, DateOnly EndsOn, string Status, long Version,
    Guid PreparedByUserId, DateTimeOffset PreparedAtUtc, Guid? ApprovedByUserId, DateTimeOffset? ApprovedAtUtc,
    Guid? ClosedByUserId, DateTimeOffset? ClosedAtUtc, bool IsOwnProposal);

public sealed record PeriodPageV1(IReadOnlyList<PeriodViewV1> Items, string? NextCursor);

/// <summary>
/// A journal as recorded (M-04, read-only): its source link (source id, posting purpose, rule version),
/// its kind and link, its dates and its balanced totals. It is a derived accounting record of a source
/// fact, never a second money movement (Master §620).
/// </summary>
public sealed record JournalSummaryV1(
    Guid JournalId, string Kind, Guid SourceId, string PostingPurpose, string PostingRuleVersion, Guid CurrencyId,
    DateOnly TransactionDate, DateOnly AccountingDate, DateOnly? ValueDate, Guid PeriodId, Guid? LinkedJournalId,
    string TotalDebit, string TotalCredit, int LineCount, DateTimeOffset StoredAtUtc);

public sealed record JournalLineViewV1(int LineNumber, Guid AccountId, string Debit, string Credit);

public sealed record JournalDetailV1(JournalSummaryV1 Journal, IReadOnlyList<JournalLineViewV1> Lines);

public sealed record JournalPageV1(IReadOnlyList<JournalSummaryV1> Items, string? NextCursor);

/// <summary>A fact whose posting waits for an account mapping (M-01): never posted to a guessed account.</summary>
public sealed record PostingExceptionViewV1(
    Guid SourceId, string PostingPurpose, string PostingRuleVersion, IReadOnlyList<string> MissingMappingKeys,
    string FirstMessageId);

/// <summary>A fact whose posting waits for an amount (M-01): an unknown amount is never a zero.</summary>
public sealed record PendingValuationViewV1(
    Guid SourceId, string PostingPurpose, string PostingRuleVersion, int LinesAwaitingAmount, string FirstMessageId);

public sealed record PostingQueuesV1(IReadOnlyList<PostingExceptionViewV1> Exceptions, IReadOnlyList<PendingValuationViewV1> Pending);

/// <summary>How a request ended, before HTTP: the endpoint maps it and adds nothing of its own.</summary>
public enum AccountingApiStatus
{
    Created = 1,
    Ok = 2,
    Invalid = 3,
    Denied = 4,
    NotFound = 5,
    Conflict = 6,
    Unprocessable = 7,
    Unavailable = 8,
}

/// <summary>
/// One answer. <see cref="IsReplay"/> is true when this is a stored answer; an idempotency replay
/// (ADR-011 §4) also carries the first attempt's <see cref="OriginalCorrelationId"/>.
/// </summary>
public sealed record AccountingApiResult<T>(
    AccountingApiStatus Status, T? Resource, bool IsReplay, string? Code, string? Field = null,
    long? ExpectedVersion = null, long? CurrentVersion = null, string? OriginalCorrelationId = null)
    where T : class;

/// <summary>Codes the Accounting surface adds; everything else is the domain's, the store's or Settings'.</summary>
public static class AccountingApiCodes
{
    /// <summary>The request is malformed: a missing id or version, a bad date pair, key, cursor or page size.</summary>
    public const string RequestInvalid = "accounting_request_invalid";

    /// <summary>The server's actor or authority for this company is not (or no longer) there.</summary>
    public const string AuthorityDenied = "accounting_authority_denied";

    /// <summary>The caller named a period version that is not the stored one (ADR-011 §7: recorded under the key).</summary>
    public const string VersionConflict = "accounting_version_conflict";

    /// <summary>ADR-011 §3: the same key for this command was used with another request or by another actor.</summary>
    public const string IdempotencyConflict = "idempotency_conflict";
}

/// <summary>
/// The Accounting owner's permission names (ACC-V0). Each read is its own narrow name (Sinan's persona
/// rule, #66 5738113623 §4): a period reader does not read journals, and neither reads the queues. The
/// central registry (ErpPermissions) registers exactly these strings; no grant is seeded here.
/// </summary>
public static class AccountingPermissionManifestV1
{
    public const string PeriodsRead = "accounting.periods.read";
    public const string PeriodsPropose = "accounting.periods.propose";

    /// <summary>Ö4: approving a period opening — a single authorised person other than its preparer.</summary>
    public const string PeriodsApprove = "accounting.periods.approve";

    /// <summary>Ö5: closing an open period is one authorised act.</summary>
    public const string PeriodsClose = "accounting.periods.close";

    public const string JournalsRead = "accounting.journals.read";
    public const string PostingQueuesRead = "accounting.posting-queues.read";

    public static IReadOnlyList<string> All { get; } =
        [PeriodsRead, PeriodsPropose, PeriodsApprove, PeriodsClose, JournalsRead, PostingQueuesRead];
}

/// <summary>The versioned command names of the ADR-011 log: one key is one attempt per command.</summary>
public static class AccountingCommandNames
{
    public const string ProposePeriod = "accounting.period.propose.v1";
    public const string ApprovePeriod = "accounting.period.approve.v1";
    public const string ClosePeriod = "accounting.period.close.v1";
}

/// <summary>
/// ACC-V0: the period commands and the journal / queue reads, for the server-resolved company.
/// <list type="bullet">
/// <item><b>Company:</b> only the company context; no request carries a company, and another company's
/// period or journal is the store's "not found" (a code-less 404).</item>
/// <item><b>Ö4:</b> a period is proposed by one person and approved by another; the preparer's approval is
/// refused (403 <see cref="AccountingErrorCodes.PeriodSelfApproval"/>) whatever grants they hold. Closing
/// is one authorised act (Ö5).</item>
/// <item><b>Idempotency (ADR-011):</b> every write is one command attempt in the module's own log, unique
/// per (company, command name, uppercase SHA-256 of the trimmed key); the raw key is never stored. After
/// the fresh authority check, the same key, request and actor answers the stored result with
/// <c>Idempotency-Replayed</c> and the first correlation; another request or actor is 409
/// <see cref="AccountingApiCodes.IdempotencyConflict"/>, disclosing nothing. A success is recorded in the
/// same save as the change and its audit event (§6); a terminal not-found or version conflict is recorded
/// on its own (§7); two attempts racing on one key leave one record, and the loser answers from it (§8).
/// The Manufacturing command surface's shape.</item>
/// <item><b>Authority (#276):</b> the endpoint's policy answers first; the original actor and company are
/// captured with the narrow permission, every awaited command/resource read is fenced again before its
/// result is used, and terminal persistence has its own final fence. Only a company-wide grant writes;
/// its role codes go into the audit envelope.</item>
/// </list>
/// It decides no accounting, period or permission policy: no chart of accounts, no voucher, no reopen,
/// no withdraw or reject of a proposal (NEEDS-BUSINESS #66 5742097485).
/// </summary>
public sealed class AccountingApiService(
    IAccountingPeriodStore periods,
    IAccountingPostingStore postings,
    IAccountingCommandLog commands,
    IErpAuthorityRevalidationService authority,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    ICorrelationContext correlation)
{
    private const int DefaultPageSize = 50;
    private const int MaximumPageSize = 100;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ---- periods -------------------------------------------------------------------------------

    public Task<AccountingApiResult<PeriodViewV1>> ProposePeriodAsync(ProposePeriodRequestV1? request, string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (request is null || request.PeriodId == Guid.Empty || request.StartsOn == default || request.EndsOn == default)
        {
            return Task.FromResult(Invalid<PeriodViewV1>("period"));
        }

        if (request.EndsOn < request.StartsOn)
        {
            return Task.FromResult(Invalid<PeriodViewV1>("endsOn"));
        }

        var canonical = new
        {
            periodId = request.PeriodId,
            startsOn = Date(request.StartsOn),
            endsOn = Date(request.EndsOn),
        };
        return RunAsync<PeriodViewV1>(AccountingPermissionManifestV1.PeriodsPropose, idempotencyKey,
            AccountingCommandNames.ProposePeriod, canonical, AccountingApiStatus.Created, async (metadata, actor) =>
            {
                var (record, outcome) = await periods.ProposePeriodAsync(request.PeriodId, request.StartsOn, request.EndsOn,
                    actor, metadata, cancellationToken);
                return new AccountingApiResult<PeriodViewV1>(AccountingApiStatus.Created, PeriodView(record, actor),
                    outcome == AccountingWriteOutcome.Replayed, null);
            }, (stored, actor) => PeriodView((AccountingPeriodRecord)stored, actor), cancellationToken);
    }

    public Task<AccountingApiResult<PeriodViewV1>> ApprovePeriodAsync(Guid periodId, PeriodCommandRequestV1? request,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        if (periodId == Guid.Empty || request is null || request.ExpectedVersion <= 0)
        {
            return Task.FromResult(Invalid<PeriodViewV1>("expectedVersion"));
        }

        return RunAsync<PeriodViewV1>(AccountingPermissionManifestV1.PeriodsApprove, idempotencyKey,
            AccountingCommandNames.ApprovePeriod, new { periodId, expectedVersion = request.ExpectedVersion },
            AccountingApiStatus.Ok, async (metadata, actor) =>
            {
                var record = await periods.ApprovePeriodAsync(periodId, request.ExpectedVersion, actor, metadata, cancellationToken);
                return new AccountingApiResult<PeriodViewV1>(AccountingApiStatus.Ok, PeriodView(record, actor), false, null);
            }, (stored, actor) => PeriodView((AccountingPeriodRecord)stored, actor), cancellationToken);
    }

    public Task<AccountingApiResult<PeriodViewV1>> ClosePeriodAsync(Guid periodId, PeriodCommandRequestV1? request,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        if (periodId == Guid.Empty || request is null || request.ExpectedVersion <= 0)
        {
            return Task.FromResult(Invalid<PeriodViewV1>("expectedVersion"));
        }

        return RunAsync<PeriodViewV1>(AccountingPermissionManifestV1.PeriodsClose, idempotencyKey,
            AccountingCommandNames.ClosePeriod, new { periodId, expectedVersion = request.ExpectedVersion },
            AccountingApiStatus.Ok, async (metadata, actor) =>
            {
                var record = await periods.ClosePeriodAsync(periodId, request.ExpectedVersion, actor, metadata, cancellationToken);
                return new AccountingApiResult<PeriodViewV1>(AccountingApiStatus.Ok, PeriodView(record, actor), false, null);
            }, (stored, actor) => PeriodView((AccountingPeriodRecord)stored, actor), cancellationToken);
    }

    public async Task<AccountingApiResult<PeriodPageV1>> ListPeriodsAsync(int? pageSize, string? cursor,
        CancellationToken cancellationToken)
    {
        var scope = await RequireAsync(AccountingPermissionManifestV1.PeriodsRead, cancellationToken);
        if (scope is null)
        {
            return Denied<PeriodPageV1>();
        }

        if (PageSize(pageSize) is not { } size)
        {
            return Invalid<PeriodPageV1>("pageSize");
        }

        try
        {
            var page = await GuardedAsync(scope, AccountingPermissionManifestV1.PeriodsRead,
                () => periods.ListPeriodsAsync(size, cursor, cancellationToken), cancellationToken);
            return new AccountingApiResult<PeriodPageV1>(AccountingApiStatus.Ok,
                new PeriodPageV1([.. page.Items.Select(record => PeriodView(record, scope.ActorGuid))], page.NextCursor), false, null);
        }
        catch (FreshAuthorityDeniedException)
        {
            return Denied<PeriodPageV1>();
        }
        catch (AccountingStoreException exception) when (exception.Code == AccountingStoreCodes.ListRequestInvalid)
        {
            return Invalid<PeriodPageV1>("cursor");
        }
    }

    public async Task<AccountingApiResult<PeriodViewV1>> GetPeriodAsync(Guid periodId, CancellationToken cancellationToken)
    {
        var scope = await RequireAsync(AccountingPermissionManifestV1.PeriodsRead, cancellationToken);
        if (scope is null)
        {
            return Denied<PeriodViewV1>();
        }

        try
        {
            var record = periodId == Guid.Empty
                ? null
                : await GuardedAsync(scope, AccountingPermissionManifestV1.PeriodsRead,
                    () => periods.FindPeriodAsync(periodId, cancellationToken), cancellationToken);
            return record is not null
                ? new AccountingApiResult<PeriodViewV1>(AccountingApiStatus.Ok, PeriodView(record, scope.ActorGuid), false, null)
                : NotFoundResult<PeriodViewV1>();
        }
        catch (FreshAuthorityDeniedException)
        {
            return Denied<PeriodViewV1>();
        }
    }

    // ---- journals and queues (read-only) --------------------------------------------------------

    public async Task<AccountingApiResult<JournalPageV1>> ListJournalsAsync(Guid? periodId, int? pageSize, string? cursor,
        CancellationToken cancellationToken)
    {
        var scope = await RequireAsync(AccountingPermissionManifestV1.JournalsRead, cancellationToken);
        if (scope is null)
        {
            return Denied<JournalPageV1>();
        }

        if (periodId == Guid.Empty)
        {
            return Invalid<JournalPageV1>("periodId");
        }

        if (PageSize(pageSize) is not { } size)
        {
            return Invalid<JournalPageV1>("pageSize");
        }

        try
        {
            var page = await GuardedAsync(scope, AccountingPermissionManifestV1.JournalsRead,
                () => postings.ListJournalsAsync(periodId, size, cursor, cancellationToken), cancellationToken);
            return new AccountingApiResult<JournalPageV1>(AccountingApiStatus.Ok,
                new JournalPageV1([.. page.Items.Select(JournalSummary)], page.NextCursor), false, null);
        }
        catch (FreshAuthorityDeniedException)
        {
            return Denied<JournalPageV1>();
        }
        catch (AccountingStoreException exception) when (exception.Code == AccountingStoreCodes.ListRequestInvalid)
        {
            return Invalid<JournalPageV1>("cursor");
        }
    }

    public async Task<AccountingApiResult<JournalDetailV1>> GetJournalAsync(Guid journalId, CancellationToken cancellationToken)
    {
        var scope = await RequireAsync(AccountingPermissionManifestV1.JournalsRead, cancellationToken);
        if (scope is null)
        {
            return Denied<JournalDetailV1>();
        }

        try
        {
            var record = journalId == Guid.Empty
                ? null
                : await GuardedAsync(scope, AccountingPermissionManifestV1.JournalsRead,
                    () => postings.ReadJournalAsync(journalId, cancellationToken), cancellationToken);
            return record is not null
                ? new AccountingApiResult<JournalDetailV1>(AccountingApiStatus.Ok, JournalDetail(record), false, null)
                : NotFoundResult<JournalDetailV1>();
        }
        catch (FreshAuthorityDeniedException)
        {
            return Denied<JournalDetailV1>();
        }
    }

    public async Task<AccountingApiResult<PostingQueuesV1>> GetPostingQueuesAsync(CancellationToken cancellationToken)
    {
        var scope = await RequireAsync(AccountingPermissionManifestV1.PostingQueuesRead, cancellationToken);
        if (scope is null)
        {
            return Denied<PostingQueuesV1>();
        }

        try
        {
            var exceptions = await GuardedAsync(scope, AccountingPermissionManifestV1.PostingQueuesRead,
                () => postings.ReadOpenExceptionsAsync(cancellationToken), cancellationToken);
            var pending = await GuardedAsync(scope, AccountingPermissionManifestV1.PostingQueuesRead,
                () => postings.ReadPendingValuationsAsync(cancellationToken), cancellationToken);
            return new AccountingApiResult<PostingQueuesV1>(AccountingApiStatus.Ok, new PostingQueuesV1(
                [.. exceptions.Select(entry => new PostingExceptionViewV1(entry.Source.SourceId, entry.Source.PostingPurpose,
                    entry.PostingRuleVersion, [.. entry.MissingMappingKeys], entry.FirstMessageId))],
                [.. pending.Select(entry => new PendingValuationViewV1(entry.Source.SourceId, entry.Source.PostingPurpose,
                    entry.PostingRuleVersion, entry.LinesAwaitingAmount, entry.FirstMessageId))]), false, null);
        }
        catch (FreshAuthorityDeniedException)
        {
            return Denied<PostingQueuesV1>();
        }
    }

    // ---- the command run (ADR-011 §2–§8, #276) --------------------------------------------------

    private async Task<AccountingApiResult<T>> RunAsync<T>(string permission, string idempotencyKey, string commandName,
        object canonicalRequest, AccountingApiStatus successStatus,
        Func<AccountingCommandMetadata, Guid, Task<AccountingApiResult<T>>> write, Func<object, Guid, T> view,
        CancellationToken cancellationToken)
        where T : class
    {
        var scope = await RequireAsync(permission, cancellationToken);
        if (scope is null)
        {
            return Denied<T>();
        }

        var actor = scope.ActorGuid;
        var actorId = scope.ActorId;
        var keyHash = Sha256(idempotencyKey.Trim());
        var requestHash = Sha256(commandName + "|" + JsonSerializer.Serialize(canonicalRequest, Json));

        AccountingCommandRecord? prior;
        try
        {
            prior = await GuardedAsync(scope, permission,
                () => commands.FindCommandAsync(commandName, keyHash, cancellationToken), cancellationToken);
        }
        catch (FreshAuthorityDeniedException)
        {
            return Denied<T>();
        }

        // §4/§3: a stored answer for this command and key, after a post-read fence and before any other read.
        if (prior is not null)
        {
            return Answer<T>(prior, actorId, requestHash);
        }

        AccountingApiResult<T>? completed = null;
        AccountingCommandRecord Complete(object stored)
        {
            completed = new AccountingApiResult<T>(successStatus, view(stored, actor), false, null);
            return Record(commandName, keyHash, actorId, requestHash, completed);
        }

        var metadata = new AccountingCommandMetadata(actorId, permission,
            [.. scope.RoleCodes.Order(StringComparer.Ordinal)], correlation.CorrelationId, correlation.CausationId,
            idempotencyKey, correlation.SourceChannel, correlation.ClientIpAddress, correlation.UserAgent, Complete);
        AccountingApiResult<T> result;
        try
        {
            result = await write(metadata, actor);
        }
        catch (AccountingVersionConflictException exception)
        {
            result = new AccountingApiResult<T>(AccountingApiStatus.Conflict, null, false, AccountingApiCodes.VersionConflict,
                "expectedVersion", exception.ExpectedVersion, exception.CurrentVersion);
        }
        catch (AccountingDomainException exception) when (exception.Code == AccountingErrorCodes.PeriodSelfApproval)
        {
            // Ö4: the preparer is refused whatever grants they hold.
            result = new AccountingApiResult<T>(AccountingApiStatus.Denied, null, false, exception.Code, "approval");
        }
        catch (AccountingDomainException exception)
        {
            result = new AccountingApiResult<T>(AccountingApiStatus.Unprocessable, null, false, exception.Code);
        }
        catch (AccountingStoreException exception) when (exception.Code == AccountingStoreCodes.ConcurrentChange)
        {
            // §8: the same key's other attempt may have won; then its record is the answer.
            try
            {
                var winner = await GuardedAsync(scope, permission,
                    () => commands.FindCommandAsync(commandName, keyHash, cancellationToken), cancellationToken);
                return winner is not null
                    ? Answer<T>(winner, actorId, requestHash)
                    : new AccountingApiResult<T>(AccountingApiStatus.Conflict, null, false, exception.Code);
            }
            catch (FreshAuthorityDeniedException)
            {
                return Denied<T>();
            }
        }
        catch (AccountingStoreException exception)
        {
            result = exception.Code switch
            {
                // A not-found answer names no code: the same answer as for any unknown id or another company's.
                AccountingStoreCodes.PeriodNotFound => NotFoundResult<T>(),
                AccountingStoreCodes.CompanyContextMismatch => Denied<T>(),
                AccountingStoreCodes.PeriodConflict or AccountingStoreCodes.PeriodPreparerConflict
                    or AccountingStoreCodes.PeriodOverlap =>
                    new AccountingApiResult<T>(AccountingApiStatus.Conflict, null, false, exception.Code),
                _ => new AccountingApiResult<T>(AccountingApiStatus.Unprocessable, null, false, exception.Code),
            };
        }
        catch (ArgumentException exception)
        {
            return Invalid<T>(exception.ParamName ?? "request");
        }

        if (completed is not null && result.Status == successStatus)
        {
            // The success was recorded in the business save (§6).
            return result;
        }

        // §7: a terminal not-found or version conflict, and a success the store already held (R3: the same
        // period id, dates and preparer under a new key), are this key's answer from now on. Anything else
        // (validation, authority, rules, overlap, a lost race without a winner) is not recorded.
        var terminal = result.Status == AccountingApiStatus.NotFound
            || (result.Status == AccountingApiStatus.Conflict && result.Code == AccountingApiCodes.VersionConflict)
            || result.Status == successStatus;
        if (!terminal)
        {
            if (result.Status is AccountingApiStatus.Invalid or AccountingApiStatus.Unavailable)
            {
                return result;
            }

            try
            {
                var winner = await GuardedAsync(scope, permission,
                    () => commands.FindCommandAsync(commandName, keyHash, cancellationToken), cancellationToken);
                return winner is not null ? Answer<T>(winner, actorId, requestHash) : result;
            }
            catch (FreshAuthorityDeniedException)
            {
                return Denied<T>();
            }
        }

        try
        {
            await RequireStillAsync(scope, permission, cancellationToken);
            await commands.RecordCommandAsync(Record(commandName, keyHash, actorId, requestHash, result), cancellationToken);
            return result;
        }
        catch (FreshAuthorityDeniedException)
        {
            return Denied<T>();
        }
        catch (AccountingStoreException exception) when (exception.Code == AccountingStoreCodes.ConcurrentChange)
        {
            try
            {
                var winner = await GuardedAsync(scope, permission,
                    () => commands.FindCommandAsync(commandName, keyHash, cancellationToken), cancellationToken);
                return winner is not null ? Answer<T>(winner, actorId, requestHash) : result;
            }
            catch (FreshAuthorityDeniedException)
            {
                return Denied<T>();
            }
        }
    }

    /// <summary>§3/§4: the stored answer for the same request and actor; otherwise a conflict that discloses nothing.</summary>
    private static AccountingApiResult<T> Answer<T>(AccountingCommandRecord prior, string actorId, string requestHash)
        where T : class
    {
        if (!string.Equals(prior.ActorId, actorId, StringComparison.Ordinal)
            || !string.Equals(prior.RequestHash, requestHash, StringComparison.Ordinal))
        {
            return new AccountingApiResult<T>(AccountingApiStatus.Conflict, null, false, AccountingApiCodes.IdempotencyConflict);
        }

        var stored = JsonSerializer.Deserialize<AccountingApiResult<T>>(prior.ResultJson, Json)
            ?? throw new InvalidOperationException("Kayıtlı muhasebe komutu sonucu okunamadı.");
        return stored with { IsReplay = true, OriginalCorrelationId = prior.OriginalCorrelationId };
    }

    private AccountingCommandRecord Record<T>(string commandName, string keyHash, string actorId, string requestHash,
        AccountingApiResult<T> result)
        where T : class =>
        new(commandName, keyHash, actorId, requestHash,
            JsonSerializer.Serialize(result with { IsReplay = false, OriginalCorrelationId = null }, Json),
            correlation.CorrelationId);

    // ---- views and helpers ----------------------------------------------------------------------

    private sealed record AccessScope(Guid CompanyId, string ActorId, Guid ActorGuid, IReadOnlyCollection<string> RoleCodes);

    private sealed class FreshAuthorityDeniedException : Exception
    {
    }

    private async Task<AccessScope?> RequireAsync(string permission, CancellationToken cancellationToken)
    {
        if (!HasContext() || ActorGuid() is not { } actor)
        {
            return null;
        }

        var companyId = companyContext.CompanyId;
        var actorId = currentUser.UserId!;
        var granted = await authority.RevalidatePermissionScopeAsync(permission, companyId, cancellationToken);
        return granted.IsGranted && granted.IsCompanyWide && Matches(companyId, actorId)
            ? new AccessScope(companyId, actorId, actor, [.. granted.RoleCodes.Order(StringComparer.Ordinal)])
            : null;
    }

    private async Task RequireStillAsync(AccessScope expected, string permission, CancellationToken cancellationToken)
    {
        var current = await authority.RevalidatePermissionScopeAsync(permission, expected.CompanyId, cancellationToken);
        if (!current.IsGranted || !current.IsCompanyWide || !Matches(expected.CompanyId, expected.ActorId))
        {
            throw new FreshAuthorityDeniedException();
        }
    }

    private async Task<T> GuardedAsync<T>(AccessScope scope, string permission, Func<Task<T>> read,
        CancellationToken cancellationToken)
    {
        T result;
        try
        {
            result = await read();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            await RequireStillAsync(scope, permission, cancellationToken);
            throw;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await RequireStillAsync(scope, permission, cancellationToken);
        return result;
    }

    private bool Matches(Guid companyId, string actorId) =>
        currentUser.IsAuthenticated && string.Equals(currentUser.UserId, actorId, StringComparison.Ordinal)
        && companyContext.IsResolved && companyContext.CompanyId == companyId && currentUser.CanAccessCompany(companyId)
        && correlation.IsResolved;

    private bool HasContext() =>
        companyContext.IsResolved && companyContext.CompanyId != Guid.Empty && currentUser.IsAuthenticated
        && !string.IsNullOrWhiteSpace(currentUser.UserId) && currentUser.CanAccessCompany(companyContext.CompanyId)
        && correlation.IsResolved;

    private Guid? ActorGuid() => Guid.TryParse(currentUser.UserId, out var actor) && actor != Guid.Empty ? actor : null;

    private static int? PageSize(int? requested) =>
        requested switch
        {
            null => DefaultPageSize,
            >= 1 and <= MaximumPageSize => requested,
            _ => null,
        };

    /// <summary>ADR-011 §2: the uppercase hex SHA-256 of the UTF-8 text.</summary>
    internal static string Sha256(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    internal static PeriodViewV1 PeriodView(AccountingPeriodRecord record, Guid viewer)
    {
        var period = record.Period;
        return new PeriodViewV1(period.Id, period.CompanyId, period.StartsOn, period.EndsOn, period.Status.ToString(),
            record.Version, period.PreparedByUserId, period.PreparedAtUtc, period.ApprovedByUserId, period.ApprovedAtUtc,
            period.ClosedByUserId, period.ClosedAtUtc,
            IsOwnProposal: period.Status == AccountingPeriodStatus.Proposed && period.PreparedByUserId == viewer);
    }

    internal static JournalSummaryV1 JournalSummary(AccountingJournalRecord record)
    {
        var journal = record.Journal;
        return new JournalSummaryV1(journal.Id, journal.Kind.ToString(), journal.Source.SourceId, journal.Source.PostingPurpose,
            journal.PostingRuleVersion, journal.CurrencyId, journal.Dates.TransactionDate, journal.Dates.AccountingDate,
            journal.Dates.ValueDate, record.PeriodId, journal.LinkedJournalId,
            Text(journal.Lines.Sum(line => line.Debit)), Text(journal.Lines.Sum(line => line.Credit)), journal.Lines.Count,
            record.StoredAtUtc);
    }

    internal static JournalDetailV1 JournalDetail(AccountingJournalRecord record) =>
        new(JournalSummary(record),
            [.. record.Journal.Lines.Select((line, index) =>
                new JournalLineViewV1(index + 1, line.Account.AccountId, Text(line.Debit), Text(line.Credit)))]);

    /// <summary>Lossless invariant text without trailing zeros: 5.00 and 5 are both "5".</summary>
    internal static string Text(decimal value) =>
        (value / 1.0000000000000000000000000000m).ToString(CultureInfo.InvariantCulture);

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static AccountingApiResult<T> Invalid<T>(string field) where T : class =>
        new(AccountingApiStatus.Invalid, null, false, AccountingApiCodes.RequestInvalid, field);

    private static AccountingApiResult<T> Denied<T>() where T : class =>
        new(AccountingApiStatus.Denied, null, false, AccountingApiCodes.AuthorityDenied);

    private static AccountingApiResult<T> NotFoundResult<T>() where T : class =>
        new(AccountingApiStatus.NotFound, null, false, null);
}
