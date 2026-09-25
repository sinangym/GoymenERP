using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;

namespace GoymenERP.Modules.Accounting.Application;

public static class AccountingJournalReversalPermissionNamesV1
{
    public const string Propose = "accounting.journal.reverse.propose";
    public const string Approve = "accounting.journal.reverse.approve";
}

public static class AccountingJournalReversalCommandNamesV1
{
    public const string Propose = "accounting.journal-reversal.propose.v1";
    public const string Approve = "accounting.journal-reversal.approve.v1";
}

public static class AccountingJournalReversalStoreCodesV1
{
    public const string NotFound = "accounting_journal_reversal_proposal_not_found";
    public const string ContentConflict = "accounting_journal_reversal_proposal_content_conflict";
    public const string PostingConflict = "accounting_journal_reversal_posting_conflict";
}

public sealed record AccountingJournalReversalProposalViewV1(
    Guid ProposalId,
    Guid CompanyId,
    Guid SourceJournalId,
    Guid? ReversalJournalId,
    Guid PeriodId,
    DateOnly TransactionDate,
    DateOnly AccountingDate,
    DateOnly? ValueDate,
    string Reason,
    Guid ProposedBy,
    DateTimeOffset ProposedAtUtc,
    string Status,
    Guid? DecidedBy,
    DateTimeOffset? DecidedAtUtc,
    string? DecisionReason,
    int Version);

public interface IAccountingJournalReversalProposalStoreV1
{
    Task<(AccountingJournalReversalProposalV1 Proposal, AccountingWriteOutcome Outcome)> CreateAsync(
        AccountingJournalReversalProposalV1 proposal,
        Func<CancellationToken, Task> freshAuthorityFence,
        AccountingCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    Task<AccountingJournalReversalProposalV1?> FindAsync(
        Guid proposalId,
        CancellationToken cancellationToken = default);

    Task<AccountingJournalReversalProposalV1> ApproveAndReverseAsync(
        Guid proposalId,
        int expectedVersion,
        Guid checkerId,
        Func<CancellationToken, Task> freshAuthorityFence,
        AccountingCommandMetadata metadata,
        CancellationToken cancellationToken = default);
}

public sealed class AccountingJournalReversalAuthorityDeniedException()
    : Exception("The accounting journal reversal authority is no longer valid.");

/// <summary>
/// Owner-side command handler for the two-step reversal workflow. Permission literals are bindings only;
/// registration, role grants and production activation remain central/human-owned.
/// </summary>
public interface IAccountingJournalReversalLifecycleV1
{
    Task<AccountingApiResult<AccountingJournalReversalProposalViewV1>> ProposeAsync(
        ProposeAccountingJournalReversalV1? request,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<AccountingApiResult<AccountingJournalReversalProposalViewV1>> ApproveAsync(
        Guid proposalId,
        int expectedVersion,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
}

public sealed class AccountingJournalReversalServiceV1(
    IAccountingJournalReversalProposalStoreV1 proposals,
    IAccountingCommandLog commands,
    IErpAuthorityRevalidationService authority,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    ICorrelationContext correlation,
    TimeProvider timeProvider) : IAccountingJournalReversalLifecycleV1
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<AccountingApiResult<AccountingJournalReversalProposalViewV1>> ProposeAsync(
        ProposeAccountingJournalReversalV1? request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return Task.FromResult(Invalid("request"));
        }

        return RunAsync(
            AccountingJournalReversalPermissionNamesV1.Propose,
            AccountingJournalReversalCommandNamesV1.Propose,
            idempotencyKey,
            request,
            AccountingApiStatus.Created,
            async (metadata, actor) =>
            {
                var proposal = AccountingJournalReversalPolicyV1.Propose(
                    companyContext.CompanyId, actor, request, timeProvider.GetUtcNow());
                var (stored, outcome) = await proposals.CreateAsync(
                    proposal,
                    token => FenceAsync(AccountingJournalReversalPermissionNamesV1.Propose, actor, token),
                    metadata,
                    cancellationToken);
                return new AccountingApiResult<AccountingJournalReversalProposalViewV1>(
                    AccountingApiStatus.Created, View(stored), outcome == AccountingWriteOutcome.Replayed, null);
            },
            stored => View((AccountingJournalReversalProposalV1)stored),
            cancellationToken);
    }

    public Task<AccountingApiResult<AccountingJournalReversalProposalViewV1>> ApproveAsync(
        Guid proposalId,
        int expectedVersion,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (proposalId == Guid.Empty || expectedVersion <= 0)
        {
            return Task.FromResult(Invalid("proposalId"));
        }

        return RunAsync(
            AccountingJournalReversalPermissionNamesV1.Approve,
            AccountingJournalReversalCommandNamesV1.Approve,
            idempotencyKey,
            new { proposalId, expectedVersion },
            AccountingApiStatus.Ok,
            async (metadata, actor) =>
            {
                var stored = await proposals.ApproveAndReverseAsync(
                    proposalId,
                    expectedVersion,
                    actor,
                    token => FenceAsync(AccountingJournalReversalPermissionNamesV1.Approve, actor, token),
                    metadata,
                    cancellationToken);
                return new AccountingApiResult<AccountingJournalReversalProposalViewV1>(
                    AccountingApiStatus.Ok, View(stored), false, null);
            },
            stored => View((AccountingJournalReversalProposalV1)stored),
            cancellationToken);
    }

