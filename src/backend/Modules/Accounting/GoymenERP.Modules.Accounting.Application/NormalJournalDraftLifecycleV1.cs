using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Domain;
using GoymenERP.Modules.Accounting.Domain.Journals;
using GoymenERP.Modules.Accounting.Domain.Posting;

namespace GoymenERP.Modules.Accounting.Application;

/// <summary>The minimum immutable owner snapshot needed by the approval policy.</summary>
public interface INormalJournalDraftApprovalSnapshotV1
{
    Guid Id { get; }
    Guid CompanyId { get; }
    long Version { get; }
    Guid PreparedByUserId { get; }
}

public enum NormalJournalDraftStatusV1
{
    Draft = 1,
    Approved = 2,
}

/// <summary>A durable draft snapshot. It is not a posted journal; PostedJournalId exists only after approval.</summary>
public sealed record NormalJournalDraftRecordV1(
    Guid Id,
    Guid CompanyId,
    long Version,
    NormalJournalDraftStatusV1 Status,
    Guid PreparedByUserId,
    DateTimeOffset PreparedAtUtc,
    Guid? ApprovedByUserId,
    DateTimeOffset? ApprovedAtUtc,
    Guid? PeriodId,
    Guid? PostedJournalId,
    string AccountPlanSourceVersion,
    string AccountPlanSourceSha256,
    Guid AccountPlanApprovedByUserId,
    DateTimeOffset AccountPlanApprovedAtUtc,
    Guid PostingRuleApprovedByUserId,
    DateTimeOffset PostingRuleApprovedAtUtc,
    PostingRequest Request) : INormalJournalDraftApprovalSnapshotV1;

public sealed record NormalJournalDraftLineViewV1(
    int LineNumber, string MappingKey, string Side, Guid AccountId, string Amount);

public sealed record NormalJournalDraftViewV1(
    Guid DraftId,
    Guid CompanyId,
    long Version,
    string Status,
    Guid PreparedByUserId,
    DateTimeOffset PreparedAtUtc,
    Guid? ApprovedByUserId,
    DateTimeOffset? ApprovedAtUtc,
    Guid? PeriodId,
    Guid? PostedJournalId,
    Guid SourceId,
    string PostingPurpose,
    string PostingRuleVersion,
    string AccountPlanSourceVersion,
    string AccountPlanSourceSha256,
    Guid CurrencyId,
    DateOnly TransactionDate,
    DateOnly AccountingDate,
    DateOnly? ValueDate,
    IReadOnlyList<NormalJournalDraftLineViewV1> Lines);

public static class NormalJournalCommandNamesV1
{
    public const string CreateDraft = "accounting.normal-journal-draft.create.v1";
    public const string ApproveDraft = "accounting.normal-journal-draft.approve.v1";
}

public static class NormalJournalDraftStoreCodesV1
{
    public const string NotFound = "accounting_normal_journal_draft_not_found";
    public const string ContentConflict = "accounting_normal_journal_draft_content_conflict";
    public const string NotPending = "accounting_normal_journal_draft_not_pending";
    public const string PostingConflict = "accounting_normal_journal_draft_posting_conflict";
}

