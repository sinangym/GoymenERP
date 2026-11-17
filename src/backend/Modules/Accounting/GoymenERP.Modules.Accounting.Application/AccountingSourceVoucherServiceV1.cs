using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Domain;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Accounting.Application;

public sealed class AccountingSourceVoucherServiceV1(IAccountingSourceVoucherStoreV1 store,
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
            ? InvalidVersionOrIdentity() : Run("approve", NormalJournalPermissionNamesV1.DraftApprove,
            new { id, version }, key, (actor, fence, metadata) => store.ApproveAsync(id, version, actor, fence, metadata, cancellationToken), cancellationToken);

    private static Task<AccountingApiResult<AccountingSourceVoucherViewV1>> InvalidVersionOrIdentity() => Task.FromResult(
        new AccountingApiResult<AccountingSourceVoucherViewV1>(AccountingApiStatus.Invalid, null, false,
            AccountingApiCodes.RequestInvalid, "expectedVersion"));

    private async Task<AccountingApiResult<AccountingSourceVoucherViewV1>> Run(string action, string permission, object payload,
        string key, Func<Guid, Func<CancellationToken, Task>, AccountingCommandMetadata, Task<AccountingSourceVoucherViewV1>> write,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128 || payload is null)
            return new(AccountingApiStatus.Invalid, null, false, AccountingApiCodes.RequestInvalid);
        if (!Capture(out var companyId, out var actor, out var actorText)
            || !await Allowed(permission, companyId, actor, actorText, cancellationToken)) return Denied<AccountingSourceVoucherViewV1>();
        var command = "accounting.source-voucher." + action + ".v1";
        var keyHash = AccountingApiService.Sha256(key.Trim());
        var requestHash = AccountingApiService.Sha256(command + "|" + JsonSerializer.Serialize(payload, Json));
        var prior = await commands.FindCommandAsync(command, keyHash, cancellationToken);
        if (!await Allowed(permission, companyId, actor, actorText, cancellationToken)) return Denied<AccountingSourceVoucherViewV1>();
        if (prior is not null) return Replay(prior, actorText, requestHash);
        var scope = await authority.RevalidatePermissionScopeAsync(permission, companyId, cancellationToken);
        if (!scope.IsGranted || !scope.IsCompanyWide || !Current(companyId, actor, actorText)) return Denied<AccountingSourceVoucherViewV1>();
        AccountingCommandRecord Complete(object value) => new(command, keyHash, actorText, requestHash,
            JsonSerializer.Serialize(new AccountingApiResult<AccountingSourceVoucherViewV1>(AccountingApiStatus.Ok,
                (AccountingSourceVoucherViewV1)value, false, null), Json), correlation.CorrelationId);
        var metadata = new AccountingCommandMetadata(actorText, permission, [.. scope.RoleCodes.Order(StringComparer.Ordinal)],
            correlation.CorrelationId, correlation.CausationId, key.Trim(), correlation.SourceChannel,
            correlation.ClientIpAddress, correlation.UserAgent, Complete);
        async Task Fence(CancellationToken token)
        {
            if (!await Allowed(permission, companyId, actor, actorText, token)) throw new DeniedException();
        }
        async Task<AccountingApiResult<AccountingSourceVoucherViewV1>> Terminal(AccountingApiResult<AccountingSourceVoucherViewV1> result)
        {
            if (!await Allowed(permission, companyId, actor, actorText, cancellationToken)) return Denied<AccountingSourceVoucherViewV1>();
            try
            {
                await commands.RecordCommandAsync(new(command, keyHash, actorText, requestHash,
                    JsonSerializer.Serialize(result, Json), correlation.CorrelationId), cancellationToken);
                return result;
            }
            catch (AccountingStoreException exception) when (exception.Code == AccountingStoreCodes.ConcurrentChange)
            {
                var winner = await commands.FindCommandAsync(command, keyHash, cancellationToken);
                if (!await Allowed(permission, companyId, actor, actorText, cancellationToken)) return Denied<AccountingSourceVoucherViewV1>();
                return winner is null ? result : Replay(winner, actorText, requestHash);
            }
        }
        try
        {
            var result = await write(actor, Fence, metadata);
            return new(AccountingApiStatus.Ok, result, false, null);
        }
        catch (DeniedException) { return Denied<AccountingSourceVoucherViewV1>(); }
        catch (AccountingVersionConflictException exception)
        {
            if (!await Allowed(permission, companyId, actor, actorText, cancellationToken)) return Denied<AccountingSourceVoucherViewV1>();
            return await Terminal(new(AccountingApiStatus.Conflict, null, false, AccountingApiCodes.VersionConflict, "expectedVersion",
                exception.ExpectedVersion, exception.CurrentVersion));
        }
        catch (AccountingStoreException exception)
        {
            if (!await Allowed(permission, companyId, actor, actorText, cancellationToken)) return Denied<AccountingSourceVoucherViewV1>();
            if (exception.Code == AccountingStoreCodes.ConcurrentChange)
            {
                var winner = await commands.FindCommandAsync(command, keyHash, cancellationToken);
                if (!await Allowed(permission, companyId, actor, actorText, cancellationToken)) return Denied<AccountingSourceVoucherViewV1>();
                return winner is null ? new(AccountingApiStatus.Conflict, null, false, exception.Code) : Replay(winner, actorText, requestHash);
            }
            if (exception.Code == "source_voucher_not_found")
            {
                if (!await Allowed(permission, companyId, actor, actorText, cancellationToken)) return Denied<AccountingSourceVoucherViewV1>();
                return await Terminal(new(AccountingApiStatus.NotFound, null, false, null));
            }
            if (exception.Code == "source_voucher_not_approvable")
                return await Terminal(new(AccountingApiStatus.Conflict, null, false, exception.Code));
            return new(exception.Code switch
            {
                AccountingStoreCodes.CompanyContextMismatch or "source_voucher_approval_refused" => AccountingApiStatus.Denied,
                "source_voucher_not_found" => AccountingApiStatus.NotFound,
                "source_voucher_source_changed" or "source_voucher_source_conflict" or "source_voucher_posting_conflict" => AccountingApiStatus.Conflict,
                "source_voucher_tl_currency_unavailable" => AccountingApiStatus.Unavailable,
                _ => AccountingApiStatus.Unprocessable,
            }, null, false, exception.Code);
        }
        catch (AccountingAmountScaleException)
        {
            if (!await Allowed(permission, companyId, actor, actorText, cancellationToken)) return Denied<AccountingSourceVoucherViewV1>();
            return new(AccountingApiStatus.Unprocessable, null, false, "source_voucher_amount_unaccepted");
        }
        catch (AccountingDomainException)
        {
            if (!await Allowed(permission, companyId, actor, actorText, cancellationToken)) return Denied<AccountingSourceVoucherViewV1>();
            return new(AccountingApiStatus.Unprocessable, null, false, "source_voucher_posting_refused");
        }
        catch (SettingsPrerequisiteException) { return new(AccountingApiStatus.Unavailable, null, false, null); }
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
