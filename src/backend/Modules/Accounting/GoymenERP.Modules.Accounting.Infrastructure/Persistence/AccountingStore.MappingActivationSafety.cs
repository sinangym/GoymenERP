using System.Text.Json;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Audit.Application.Contracts;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

public sealed partial class AccountingStore
{
    private AccountingPostingAccountMappingStoreV1? ownedMappingActivationCaller;

    internal async Task<AccountingApiResult<AccountingPostingAccountMappingViewV1>> RunMappingActivationAsync(
        AccountingPostingAccountMappingStoreV1 caller, AccountingDbContext candidateDb, IAuditEventCollector candidateCollector,
        Func<bool, Task<AccountingApiResult<AccountingPostingAccountMappingViewV1>>> attempt,
        Func<CancellationToken, Task> fence, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(attempt); ArgumentNullException.ThrowIfNull(fence);
        if (ownedMappingActivationCaller is not null || !SameMappingScope(candidateDb, candidateCollector))
            return new(AccountingApiStatus.Unavailable, null, false, null);
        // Retain the exact caller through commit validation, including a saved native maker refusal.
        ownedMappingActivationCaller = caller;
        try
        {
            return await RunOwnedAccountingApprovalAsync(AccountingPostingAccountMappingCommandNamesV1.Activate, replayOnly =>
            {
                RequireOwnedMappingActivation(caller, candidateDb, candidateCollector);
                return attempt(replayOnly);
            }, fence, token);
        }
        finally { ownedMappingActivationCaller = null; }
    }
    private bool SameMappingScope(AccountingDbContext candidateDb, IAuditEventCollector candidateCollector) =>
        ReferenceEquals(dbContext, candidateDb) && ReferenceEquals(auditEvents, candidateCollector) && normalApprovalAuditWriter is not null;
    internal bool IsOwnedMappingActivation(AccountingPostingAccountMappingStoreV1 caller, AccountingDbContext candidateDb, IAuditEventCollector candidateCollector) =>
        ReferenceEquals(ownedMappingActivationCaller, caller) && SameMappingScope(candidateDb, candidateCollector)
        && ownedNormalApproval?.CommandName == AccountingPostingAccountMappingCommandNamesV1.Activate;
    private void RequireOwnedMappingActivation(AccountingPostingAccountMappingStoreV1 caller, AccountingDbContext candidateDb, IAuditEventCollector candidateCollector)
    {
        if (!IsOwnedMappingActivation(caller, candidateDb, candidateCollector))
            throw new InvalidOperationException("Mapping activation cannot borrow another store, context, collector or command marker.");
    }
    internal Task MappingActivationFenceAsync(AccountingPostingAccountMappingStoreV1 caller, AccountingDbContext candidateDb, IAuditEventCollector candidateCollector, CancellationToken token)
    { RequireOwnedMappingActivation(caller, candidateDb, candidateCollector); return NormalApprovalFence(token); }
    internal void CompleteOwnedMappingActivation(AccountingPostingAccountMappingStoreV1 caller, AccountingDbContext candidateDb, IAuditEventCollector candidateCollector, AccountingCommandRecord command)
    { RequireOwnedMappingActivation(caller, candidateDb, candidateCollector); AddCommand(RequireResolvedCompany(), command); }
    internal Task SaveOwnedMappingActivationAsync(AccountingPostingAccountMappingStoreV1 caller, AccountingDbContext candidateDb, IAuditEventCollector candidateCollector, CancellationToken token)
    { RequireOwnedMappingActivation(caller, candidateDb, candidateCollector); return SaveOwnedNormalApprovalAsync(token); }

    private bool CanCommitOwnedMappingSelfActivation<T>(string commandName, AccountingApiResult<T> result, OwnedNormalApproval state) where T : class =>
        commandName == AccountingPostingAccountMappingCommandNamesV1.Activate
        && state.CommandName == commandName && !state.ReplayOnly
        && ownedMappingActivationCaller is { } caller && caller.IsOwnedBy(this)
        && result is AccountingApiResult<AccountingPostingAccountMappingViewV1>
        {
            Status: AccountingApiStatus.Denied, Code: AccountingPostingAccountMappingCodesV1.SelfActivation,
            Field: "activation", Resource: null, IsReplay: false, OriginalCorrelationId: null,
            ExpectedVersion: null, CurrentVersion: null
        }
        && state.Saved && state.Command is { } command && command.CommandName == commandName
        && command.ResultJson == JsonSerializer.Serialize(result with { IsReplay = false, OriginalCorrelationId = null }, Json);
}