public interface IAccountingNormalJournalDraftStore
{
    Task<(NormalJournalDraftRecordV1 Record, AccountingWriteOutcome Outcome)> CreateNormalDraftAsync(
        NormalJournalDraftV1 draft,
        Func<CancellationToken, Task> freshAuthorityFence,
        AccountingCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    Task<NormalJournalDraftRecordV1?> FindNormalDraftAsync(
        Guid draftId,
        CancellationToken cancellationToken = default);

    Task<NormalJournalDraftRecordV1> ApproveAndPostNormalDraftAsync(
        Guid draftId,
        long expectedVersion,
        Guid periodId,
        Guid approvedByUserId,
        Func<CancellationToken, Task> freshAuthorityFence,
        AccountingCommandMetadata metadata,
        CancellationToken cancellationToken = default);
}

/// <summary>Signals that the final authority fence failed. It is never persisted as a successful command.</summary>
public sealed class NormalJournalAuthorityDeniedException()
    : Exception("The normal journal approval authority is no longer valid.")
{
}

public interface INormalJournalDraftLifecycleV1
{
    Task<AccountingApiResult<NormalJournalDraftViewV1>> CreateAsync(
        NormalJournalDraftV1? draft, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<AccountingApiResult<NormalJournalDraftViewV1>> ReadAsync(
        Guid draftId, CancellationToken cancellationToken = default);

    Task<AccountingApiResult<NormalJournalDraftViewV1>> ApproveAsync(
        Guid draftId, long expectedVersion, Guid periodId, string idempotencyKey,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The non-HTTP owner handler. A trusted adapter supplies a previously prepared draft; this service
/// binds actor, exact permission, ADR-011 replay, approval eligibility and the store's final fence.
/// It registers or grants no permission and imports no account-plan file.
/// </summary>
public sealed class NormalJournalDraftServiceV1(
    IAccountingNormalJournalDraftStore drafts,
    IAccountingCommandLog commands,
    NormalJournalDraftApprovalPolicyV1 approvalPolicy,
    NormalJournalPermissionBindingV1 permissions,
    IErpAuthorityRevalidationService authority,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    ICorrelationContext correlation) : INormalJournalDraftLifecycleV1
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<AccountingApiResult<NormalJournalDraftViewV1>> CreateAsync(
        NormalJournalDraftV1? draft,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (draft is null || draft.Id == Guid.Empty || draft.Version != 1 || draft.PreparedByUserId == Guid.Empty)
        {
            return Task.FromResult(Invalid("draft"));
        }

        return RunAsync(
            permissions.CreatePermission,
            idempotencyKey,
            NormalJournalCommandNamesV1.CreateDraft,
            Canonical(draft),
            AccountingApiStatus.Created,
            async (metadata, actor) =>
            {
                if (draft.PreparedByUserId != actor)
                {
                    return Denied();
                }

                var (record, outcome) = await drafts.CreateNormalDraftAsync(
                    draft,
                    token => FenceAsync(permissions.CreatePermission, actor, token),
                    metadata,
                    cancellationToken);
                return new AccountingApiResult<NormalJournalDraftViewV1>(
                    AccountingApiStatus.Created,
                    View(record),
                    outcome == AccountingWriteOutcome.Replayed,
                    null);
            },
            stored => View((NormalJournalDraftRecordV1)stored),
            cancellationToken);
    }

    public Task<AccountingApiResult<NormalJournalDraftViewV1>> ApproveAsync(
        Guid draftId,
        long expectedVersion,
        Guid periodId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (draftId == Guid.Empty || expectedVersion <= 0 || periodId == Guid.Empty)
        {
            return Task.FromResult(Invalid("draft"));
        }

        return RunAsync(
            permissions.ApprovePermission,
            idempotencyKey,
            NormalJournalCommandNamesV1.ApproveDraft,
            new { draftId, expectedVersion, periodId },
            AccountingApiStatus.Ok,
            async (metadata, actor) =>
            {
                var snapshot = await drafts.FindNormalDraftAsync(draftId, cancellationToken);
                var eligible = await approvalPolicy.CheckAsync(snapshot, expectedVersion, cancellationToken);
                if (eligible.Status != AccountingApiStatus.Ok)
                {
                    return new AccountingApiResult<NormalJournalDraftViewV1>(eligible.Status, null, false,
                        eligible.Code, eligible.Field, eligible.ExpectedVersion, eligible.CurrentVersion);
                }

                var record = await drafts.ApproveAndPostNormalDraftAsync(
                    draftId,
                    expectedVersion,
                    periodId,
                    actor,
                    token => FenceAsync(permissions.ApprovePermission, actor, token),
                    metadata,
                    cancellationToken);
                return new AccountingApiResult<NormalJournalDraftViewV1>(AccountingApiStatus.Ok, View(record), false, null);
            },
            stored => View((NormalJournalDraftRecordV1)stored),
            cancellationToken);
    }

    public async Task<AccountingApiResult<NormalJournalDraftViewV1>> ReadAsync(
        Guid draftId,
        CancellationToken cancellationToken = default)
    {
        if (draftId == Guid.Empty)
        {
            return Invalid("draftId");
        }

        if (!HasContext() || ActorGuid() is not { } actor)
        {
            return Denied();
        }

        var record = await drafts.FindNormalDraftAsync(draftId, cancellationToken);
        if (record is null || record.CompanyId != companyContext.CompanyId)
        {
            return new AccountingApiResult<NormalJournalDraftViewV1>(
                AccountingApiStatus.NotFound, null, false, null);
        }

        var mayReadOwn = record.PreparedByUserId == actor
            && await HasPermissionAsync(permissions.CreatePermission, actor, cancellationToken);
        var mayApprove = await HasPermissionAsync(permissions.ApprovePermission, actor, cancellationToken);
        return mayReadOwn || mayApprove
            ? new AccountingApiResult<NormalJournalDraftViewV1>(AccountingApiStatus.Ok, View(record), false, null)
            : Denied();
    }

    private async Task<AccountingApiResult<T>> RunAsync<T>(
        string permission,
        string idempotencyKey,
        string commandName,
        object canonicalRequest,
        AccountingApiStatus successStatus,
        Func<AccountingCommandMetadata, Guid, Task<AccountingApiResult<T>>> write,
        Func<object, T> view,
        CancellationToken cancellationToken)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return new AccountingApiResult<T>(AccountingApiStatus.Invalid, null, false,
                AccountingApiCodes.RequestInvalid, "idempotencyKey");
        }

        if (!HasContext() || ActorGuid() is not { } actor)
        {
            return Denied<T>();
        }

        var company = companyContext.CompanyId;
        var scope = await authority.RevalidatePermissionScopeAsync(permission, company, cancellationToken);
        if (!scope.IsGranted || !scope.IsCompanyWide || !HasContext())
        {
            return Denied<T>();
        }

        var actorId = currentUser.UserId!;
        var keyHash = AccountingApiService.Sha256(idempotencyKey.Trim());
        var requestHash = AccountingApiService.Sha256(commandName + "|" + JsonSerializer.Serialize(canonicalRequest, Json));
        if (await commands.FindCommandAsync(commandName, keyHash, cancellationToken) is { } prior)
        {
            return Answer<T>(prior, actorId, requestHash);
        }

        var second = await authority.RevalidatePermissionScopeAsync(permission, company, cancellationToken);
        if (!second.IsGranted || !second.IsCompanyWide || !HasContext()
            || !string.Equals(currentUser.UserId, actorId, StringComparison.Ordinal))
        {
            return Denied<T>();
        }

        AccountingApiResult<T>? completed = null;
        AccountingCommandRecord Complete(object stored)
        {
            completed = new AccountingApiResult<T>(successStatus, view(stored), false, null);
            return Record(commandName, keyHash, actorId, requestHash, completed);
        }

        var metadata = new AccountingCommandMetadata(
            actorId,
            permission,
            [.. scope.RoleCodes.Order(StringComparer.Ordinal)],
            correlation.CorrelationId,
            correlation.CausationId,
            idempotencyKey,
            correlation.SourceChannel,
            correlation.ClientIpAddress,
            correlation.UserAgent,
            Complete);

        AccountingApiResult<T> result;
        try
        {
            result = await write(metadata, actor);
        }
        catch (NormalJournalAuthorityDeniedException)
        {
            return Denied<T>();
        }
        catch (AccountingVersionConflictException exception)
        {
            result = new AccountingApiResult<T>(AccountingApiStatus.Conflict, null, false,
                AccountingApiCodes.VersionConflict, "expectedVersion", exception.ExpectedVersion, exception.CurrentVersion);
        }
        catch (AccountingDomainException exception)
        {
            result = new AccountingApiResult<T>(AccountingApiStatus.Unprocessable, null, false, exception.Code);
        }
        catch (AccountingStoreException exception) when (exception.Code == AccountingStoreCodes.ConcurrentChange)
        {
            return await commands.FindCommandAsync(commandName, keyHash, cancellationToken) is { } winner
                ? Answer<T>(winner, actorId, requestHash)
                : new AccountingApiResult<T>(AccountingApiStatus.Conflict, null, false, exception.Code);
        }
        catch (AccountingStoreException exception)
        {
            result = exception.Code switch
            {
                NormalJournalDraftStoreCodesV1.NotFound or AccountingStoreCodes.PeriodNotFound =>
                    new AccountingApiResult<T>(AccountingApiStatus.NotFound, null, false, null),
                AccountingStoreCodes.CompanyContextMismatch => Denied<T>(),
                NormalJournalDraftStoreCodesV1.ContentConflict or NormalJournalDraftStoreCodesV1.NotPending
                    or NormalJournalDraftStoreCodesV1.PostingConflict =>
                    new AccountingApiResult<T>(AccountingApiStatus.Conflict, null, false, exception.Code),
                _ => new AccountingApiResult<T>(AccountingApiStatus.Unprocessable, null, false, exception.Code),
            };
        }
        catch (ArgumentException exception)
        {
            return new AccountingApiResult<T>(AccountingApiStatus.Invalid, null, false,
                AccountingApiCodes.RequestInvalid, exception.ParamName ?? "request");
        }

        if (completed is not null && result.Status == successStatus)
        {
            return result;
        }

        var terminal = result.Status == AccountingApiStatus.NotFound
            || (result.Status == AccountingApiStatus.Conflict && result.Code == AccountingApiCodes.VersionConflict)
            || result.Status == successStatus;
        if (!terminal)
        {
            return result;
        }

        try
        {
            await commands.RecordCommandAsync(Record(commandName, keyHash, actorId, requestHash, result), cancellationToken);
            return result;
        }
        catch (AccountingStoreException exception) when (exception.Code == AccountingStoreCodes.ConcurrentChange)
        {
            return await commands.FindCommandAsync(commandName, keyHash, cancellationToken) is { } winner
                ? Answer<T>(winner, actorId, requestHash)
                : result;
        }
    }

