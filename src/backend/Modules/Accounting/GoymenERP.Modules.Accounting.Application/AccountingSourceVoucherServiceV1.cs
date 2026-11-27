using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Domain;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Accounting.Application;

public sealed partial class AccountingSourceVoucherServiceV1(IAccountingSourceVoucherStoreV1 store,
    IAccountingCommandLog commands, IErpAuthorityRevalidationService authority, ICompanyContext company,
    ICurrentUser user, ICorrelationContext correlation)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private sealed class DeniedException : Exception { }

    public async Task<AccountingApiResult<AccountingSourceVoucherListV1>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!Capture(out var companyId, out var actor, out var actorText)) return Denied<AccountingSourceVoucherListV1>();
        var permission = ErpPermissions.AccountingJournalsRead;
        if (!await Allowed(permission, companyId, actor, actorText, cancellationToken)) return Denied<AccountingSourceVoucherListV1>();
        IReadOnlyList<AccountingSourceVoucherViewV1> items;
        try { items = await store.ListAsync(cancellationToken); }
        catch (AccountingStoreException) { return new(AccountingApiStatus.Unavailable, null, false, null); }
        catch (SettingsPrerequisiteException) { return new(AccountingApiStatus.Unavailable, null, false, null); }
        if (!await Allowed(permission, companyId, actor, actorText, cancellationToken)) return Denied<AccountingSourceVoucherListV1>();
        if (items.Any(x => x.CompanyId != companyId)) return Denied<AccountingSourceVoucherListV1>();
        return new(AccountingApiStatus.Ok, new(companyId, items), false, null);
    }

    public Task<AccountingApiResult<AccountingSourceVoucherViewV1>> ApproveAsync(Guid id, long version,
        string key, CancellationToken cancellationToken = default) => id == Guid.Empty || version < 1
            ? InvalidVersionOrIdentity() : RunApprovalAsync(id, version, key, cancellationToken);

    private static Task<AccountingApiResult<AccountingSourceVoucherViewV1>> InvalidVersionOrIdentity() => Task.FromResult(
        new AccountingApiResult<AccountingSourceVoucherViewV1>(AccountingApiStatus.Invalid, null, false,
            AccountingApiCodes.RequestInvalid, "expectedVersion"));

    public Task<AccountingApiResult<AccountingSourceVoucherViewV1>> ProposeCancellationReversalAsync(Guid id,
        ProposeAccountingSourceVoucherReversalV1 request, string key, CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty || request.ExpectedVersion < 1) return InvalidVersionOrIdentity();
        if (request.ProposalId == Guid.Empty || request.PeriodId == Guid.Empty || request.TransactionDate == default
            || request.AccountingDate == default || request.ValueDate is { } valueDate && valueDate == default
            || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > AccountingJournalReversalPolicyV1.ReasonMaximumLength)
            return Task.FromResult(new AccountingApiResult<AccountingSourceVoucherViewV1>(AccountingApiStatus.Invalid, null, false,
                AccountingApiCodes.RequestInvalid));
        return Run("cancellation-reversal.propose", AccountingJournalReversalPermissionNamesV1.Propose,
            new { id, request }, key, (actor, fence, metadata) =>
                store.ProposeCancellationReversalAsync(id, request, actor, fence, metadata, cancellationToken), cancellationToken);
    }

    private async Task<AccountingApiResult<AccountingSourceVoucherViewV1>> Run(string action, string permission, object payload,
        string key, Func<Guid, Func<CancellationToken, Task>, AccountingCommandMetadata, Task<AccountingSourceVoucherViewV1>> write,
        CancellationToken cancellationToken, ApprovalCapture? approval = null, bool replayOnly = false, Guid approvalId = default, long approvalVersion = 0)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128 || payload is null)
            return new(AccountingApiStatus.Invalid, null, false, AccountingApiCodes.RequestInvalid);
        if (!Capture(out var companyId, out var actor, out var actorText)
            || !await AllowedRun(cancellationToken)) return Denied<AccountingSourceVoucherViewV1>();
        if (approval is not null && !Current(approval.CompanyId, approval.ActorId, approval.ActorText)) return Denied<AccountingSourceVoucherViewV1>();
        var command = "accounting.source-voucher." + action + ".v1";
        var keyHash = AccountingApiService.Sha256(key.Trim());
        var requestHash = AccountingApiService.Sha256(command + "|" + JsonSerializer.Serialize(payload, Json));
        var prior = await commands.FindCommandAsync(command, keyHash, cancellationToken);
        if (!await AllowedRun(cancellationToken)) return Denied<AccountingSourceVoucherViewV1>();
        if (prior is not null)
        {
            var replay = Replay(prior, actorText, requestHash);
            return approval is not null ? await AttachSourceVoucherReceiptAsync(replay, approvalId, approvalVersion, key, approval, cancellationToken, prior) : replay;
        }
        if (replayOnly) return new(AccountingApiStatus.Conflict, null, false, AccountingStoreCodes.ConcurrentChange);
        var scope = await authority.RevalidatePermissionScopeAsync(permission, companyId, cancellationToken);
        if (approval is not null) cancellationToken.ThrowIfCancellationRequested();
        if (approval is not null && scope is null) return Denied<AccountingSourceVoucherViewV1>();
        if (!scope!.IsGranted || !scope.IsCompanyWide || !Current(companyId, actor, actorText)) return Denied<AccountingSourceVoucherViewV1>();
        AccountingCommandRecord Complete(object value) => new(command, keyHash, actorText, requestHash,
            JsonSerializer.Serialize(new AccountingApiResult<AccountingSourceVoucherViewV1>(AccountingApiStatus.Ok,
                (AccountingSourceVoucherViewV1)value, false, null), Json), correlation.CorrelationId);
        var metadata = new AccountingCommandMetadata(actorText, permission, [.. scope.RoleCodes.Order(StringComparer.Ordinal)],
            correlation.CorrelationId, correlation.CausationId, key.Trim(), correlation.SourceChannel,
            correlation.ClientIpAddress, correlation.UserAgent, Complete);
        async Task Fence(CancellationToken token)
        {
            if (approval is not null) await ApprovalFenceAsync(approval, token);
            else if (!await Allowed(permission, companyId, actor, actorText, token)) throw new DeniedException();
        }
        async Task<AccountingApiResult<AccountingSourceVoucherViewV1>> Terminal(AccountingApiResult<AccountingSourceVoucherViewV1> result)
        {
            if (!await AllowedRun(cancellationToken)) return Denied<AccountingSourceVoucherViewV1>();
            try
            {
                var record = new AccountingCommandRecord(command, keyHash, actorText, requestHash,
                    JsonSerializer.Serialize(result, Json), correlation.CorrelationId);
                if (approval is not null) await store.RecordApprovalAttemptAsync(record, Fence, cancellationToken);
                else await commands.RecordCommandAsync(record, cancellationToken);
                if (approval is not null && !await AllowedRun(cancellationToken)) return Denied<AccountingSourceVoucherViewV1>();
                return result;
            }
            catch (AccountingStoreException exception) when (exception.Code == AccountingStoreCodes.ConcurrentChange)
            {
                if (approval is not null) throw new NormalJournalApprovalRaceException(exception.Code, exception);
                var winner = await commands.FindCommandAsync(command, keyHash, cancellationToken);
                if (!await AllowedRun(cancellationToken)) return Denied<AccountingSourceVoucherViewV1>();
                return winner is null ? result : Replay(winner, actorText, requestHash);
            }
        }
        try
        {
            var result = await write(actor, Fence, metadata);
            if (approval is not null && !await AllowedRun(cancellationToken)) return Denied<AccountingSourceVoucherViewV1>();
            var answer = new AccountingApiResult<AccountingSourceVoucherViewV1>(AccountingApiStatus.Ok, result, false, null);
            return approval is not null ? await AttachSourceVoucherReceiptAsync(answer, approvalId, approvalVersion, key, approval, cancellationToken) : answer;
        }
        catch (DeniedException) { return Denied<AccountingSourceVoucherViewV1>(); }
        catch (AccountingVersionConflictException exception)
        {
            if (!await AllowedRun(cancellationToken)) return Denied<AccountingSourceVoucherViewV1>();
            return await Terminal(new(AccountingApiStatus.Conflict, null, false, AccountingApiCodes.VersionConflict, "expectedVersion",
                exception.ExpectedVersion, exception.CurrentVersion));
        }
        catch (AccountingStoreException exception)
        {
            if (!await AllowedRun(cancellationToken)) return Denied<AccountingSourceVoucherViewV1>();
            if (exception.Code == AccountingStoreCodes.ConcurrentChange)
            {
                if (approval is not null) throw new NormalJournalApprovalRaceException(exception.Code, exception);
                var winner = await commands.FindCommandAsync(command, keyHash, cancellationToken);
                if (!await AllowedRun(cancellationToken)) return Denied<AccountingSourceVoucherViewV1>();
                return winner is null ? new(AccountingApiStatus.Conflict, null, false, exception.Code) : Replay(winner, actorText, requestHash);
            }
            if (exception.Code == "source_voucher_not_found")
            {
                if (!await AllowedRun(cancellationToken)) return Denied<AccountingSourceVoucherViewV1>();
                return await Terminal(new(AccountingApiStatus.NotFound, null, false, null));
            }
            if (exception.Code is "source_voucher_not_approvable" or "source_voucher_reversal_not_proposable")
                return await Terminal(new(AccountingApiStatus.Conflict, null, false, exception.Code));
            return new(exception.Code switch
            {
                AccountingStoreCodes.CompanyContextMismatch or "source_voucher_approval_refused" => AccountingApiStatus.Denied,
                "source_voucher_not_found" => AccountingApiStatus.NotFound,
                "source_voucher_source_changed" or "source_voucher_source_conflict" or "source_voucher_posting_conflict" => AccountingApiStatus.Conflict,
                AccountingJournalReversalStoreCodesV1.ContentConflict => AccountingApiStatus.Conflict,
                "source_voucher_tl_currency_unavailable" => AccountingApiStatus.Unavailable,
                _ => AccountingApiStatus.Unprocessable,
            }, null, false, exception.Code);
        }
        catch (AccountingAmountScaleException)
        {
            if (!await AllowedRun(cancellationToken)) return Denied<AccountingSourceVoucherViewV1>();
            return new(AccountingApiStatus.Unprocessable, null, false, "source_voucher_amount_unaccepted");
        }
        catch (AccountingDomainException)
        {
            if (!await AllowedRun(cancellationToken)) return Denied<AccountingSourceVoucherViewV1>();
            return new(AccountingApiStatus.Unprocessable, null, false, "source_voucher_posting_refused");
        }
        catch (SettingsPrerequisiteException) { return new(AccountingApiStatus.Unavailable, null, false, null); }
        async Task<bool> AllowedRun(CancellationToken token)
        {
            if (approval is null) return await Allowed(permission, companyId, actor, actorText, token);
            try { await ApprovalFenceAsync(approval, token); return true; }
            catch (NormalJournalAuthorityDeniedException) { return false; }
        }
    }

    private async Task<bool> Allowed(string permission, Guid companyId, Guid actor, string actorText, CancellationToken cancellationToken)
    {
        if (!Current(companyId, actor, actorText)) return false;
        var scope = await authority.RevalidatePermissionScopeAsync(permission, companyId, cancellationToken);
        return scope.IsGranted && scope.IsCompanyWide && Current(companyId, actor, actorText);
    }
    private bool Capture(out Guid companyId, out Guid actor, out string actorText)
    {
        companyId = company.IsResolved ? company.CompanyId : Guid.Empty;
        actorText = user.UserId ?? "";
        return Guid.TryParse(actorText, out actor) && Current(companyId, actor, actorText);
    }
    private bool Current(Guid companyId, Guid actor, string actorText) => company.IsResolved && company.CompanyId == companyId
        && companyId != Guid.Empty && actor != Guid.Empty && user.IsAuthenticated && user.UserId == actorText
        && user.CanAccessCompany(companyId) && correlation.IsResolved;
    private static AccountingApiResult<T> Denied<T>() where T : class => new(AccountingApiStatus.Denied, null, false, null);
    private static AccountingApiResult<AccountingSourceVoucherViewV1> Replay(AccountingCommandRecord previous, string actor, string hash) =>
        previous.ActorId != actor || previous.RequestHash != hash
            ? new(AccountingApiStatus.Conflict, null, false, AccountingApiCodes.IdempotencyConflict)
            : (JsonSerializer.Deserialize<AccountingApiResult<AccountingSourceVoucherViewV1>>(previous.ResultJson, Json)
                ?? throw new InvalidOperationException("Kaynak fiş komut sonucu okunamadı."))
                with { IsReplay = true, OriginalCorrelationId = previous.OriginalCorrelationId };
}
