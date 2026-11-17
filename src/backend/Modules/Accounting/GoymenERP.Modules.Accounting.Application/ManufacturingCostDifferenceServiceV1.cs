using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Domain;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Accounting.Application;

public sealed record ManufacturingCostDifferenceListV1(Guid CompanyId, IReadOnlyList<ManufacturingCostDifferenceViewV1> Items);
public sealed record ApproveManufacturingCostDifferenceV1(long ExpectedVersion);

public sealed class ManufacturingCostDifferenceServiceV1(IManufacturingCostDifferenceStoreV1 store,
    IAccountingCommandLog commands, IErpAuthorityRevalidationService authority, ICompanyContext company,
    ICurrentUser user, ICorrelationContext correlation)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private sealed class DeniedException : Exception { }

    public async Task<AccountingApiResult<ManufacturingCostDifferenceListV1>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!Capture(out var companyId, out var actor, out var actorText)) return Denied<ManufacturingCostDifferenceListV1>();
        var permission = AccountingJournalReversalPermissionNamesV1.Propose;
        if (!await Allowed(permission, companyId, actor, actorText, cancellationToken))
        {
            permission = AccountingJournalReversalPermissionNamesV1.Approve;
            if (!await Allowed(permission, companyId, actor, actorText, cancellationToken)) return Denied<ManufacturingCostDifferenceListV1>();
        }
        var items = await store.ListAsync(cancellationToken);
        if (!await Allowed(permission, companyId, actor, actorText, cancellationToken)) return Denied<ManufacturingCostDifferenceListV1>();
        if (items.Any(x => x.CompanyId != companyId)) return Denied<ManufacturingCostDifferenceListV1>();
        return new(AccountingApiStatus.Ok, new(companyId, items), false, null);
    }

    public Task<AccountingApiResult<ManufacturingCostDifferenceViewV1>> PrepareAsync(PrepareManufacturingCostDifferenceV1 request,
        string key, CancellationToken cancellationToken = default) => request is null || request.ProposalId == Guid.Empty
            || request.ExpectedVersion < 1 ? InvalidVersionOrIdentity() : Run("prepare", AccountingJournalReversalPermissionNamesV1.Propose,
                request, key, (actor, fence, metadata) => store.PrepareAsync(request, actor, fence, metadata, cancellationToken), cancellationToken);

    public Task<AccountingApiResult<ManufacturingCostDifferenceViewV1>> ApproveAsync(Guid id, long version,
        string key, CancellationToken cancellationToken = default) => id == Guid.Empty || version < 1
            ? InvalidVersionOrIdentity() : Run("approve", AccountingJournalReversalPermissionNamesV1.Approve,
            new { id, version }, key, (actor, fence, metadata) => store.ApproveAsync(id, version, actor, fence, metadata, cancellationToken), cancellationToken);

    private static Task<AccountingApiResult<ManufacturingCostDifferenceViewV1>> InvalidVersionOrIdentity() => Task.FromResult(
        new AccountingApiResult<ManufacturingCostDifferenceViewV1>(AccountingApiStatus.Invalid, null, false,
            AccountingApiCodes.RequestInvalid, "expectedVersion"));

    private async Task<AccountingApiResult<ManufacturingCostDifferenceViewV1>> Run(string action, string permission, object payload,
        string key, Func<Guid, Func<CancellationToken, Task>, AccountingCommandMetadata, Task<ManufacturingCostDifferenceViewV1>> write,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128 || payload is null)
            return new(AccountingApiStatus.Invalid, null, false, AccountingApiCodes.RequestInvalid);
        if (!Capture(out var companyId, out var actor, out var actorText)
            || !await Allowed(permission, companyId, actor, actorText, cancellationToken)) return Denied<ManufacturingCostDifferenceViewV1>();
        var command = "accounting.manufacturing-cost-difference." + action + ".v1";
        var keyHash = AccountingApiService.Sha256(key.Trim());
        var requestHash = AccountingApiService.Sha256(command + "|" + JsonSerializer.Serialize(payload, Json));
        var prior = await commands.FindCommandAsync(command, keyHash, cancellationToken);
        if (!await Allowed(permission, companyId, actor, actorText, cancellationToken)) return Denied<ManufacturingCostDifferenceViewV1>();
        if (prior is not null) return Replay(prior, actorText, requestHash);
        var scope = await authority.RevalidatePermissionScopeAsync(permission, companyId, cancellationToken);
        if (!scope.IsGranted || !scope.IsCompanyWide || !Current(companyId, actor, actorText)) return Denied<ManufacturingCostDifferenceViewV1>();
        AccountingCommandRecord Complete(object value) => new(command, keyHash, actorText, requestHash,
            JsonSerializer.Serialize(new AccountingApiResult<ManufacturingCostDifferenceViewV1>(AccountingApiStatus.Ok,
                (ManufacturingCostDifferenceViewV1)value, false, null), Json), correlation.CorrelationId);
        var metadata = new AccountingCommandMetadata(actorText, permission, [.. scope.RoleCodes.Order(StringComparer.Ordinal)],
            correlation.CorrelationId, correlation.CausationId, key.Trim(), correlation.SourceChannel,
            correlation.ClientIpAddress, correlation.UserAgent, Complete);
        async Task Fence(CancellationToken token)
        {
            if (!await Allowed(permission, companyId, actor, actorText, token)) throw new DeniedException();
        }
        async Task<AccountingApiResult<ManufacturingCostDifferenceViewV1>> Terminal(AccountingApiResult<ManufacturingCostDifferenceViewV1> result)
        {
            if (!await Allowed(permission, companyId, actor, actorText, cancellationToken)) return Denied<ManufacturingCostDifferenceViewV1>();
            try
            {
                await commands.RecordCommandAsync(new(command, keyHash, actorText, requestHash,
                    JsonSerializer.Serialize(result, Json), correlation.CorrelationId), cancellationToken);
                return result;
            }
            catch (AccountingStoreException exception) when (exception.Code == AccountingStoreCodes.ConcurrentChange)
            {
                var winner = await commands.FindCommandAsync(command, keyHash, cancellationToken);
                if (!await Allowed(permission, companyId, actor, actorText, cancellationToken)) return Denied<ManufacturingCostDifferenceViewV1>();
                return winner is null ? result : Replay(winner, actorText, requestHash);
            }
        }
        try
        {
            var result = await write(actor, Fence, metadata);
            return new(AccountingApiStatus.Ok, result, false, null);
        }
        catch (DeniedException) { return Denied<ManufacturingCostDifferenceViewV1>(); }
        catch (AccountingVersionConflictException exception)
        {
            if (!await Allowed(permission, companyId, actor, actorText, cancellationToken)) return Denied<ManufacturingCostDifferenceViewV1>();
            return await Terminal(new(AccountingApiStatus.Conflict, null, false, AccountingApiCodes.VersionConflict, "expectedVersion",
                exception.ExpectedVersion, exception.CurrentVersion));
        }
        catch (AccountingStoreException exception)
        {
            if (exception.Code == AccountingStoreCodes.ConcurrentChange)
            {
                var winner = await commands.FindCommandAsync(command, keyHash, cancellationToken);
                if (!await Allowed(permission, companyId, actor, actorText, cancellationToken)) return Denied<ManufacturingCostDifferenceViewV1>();
                return winner is null ? new(AccountingApiStatus.Conflict, null, false, exception.Code) : Replay(winner, actorText, requestHash);
            }
            if (exception.Code == "cost_difference_not_found")
            {
                if (!await Allowed(permission, companyId, actor, actorText, cancellationToken)) return Denied<ManufacturingCostDifferenceViewV1>();
                return await Terminal(new(AccountingApiStatus.NotFound, null, false, null));
            }
            return new(exception.Code switch
            {
                AccountingStoreCodes.CompanyContextMismatch or "cost_difference_approval_refused" => AccountingApiStatus.Denied,
                "cost_difference_not_found" => AccountingApiStatus.NotFound,
                "cost_difference_source_changed" or "cost_difference_source_conflict" or "cost_difference_posting_conflict" => AccountingApiStatus.Conflict,
                "cost_difference_tl_currency_unavailable" => AccountingApiStatus.Unavailable,
                _ => AccountingApiStatus.Unprocessable,
            }, null, false, exception.Code);
        }
        catch (AccountingAmountScaleException) { return new(AccountingApiStatus.Unprocessable, null, false, "cost_difference_amount_unaccepted"); }
        catch (AccountingDomainException) { return new(AccountingApiStatus.Unprocessable, null, false, "cost_difference_posting_refused"); }
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
    private static AccountingApiResult<ManufacturingCostDifferenceViewV1> Replay(AccountingCommandRecord previous, string actor, string hash) =>
        previous.ActorId != actor || previous.RequestHash != hash
            ? new(AccountingApiStatus.Conflict, null, false, AccountingApiCodes.IdempotencyConflict)
            : (JsonSerializer.Deserialize<AccountingApiResult<ManufacturingCostDifferenceViewV1>>(previous.ResultJson, Json)
                ?? throw new InvalidOperationException("Maliyet farkı komut sonucu okunamadı."))
                with { IsReplay = true, OriginalCorrelationId = previous.OriginalCorrelationId };
}
