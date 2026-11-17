using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Domain;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Accounting.Application;


public sealed record ApproveAccountingInventoryCostV1(long ExpectedVersion);

public sealed class AccountingInventoryCostServiceV1(IAccountingInventoryCostStoreV1 store,
    IAccountingCommandLog commands, IErpAuthorityRevalidationService authority, ICompanyContext company,
    ICurrentUser user, ICorrelationContext correlation)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private sealed class DeniedException : Exception { }

    public async Task<AccountingApiResult<AccountingInventoryCostListV1>> ListAsync(AccountingInventoryCostKindV1 kind, CancellationToken cancellationToken = default)
    {
        if (!Capture(out var companyId, out var actor, out var actorText)) return Denied<AccountingInventoryCostListV1>();
        if (!Enum.IsDefined(kind)) return new(AccountingApiStatus.Invalid, null, false, AccountingApiCodes.RequestInvalid);
        var permission = Permission(kind, false);
        if (!await Allowed(permission, companyId, actor, actorText, cancellationToken))
        {
            permission = Permission(kind, true);
            if (!await Allowed(permission, companyId, actor, actorText, cancellationToken))
            {
                permission = AccountingJournalReversalPermissionNamesV1.Propose;
                if (!await Allowed(permission, companyId, actor, actorText, cancellationToken))
                {
                    permission = AccountingJournalReversalPermissionNamesV1.Approve;
                    if (!await Allowed(permission, companyId, actor, actorText, cancellationToken)) return Denied<AccountingInventoryCostListV1>();
                }
            }
        }
        try
        {
            var items = await store.ListAsync(kind, cancellationToken);
            if (!await Allowed(permission, companyId, actor, actorText, cancellationToken)) return Denied<AccountingInventoryCostListV1>();
            if (items.Any(x => x.CompanyId != companyId)) return Denied<AccountingInventoryCostListV1>();
            return new(AccountingApiStatus.Ok, new(companyId, items), false, null);
        }
        catch (AccountingStoreException)
        {
            return await Allowed(permission, companyId, actor, actorText, cancellationToken)
                ? new(AccountingApiStatus.Unavailable, null, false, "inventory_cost_source_unavailable") : Denied<AccountingInventoryCostListV1>();
        }
    }

    public async Task<AccountingApiResult<InvoiceLineCogsOptionsV1>> OptionsAsync(AccountingInventoryCostKindV1 kind, CancellationToken token = default)
    {
        if (!Enum.IsDefined(kind) || !Capture(out var companyId, out var actor, out var actorText)) return Denied<InvoiceLineCogsOptionsV1>();
        var permission = Permission(kind, false);
        if (!await Allowed(permission, companyId, actor, actorText, token))
        {
            permission = AccountingJournalReversalPermissionNamesV1.Propose;
            if (!await Allowed(permission, companyId, actor, actorText, token)) return Denied<InvoiceLineCogsOptionsV1>();
        }
        try
        {
            var result = await store.OptionsAsync(token);
            if (result.CompanyId != companyId || !await Allowed(permission, companyId, actor, actorText, token)) return Denied<InvoiceLineCogsOptionsV1>();
            return new(AccountingApiStatus.Ok, result, false, null);
        }
        catch (AccountingStoreException)
        {
            return await Allowed(permission, companyId, actor, actorText, token)
                ? new(AccountingApiStatus.Unavailable, null, false, "inventory_cost_account_plan_missing") : Denied<InvoiceLineCogsOptionsV1>();
        }
    }

    public Task<AccountingApiResult<AccountingInventoryCostProposalV1>> PrepareAsync(Guid id, AccountingInventoryCostKindV1 kind,
        PrepareAccountingInventoryCostV1 request, string key, CancellationToken cancellationToken = default) =>
        request is null || id == Guid.Empty || request.ExpectedVersion < 1 || !Enum.IsDefined(kind) ? InvalidVersionOrIdentity()
            : Run("prepare", Permission(kind, false), new { id, kind, request }, key,
                (actor, fence, metadata) => store.PrepareAsync(id, kind, request, actor, fence, metadata, cancellationToken), cancellationToken);

    public Task<AccountingApiResult<AccountingInventoryCostProposalV1>> ApproveAsync(Guid id, AccountingInventoryCostKindV1 kind, long version,
        string key, CancellationToken cancellationToken = default) => id == Guid.Empty || version < 1 || !Enum.IsDefined(kind)
            ? InvalidVersionOrIdentity() : Run("approve", Permission(kind, true), new { id, kind, version }, key,
                (actor, fence, metadata) => store.ApproveAsync(id, kind, version, actor, fence, metadata, cancellationToken), cancellationToken);

    public Task<AccountingApiResult<AccountingInventoryCostProposalV1>> ProposeReversalAsync(Guid id, ProposeAccountingInventoryCostReversalV1 request,
        string key, CancellationToken cancellationToken = default) => request is null || id == Guid.Empty || request.ExpectedVersion < 1
            ? InvalidVersionOrIdentity() : Run("propose-cancellation-reversal", AccountingJournalReversalPermissionNamesV1.Propose,
                new { id, request }, key, (actor, fence, metadata) => store.ProposeReversalAsync(id, request, actor, fence, metadata, cancellationToken), cancellationToken);

    private static string Permission(AccountingInventoryCostKindV1 kind, bool approve) => kind == AccountingInventoryCostKindV1.SalesInvoicedShipment
        ? approve ? NormalJournalPermissionNamesV1.DraftApprove : NormalJournalPermissionNamesV1.DraftCreate
        : approve ? AccountingJournalReversalPermissionNamesV1.Approve : AccountingJournalReversalPermissionNamesV1.Propose;

    private static Task<AccountingApiResult<AccountingInventoryCostProposalV1>> InvalidVersionOrIdentity() => Task.FromResult(
        new AccountingApiResult<AccountingInventoryCostProposalV1>(AccountingApiStatus.Invalid, null, false,
            AccountingApiCodes.RequestInvalid, "expectedVersion"));

    private async Task<AccountingApiResult<AccountingInventoryCostProposalV1>> Run(string action, string permission, object payload,
        string key, Func<Guid, Func<CancellationToken, Task>, AccountingCommandMetadata, Task<AccountingInventoryCostProposalV1>> write,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128 || payload is null)
            return new(AccountingApiStatus.Invalid, null, false, AccountingApiCodes.RequestInvalid);
        if (!Capture(out var companyId, out var actor, out var actorText)
            || !await Allowed(permission, companyId, actor, actorText, cancellationToken)) return Denied<AccountingInventoryCostProposalV1>();
        var command = "accounting.inventory-cost." + action + ".v1";
        var keyHash = AccountingApiService.Sha256(key.Trim());
        var requestHash = AccountingApiService.Sha256(command + "|" + JsonSerializer.Serialize(payload, Json));
        var prior = await commands.FindCommandAsync(command, keyHash, cancellationToken);
        if (!await Allowed(permission, companyId, actor, actorText, cancellationToken)) return Denied<AccountingInventoryCostProposalV1>();
        if (prior is not null) return Replay(prior, actorText, requestHash);
        var scope = await authority.RevalidatePermissionScopeAsync(permission, companyId, cancellationToken);
        if (!scope.IsGranted || !scope.IsCompanyWide || !Current(companyId, actor, actorText)) return Denied<AccountingInventoryCostProposalV1>();
        AccountingCommandRecord Complete(object value) => new(command, keyHash, actorText, requestHash,
            JsonSerializer.Serialize(new AccountingApiResult<AccountingInventoryCostProposalV1>(AccountingApiStatus.Ok,
                (AccountingInventoryCostProposalV1)value, false, null), Json), correlation.CorrelationId);
        var metadata = new AccountingCommandMetadata(actorText, permission, [.. scope.RoleCodes.Order(StringComparer.Ordinal)],
            correlation.CorrelationId, correlation.CausationId, key.Trim(), correlation.SourceChannel,
            correlation.ClientIpAddress, correlation.UserAgent, Complete);
        async Task Fence(CancellationToken token)
        {
            if (!await Allowed(permission, companyId, actor, actorText, token)) throw new DeniedException();
        }
        async Task<AccountingApiResult<AccountingInventoryCostProposalV1>> Terminal(AccountingApiResult<AccountingInventoryCostProposalV1> result)
        {
            if (!await Allowed(permission, companyId, actor, actorText, cancellationToken)) return Denied<AccountingInventoryCostProposalV1>();
            try
            {
                await store.RecordAttemptAsync(new(command, keyHash, actorText, requestHash,
                    JsonSerializer.Serialize(result, Json), correlation.CorrelationId), Fence, cancellationToken);
                return result;
            }
            catch (DeniedException) { return Denied<AccountingInventoryCostProposalV1>(); }
            catch (AccountingStoreException exception) when (exception.Code == AccountingStoreCodes.ConcurrentChange)
            {
                var winner = await commands.FindCommandAsync(command, keyHash, cancellationToken);
                if (!await Allowed(permission, companyId, actor, actorText, cancellationToken)) return Denied<AccountingInventoryCostProposalV1>();
                return winner is null ? result : Replay(winner, actorText, requestHash);
            }
        }
        try
        {
            var result = await write(actor, Fence, metadata);
            return new(AccountingApiStatus.Ok, result, false, null);
        }
        catch (DeniedException) { return Denied<AccountingInventoryCostProposalV1>(); }
        catch (AccountingVersionConflictException exception)
        {
            if (!await Allowed(permission, companyId, actor, actorText, cancellationToken)) return Denied<AccountingInventoryCostProposalV1>();
            return await Terminal(new(AccountingApiStatus.Conflict, null, false, AccountingApiCodes.VersionConflict, "expectedVersion",
                exception.ExpectedVersion, exception.CurrentVersion));
        }
        catch (AccountingStoreException exception)
        {
            if (exception.Code == AccountingStoreCodes.ConcurrentChange)
            {
                var winner = await commands.FindCommandAsync(command, keyHash, cancellationToken);
                if (!await Allowed(permission, companyId, actor, actorText, cancellationToken)) return Denied<AccountingInventoryCostProposalV1>();
                return winner is null ? new(AccountingApiStatus.Conflict, null, false, exception.Code) : Replay(winner, actorText, requestHash);
            }
            if (exception.Code == "inventory_cost_not_found")
            {
                if (!await Allowed(permission, companyId, actor, actorText, cancellationToken)) return Denied<AccountingInventoryCostProposalV1>();
                return await Terminal(new(AccountingApiStatus.NotFound, null, false, null));
            }
            if (!await Allowed(permission, companyId, actor, actorText, cancellationToken)) return Denied<AccountingInventoryCostProposalV1>();
            var refused = new AccountingApiResult<AccountingInventoryCostProposalV1>(exception.Code switch
            {
                AccountingStoreCodes.CompanyContextMismatch or "inventory_cost_approval_refused" or "inventory_cost_authority_denied" => AccountingApiStatus.Denied,
                "inventory_cost_not_found" => AccountingApiStatus.NotFound,
                "inventory_cost_preparation_stale" or "inventory_cost_source_conflict" or "inventory_cost_posting_conflict" => AccountingApiStatus.Conflict,
                "inventory_cost_source_unavailable" or "inventory_cost_tl_currency_unavailable" => AccountingApiStatus.Unavailable,
                _ => AccountingApiStatus.Unprocessable,
            }, null, false, exception.Code);
            return refused.Status is AccountingApiStatus.Unprocessable or AccountingApiStatus.Conflict ? await Terminal(refused) : refused;
        }
        catch (AccountingAmountScaleException exception)
        {
            if (exception.Code == AccountingAmountScaleCodes.ScaleExceeded)
                return await Terminal(new(AccountingApiStatus.Unprocessable, null, false, "inventory_cost_amount_unaccepted"));
            return await Allowed(permission, companyId, actor, actorText, cancellationToken)
                ? new(AccountingApiStatus.Unavailable, null, false, "inventory_cost_money_profile_unavailable") : Denied<AccountingInventoryCostProposalV1>();
        }
        catch (AccountingDomainException) { return await Terminal(new(AccountingApiStatus.Unprocessable, null, false, "inventory_cost_posting_refused")); }
        catch (SettingsPrerequisiteException)
        { return await Allowed(permission, companyId, actor, actorText, cancellationToken) ? new(AccountingApiStatus.Unavailable, null, false, null) : Denied<AccountingInventoryCostProposalV1>(); }
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
    private static AccountingApiResult<AccountingInventoryCostProposalV1> Replay(AccountingCommandRecord previous, string actor, string hash) =>
        previous.ActorId != actor || previous.RequestHash != hash
            ? new(AccountingApiStatus.Conflict, null, false, AccountingApiCodes.IdempotencyConflict)
            : (JsonSerializer.Deserialize<AccountingApiResult<AccountingInventoryCostProposalV1>>(previous.ResultJson, Json)
                ?? throw new InvalidOperationException("Maliyet farkı komut sonucu okunamadı."))
                with { IsReplay = true, OriginalCorrelationId = previous.OriginalCorrelationId };
}
