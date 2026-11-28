using System.Data;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Organization.Application.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Organization.Infrastructure.Persistence;

internal sealed partial class OrganizationMasterDataService
{
    private OwnedCompanyTaxIdDecision? ownedCompanyTaxIdDecision;

    // Runtime tax-id interface only; the other native master/calendar interfaces do not expose Decide.
    internal IOrganizationCompanyTaxIdService WithCompanyTaxIdApprovalSafety(IAuditTransactionWriterV1? writer) =>
        new GuardedCompanyTaxIdService(this, writer);

    private void ValidateCompanyTaxIdDecisionEntry(Guid requestId, DecideCompanyTaxIdChange command, CommandMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateCommandMetadata(metadata);
        if (requestId == Guid.Empty) throw new ArgumentException("VKN değişiklik talebi kimliği boş olamaz.", nameof(requestId));
        if (!OrganizationCompanyTaxIdRules.IsDecision(command.Decision))
            throw new ArgumentException("Karar 'approve' veya 'reject' olmalıdır.", nameof(command));
        if (!OrganizationCompanyTaxIdRules.IsValidNote(command.Note))
            throw new ArgumentException("Karar notu tanımlı sınırlar içinde olmalıdır.", nameof(command));
        RequireExpectedVersion(command.ExpectedVersion);
    }

    private static CommandExecutionResult CompanyTaxIdDecisionUnavailable(CommandMetadata metadata) =>
        new(CommandResponses.For(CommandResult.DependencyUnavailable), false, metadata.CorrelationId);

