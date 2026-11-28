using System.Data;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

internal sealed partial class CounterpartyCommandService
{
    private MergePurposeState? mergePurpose;

    public async Task<CounterpartyPartyMergePurposeResultV1> ApprovePurposeAsync(Guid survivorPartyId,
        ApproveCounterpartyPartyMerge command, string seenImpactDigest, CounterpartyCommandMetadata metadata,
        Func<CancellationToken, Task> consumerFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(consumerFence);
        ValidateCommandMetadata(metadata);
        var captured = CounterpartyContactInformationAuthorityFence.Capture(currentUser, companyContext);
        if (captured.CompanyId != metadata.CompanyId || captured.ActorId != metadata.ActorId) throw new UnauthorizedAccessException();
        await RequireCompanyWidePermissionAsync(CounterpartyPartyMergePermissionsV1.Approve, cancellationToken);
        await RequireCompanyWidePermissionAsync(GoymenERP.BuildingBlocks.Security.ErpPermissions.CounterpartyPartiesRead, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        // Preserve native authorized shape validation before refusing a dirty caller. No ownership has started.
        var shape = MergeShape(survivorPartyId, command);
        if (seenImpactDigest is not { Length: 64 } || seenImpactDigest.Any(x => x is not (>= 'A' and <= 'F' or >= '0' and <= '9')))
            throw new ArgumentException("Birleştirme etki bağı geçersiz.", nameof(seenImpactDigest));
        var hash = MergePayloadHash(survivorPartyId, command, shape);
        var key = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        try { return await Attempt(false); }
        catch (MergePurposeRace) { return await Attempt(true); }

        bool Clean() => identityDecisionAuditWriter is not null && mergePurposeAudit is not null && dbContext.Database.IsSqlServer()
            && System.Transactions.Transaction.Current is null && dbContext.Database.CurrentTransaction is null
            && !dbContext.ChangeTracker.HasChanges() && !identityDecisionAuditWriter.HasPendingDrafts
            && mergePurpose is null && identityDecision is null && createWithTaxIdentity is null;
        async Task<CounterpartyPartyMergePurposeResultV1> Attempt(bool readOnly)
        {
            if (!Clean()) return new(CounterpartyPartyMergePurposeStatusV1.Unavailable, null);
            await consumerFence(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await captured.RequireStillAsync(currentUser, companyContext, authorityRevalidationService, PartyMergePermissions, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!Clean()) return new(CounterpartyPartyMergePurposeStatusV1.Unavailable, null);
            var state = new MergePurposeState(metadata.CompanyId, metadata.ActorId, key, hash, seenImpactDigest, readOnly);
            state.Fence = async token =>
            {
                token.ThrowIfCancellationRequested();
                if (state.Fencing) return;
                state.Fencing = true;
                try
                {
                    await consumerFence(token); token.ThrowIfCancellationRequested();
                    await captured.RequireStillAsync(currentUser, companyContext, authorityRevalidationService, PartyMergePermissions, token);
                    foreach (var company in state.Companies)
                    {
                        if (!currentUser.CanAccessCompany(company)) throw new UnauthorizedAccessException();
                        foreach (var permission in PartyMergePermissions)
                        {
                            await RequireFreshCompanyWidePermissionAsync(permission, company, token);
                            token.ThrowIfCancellationRequested();
                        }
                    }
                    await captured.RequireStillAsync(currentUser, companyContext, authorityRevalidationService, PartyMergePermissions, token);
                    token.ThrowIfCancellationRequested();
                }
                finally { state.Fencing = false; }
            };
            IDbContextTransaction? owned = null;
            var commitStarted = false; var committed = false; var disposing = false;
            try
            {
                mergePurpose = state;
                owned = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                await state.Fence(cancellationToken);
                CounterpartyCommandExecution answer;
                if (readOnly)
                {
                    // Read the original native key once. Never re-enter the mutation or create a losing terminal row.
                    var survivor = await FindFencedPartyAsync(metadata.CompanyId, survivorPartyId, false, cancellationToken);
                    await state.Fence(cancellationToken);
                    var companies = await dbContext.CompanyPartyAccounts.AsNoTracking()
                        .Where(x => x.PartyId == survivorPartyId || x.PartyId == command.MergedPartyId)
                        .Select(x => x.CompanyId).Distinct().Order().ToArrayAsync(cancellationToken);
                    state.Companies = companies; await state.Fence(cancellationToken);
                    var winner = await FindCommandExecutionAsync(ApprovePartyMergeCommandName, key, cancellationToken);
                    await state.Fence(cancellationToken);
                    if (winner is null)
                    {
                        await owned.RollbackAsync(CancellationToken.None);
                        var completedRead = owned; owned = null; disposing = true;
                        await completedRead.DisposeAsync(); disposing = false;
                        await state.Fence(cancellationToken);
                        return new(CounterpartyPartyMergePurposeStatusV1.Unavailable, null);
                    }
                    answer = ReplayFenced(winner, hash, metadata, survivor is not null);
                }
                else answer = await ApproveMergeAsync(survivorPartyId, command, metadata, cancellationToken);
                await state.Fence(cancellationToken);
                if (state.CapturedImpact is not null && !state.Saved) throw new InvalidOperationException("Birleştirme makbuzu kaydedilmedi.");
                if (answer.Response.Result == CounterpartyCommandResult.Succeeded
                    && !await MergePurposeReceiptAsync(survivorPartyId, command, metadata, cancellationToken)) throw new MergePurposeUnproven();
                // The exact native save hook flushes the existing single draft before SaveChanges.
                // A late additional draft is an abort, never a second write hidden in the approval.
                if (identityDecisionAuditWriter!.HasPendingDrafts || dbContext.ChangeTracker.HasChanges())
                    throw new InvalidOperationException("Birleştirme işleminin kaydedilmemiş işi kaldı.");
                await state.Fence(cancellationToken);
                commitStarted = true;
                await owned.CommitAsync(cancellationToken); committed = true;
                var completed = owned; owned = null; disposing = true;
                await completed.DisposeAsync(); disposing = false;
                await state.Fence(cancellationToken);
                return new(CounterpartyPartyMergePurposeStatusV1.Executed, answer);
            }
            catch (Exception failure)
            {
                var cleanup = new List<Exception>();
                var deadlock = SqlNumber(failure) == 1205;
                if (owned is not null)
                {
                    if (!committed) try
                    {
                        if (!deadlock || owned.GetDbTransaction() is not SqlTransaction { Connection: null })
                            await owned.RollbackAsync(CancellationToken.None);
                    }
                    catch (Exception error) { cleanup.Add(error); }
                    try { await owned.DisposeAsync(); } catch (Exception error) { cleanup.Add(error); }
                }
                try { dbContext.ChangeTracker.Clear(); } catch (Exception error) { cleanup.Add(error); }
                try { identityDecisionAuditWriter!.DiscardPending(); } catch (Exception error) { cleanup.Add(error); }
                if (cleanup.Count != 0 || disposing) throw new AggregateException("Birleştirme temizliği doğrulanamadı.", [failure, .. cleanup]);
                // A failed cleanup or a commit already attempted is never a safe read-retry claim.
                if (!readOnly && !commitStarted && (failure is DbUpdateConcurrencyException || SqlNumber(failure) is 1205 or 1222 or 2601 or 2627))
                    throw new MergePurposeRace();
                if (!commitStarted && failure is MergeImpactChanged)
                {
                    await state.Fence(cancellationToken);
                    return new(CounterpartyPartyMergePurposeStatusV1.Changed, null);
                }
                if (!commitStarted && failure is MergePurposeUnproven)
                {
                    await state.Fence(cancellationToken);
                    return new(CounterpartyPartyMergePurposeStatusV1.Unavailable, null);
                }
                await state.Fence(cancellationToken);
                throw;
            }
            finally { mergePurpose = null; }
        }
    }

    private static int? SqlNumber(Exception error) => error is SqlException sql ? sql.Number
        : error is DbUpdateException { InnerException: SqlException inner } ? inner.Number : null;
    private sealed class MergeImpactChanged : Exception { }
    private sealed class MergePurposeRace : Exception { }
    private sealed class MergePurposeUnproven : Exception { }
    private sealed class MergePurposeState(Guid company, string actor, string key, string hash, string seenImpact, bool readOnly)
    {
        internal Guid CompanyId { get; } = company;
        internal string ActorId { get; } = actor;
        internal string KeyHash { get; } = key;
        internal string RequestHash { get; } = hash;
        internal string SeenImpactDigest { get; } = seenImpact;
        internal bool ReadOnly { get; } = readOnly;
        internal Guid[] Companies = [];
        internal Func<CancellationToken, Task> Fence = _ => Task.CompletedTask;
        internal bool Fencing;
        internal bool Saved;
        internal CounterpartyPartyMergeImpactPreviewV1? CapturedImpact;
    }
}
