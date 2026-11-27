using System.Runtime.ExceptionServices;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Counterparty.Application.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Counterparty.Infrastructure.Persistence;

internal sealed partial class CounterpartyCommandService
{
    private IdentityDecisionState? identityDecision;

    // Only the existing tax-identity/legal-name decision commands enter this boundary. All other
    // Counterparty commands retain their native save/refusal behavior and receipt schema.
    private async Task<CounterpartyCommandExecution> RunIdentityDecisionAsync(string commandName, string permission,
        Guid partyId, string expectedVersion, CounterpartyCommandMetadata metadata,
        Func<CancellationToken, Task<CounterpartyCommandExecution>> execute, CancellationToken token)
    {
        try { return await RunScopeAsync(execute, canReadRace: true); }
        catch (IdentityDecisionSaveRace race)
        {
            // The failed write scope has positively rolled back and disposed its handles before this
            // one bounded read-only scope. No retry of the mutation or new conflict receipt is permitted.
            return await RunScopeAsync(async current =>
            {
                var party = await FindFencedPartyAsync(metadata.CompanyId, partyId, tracking: false, current);
                await identityDecision!.Fence(current);
                var winner = await FindCommandExecutionAsync(commandName, race.KeyHash, current);
                await identityDecision.Fence(current);
                if (winner is not null) return ReplayFenced(winner, race.RequestHash, metadata, party is not null);
                if (race.InnerException is DbUpdateConcurrencyException
                    || race.InnerException is DbUpdateException update && IsDatedHistoryRace(update))
                    return new(party is null ? Responses.NotFound()
                        : Responses.VersionConflict(CounterpartyContactInformationEditor.ParseVersion(expectedVersion), party.Version),
                        false, metadata.CorrelationId);
                ExceptionDispatchInfo.Capture(race.InnerException!).Throw();
                throw new InvalidOperationException("An unclassified identity save failure cannot become a native receipt.");
            }, canReadRace: false);
        }

        async Task<CounterpartyCommandExecution> RunScopeAsync(
            Func<CancellationToken, Task<CounterpartyCommandExecution>> action, bool canReadRace)
        {
            // Refuse BEFORE ownership/cleanup: caller transactions, changes and audit drafts remain theirs.
            if (identityDecisionAuditWriter is null || !dbContext.Database.IsRelational()
                || System.Transactions.Transaction.Current is not null || dbContext.Database.CurrentTransaction is not null
                || dbContext.ChangeTracker.HasChanges() || identityDecisionAuditWriter.HasPendingDrafts || identityDecision is not null)
                throw new InvalidOperationException("An identity decision requires its own clean relational transaction and Audit writer.");
            ValidateCommandMetadata(metadata);
            var authorityFence = CounterpartyContactInformationAuthorityFence.Capture(currentUser, companyContext);
            if (authorityFence.CompanyId != metadata.CompanyId || authorityFence.ActorId != metadata.ActorId)
                throw new UnauthorizedAccessException("The identity decision context changed.");
            Task Fence(CancellationToken current) => authorityFence.RequireStillAsync(currentUser, companyContext,
                authorityRevalidationService, [permission, ErpPermissions.CounterpartyPartiesRead], current);
            var state = new IdentityDecisionState(commandName, Fence);
            try
            {
                identityDecision = state;
                await using var owned = await dbContext.Database.BeginTransactionAsync(token);
                try
                {
                    await Fence(token);
                    var answer = await action(token);
                    await Fence(token);
                    if (state.ReceiptProven)
                    {
                        if (identityDecisionAuditWriter.HasPendingDrafts)
                        {
                            await identityDecisionAuditWriter.WritePendingAsync(owned.GetDbTransaction(), token);
                            await Fence(token);
                        }
                        if (identityDecisionAuditWriter.HasPendingDrafts)
                            throw new InvalidOperationException("An identity decision cannot commit pending Audit drafts.");
                        await Fence(token);
                        await owned.CommitAsync(token);
                    }
                    else
                    {
                        // Native immutable replays and refusals after the race are reads. In particular,
                        // a success without a stored native receipt is never manufactured by this wrapper.
                        if (dbContext.ChangeTracker.HasChanges() || identityDecisionAuditWriter.HasPendingDrafts
                            || answer.Response.Result == CounterpartyCommandResult.Succeeded && !answer.IsReplay)
                            throw new InvalidOperationException("An identity decision result has no native receipt proof.");
                        await owned.RollbackAsync(CancellationToken.None);
                        dbContext.ChangeTracker.Clear();
                    }
                    return answer;
                }
                catch (Exception exception)
                {
                    await owned.RollbackAsync(CancellationToken.None);
                    await owned.DisposeAsync();
                    dbContext.ChangeTracker.Clear();
                    identityDecisionAuditWriter.DiscardPending();
                    if (canReadRace && !state.ReceiptProven && exception is DbUpdateException update
                        && state.KeyHash is { } keyHash && state.RequestHash is { } requestHash)
                        throw new IdentityDecisionSaveRace(update, keyHash, requestHash);
                    throw;
                }
            }
            finally
            {
                identityDecision = null;
                identityDecisionAuditWriter.DiscardPending();
            }
        }
    }

    private sealed class IdentityDecisionState(string commandName, Func<CancellationToken, Task> fence)
    {
        public string CommandName { get; } = commandName;
        public Func<CancellationToken, Task> Fence { get; } = fence;
        public string? KeyHash { get; set; }
        public string? RequestHash { get; set; }
        public bool ReceiptProven { get; set; }
    }
    private sealed class IdentityDecisionSaveRace(DbUpdateException inner, string keyHash, string requestHash)
        : Exception("Identity decision save race.", inner)
    {
        public string KeyHash { get; } = keyHash;
        public string RequestHash { get; } = requestHash;
    }
}