    private async Task FenceAsync(string permission, Guid actor, CancellationToken cancellationToken)
    {
        var company = companyContext.CompanyId;
        var scope = await authority.RevalidatePermissionScopeAsync(permission, company, cancellationToken);
        if (!scope.IsGranted || !scope.IsCompanyWide || !HasContext()
            || !Guid.TryParse(currentUser.UserId, out var currentActor) || currentActor != actor)
        {
            throw new NormalJournalAuthorityDeniedException();
        }
    }

    private async Task<bool> HasPermissionAsync(string permission, Guid actor, CancellationToken cancellationToken)
    {
        var company = companyContext.CompanyId;
        var scope = await authority.RevalidatePermissionScopeAsync(permission, company, cancellationToken);
        return scope.IsGranted && scope.IsCompanyWide && HasContext()
            && Guid.TryParse(currentUser.UserId, out var currentActor) && currentActor == actor;
    }

    private static object Canonical(NormalJournalDraftV1 draft) => new
    {
        draft.Id,
        draft.CompanyId,
        draft.Version,
        draft.PreparedByUserId,
        mirror = new
        {
            draft.Rule.Mirror.SourceVersion,
            sha256 = draft.Rule.Mirror.SourceSha256.ToUpperInvariant(),
            draft.Rule.Mirror.ApprovedByUserId,
            approvedAt = Utc(draft.Rule.Mirror.ApprovedAtUtc),
        },
        rule = new
        {
            draft.Rule.Version,
            draft.Rule.ApprovedByUserId,
            approvedAt = Utc(draft.Rule.ApprovedAtUtc),
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
            transactionDate = Date(draft.Request.Dates.TransactionDate),
            accountingDate = Date(draft.Request.Dates.AccountingDate),
            valueDate = draft.Request.Dates.ValueDate is { } valueDate ? Date(valueDate) : null,
            recordedAt = Utc(draft.Request.Dates.RecordedAtUtc),
            lines = draft.Request.Lines.Select((line, index) => new
                {
                    lineNumber = index + 1,
                    line.MappingKey,
                    accountId = line.Account!.Value.AccountId,
                    side = (int)line.Side,
                    amount = Text(line.Amount!.Value),
                })
                .ToArray(),
        },
    };