    private async Task RequireCompanyTaxIdDecisionFenceAsync(CommandMetadata metadata, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Same()) throw new CompanyTaxIdDecisionRefusal(NotRecordedNotFound(metadata));
        var scope = await authorityRevalidationService.RevalidatePermissionScopeAsync(
            OrganizationCompanyTaxIdPermissionsV1.Approve, metadata.CompanyId, token);
        token.ThrowIfCancellationRequested();
        if (!IsCompanyWideGrant(scope) || !Same()) throw new CompanyTaxIdDecisionRefusal(NotRecordedNotFound(metadata));
        bool Same() => currentUser.IsAuthenticated && currentUser.UserId == metadata.ActorId
            && companyContext.IsResolved && companyContext.CompanyId == metadata.CompanyId
            && currentUser.CanAccessCompany(metadata.CompanyId);
    }

    private async Task<T> CompanyTaxIdDecisionAwaitAsync<T>(Func<CancellationToken, Task<T>> read,
        CommandMetadata metadata, CancellationToken token)
    {
        if (ownedCompanyTaxIdDecision is null) return await read(token);
        await RequireCompanyTaxIdDecisionFenceAsync(metadata, token);
        var value = await read(token);
        await RequireCompanyTaxIdDecisionFenceAsync(metadata, token);
        return value;
    }

    internal async Task<CommandExecutionResult> RunGuardedCompanyTaxIdDecisionAsync(Guid id,
        DecideCompanyTaxIdChange command, CommandMetadata metadata, IAuditTransactionWriterV1? writer, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ValidateCompanyTaxIdDecisionEntry(id, command, metadata);
        // Missing persistence composition never runs the native decision or opens a SQL connection.
        if (writer is null) return CompanyTaxIdDecisionUnavailable(metadata);
        Func<DbUpdateException, CancellationToken, Task<CommandResponseSnapshot?>>? recovery = null;
        DbUpdateException? raceFailure = null;
        try { return await AttemptAsync(false); }
        catch (CompanyTaxIdDecisionRace) { return await AttemptAsync(true); }

        async Task<CommandExecutionResult> AttemptAsync(bool replayOnly)
        {
            var cleanBefore = CleanEntry();
            try { await RequireCompanyTaxIdDecisionFenceAsync(metadata, token); }
            catch (CompanyTaxIdDecisionRefusal refusal) { return refusal.Result; }
            // A first awaited authority callback may itself introduce caller work. Never adopt or clear it.
            if (!cleanBefore || !CleanEntry()) return CompanyTaxIdDecisionUnavailable(metadata);
            var state = new OwnedCompanyTaxIdDecision(writer, replayOnly) { Recover = recovery, RaceFailure = raceFailure };
            IDbContextTransaction? transaction = null;
            var committed = false;
            ownedCompanyTaxIdDecision = state;
            try
            {
                transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                await RequireCompanyTaxIdDecisionFenceAsync(metadata, token);
                var result = await DecideCompanyTaxIdChangeAsync(id, command, metadata, token);
                await RequireCompanyTaxIdDecisionFenceAsync(metadata, token);
                RequireFlushed();
                if (result.Result == CommandResult.Succeeded && !result.IsReplay && !state.Saved)
                    throw new InvalidOperationException("VKN kararı kalıcı komut makbuzu olmadan tamamlanamaz.");
                await RequireCompanyTaxIdDecisionFenceAsync(metadata, token);
                RequireFlushed();
                await transaction.CommitAsync(token);
                committed = true;
                await RequireCompanyTaxIdDecisionFenceAsync(metadata, token);
                var completed = transaction;
                transaction = null;
                await completed.DisposeAsync();
                // Refusal here suppresses release; it never asserts that an already committed VKN decision rolled back.
                await RequireCompanyTaxIdDecisionFenceAsync(metadata, token);
                RequireFlushed();
                return result;
            }
            catch (Exception failure)
            {
                var deadlock = failure is SqlException { Number: 1205 }
                    or DbUpdateException { InnerException: SqlException { Number: 1205 } };
                var faults = new List<Exception>();
                if (transaction is not null)
                {
                    if (!committed) try
                    {
                        if (!deadlock || transaction.GetDbTransaction() is not SqlTransaction { Connection: null })
                            await transaction.RollbackAsync(CancellationToken.None);
                    }
                    catch (Exception error) { faults.Add(error); }
                    try { await transaction.DisposeAsync(); } catch (Exception error) { faults.Add(error); }
                }
                try { dbContext.ChangeTracker.Clear(); } catch (Exception error) { faults.Add(error); }
                try { writer.DiscardPending(); } catch (Exception error) { faults.Add(error); }
                if (faults.Count != 0) throw new AggregateException("VKN kararı temizlenemedi; yeniden yürütme güvenli değil.", [failure, .. faults]);
                if (failure is CompanyTaxIdDecisionRefusal refusal) return refusal.Result;
                if (!replayOnly && !committed && (deadlock || state.SaveAttempted && !state.Saved && IsCompanyTaxIdDecisionRace(failure)))
                {
                    recovery = state.Recover;
                    raceFailure = failure as DbUpdateException;
                    throw new CompanyTaxIdDecisionRace();
                }
                throw;
            }
            finally { ownedCompanyTaxIdDecision = null; }

            void RequireFlushed()
            {
                if (writer.HasPendingDrafts || dbContext.ChangeTracker.HasChanges())
                    throw new InvalidOperationException("VKN kararı kaydedilmemiş iş veya audit içeriyor.");
            }
        }
        bool CleanEntry() => ownedCompanyTaxIdDecision is null && dbContext.Database.IsRelational()
            && System.Transactions.Transaction.Current is null && dbContext.Database.CurrentTransaction is null
            && !dbContext.ChangeTracker.HasChanges() && !writer.HasPendingDrafts;
    }

    private static bool IsCompanyTaxIdDecisionRace(Exception failure) => failure is DbUpdateConcurrencyException
        or DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } };

    private async Task<CommandExecutionResult> CompleteCompanyTaxIdDecisionAsync(CommandRequest request, CommandMetadata metadata,
        CommandResponseSnapshot response, CancellationToken token)
    {
        if (ownedCompanyTaxIdDecision is null) return await CompleteAsync(request, metadata, response, token);
        if (ownedCompanyTaxIdDecision.ReplayOnly) return CompanyTaxIdDecisionUnavailable(metadata);
        AddCommandExecution(request, metadata, response);
        await SaveOwnedCompanyTaxIdDecisionAsync(metadata, token);
        return new(response, false, metadata.CorrelationId);
    }

    private Task<CommandExecutionResult> CompleteCompanyTaxIdDecisionMutationAsync(CommandRequest request, CommandMetadata metadata,
        CommandResponseSnapshot response, Func<DbUpdateException, CancellationToken, Task<CommandResponseSnapshot?>> recover,
        Guid entityId, CancellationToken token)
    {
        if (ownedCompanyTaxIdDecision is null)
            return CompleteMutationAsync(request, metadata, response, recover, entityId, token);
        ownedCompanyTaxIdDecision.Recover = recover;
        return CompleteCompanyTaxIdDecisionAsync(request, metadata, response, token);
    }

    private async Task<CommandExecutionResult> ReconcileCompanyTaxIdDecisionAsync(CommandMetadata metadata, CancellationToken token)
    {
        var state = ownedCompanyTaxIdDecision!;
        // Preserve the native readonly collision classification without writing another terminal receipt.
        if (state.RaceFailure is { } failure && state.Recover is { } recover)
        {
            var response = await recover(failure, token);
            if (response is not null) return new(response, false, metadata.CorrelationId);
        }
        return CompanyTaxIdDecisionUnavailable(metadata);
    }

    private async Task SaveOwnedCompanyTaxIdDecisionAsync(CommandMetadata metadata, CancellationToken token)
    {
        var state = ownedCompanyTaxIdDecision ?? throw new InvalidOperationException("VKN karar sahipliği yok.");
        if (state.ReplayOnly || dbContext.Database.CurrentTransaction is not { } transaction)
            throw new InvalidOperationException("VKN tekrar uzlaştırması yeni kayıt yazamaz.");
        await RequireCompanyTaxIdDecisionFenceAsync(metadata, token);
        if (state.Writer.HasPendingDrafts)
        {
            await state.Writer.WritePendingAsync(transaction.GetDbTransaction(), token);
            await RequireCompanyTaxIdDecisionFenceAsync(metadata, token);
        }
        if (state.Writer.HasPendingDrafts) throw new InvalidOperationException("VKN audit kaydı boşaltılamadı.");
        state.SaveAttempted = true;
        await dbContext.SaveChangesAsync(token);
        state.Saved = true;
        await RequireCompanyTaxIdDecisionFenceAsync(metadata, token);
    }

    private sealed record OwnedCompanyTaxIdDecision(IAuditTransactionWriterV1 Writer, bool ReplayOnly)
    {
        internal bool SaveAttempted; internal bool Saved;
        internal Func<DbUpdateException, CancellationToken, Task<CommandResponseSnapshot?>>? Recover;
        internal DbUpdateException? RaceFailure;
    }
    private sealed class CompanyTaxIdDecisionRefusal(CommandExecutionResult result) : Exception
    { internal CommandExecutionResult Result { get; } = result; }
    private sealed class CompanyTaxIdDecisionRace : Exception;
}