    private async Task<AccountingApiResult<T>> RunAsync<T>(
        string permission,
        string commandName,
        string idempotencyKey,
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

        var companyId = companyContext.CompanyId;
        var scope = await authority.RevalidatePermissionScopeAsync(permission, companyId, cancellationToken);
        if (!scope.IsGranted || !scope.IsCompanyWide || !HasContext())
        {
            return Denied<T>();
        }

        var actorId = currentUser.UserId!;
        var keyHash = AccountingApiService.Sha256(idempotencyKey.Trim());
        var requestHash = AccountingApiService.Sha256(
            commandName + "|" + JsonSerializer.Serialize(canonicalRequest, Json));
        if (await commands.FindCommandAsync(commandName, keyHash, cancellationToken) is { } prior)
        {
            return Answer<T>(prior, actorId, requestHash);
        }

        var second = await authority.RevalidatePermissionScopeAsync(permission, companyId, cancellationToken);
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
        catch (AccountingJournalReversalAuthorityDeniedException)
        {
            return Denied<T>();
        }
        catch (AccountingVersionConflictException exception)
        {
            result = new AccountingApiResult<T>(AccountingApiStatus.Conflict, null, false,
                AccountingApiCodes.VersionConflict, "expectedVersion", exception.ExpectedVersion, exception.CurrentVersion);
        }
        catch (AccountingJournalReversalPolicyExceptionV1 exception)
        {
            result = exception.Code switch
            {
                AccountingJournalReversalPolicyCodesV1.MakerCheckerRequired => Denied<T>(),
                AccountingJournalReversalPolicyCodesV1.NotPending or
                    AccountingJournalReversalPolicyCodesV1.VersionConflict =>
                    new AccountingApiResult<T>(AccountingApiStatus.Conflict, null, false,
                        exception.Code, exception.Field),
                AccountingJournalReversalPolicyCodesV1.InvalidRequest =>
                    new AccountingApiResult<T>(AccountingApiStatus.Invalid, null, false,
                        exception.Code, exception.Field),
                _ => new AccountingApiResult<T>(AccountingApiStatus.Unprocessable, null, false,
                    exception.Code, exception.Field),
            };
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
                AccountingJournalReversalStoreCodesV1.NotFound =>
                    new AccountingApiResult<T>(AccountingApiStatus.NotFound, null, false, null),
                AccountingStoreCodes.CompanyContextMismatch => Denied<T>(),
                AccountingJournalReversalStoreCodesV1.ContentConflict or
                    AccountingJournalReversalStoreCodesV1.PostingConflict =>
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
        var scope = await authority.RevalidatePermissionScopeAsync(permission, companyContext.CompanyId, cancellationToken);
        if (!scope.IsGranted || !scope.IsCompanyWide || !HasContext() || ActorGuid() != actor)
        {
            throw new AccountingJournalReversalAuthorityDeniedException();
        }
    }

    private bool HasContext() =>
        companyContext.IsResolved && companyContext.CompanyId != Guid.Empty && currentUser.IsAuthenticated
        && !string.IsNullOrWhiteSpace(currentUser.UserId) && currentUser.CanAccessCompany(companyContext.CompanyId)
        && correlation.IsResolved;

    private Guid? ActorGuid() =>
        Guid.TryParse(currentUser.UserId, out var actor) && actor != Guid.Empty ? actor : null;

    private AccountingCommandRecord Record<T>(
        string commandName,
        string keyHash,
        string actorId,
        string requestHash,
        AccountingApiResult<T> result)
        where T : class =>
        new(commandName, keyHash, actorId, requestHash,
            JsonSerializer.Serialize(result with { IsReplay = false, OriginalCorrelationId = null }, Json),
            correlation.CorrelationId);

    private static AccountingApiResult<T> Answer<T>(AccountingCommandRecord prior, string actorId, string requestHash)
        where T : class
    {
        if (!string.Equals(prior.ActorId, actorId, StringComparison.Ordinal)
            || !string.Equals(prior.RequestHash, requestHash, StringComparison.Ordinal))
        {
            return new AccountingApiResult<T>(AccountingApiStatus.Conflict, null, false,
                AccountingApiCodes.IdempotencyConflict);
        }

        var stored = JsonSerializer.Deserialize<AccountingApiResult<T>>(prior.ResultJson, Json)
            ?? throw new InvalidOperationException("Kayıtlı muhasebe ters kayıt komutu sonucu okunamadı.");
        return stored with { IsReplay = true, OriginalCorrelationId = prior.OriginalCorrelationId };
    }

    private static AccountingJournalReversalProposalViewV1 View(AccountingJournalReversalProposalV1 proposal) => new(
        proposal.ProposalId,
        proposal.CompanyId,
        proposal.SourceJournalId,
        proposal.ReversalJournalId,
        proposal.PeriodId,
        proposal.TransactionDate,
        proposal.AccountingDate,
        proposal.ValueDate,
        proposal.Reason,
        proposal.ProposedBy,
        proposal.ProposedAtUtc,
        proposal.Status.ToString(),
        proposal.DecidedBy,
        proposal.DecidedAtUtc,
        proposal.DecisionReason,
        proposal.Version);

    private static AccountingApiResult<AccountingJournalReversalProposalViewV1> Invalid(string field) =>
        new(AccountingApiStatus.Invalid, null, false, AccountingApiCodes.RequestInvalid, field);

    private static AccountingApiResult<T> Denied<T>() where T : class =>
        new(AccountingApiStatus.Denied, null, false, null);
}