    private static NormalJournalDraftViewV1 View(NormalJournalDraftRecordV1 record) => new(
        record.Id,
        record.CompanyId,
        record.Version,
        record.Status.ToString(),
        record.PreparedByUserId,
        record.PreparedAtUtc,
        record.ApprovedByUserId,
        record.ApprovedAtUtc,
        record.PeriodId,
        record.PostedJournalId,
        record.Request.Source.SourceId,
        record.Request.Source.PostingPurpose,
        record.Request.PostingRuleVersion,
        record.AccountPlanSourceVersion,
        record.AccountPlanSourceSha256,
        record.Request.CurrencyId,
        record.Request.Dates.TransactionDate,
        record.Request.Dates.AccountingDate,
        record.Request.Dates.ValueDate,
        [.. record.Request.Lines.Select((line, index) => new NormalJournalDraftLineViewV1(
            index + 1,
            line.MappingKey,
            line.Side.ToString(),
            line.Account!.Value.AccountId,
            Text(line.Amount!.Value)))]);

    private static AccountingApiResult<T> Answer<T>(AccountingCommandRecord prior, string actorId, string requestHash)
        where T : class
    {
        if (!string.Equals(prior.ActorId, actorId, StringComparison.Ordinal)
            || !string.Equals(prior.RequestHash, requestHash, StringComparison.Ordinal))
        {
            return new AccountingApiResult<T>(AccountingApiStatus.Conflict, null, false, AccountingApiCodes.IdempotencyConflict);
        }

        var stored = JsonSerializer.Deserialize<AccountingApiResult<T>>(prior.ResultJson, Json)
            ?? throw new InvalidOperationException("Stored normal-journal command result could not be read.");
        return stored with { IsReplay = true, OriginalCorrelationId = prior.OriginalCorrelationId };
    }

