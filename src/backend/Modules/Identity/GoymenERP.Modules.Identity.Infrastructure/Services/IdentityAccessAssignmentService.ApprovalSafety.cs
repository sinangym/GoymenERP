using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Approvals;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Identity.Infrastructure.Services;

internal sealed partial class IdentityAccessAssignmentService
{
    private AccessApprovalScope? accessApproval;
    private bool mandatoryAccessApprovalSafety;

    internal IdentityAccessAssignmentService RequireAccessGrantApprovalSafety()
    {
        mandatoryAccessApprovalSafety = true;
        return this;
    }

    internal IIdentityAccessAssignmentService WithAccessGrantApprovalSafety() =>
        new GuardedAccessAssignmentService(RequireAccessGrantApprovalSafety());

    // Only the runtime approval facade enters this scope. Other ID-3 commands retain their native protocol.
    private async Task<IdentityAccessCommandResultV1> RunAccessApprovalAsync(
        Func<Task<IdentityAccessCommandResultV1>> native, bool validShape, IdentityAccessCommandMetadataV1 metadata, CancellationToken token,
        AccessPurposeAttempt? purpose = null)
    {
        token.ThrowIfCancellationRequested();
        var company = RequireContextCompany();
        ValidateMetadata(metadata);
        if (!Guid.TryParse(metadata.ActorId, out var actor) || actor == Guid.Empty) throw new UnauthorizedAccessException();
        await RequireAsync(IdentityPermissionManifestV1.AccessApprove, company, token);
        // Preserve native authorized validation (its standalone recorder owns a separate Audit scope), never clear caller work.
        if (!validShape) return await native();
        if (!CleanEntry()) return Unstored(Answers.Unavailable(), metadata);
        await FreshAsync();
        // The authority await may have introduced another caller's work; ownership starts only after this second check.
        if (!CleanEntry()) return Unstored(Answers.Unavailable(), metadata);
        var state = new AccessApprovalScope(metadata);
        state.Purpose = purpose;
        accessApproval = state;
        try
        {
            var result = await native();
            await AccessApprovalFenceAsync(token);
            return result;
        }
        finally { accessApproval = null; }

        bool CleanEntry() => accessApproval is null && database.Database.IsRelational()
            && System.Transactions.Transaction.Current is null && database.Database.CurrentTransaction is null
            && !database.ChangeTracker.HasChanges() && !auditWriter.HasPendingDrafts;
        async Task FreshAsync()
        {
            token.ThrowIfCancellationRequested();
            if (!SameAccessApprovalContext(metadata)) throw new UnauthorizedAccessException();
            await RequireStillAsync(IdentityPermissionManifestV1.AccessApprove, company, token);
            token.ThrowIfCancellationRequested();
            if (!SameAccessApprovalContext(metadata)) throw new UnauthorizedAccessException();
        }
    }

    private bool SameAccessApprovalContext(IdentityAccessCommandMetadataV1 metadata) =>
        currentUser.IsAuthenticated && string.Equals(currentUser.UserId, metadata.ActorId, StringComparison.Ordinal)
        && companyContext.IsResolved && companyContext.CompanyId == metadata.CompanyId
        && currentUser.CanAccessCompany(metadata.CompanyId);

    private async Task AccessApprovalFenceAsync(CancellationToken token)
    {
        if (accessApproval is not { } state) return;
        token.ThrowIfCancellationRequested();
        if (!SameAccessApprovalContext(state.Metadata)) throw new UnauthorizedAccessException();
        // Revalidation may use native Identity read helpers. It still reaches the actual authority; only nested relay is suppressed.
        if (state.Fencing) return;
        state.Fencing = true;
        try
        {
            await RequireStillAsync(IdentityPermissionManifestV1.AccessApprove, state.Metadata.CompanyId, token);
            if (state.Purpose is { } purpose)
            {
                await purpose.ConsumerFence(token);
                token.ThrowIfCancellationRequested();
                await RequireStillAsync(IdentityPermissionManifestV1.AccessApprove, state.Metadata.CompanyId, token);
            }
            token.ThrowIfCancellationRequested();
            if (!SameAccessApprovalContext(state.Metadata)) throw new UnauthorizedAccessException();
        }
        finally { state.Fencing = false; }
    }