internal sealed class GuardedCompanyTaxIdService(OrganizationMasterDataService core, IAuditTransactionWriterV1? writer)
    : IOrganizationCompanyTaxIdService, IOrganizationCompanyTaxIdDecisionReadV1
{
    public Task<OrganizationCompanyTaxIdDecisionReadResultV1> ReadDecisionAsync(Guid requestId, Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default) =>
        core.ReadTaxIdDecisionAsync(requestId, freshConsumerFence, cancellationToken);
    public Task<OrganizationCompanyTaxIdDecisionReceiptV1?> ReadDecisionReceiptAsync(Guid requestId, DecideCompanyTaxIdChange command, string key,
        CommandExecutionResult original, Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default) =>
        core.ReadTaxIdDecisionReceiptAsync(requestId, command, key, original, freshConsumerFence, cancellationToken);
    public Task<CommandExecutionResult> DecideCompanyTaxIdChangeAsync(Guid id, DecideCompanyTaxIdChange command, CommandMetadata metadata, CancellationToken token) =>
        core.RunGuardedCompanyTaxIdDecisionAsync(id, command, metadata, writer, token);
    public Task<CompanyTaxIdView?> GetCompanyTaxIdAsync(CancellationToken token) => core.GetCompanyTaxIdAsync(token);
    public Task<CommandExecutionResult> RequestCompanyTaxIdChangeAsync(RequestCompanyTaxIdChange command, CommandMetadata metadata, CancellationToken token) => core.RequestCompanyTaxIdChangeAsync(command, metadata, token);
    public Task<CompanyTaxIdCorrectionsView?> GetCompanyTaxIdCorrectionsAsync(CancellationToken token) => core.GetCompanyTaxIdCorrectionsAsync(token);
    public Task<CommandExecutionResult> ApplyCompanyTaxIdCorrectionAsync(Guid id, ApplyCompanyTaxIdCorrection command, CommandMetadata metadata, CancellationToken token) => core.ApplyCompanyTaxIdCorrectionAsync(id, command, metadata, token);
}