    private AccountingCommandRecord Record<T>(
        string commandName,
        string keyHash,
        string actorId,
        string requestHash,
        AccountingApiResult<T> result)
        where T : class =>
        new(
            commandName,
            keyHash,
            actorId,
            requestHash,
            JsonSerializer.Serialize(result with { IsReplay = false, OriginalCorrelationId = null }, Json),
            correlation.CorrelationId);

    private bool HasContext() =>
        companyContext.IsResolved && companyContext.CompanyId != Guid.Empty && currentUser.IsAuthenticated
        && !string.IsNullOrWhiteSpace(currentUser.UserId) && currentUser.CanAccessCompany(companyContext.CompanyId)
        && correlation.IsResolved;

    private Guid? ActorGuid() =>
        Guid.TryParse(currentUser.UserId, out var actor) && actor != Guid.Empty ? actor : null;

    private static AccountingApiResult<NormalJournalDraftViewV1> Invalid(string field) =>
        new(AccountingApiStatus.Invalid, null, false, AccountingApiCodes.RequestInvalid, field);

    private static AccountingApiResult<NormalJournalDraftViewV1> Denied() =>
        new(AccountingApiStatus.Denied, null, false, AccountingApiCodes.AuthorityDenied);

    private static AccountingApiResult<T> Denied<T>() where T : class =>
        new(AccountingApiStatus.Denied, null, false, AccountingApiCodes.AuthorityDenied);

    private static string Date(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Utc(DateTimeOffset value) => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static string Text(decimal value) =>
        (value / 1.0000000000000000000000000000m).ToString(CultureInfo.InvariantCulture);
}
