using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Accounting.Application.Contracts;

namespace GoymenERP.Modules.Accounting.Application;

/// <summary>
/// B-04b: the Accounting side of the source document acceptance port (see <see cref="IAccountingSourceDocumentAdmissionPortV1"/>).
/// The k3 skeleton, shaped for a cross-module port: server company fence and a fresh host authority measurement before the
/// read and again right before the write; identity validation; a request hash over the document content only; replay by key
/// (the same content replays the stored answer with no second voucher, other content under the key is a refusal); the
/// account mapping read through the B-04a read port (never a guessed account); ONE store write holding the draft voucher, its
/// lines, the attempt/refusal record (the admission row) and the audit event. A content refusal is recorded once; a mapping
/// gap is recorded as a retryable waiting row ("eşleme bekliyor") so the same key can still be admitted later.
/// No permission is registered, no grant is made and no account code is chosen here.
/// </summary>
public sealed class AccountingSourceDocumentAdmissionServiceV1(
    IAccountingSourceDocumentAdmissionStoreV1 store,
    IAccountingPostingAccountMappingResolverV1 mappings,
    IAccountingSourceDocumentAdmissionAuthorityV1 authority,
    ICompanyContext companyContext,
    ICorrelationContext correlation)
    : IAccountingSourceDocumentAdmissionPortV1
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<AccountingSourceDocumentAdmissionAnswerV1> AdmitAsync(
        AccountingSourceDocumentAdmissionRequestV1 request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
        {
            return AccountingSourceDocumentAdmissionAnswerV1.Unavailable;
        }

        if (request.CompanyId != companyContext.CompanyId)
        {
            return AccountingSourceDocumentAdmissionAnswerV1.Refused(AccountingSourceDocumentAdmissionReasonsV1.AuthorityDenied);
        }

        var identity = ValidateIdentity(request);
        if (identity is not null)
        {
            return AccountingSourceDocumentAdmissionAnswerV1.Refused(identity);
        }

        var granted = await FenceAsync(request, cancellationToken);
        if (granted.Answer is not null)
        {
            return granted.Answer;
        }

        var keyHash = AccountingApiService.Sha256(request.IdempotencyKey);
        var requestHash = AccountingApiService.Sha256(JsonSerializer.Serialize(Canonical(request), Json));
        AccountingSourceDocumentAdmissionRecordV1? prior;
        try
        {
            prior = await store.FindAdmissionAsync(keyHash, request.DocumentKind, request.InvoiceId, request.FinalizedVersion,
                cancellationToken);
        }
        catch (AccountingStoreException)
        {
            return AccountingSourceDocumentAdmissionAnswerV1.Unavailable;
        }

        if (prior is not null)
        {
            if (!string.Equals(prior.RequestHash, requestHash, StringComparison.Ordinal)
                || !string.Equals(prior.IdempotencyKeyHash, keyHash, StringComparison.Ordinal)
                || prior.SourceDocumentId != request.SourceDocumentId)
            {
                return AccountingSourceDocumentAdmissionAnswerV1.Refused(
                    AccountingSourceDocumentAdmissionReasonsV1.IdempotencyConflict);
            }

            if (prior.Status != AccountingSourceDocumentAdmissionStatusV1.Waiting)
            {
                return FromRecord(prior, isReplay: true);
            }
        }

        var expectedVersion = prior?.Version ?? 0;
        var plan = AccountingSourceDocumentVoucherPlanV1.Plan(request);
        if (plan.RefusalReason is not null)
        {
            return await WriteAsync(request, keyHash, requestHash,
                AccountingSourceDocumentAdmissionStatusV1.Refused, plan.RefusalReason, expectedVersion, null,
                cancellationToken);
        }

        var resolved = await ResolveAsync(request, plan, cancellationToken);
        if (resolved.Answer is not null)
        {
            return resolved.Answer;
        }

        if (resolved.Draft is null)
        {
            return await WriteAsync(request, keyHash, requestHash,
                resolved.Terminal ? AccountingSourceDocumentAdmissionStatusV1.Refused
                    : AccountingSourceDocumentAdmissionStatusV1.Waiting,
                resolved.Reason!, expectedVersion, null, cancellationToken);
        }

        return await WriteAsync(request, keyHash, requestHash,
            AccountingSourceDocumentAdmissionStatusV1.Admitted, AccountingSourceDocumentAdmissionReasonsV1.Admitted,
            expectedVersion, resolved.Draft, cancellationToken);
    }

    private static string? ValidateIdentity(AccountingSourceDocumentAdmissionRequestV1 request)
    {
        if (string.IsNullOrWhiteSpace(request.ActorId)
            || request.ActorId.Length > AccountingSourceDocumentLimitsV1.MaximumActorIdLength
            || string.IsNullOrWhiteSpace(request.CorrelationId)
            || request.CorrelationId.Length > AccountingSourceDocumentLimitsV1.MaximumCorrelationIdLength
            || request.CausationId is { Length: > AccountingSourceDocumentLimitsV1.MaximumCorrelationIdLength }
            || request.SourceDocumentId == Guid.Empty || request.InvoiceId == Guid.Empty || request.FinalizedVersion < 1)
        {
            return AccountingSourceDocumentAdmissionReasonsV1.RequestInvalid;
        }

        if (!string.Equals(request.DocumentKind, AccountingSourceDocumentKindsV1.SalesInvoice, StringComparison.Ordinal))
        {
            return AccountingSourceDocumentAdmissionReasonsV1.DocumentKindUnsupported;
        }

        return string.Equals(request.IdempotencyKey,
            AccountingSourceDocumentKindsV1.SalesInvoiceKey(request.InvoiceId, request.FinalizedVersion), StringComparison.Ordinal)
            ? null
            : AccountingSourceDocumentAdmissionReasonsV1.IdempotencyKeyInvalid;
    }

    private async Task<(AccountingSourceDocumentAdmissionAnswerV1? Answer, AccountingSourceDocumentAuthorityAnswerV1? Authority)>
        FenceAsync(AccountingSourceDocumentAdmissionRequestV1 request, CancellationToken cancellationToken)
    {
        AccountingSourceDocumentAuthorityAnswerV1 answer;
        try
        {
            answer = await authority.CheckAsync(request.CompanyId, request.ActorId, request.DocumentKind,
                request.SourceDocumentId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return (AccountingSourceDocumentAdmissionAnswerV1.Unavailable, null);
        }

        if (answer is null || answer.Decision == AccountingSourceDocumentAuthorityDecisionV1.Unavailable
            || (answer.Decision == AccountingSourceDocumentAuthorityDecisionV1.Allowed
                && string.IsNullOrWhiteSpace(answer.PermissionCode)))
        {
            return (AccountingSourceDocumentAdmissionAnswerV1.Unavailable, null);
        }

        return answer.Decision == AccountingSourceDocumentAuthorityDecisionV1.Allowed
            ? (null, answer)
            : (AccountingSourceDocumentAdmissionAnswerV1.Refused(AccountingSourceDocumentAdmissionReasonsV1.AuthorityDenied), null);
    }

    private sealed record Resolution(
        AccountingSourceDocumentAdmissionAnswerV1? Answer,
        AccountingSourceDocumentVoucherDraftV1? Draft,
        string? Reason,
        bool Terminal);

    private async Task<Resolution> ResolveAsync(
        AccountingSourceDocumentAdmissionRequestV1 request,
        AccountingSourceDocumentPlanV1 plan,
        CancellationToken cancellationToken)
    {
        var cache = new Dictionary<(string, string?, string?, bool?, string?), AccountingPostingAccountMappingResolutionV1>();
        var lines = new List<AccountingSourceDocumentVoucherLineV1>();
        long? version = null;
        string? waiting = null;
        foreach (var line in plan.Lines)
        {
            var selector = (line.MappingKey, line.ItemType, line.LinePurpose, line.IsExport, line.VatRateCode);
            if (!cache.TryGetValue(selector, out var resolution))
            {
                try
                {
                    resolution = await mappings.ResolveAsync(request.CompanyId,
                        AccountingPostingAccountMappingPurposesV1.SalesInvoice, request.DocumentDate, line.MappingKey,
                        line.ItemType, line.LinePurpose, line.IsExport, line.VatRateCode, cancellationToken);
                }
                catch (AccountingStoreException)
                {
                    return new(AccountingSourceDocumentAdmissionAnswerV1.Unavailable, null, null, false);
                }

                cache[selector] = resolution;
            }

            switch (resolution.Kind)
            {
                case AccountingPostingAccountMappingResolutionKindV1.Mapped
                    when resolution.AccountId is { } accountId && accountId != Guid.Empty && resolution.MappingVersion is { } mapped:
                    if (version is not null && version != mapped)
                    {
                        // Two versions answered one voucher: nothing is mixed; the caller re-drives.
                        return new(AccountingSourceDocumentAdmissionAnswerV1.Unavailable, null, null, false);
                    }

                    version = mapped;
                    lines.Add(new(lines.Count + 1, line.MappingKey, line.Side, accountId, line.Amount, line.SourceLineNumber,
                        line.VatRateCode));
                    break;
                case AccountingPostingAccountMappingResolutionKindV1.Unmapped:
                    waiting ??= AccountingSourceDocumentAdmissionReasonsV1.MappingPending;
                    break;
                case AccountingPostingAccountMappingResolutionKindV1.Ambiguous:
                    waiting = AccountingSourceDocumentAdmissionReasonsV1.MappingAmbiguous;
                    break;
                case AccountingPostingAccountMappingResolutionKindV1.Unavailable
                    when resolution.UnavailableReason is AccountingPostingAccountMappingUnavailableReasonsV1.QueryInvalid:
                    return new(null, null, AccountingSourceDocumentAdmissionReasonsV1.MappingQueryInvalid, true);
                case AccountingPostingAccountMappingResolutionKindV1.Unavailable
                    when resolution.UnavailableReason is AccountingPostingAccountMappingUnavailableReasonsV1.NoActiveMapping
                        or AccountingPostingAccountMappingUnavailableReasonsV1.NotEffectiveForDate
                        or AccountingPostingAccountMappingUnavailableReasonsV1.AccountNotInActivePlan:
                    waiting ??= AccountingSourceDocumentAdmissionReasonsV1.MappingPending;
                    break;
                default:
                    // company_not_resolved, account_plan_unavailable or a malformed answer: nothing is known.
                    return new(AccountingSourceDocumentAdmissionAnswerV1.Unavailable, null, null, false);
            }
        }

        if (waiting is not null)
        {
            return new(null, null, waiting, false);
        }

        var debit = lines.Where(line => line.Side == AccountingSourceDocumentSidesV1.Debit).Sum(line => line.Amount);
        var credit = lines.Where(line => line.Side == AccountingSourceDocumentSidesV1.Credit).Sum(line => line.Amount);
        if (version is null || debit != credit)
        {
            return new(null, null, AccountingSourceDocumentAdmissionReasonsV1.VoucherUnbalanced, true);
        }

        return new(null, new AccountingSourceDocumentVoucherDraftV1(Guid.NewGuid(), version.Value, lines), null, false);
    }

    private async Task<AccountingSourceDocumentAdmissionAnswerV1> WriteAsync(
        AccountingSourceDocumentAdmissionRequestV1 request,
        string keyHash,
        string requestHash,
        AccountingSourceDocumentAdmissionStatusV1 status,
        string reason,
        long expectedVersion,
        AccountingSourceDocumentVoucherDraftV1? draft,
        CancellationToken cancellationToken)
    {
        // The fence again right before the write: the authority must still be the same measured grant.
        var still = await FenceAsync(request, cancellationToken);
        if (still.Answer is not null)
        {
            return still.Answer;
        }

        var metadata = new AccountingCommandMetadata(
            request.ActorId,
            still.Authority!.PermissionCode,
            still.Authority.RoleCodes.Order(StringComparer.Ordinal).ToArray(),
            request.CorrelationId,
            request.CausationId,
            request.IdempotencyKey,
            correlation.IsResolved ? correlation.SourceChannel : "port",
            correlation.IsResolved ? correlation.ClientIpAddress : null,
            correlation.IsResolved ? correlation.UserAgent : null);
        try
        {
            var stored = await store.WriteAsync(
                new AccountingSourceDocumentAdmissionWriteV1(metadata, request, requestHash, keyHash, status, reason,
                    expectedVersion, draft),
                cancellationToken);
            return FromRecord(stored, isReplay: false);
        }
        catch (AccountingStoreException exception) when (exception.Code == AccountingStoreCodes.ConcurrentChange)
        {
            // A concurrent write of the same key won: answer from what it stored; nothing is created twice.
            try
            {
                var winner = await store.FindAdmissionAsync(keyHash, request.DocumentKind, request.InvoiceId, request.FinalizedVersion,
                    cancellationToken);
                return winner is not null
                    && string.Equals(winner.RequestHash, requestHash, StringComparison.Ordinal)
                    && winner.Status != AccountingSourceDocumentAdmissionStatusV1.Waiting
                        ? FromRecord(winner, isReplay: true)
                        : AccountingSourceDocumentAdmissionAnswerV1.Unavailable;
            }
            catch (AccountingStoreException)
            {
                return AccountingSourceDocumentAdmissionAnswerV1.Unavailable;
            }
        }
        catch (AccountingStoreException exception) when (exception.Code == AccountingSourceDocumentStoreCodesV1.ContentConflict)
        {
            return AccountingSourceDocumentAdmissionAnswerV1.Refused(AccountingSourceDocumentAdmissionReasonsV1.IdempotencyConflict);
        }
        catch (AccountingStoreException)
        {
            return AccountingSourceDocumentAdmissionAnswerV1.Unavailable;
        }
    }

    private static AccountingSourceDocumentAdmissionAnswerV1 FromRecord(
        AccountingSourceDocumentAdmissionRecordV1 record,
        bool isReplay) =>
        record.Status switch
        {
            AccountingSourceDocumentAdmissionStatusV1.Admitted when record.VoucherDraftId is { } id =>
                AccountingSourceDocumentAdmissionAnswerV1.Admitted(id, isReplay),
            AccountingSourceDocumentAdmissionStatusV1.Refused =>
                AccountingSourceDocumentAdmissionAnswerV1.Refused(record.ReasonCode, isReplay, retryable: false),
            AccountingSourceDocumentAdmissionStatusV1.Waiting =>
                AccountingSourceDocumentAdmissionAnswerV1.Refused(record.ReasonCode, isReplay, retryable: true),
            _ => AccountingSourceDocumentAdmissionAnswerV1.Unavailable,
        };

    /// <summary>The document content the request hash covers. Actor, correlation and causation are the call's context, not content.</summary>
    private static object Canonical(AccountingSourceDocumentAdmissionRequestV1 request) => new
    {
        kind = request.DocumentKind,
        sourceDocumentId = request.SourceDocumentId.ToString("D"),
        invoiceId = request.InvoiceId.ToString("D"),
        request.FinalizedVersion,
        sourceActorId = request.SourceActorId.ToString("D"),
        partyId = request.PartyId.ToString("D"),
        currencyId = request.CurrencyId.ToString("D"),
        request.ExchangeRate,
        documentDate = request.DocumentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        dueDate = request.DueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        documentTotals = request.DocumentTotals,
        functionalTotals = request.FunctionalTotals,
        lines = request.Lines?.Select(line => line is null
            ? null
            : new { line.LineNumber, line.ItemType, line.LinePurpose, line.IsExport, line.VatRateCode, line.Net, line.Vat, line.Withholding })
            .ToArray(),
    };
}

/// <summary>The default when the host composes no authority measurement: the port answers Unavailable (fail closed).</summary>
public sealed class UnavailableAccountingSourceDocumentAdmissionAuthorityV1 : IAccountingSourceDocumentAdmissionAuthorityV1
{
    public Task<AccountingSourceDocumentAuthorityAnswerV1> CheckAsync(
        Guid companyId,
        string actorId,
        string documentKind,
        Guid sourceDocumentId,
        CancellationToken cancellationToken) =>
        Task.FromResult(AccountingSourceDocumentAuthorityAnswerV1.Unavailable);
}
