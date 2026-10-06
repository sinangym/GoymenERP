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

public sealed record NormalJournalPreparedCreateLineV2(Guid AccountId, PostingSide Side, decimal Amount);

public sealed record NormalJournalPreparedCreateCommandV2(
    Guid DraftId,
    Guid SourceId,
    DateOnly TransactionDate,
    DateOnly AccountingDate,
    DateOnly? ValueDate,
    string ExpectedPreparationVersion,
    IReadOnlyList<NormalJournalPreparedCreateLineV2> Lines);

public static class NormalJournalCommandNamesV1
{
    public const string CreateDraft = "accounting.normal-journal-draft.create.v1";
    public const string CreatePreparedDraft = "accounting.normal-journal-draft.create.v2";
    public const string ApproveDraft = "accounting.normal-journal-draft.approve.v1";
}

public static class NormalJournalDraftPreparationCodesV1
{
    public const string VersionConflict = "accounting_normal_journal_preparation_version_conflict";
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

    Task<AccountingApiResult<NormalJournalDraftViewV1>?> TryReplayPreparedCreateAsync(
        NormalJournalPreparedCreateCommandV2 command,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<AccountingApiResult<NormalJournalDraftViewV1>> CreatePreparedAsync(
        NormalJournalDraftV1? draft,
        NormalJournalPreparedCreateCommandV2 command,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

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

    public Task<AccountingApiResult<NormalJournalDraftViewV1>?> TryReplayPreparedCreateAsync(
        NormalJournalPreparedCreateCommandV2 command,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        TryReplayAsync(command, idempotencyKey, cancellationToken);

    public Task<AccountingApiResult<NormalJournalDraftViewV1>> CreatePreparedAsync(
        NormalJournalDraftV1? draft,
        NormalJournalPreparedCreateCommandV2 command,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (draft is null || !PreparedCommandMatchesDraft(command, draft))
        {
            return Task.FromResult(Invalid("draft"));
        }

        return RunAsync(
            permissions.CreatePermission,
            idempotencyKey,
            NormalJournalCommandNamesV1.CreatePreparedDraft,
            Canonical(command),
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

                NormalJournalDraftRecordV1 record;
                try
                {
                    record = await drafts.ApproveAndPostNormalDraftAsync(
                        draftId,
                        expectedVersion,
                        periodId,
                        actor,
                        token => FenceAsync(permissions.ApprovePermission, actor, token),
                        metadata,
                        cancellationToken);
                }
                catch (AccountingStoreException exception) when (exception.Code == AccountingStoreCodes.JournalIdTaken)
                {
                    // The approval posts exactly one journal: the draft's own, whose id IS the draft id
                    // (NormalJournalDraftV1.Id is its request's JournalId). "Taken" therefore means that journal already
                    // exists, e.g. a racing approval committed it after this attempt read the ledger. That is the same
                    // refusal the store reports as a posting conflict when it meets the delivery or the original first,
                    // so it is answered as one (replay of this key's winner, else the route's declared 409).
                    throw new AccountingStoreException(NormalJournalDraftStoreCodesV1.PostingConflict,
                        exception.Message, exception);
                }

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
        catch (AccountingStoreException exception) when (
            exception.Code == NormalJournalDraftStoreCodesV1.PostingConflict
            && string.Equals(commandName, NormalJournalCommandNamesV1.ApproveDraft, StringComparison.Ordinal))
        {
            // An approval that read the draft pending while a racing approval of it committed is refused by the store
            // (nothing of it is saved). When that winner answered under THIS key with the same actor and request, its
            // recorded answer is this key's replay (ADR-011), exactly as for a lost save; otherwise it stays 409.
            return await commands.FindCommandAsync(commandName, keyHash, cancellationToken) is { } winner
                && Binds(winner, actorId, requestHash)
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

    private async Task<AccountingApiResult<NormalJournalDraftViewV1>?> TryReplayAsync(
        NormalJournalPreparedCreateCommandV2 command,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (!Valid(command) || string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Invalid("request");
        }

        if (!HasContext() || ActorGuid() is not { })
        {
            return Denied();
        }

        var company = companyContext.CompanyId;
        var first = await authority.RevalidatePermissionScopeAsync(
            permissions.CreatePermission, company, cancellationToken);
        if (!first.IsGranted || !first.IsCompanyWide || !HasContext())
        {
            return Denied();
        }

        var actorId = currentUser.UserId!;
        var keyHash = AccountingApiService.Sha256(idempotencyKey.Trim());
        var requestHash = AccountingApiService.Sha256(NormalJournalCommandNamesV1.CreatePreparedDraft
            + "|" + JsonSerializer.Serialize(Canonical(command), Json));
        var prior = await commands.FindCommandAsync(
            NormalJournalCommandNamesV1.CreatePreparedDraft, keyHash, cancellationToken);

        var second = await authority.RevalidatePermissionScopeAsync(
            permissions.CreatePermission, company, cancellationToken);
        if (!second.IsGranted || !second.IsCompanyWide || !HasContext()
            || !string.Equals(currentUser.UserId, actorId, StringComparison.Ordinal))
        {
            return Denied();
        }

        return prior is null ? null : Answer<NormalJournalDraftViewV1>(prior, actorId, requestHash);
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

    private static object Canonical(NormalJournalPreparedCreateCommandV2 command) => new
    {
        command.DraftId,
        command.SourceId,
        transactionDate = Date(command.TransactionDate),
        accountingDate = Date(command.AccountingDate),
        valueDate = command.ValueDate is { } valueDate ? Date(valueDate) : null,
        expectedPreparationVersion = command.ExpectedPreparationVersion.ToUpperInvariant(),
        lines = command.Lines.Select((line, index) => new
        {
            lineNumber = index + 1,
            line.AccountId,
            side = (int)line.Side,
            amount = Text(line.Amount),
        }).ToArray(),
    };

    private static bool Valid(NormalJournalPreparedCreateCommandV2? command) => command is not null
        && command.DraftId != Guid.Empty && command.SourceId != Guid.Empty
        && command.TransactionDate != default && command.AccountingDate != default
        && NormalJournalDraftSourcePreparationV1.IsPreparationVersion(command.ExpectedPreparationVersion)
        && command.Lines is { Count: >= 2 }
        && command.Lines.All(line => line is not null && line.AccountId != Guid.Empty
            && Enum.IsDefined(line.Side) && line.Amount > 0m);

    private static bool PreparedCommandMatchesDraft(
        NormalJournalPreparedCreateCommandV2 command,
        NormalJournalDraftV1 draft)
    {
        if (!Valid(command) || draft.Id != command.DraftId
            || draft.Request.Source.SourceId != command.SourceId
            || draft.Request.Dates.TransactionDate != command.TransactionDate
            || draft.Request.Dates.AccountingDate != command.AccountingDate
            || draft.Request.Dates.ValueDate != command.ValueDate
            || draft.Request.Lines.Count != command.Lines.Count)
        {
            return false;
        }

        return draft.Request.Lines.Zip(command.Lines).All(pair =>
            pair.First.Account == new AccountRef(draft.CompanyId, pair.Second.AccountId)
            && pair.First.Side == pair.Second.Side
            && pair.First.Amount == pair.Second.Amount);
    }

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

    /// <summary>A recorded command answers this attempt only for the same actor and the same canonical request.</summary>
    private static bool Binds(AccountingCommandRecord prior, string actorId, string requestHash) =>
        string.Equals(prior.ActorId, actorId, StringComparison.Ordinal)
        && string.Equals(prior.RequestHash, requestHash, StringComparison.Ordinal);

    private static AccountingApiResult<T> Answer<T>(AccountingCommandRecord prior, string actorId, string requestHash)
        where T : class
    {
        if (!Binds(prior, actorId, requestHash))
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