    private async Task PrepareAccessApprovalCommitAsync(CancellationToken token)
    {
        if (accessApproval is null) return;
        await AccessApprovalFenceAsync(token);
        if (database.ChangeTracker.HasChanges() || auditWriter.HasPendingDrafts)
            throw new InvalidOperationException("Yetki kararı kaydedilmemiş iş veya audit içeriyor.");
    }

    private async Task RollbackAccessPrimaryAsync(IDbContextTransaction transaction, CancellationToken token)
    {
        await transaction.RollbackAsync(token);
        if (accessApproval is { } state) state.RolledBack = true;
    }

    private async Task AbortAccessApprovalAsync(Exception failure, bool commitStarted)
    {
        var state = accessApproval!;
        var faults = new List<Exception>();
        if (state.Transaction is { } transaction)
        {
            if (!commitStarted && !state.RolledBack)
            {
                try
                {
                    var deadlock = failure is SqlException { Number: 1205 }
                        or DbUpdateException { InnerException: SqlException { Number: 1205 } }
                        or InvalidOperationException { InnerException: SqlException { Number: 1205 } }
                        or InvalidOperationException { InnerException: DbUpdateException { InnerException: SqlException { Number: 1205 } } };
                    if (!deadlock || transaction.GetDbTransaction() is not SqlTransaction { Connection: null })
                        await transaction.RollbackAsync(CancellationToken.None);
                    state.RolledBack = true;
                }
                catch (Exception error) { faults.Add(error); }
            }
            state.DisposalAttempted = true;
            try { await transaction.DisposeAsync(); } catch (Exception error) { faults.Add(error); }
        }
        try { database.ChangeTracker.Clear(); } catch (Exception error) { faults.Add(error); }
        try { auditWriter.DiscardPending(); } catch (Exception error) { faults.Add(error); }
        state.CleanupAttempted = true;
        if (faults.Count != 0)
            throw new AggregateException("Yetki kararı temizlenemedi; özgün makbuz uzlaştırması güvenli değil.", [failure, .. faults]);
        state.Cleaned = true;
    }

    private async Task<IdentityAccessCommandResultV1> RunAccessApprovalLockedAsync(
        Command command, Func<DateTimeOffset, Task<Step>> body, CancellationToken token)
    {
        var state = accessApproval!;
        // Native BeginAsync has awaited permission/idempotency reads since the facade entry check.
        // Until this point no approval write or owned transaction exists: never adopt/clear work introduced there.
        if (!CleanBeforeTransaction()) return Unstored(Answers.Unavailable(), command.Metadata);
        await AccessApprovalFenceAsync(token);
        if (!CleanBeforeTransaction()) return Unstored(Answers.Unavailable(), command.Metadata);
        try
        {
            var attempt = await RunLockedCoreAsync(command, body, token);
            // Core has completed/disposed its one native transaction before any original-key read.
            if (!state.Cleaned)
            {
                database.ChangeTracker.Clear();
                auditWriter.DiscardPending();
                state.CleanupAttempted = true;
                state.Cleaned = true;
            }
            await AccessApprovalFenceAsync(token);
            if (attempt.Result is { } result) return Done(command, result);
            var winner = attempt.Winner;
            if (winner is null)
            {
                winner = await FindExecutionAsync(command, token);
                await AccessApprovalFenceAsync(token);
            }
            // One read-only pass; no business body, standalone terminal write or mutation retry after an abort.
            if (winner is null) return Done(command, Unstored(Answers.Unavailable(), command.Metadata));
            // A raced foreign actor/body never invokes the native standalone refused-attempt writer here.
            if (!string.Equals(winner.ActorId, command.Metadata.ActorId, StringComparison.Ordinal)
                || !string.Equals(winner.RequestHash, command.RequestHash, StringComparison.Ordinal))
                return Done(command, Unstored(Answers.IdempotencyConflict(), command.Metadata));
            var replay = await ReplayAsync(winner, command, token);
            await AccessApprovalFenceAsync(token);
            return Done(command, replay);
        }
        catch (Exception failure)
        {
            // Begin/dispose failures also preserve the original failure and cannot masquerade as safe reconciliation.
            if (!state.CleanupAttempted) await AbortAccessApprovalAsync(failure, state.Committed || state.CommitAttempted);
            throw;
        }

        bool CleanBeforeTransaction() => state.Transaction is null
            && System.Transactions.Transaction.Current is null && database.Database.CurrentTransaction is null
            && !database.ChangeTracker.HasChanges() && !auditWriter.HasPendingDrafts;
    }

