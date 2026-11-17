using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Accounting.Domain;

namespace GoymenERP.Modules.Accounting.Application;

public sealed class InvoiceLineCogsCorrectionServiceV1(IInvoiceLineCogsCorrectionStoreV1 store,
    IAccountingFinalizedInvoiceLineSourceV1 source, IAccountingInvoiceLineProductReferenceV1 products, IAccountingCommandLog commands,
    IErpAuthorityRevalidationService authority, ICompanyContext company, ICurrentUser user, ICorrelationContext correlation)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private sealed class AuthorityChanged : Exception { }

    public Task<AccountingApiResult<InvoiceLineCogsOptionsV1>> OptionsAsync(CancellationToken token = default) =>
        Read(async () => await store.OptionsAsync(token), token);
    public Task<AccountingApiResult<FinalizedInvoiceCogsSelectionV1>> FindSourceAsync(string officialInvoiceNumber, CancellationToken token = default) =>
        Read(async () =>
        {
            if (string.IsNullOrWhiteSpace(officialInvoiceNumber) || officialInvoiceNumber.Length > 64 || officialInvoiceNumber != officialInvoiceNumber.Trim())
                throw new AccountingStoreException(InvoiceLineCogsCorrectionCodesV1.RequestInvalid, "Resmî fatura numarası zorunludur.");
            var captured = company.CompanyId;
            var found = await source.FindAsync(captured, officialInvoiceNumber, token);
            if (found.CompanyId != captured || found.Outcome == FinalizedInvoiceLineSourceOutcomeV1.Unavailable)
                throw new AccountingStoreException(InvoiceLineCogsCorrectionCodesV1.SourceUnavailable, "Fatura kaynağı okunamadı.");
            if (found.Outcome == FinalizedInvoiceLineSourceOutcomeV1.NotFound)
                throw new AccountingStoreException(InvoiceLineCogsCorrectionCodesV1.NotFound, "Kesinleşmiş fatura bulunamadı.");
            if (found.Outcome != FinalizedInvoiceLineSourceOutcomeV1.Available || found.Selection is not { } selection
                || selection.CompanyId != captured || selection.InvoiceId == Guid.Empty || selection.Ettn == Guid.Empty
                || selection.OfficialInvoiceNumber != officialInvoiceNumber
                || selection.Lines.Count == 0 || selection.Lines.Select(x => x.InvoiceLineId).Distinct().Count() != selection.Lines.Count
                || selection.Lines.Any(x => x.CompanyId != captured || x.InvoiceId != selection.InvoiceId || x.Ettn != selection.Ettn
                    || x.InvoiceLineId == Guid.Empty || x.InvoiceLineNo < 1 || x.FinalizationAdmissionId == Guid.Empty || x.FinalizedVersion < 1
                    || x.ProductId == Guid.Empty || x.ShipmentId == Guid.Empty || x.DispatchNoteId == Guid.Empty || x.DispatchNoteLineNo < 1
                    || x.OfficialInvoiceNumber != selection.OfficialInvoiceNumber || x.InvoiceDate == default || !ValidFingerprint(x.Fingerprint)))
                throw new AccountingStoreException(InvoiceLineCogsCorrectionCodesV1.SourceUnavailable, "Fatura satırı kaynağı doğrulanamadı.");
            return selection;
        }, token);
    public Task<AccountingApiResult<InvoiceLineCogsHistoryV1>> ReadAsync(Guid invoiceId, Guid lineId, CancellationToken token = default) =>
        Read(async () =>
        {
            var history = await store.ReadAsync(await Source(invoiceId, lineId, token), token);
            var product = await products.GetAsync(history.CompanyId, history.Source.ProductId, token);
            if (product.CompanyId != history.CompanyId || product.ProductId != history.Source.ProductId)
                throw new AccountingStoreException(InvoiceLineCogsCorrectionCodesV1.SourceUnavailable, "Ürün sınıfı kaynağı doğrulanamadı.");
            return history with { ProductReference = product };
        }, token);

    private async Task<AccountingApiResult<T>> Read<T>(Func<Task<T>> read, CancellationToken token) where T : class
    {
        if (!Capture(out var companyId, out var actor, out var text)) return Denied<T>();
        var permission = AccountingJournalReversalPermissionNamesV1.Propose;
        if (!await Allowed(permission, companyId, actor, text, token))
        {
            permission = AccountingJournalReversalPermissionNamesV1.Approve;
            if (!await Allowed(permission, companyId, actor, text, token)) return Denied<T>();
        }
        try
        {
            var result = await read();
            return await Allowed(permission, companyId, actor, text, token) ? new(AccountingApiStatus.Ok, result, false, null) : Denied<T>();
        }
        catch (AccountingStoreException exception)
        {
            if (!await Allowed(permission, companyId, actor, text, token)) return Denied<T>();
            return new(Status(exception.Code), null, false, exception.Code);
        }
        catch (System.Data.Common.DbException) { return new(AccountingApiStatus.Unavailable, null, false, InvoiceLineCogsCorrectionCodesV1.SourceUnavailable); }
    }

    public Task<AccountingApiResult<InvoiceLineCogsCorrectionViewV1>> PrepareAsync(Guid invoiceId, Guid lineId,
        PrepareInvoiceLineCogsCorrectionV1 request, string key, CancellationToken token = default) =>
        Run("prepare", AccountingJournalReversalPermissionNamesV1.Propose, invoiceId, lineId, request, key,
            async (facts, actor, fence, metadata) => await store.PrepareAsync(facts, request, actor, fence, metadata, token), token);

    public Task<AccountingApiResult<InvoiceLineCogsCorrectionViewV1>> ApproveAsync(Guid invoiceId, Guid lineId, Guid proposalId,
        ApproveInvoiceLineCogsCorrectionV1 request, string key, CancellationToken token = default) =>
        Run("approve", AccountingJournalReversalPermissionNamesV1.Approve, invoiceId, lineId, new { proposalId, request }, key,
            async (facts, actor, fence, metadata) => await store.ApproveAsync(facts, proposalId, request.ExpectedVersion, actor, fence, metadata, token), token);

    private async Task<AccountingApiResult<InvoiceLineCogsCorrectionViewV1>> Run(string operation, string permission,
        Guid invoiceId, Guid lineId, object payload, string key,
        Func<FinalizedInvoiceLineSourceV1, Guid, Func<CancellationToken, Task>, AccountingCommandMetadata,
            Task<InvoiceLineCogsCorrectionViewV1>> write, CancellationToken token)
    {
        if (invoiceId == Guid.Empty || lineId == Guid.Empty || payload is null || string.IsNullOrWhiteSpace(key)
            || key.Trim().Length > 128 || key.Contains(',')) return new(AccountingApiStatus.Invalid, null, false, AccountingApiCodes.RequestInvalid);
        if (!Capture(out var companyId, out var actor, out var actorText)
            || !await Allowed(permission, companyId, actor, actorText, token)) return Denied<InvoiceLineCogsCorrectionViewV1>();
        var command = "accounting.invoice-line-cogs." + operation + ".v1";
        var keyHash = AccountingApiService.Sha256(key.Trim());
        var hash = AccountingApiService.Sha256(JsonSerializer.Serialize(new { command, invoiceId, lineId, payload }, Json));
        var prior = await commands.FindCommandAsync(command, keyHash, token);
        if (!await Allowed(permission, companyId, actor, actorText, token)) return Denied<InvoiceLineCogsCorrectionViewV1>();
        if (prior is not null) return Replay(prior, actorText, hash);
        var scope = await authority.RevalidatePermissionScopeAsync(permission, companyId, token);
        if (!scope.IsGranted || !scope.IsCompanyWide || !Current(companyId, actor, actorText)) return Denied<InvoiceLineCogsCorrectionViewV1>();
        var metadata = new AccountingCommandMetadata(actorText, permission, [.. scope.RoleCodes.Order(StringComparer.Ordinal)],
            correlation.CorrelationId, correlation.CausationId, key.Trim(), correlation.SourceChannel,
            correlation.ClientIpAddress, correlation.UserAgent, value => new AccountingCommandRecord(command, keyHash,
                actorText, hash, JsonSerializer.Serialize(new AccountingApiResult<InvoiceLineCogsCorrectionViewV1>(AccountingApiStatus.Ok,
                    (InvoiceLineCogsCorrectionViewV1)value, false, null), Json), correlation.CorrelationId));
        async Task Fence(CancellationToken ct)
        { if (!await Allowed(permission, companyId, actor, actorText, ct)) throw new AuthorityChanged(); }
        async Task<AccountingApiResult<InvoiceLineCogsCorrectionViewV1>> Terminal(AccountingApiResult<InvoiceLineCogsCorrectionViewV1> result)
        {
            try
            {
                await Fence(token);
                await store.RecordAttemptAsync(new(command, keyHash, actorText, hash, JsonSerializer.Serialize(result, Json), correlation.CorrelationId), Fence, token);
                await Fence(token);
                return result;
            }
            catch (AccountingStoreException exception) when (exception.Code == AccountingStoreCodes.ConcurrentChange)
            {
                var winner = await commands.FindCommandAsync(command, keyHash, token);
                if (!await Allowed(permission, companyId, actor, actorText, token)) return Denied<InvoiceLineCogsCorrectionViewV1>();
                return winner is null ? result : Replay(winner, actorText, hash);
            }
            catch (AuthorityChanged) { return Denied<InvoiceLineCogsCorrectionViewV1>(); }
        }
        try
        {
            var facts = await Source(invoiceId, lineId, token);
            await Fence(token);
            var result = await write(facts, actor, Fence, metadata);
            return new(AccountingApiStatus.Ok, result, false, null);
        }
        catch (AuthorityChanged) { return Denied<InvoiceLineCogsCorrectionViewV1>(); }
        catch (AccountingVersionConflictException exception)
        {
            if (!await Allowed(permission, companyId, actor, actorText, token)) return Denied<InvoiceLineCogsCorrectionViewV1>();
            return await Terminal(new(AccountingApiStatus.Conflict, null, false, AccountingApiCodes.VersionConflict, "expectedVersion", exception.ExpectedVersion, exception.CurrentVersion));
        }
        catch (AccountingStoreException exception)
        {
            if (!await Allowed(permission, companyId, actor, actorText, token)) return Denied<InvoiceLineCogsCorrectionViewV1>();
            if (exception.Code == AccountingStoreCodes.ConcurrentChange)
            {
                var winner = await commands.FindCommandAsync(command, keyHash, token);
                if (!await Allowed(permission, companyId, actor, actorText, token)) return Denied<InvoiceLineCogsCorrectionViewV1>();
                return winner is null ? new(AccountingApiStatus.Conflict, null, false, exception.Code) : Replay(winner, actorText, hash);
            }
            var result = new AccountingApiResult<InvoiceLineCogsCorrectionViewV1>(Status(exception.Code), null, false, exception.Code);
            // Storage/business 409 and not-found are terminal. Technical/source failures, cancellation, 422 and authority
            // refusals remain retryable under the same key and cannot be presented as a completed correction.
            return result.Status is AccountingApiStatus.Conflict or AccountingApiStatus.NotFound ? await Terminal(result) : result;
        }
        catch (AccountingDomainException) { return new(AccountingApiStatus.Unprocessable, null, false, InvoiceLineCogsCorrectionCodesV1.PostingEvidenceInvalid); }
        catch (System.Data.Common.DbException) { return new(AccountingApiStatus.Unavailable, null, false, InvoiceLineCogsCorrectionCodesV1.SourceUnavailable); }
    }

    private async Task<FinalizedInvoiceLineSourceV1> Source(Guid invoiceId, Guid lineId, CancellationToken token)
    {
        var captured = company.CompanyId;
        var result = await source.GetAsync(captured, invoiceId, lineId, token);
        if (result.CompanyId != captured || result.InvoiceId != invoiceId || result.InvoiceLineId != lineId)
            throw new AccountingStoreException(InvoiceLineCogsCorrectionCodesV1.SourceUnavailable, "Fatura satırı kaynağı doğrulanamadı.");
        if (result.Outcome == FinalizedInvoiceLineSourceOutcomeV1.NotFound)
            throw new AccountingStoreException(InvoiceLineCogsCorrectionCodesV1.NotFound, "Fatura satırı bulunamadı.");
        if (result.Outcome != FinalizedInvoiceLineSourceOutcomeV1.Available || result.Source is not { } facts
            || facts.CompanyId != captured || facts.InvoiceId != invoiceId || facts.InvoiceLineId != lineId)
            throw new AccountingStoreException(InvoiceLineCogsCorrectionCodesV1.SourceUnavailable, "Fatura satırı kaynağı okunamadı.");
        return facts;
    }
    private static AccountingApiStatus Status(string code) => code switch
    {
        InvoiceLineCogsCorrectionCodesV1.NotFound => AccountingApiStatus.NotFound,
        InvoiceLineCogsCorrectionCodesV1.SelfApproval or AccountingStoreCodes.CompanyContextMismatch => AccountingApiStatus.Denied,
        InvoiceLineCogsCorrectionCodesV1.SourceChanged or InvoiceLineCogsCorrectionCodesV1.VersionConflict
            or InvoiceLineCogsCorrectionCodesV1.NotPrepared or AccountingStoreCodes.ConcurrentChange => AccountingApiStatus.Conflict,
        InvoiceLineCogsCorrectionCodesV1.SourceUnavailable or InvoiceLineCogsCorrectionCodesV1.AccountPlanUnavailable
            or InvoiceLineCogsCorrectionCodesV1.PostingEvidenceInvalid => AccountingApiStatus.Unavailable,
        _ => AccountingApiStatus.Unprocessable,
    };
    private static bool ValidFingerprint(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
    private async Task<bool> Allowed(string permission, Guid id, Guid actor, string text, CancellationToken token)
    {
        if (!Current(id, actor, text)) return false;
        var scope = await authority.RevalidatePermissionScopeAsync(permission, id, token);
        return scope.IsGranted && scope.IsCompanyWide && Current(id, actor, text);
    }
    private bool Capture(out Guid id, out Guid actor, out string text)
    { id = company.IsResolved ? company.CompanyId : Guid.Empty; text = user.UserId ?? ""; return Guid.TryParse(text, out actor) && Current(id, actor, text); }
    private bool Current(Guid id, Guid actor, string text) => id != Guid.Empty && actor != Guid.Empty && company.IsResolved
        && company.CompanyId == id && user.IsAuthenticated && user.UserId == text && user.CanAccessCompany(id) && correlation.IsResolved;
    private static AccountingApiResult<T> Denied<T>() where T : class => new(AccountingApiStatus.Denied, null, false, AccountingApiCodes.AuthorityDenied);
    private static AccountingApiResult<InvoiceLineCogsCorrectionViewV1> Replay(AccountingCommandRecord prior, string actor, string hash) =>
        prior.ActorId != actor || prior.RequestHash != hash ? new(AccountingApiStatus.Conflict, null, false, AccountingApiCodes.IdempotencyConflict)
            : (JsonSerializer.Deserialize<AccountingApiResult<InvoiceLineCogsCorrectionViewV1>>(prior.ResultJson, Json)
                ?? throw new InvalidOperationException("Fatura satırı düzeltme komut kaydı okunamadı.")) with { IsReplay = true, OriginalCorrelationId = prior.OriginalCorrelationId };
}