    private sealed class AccessApprovalScope(IdentityAccessCommandMetadataV1 metadata)
    {
        internal IdentityAccessCommandMetadataV1 Metadata { get; } = metadata;
        internal IDbContextTransaction? Transaction;
        internal bool Fencing;
        internal bool RolledBack;
        internal bool DisposalAttempted;
        internal bool CommitAttempted;
        internal bool Committed;
        internal bool CleanupAttempted;
        internal bool Cleaned;
        internal AccessPurposeAttempt? Purpose;
    }

    private sealed class GuardedAccessAssignmentService(IdentityAccessAssignmentService core) : IIdentityAccessAssignmentService, IIdentityAccessGrantDecisionV1
    {
        public Task<IdentityAccessGrantDecisionDetailV1?> ReadDecisionAsync(Guid id, IdentityAccessCommandMetadataV1 context, Func<CancellationToken, Task> consumerFence, CancellationToken cancellationToken = default) => core.ReadDecisionAsync(id, context, consumerFence, cancellationToken);
        public Task<IdentityAccessGrantDecisionReceiptV1?> ReadDecisionReceiptAsync(Guid id, bool approve, string version, string? reason, IdentityAccessCommandMetadataV1 context, Func<CancellationToken, Task> consumerFence, CancellationToken cancellationToken = default) => core.ReadDecisionReceiptAsync(id, approve, version, reason, context, consumerFence, cancellationToken);
        public Task<IdentityAccessCommandResultV1> DecidePurposeAsync(Guid id, bool approve, string version, string? reason, string impact, IdentityAccessCommandMetadataV1 context, Func<CancellationToken, Task> consumerFence, CancellationToken cancellationToken = default) => core.DecidePurposeAsync(id, approve, version, reason, impact, context, consumerFence, cancellationToken);
        public Task<IdentityAccessCommandResultV1> ApproveAsync(Guid id, ApproveAccessGrantRequestV1 request, IdentityAccessCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            return core.RunAccessApprovalAsync(() => core.ApproveNativeAsync(id, request, metadata, cancellationToken),
                TryParseVersion(request.ExpectedVersion, MinimumExpectedVersion, out _), metadata, cancellationToken);
        }
        public Task<IdentityAccessCommandResultV1> RejectAsync(Guid id, RejectAccessGrantRequestV1 request, IdentityAccessCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            return core.RunAccessApprovalAsync(() => core.RejectNativeAsync(id, request, metadata, cancellationToken),
                TryParseVersion(request.ExpectedVersion, MinimumExpectedVersion, out _) && AccessGrantRequest.IsCanonicalReasonCode(request.ReasonCode),
                metadata, cancellationToken);
        }
        public Task<IdentityAccessCommandResultV1> AssignAsync(AssignIdentityRoleRequestV1 request, IdentityAccessCommandMetadataV1 metadata, CancellationToken cancellationToken = default) => core.AssignAsync(request, metadata, cancellationToken);
        public Task<IdentityAccessCommandResultV1> RevokeAsync(Guid id, RevokeIdentityRoleAssignmentRequestV1 request, IdentityAccessCommandMetadataV1 metadata, CancellationToken cancellationToken = default) => core.RevokeAsync(id, request, metadata, cancellationToken);
        public Task<IdentityAccessCommandResultV1> CreateGrantRequestAsync(CreateAccessGrantRequestRequestV1 request, IdentityAccessCommandMetadataV1 metadata, CancellationToken cancellationToken = default) => core.CreateGrantRequestAsync(request, metadata, cancellationToken);
        public Task<IdentityAccessCommandResultV1> WithdrawAsync(Guid id, WithdrawAccessGrantRequestV1 request, IdentityAccessCommandMetadataV1 metadata, CancellationToken cancellationToken = default) => core.WithdrawAsync(id, request, metadata, cancellationToken);
        public Task<IReadOnlyList<IdentityAccessGrantRequestView>?> GetPendingGrantRequestsAsync(CancellationToken cancellationToken = default) => core.GetPendingGrantRequestsAsync(cancellationToken);
    }
}
