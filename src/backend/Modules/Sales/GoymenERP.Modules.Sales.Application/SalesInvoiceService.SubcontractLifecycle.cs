using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Monetary;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.Invoices;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>SAT-D380: native draft → separate approval → official-date rate/match → finalise → idempotent Finance admission.</summary>
public sealed partial class SalesInvoiceService : ISalesSubcontractInvoiceLifecycleServiceV1
{
    public Task<SalesSubcontractServiceInvoiceCommandResult> SubmitSubcontractInvoiceAsync(Guid invoiceId,
        SalesDraftStateCommandRequest request, SalesCommandMetadata metadata, CancellationToken cancellationToken = default) =>
        SubcontractDecisionAsync(invoiceId, request, metadata, SalesSubcontractInvoiceLifecycleCommandsV1.Submit, WritePermission,
            "Submit", "Fason hizmet faturası onaya gönderildi", (invoice, actor, version, now) => invoice.Submit(version, actor, now), cancellationToken);

    public Task<SalesSubcontractServiceInvoiceCommandResult> ApproveSubcontractInvoiceAsync(Guid invoiceId,
        SalesDraftStateCommandRequest request, SalesCommandMetadata metadata, CancellationToken cancellationToken = default) =>
        SubcontractDecisionAsync(invoiceId, request, metadata, SalesSubcontractInvoiceLifecycleCommandsV1.Approve, ApprovePermission,
            "Approve", "Fason hizmet faturası onaylandı", (invoice, actor, version, now) => invoice.Approve(version, actor, now), cancellationToken);

    public Task<SalesSubcontractServiceInvoiceCommandResult> RejectSubcontractInvoiceAsync(Guid invoiceId,
        SalesDraftStateCommandRequest request, SalesCommandMetadata metadata, CancellationToken cancellationToken = default) =>
        SubcontractDecisionAsync(invoiceId, request, metadata, SalesSubcontractInvoiceLifecycleCommandsV1.Reject, ApprovePermission,
            "Reject", "Fason hizmet faturası reddedildi", (invoice, actor, version, now) => invoice.Reject(version, actor, request.Reason, now), cancellationToken);

    public Task<SalesSubcontractServiceInvoiceCommandResult> CancelSubcontractInvoiceAsync(Guid invoiceId,
        SalesDraftStateCommandRequest request, SalesCommandMetadata metadata, CancellationToken cancellationToken = default) =>
        SubcontractDecisionAsync(invoiceId, request, metadata, SalesSubcontractInvoiceLifecycleCommandsV1.Cancel, WritePermission,
            "Cancel", "Fason hizmet faturası iptal edildi", (invoice, actor, version, now) => invoice.Cancel(version, actor, request.Reason, now), cancellationToken);

    private Task<SalesSubcontractServiceInvoiceCommandResult> SubcontractDecisionAsync(Guid invoiceId,
        SalesDraftStateCommandRequest request, SalesCommandMetadata metadata, string command, string permission, string operation,
        string reason, Action<SalesSubcontractServiceInvoice, string, long, DateTimeOffset> mutate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var expected = ParseExpectedVersion(request.ExpectedVersion);
        return RunSubcontractCommandAsync(invoiceId, metadata, command, permission, SubcontractHash(command, invoiceId, request),
            new(command, operation, reason), (scope, _, _) =>
                Task.FromResult<Action<SalesSubcontractServiceInvoice, DateTimeOffset>>((target, now) => mutate(target, scope.ActorId, expected, now)),
            cancellationToken);
    }

    public async Task<SalesSubcontractServiceInvoiceCommandResult> MatchSubcontractInvoiceAsync(Guid invoiceId,
        MatchSalesInvoiceRequest request, SalesCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var expected = ParseExpectedVersion(request.ExpectedVersion);
        if (string.IsNullOrWhiteSpace(request.OfficialInvoiceNo) || request.OfficialInvoiceNo.Length > SalesInvoice.MaximumOfficialNoLength
            || request.OfficialInvoiceNo != request.OfficialInvoiceNo.Trim() || !Guid.TryParseExact(request.Ettn, "D", out var ettn) || ettn == Guid.Empty)
            throw new SalesDraftServiceException(400, "validation_failed");
        var canonical = request with { Ettn = ettn.ToString("D") };
        return await RunSubcontractCommandAsync(invoiceId, metadata, SalesSubcontractInvoiceLifecycleCommandsV1.Match, MatchPermission,
            SubcontractHash(SalesSubcontractInvoiceLifecycleCommandsV1.Match, invoiceId, canonical),
            new("sales.subcontract-service-invoice.matched.v1", "Match", "Fason hizmet faturası resmî belgeyle eşleştirildi"),
            async (scope, invoice, token) =>
            {
                if (request.ApproveDifference) await RequireStillAsync(scope, ApprovePermission, token);
                var official = await ReadOfficialAsync(scope, request.OfficialInvoiceNo, canonical.Ettn, token);
                var used = await StoreAsync(scope, () => subcontractLifecycle!.FindOfficialClaimAsync(official.OfficialNo, official.Ettn, token),
                    token, MatchPermission);
                if (used is { } other && other != invoiceId)
                    throw new SalesDraftServiceException(409, SalesInvoiceProblemCodesV1.OfficialDocumentAlreadyMatched);
                var rate = invoice.Header.CurrencyId == SettingsCurrencyIdentitiesV1.TurkishLira ? null
                    : await ReadSubcontractOfficialRateAsync(scope, invoice.Header.CurrencyId, official.InvoiceDate!.Value, token);
                if (request.ApproveDifference) await RequireStillAsync(scope, ApprovePermission, token);
                return (target, now) => target.RecordMatch(expected, scope.ActorId, official, SettingsCurrencyIdentitiesV1.TurkishLira,
                    rate, (matchTolerance ?? new SalesInvoiceMatchTolerancePolicy()).ToleranceAmount,
                    request.ApproveDifference, request.DifferenceReason, now);
            }, cancellationToken, request.ApproveDifference);
    }

    public async Task<SalesSubcontractServiceInvoiceCommandResult> FinalizeSubcontractInvoiceAsync(Guid invoiceId,
        SalesDraftStateCommandRequest request, SalesCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        var committed = await SubcontractDecisionAsync(invoiceId, request, metadata, SalesSubcontractInvoiceLifecycleCommandsV1.Finalize,
            FinalizePermission, "Finalize", "Fason hizmet faturası kesinleştirildi",
            (target, actor, version, now) => target.Finalize(version, actor, Guid.NewGuid, now), cancellationToken);
        if (committed.IsReplay || financeAdmission is null) return committed;
        // Finalisation already committed. A failed hand-off never hides that fact; the explicit retry repairs it.
        try
        {
            var automatic = metadata with { IdempotencyKey = $"subcontract-finance:{invoiceId:D}:{committed.Invoice.Version}" };
            var delivered = await ResendSubcontractInvoiceToFinanceAsync(invoiceId, new(committed.Invoice.Version), automatic, cancellationToken);
            // The HTTP answer belongs to the finalisation key, even when a concurrent internal hand-off was replayed.
            return new(delivered.Invoice with { IsReplay = false }, false, committed.OriginalCorrelationId);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            diagnostics?.FinanceHandOffFailed(companyContext.CompanyId, invoiceId, "subcontract_handoff", exception);
            return committed;
        }
    }

    public Task<SalesSubcontractServiceInvoiceCommandResult> ResendSubcontractInvoiceToFinanceAsync(Guid invoiceId,
        SalesDraftStateCommandRequest request, SalesCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var expected = ParseExpectedVersion(request.ExpectedVersion);
        return RunSubcontractCommandAsync(invoiceId, metadata, SalesSubcontractInvoiceLifecycleCommandsV1.ResendFinance, FinalizePermission,
            SubcontractHash(SalesSubcontractInvoiceLifecycleCommandsV1.ResendFinance, invoiceId, request),
            new("sales.subcontract-service-invoice.finance-offered.v1", "FinanceOffer", "Fason hizmet faturası Finans'a sunuldu"),
            async (scope, invoice, token) =>
            {
                if (invoice.Version != expected) throw new SalesVersionConflictException(expected, invoice.Version);
                if (invoice.Finalization is not { FinanceStatus: SalesInvoiceFinanceStatus.Waiting } finalization)
                    throw new SalesRuleException(SalesRuleCodes.StateConflict, "Fatura Finans kabulü beklemiyor.");
                if (financeAdmission is null) throw new SalesDraftServiceException(503, "service_unavailable");
                await RequireStillAsync(scope, FinalizePermission, token);
                await RequirePriceReadAsync(scope, token);
                var offer = new SalesFinanceAdmissionRequestV1(scope.CompanyId, scope.ActorId, metadata.CorrelationId, metadata.CausationId,
                    RedriveKey(invoice.Id, finalization), finalization.AdmissionId, invoice.Id, invoice.Version.ToString(CultureInfo.InvariantCulture),
                    invoice.Header.PartyId, invoice.Header.CompanyAccountId, invoice.Header.CurrencyId,
                    invoice.Rate is { } rate ? Decimal(rate.Rate) : null,
                    [.. finalization.Plan.Select(item => new SalesFinanceInstalmentV1(item.OpenItemId, item.InstallmentId, item.SequenceNo,
                        item.DueDate, Text(item.Amount, invoice.Header.AmountScale),
                        item.FunctionalAmount is { } functional ? Text(functional, invoice.Rate!.FunctionalScale) : null))],
                    invoice.Match!.OfficialNo, invoice.Match.Ettn);
                SalesFinanceAdmissionAnswerV1 answer;
                try { answer = await financeAdmission.AdmitAsync(offer, token) ?? SalesFinanceAdmissionAnswerV1.Unavailable; }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    diagnostics?.FinanceHandOffFailed(scope.CompanyId, invoiceId, "subcontract_finance_offer", exception);
                    answer = SalesFinanceAdmissionAnswerV1.Unavailable;
                }
                await RequireStillAsync(scope, FinalizePermission, token);
                return (target, now) => target.RecordFinanceAttempt(answer.Outcome == SalesFinanceAdmissionOutcomeV1.Admitted, OutcomeText(answer), now);
            }, cancellationToken);
    }

    private async Task<SalesSubcontractServiceInvoiceCommandResult> RunSubcontractCommandAsync(Guid invoiceId, SalesCommandMetadata metadata,
        string command, string permission, string hash, SalesDraftStateAudit audit,
        Func<Scope, SalesSubcontractServiceInvoice, CancellationToken, Task<Action<SalesSubcontractServiceInvoice, DateTimeOffset>>> prepare,
        CancellationToken token, bool requireApproval = false)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var scope = await RequireAsync(permission, token);
        await RequirePriceReadAsync(scope, token);
        if (requireApproval) await RequireStillAsync(scope, ApprovePermission, token);
        var store = subcontractLifecycle ?? throw new SalesDraftServiceException(503, "service_unavailable");
        var reader = subcontractInvoices ?? throw new SalesDraftServiceException(503, "service_unavailable");
        var envelope = new SalesDraftWriteEnvelope(scope.ActorId, permission, scope.RoleCodes, metadata.CorrelationId, metadata.CausationId,
            metadata.IdempotencyKey, correlation.SourceChannel, correlation.ClientIpAddress, correlation.UserAgent);
        var replay = await StoreAsync(scope, () => store.FindLifecycleReplayAsync(envelope, hash, command, token), token, permission);
        await FreshFence(token);
        if (replay is not null) return SubcontractReplay(replay);
        try
        {
            var invoice = await StoreAsync(scope, () => reader.FindSubcontractServiceInvoiceAsync(invoiceId, token), token, permission)
                ?? throw new SalesDraftServiceException(404, "not_found");
            var mutate = await prepare(scope, invoice, token);
            await RequireStillAsync(scope, permission, token);
            await RequirePriceReadAsync(scope, token);
            if (requireApproval) await RequireStillAsync(scope, ApprovePermission, token);
            var result = await store.UpdateLifecycleWithFenceAsync(envelope, hash, command, invoiceId, mutate, audit, FreshFence, token)
                ?? throw new SalesDraftServiceException(404, "not_found");
            return SubcontractResult(result, result.Outcome == SalesDraftWriteOutcome.Replayed);
        }
        catch (SalesVersionConflictException exception)
        {
            return await Refusal(new(409, SalesDraftProblemCodesV1.VersionConflict, [new("expectedVersion", exception.Code)]));
        }
        catch (SalesRuleException exception) when (exception.Code is SalesRuleCodes.StateConflict or SalesRuleCodes.InvoiceSelfApproval)
        {
            return await Refusal(new(409, exception.Code == SalesRuleCodes.InvoiceSelfApproval
                ? SalesInvoiceProblemCodesV1.SelfApproval : SalesDraftProblemCodesV1.StateConflict, [new("state", exception.Code)]));
        }
        catch (SalesRuleException exception)
        {
            var code = exception.Code switch
            {
                SalesRuleCodes.InvoiceMatchMismatch => SalesInvoiceProblemCodesV1.OfficialMatchMismatch,
                SalesRuleCodes.InvoiceMatchDifferenceNotApproved => SalesInvoiceProblemCodesV1.DifferenceApprovalRequired,
                SalesRuleCodes.InvoiceMatchRequired or SalesRuleCodes.InvoiceMatchDifferenceOpen
                    or SalesRuleCodes.InvoiceMatchSourceProofRequired => exception.Code,
                _ => SalesInvoiceProblemCodesV1.InvoiceNotReady,
            };
            throw new SalesDraftServiceException(409, code, [new("invoice", exception.Code)]);
        }
        catch (ArgumentException) { throw new SalesDraftServiceException(400, "validation_failed"); }
        catch (SalesDraftStoreException exception) when (exception.Code == SalesDraftStoreCodes.ConcurrentChange)
        {
            var winner = await StoreAsync(scope, () => store.FindLifecycleReplayAsync(envelope, hash, command, token), token, permission);
            await FreshFence(token);
            return winner is null ? throw Store(exception) : SubcontractReplay(winner);
        }
        catch (SalesDraftStoreException exception) { throw Store(exception); }

        async Task FreshFence(CancellationToken ct)
        {
            await RequireStillAsync(scope, permission, ct);
            await RequirePriceReadAsync(scope, ct);
            if (requireApproval) await RequireStillAsync(scope, ApprovePermission, ct);
        }

        async Task<SalesSubcontractServiceInvoiceCommandResult> Refusal(SalesDraftTerminalRefusal refusal)
        {
            await RequireStillAsync(scope, permission, token);
            await RequirePriceReadAsync(scope, token);
            if (requireApproval) await RequireStillAsync(scope, ApprovePermission, token);
            try { return SubcontractReplay(await store.RecordLifecycleRefusalWithFenceAsync(envelope, hash, command, refusal, FreshFence, token)); }
            catch (SalesDraftStoreException exception) when (exception.Code == SalesDraftStoreCodes.ConcurrentChange)
            {
                var winner = await StoreAsync(scope, () => store.FindLifecycleReplayAsync(envelope, hash, command, token), token, permission);
                await FreshFence(token);
                return winner is null ? throw Store(exception) : SubcontractReplay(winner);
            }
        }
    }

    private async Task<SalesInvoiceRateSnapshot> ReadSubcontractOfficialRateAsync(Scope scope, Guid currencyId, DateOnly invoiceDate,
        CancellationToken token)
    {
        var resolver = exchangeRates ?? throw NotReady([new("officialInvoiceNo", SalesInvoiceReasonCodesV1.RateUnresolved)]);
        ExchangeRateResolutionV1? rate;
        try { rate = await resolver.ResolveAsync(invoiceDate, currencyId, SettingsCurrencyIdentitiesV1.TurkishLira, token); }
        catch (SettingsPrerequisiteException exception) when (exception.Code == ExchangeRateProblemCodesV1.AccessDenied)
        {
            await RequireStillAsync(scope, MatchPermission, token);
            throw new SalesDraftServiceException(403, "forbidden");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RequireStillAsync(scope, MatchPermission, token);
            throw new SalesDraftServiceException(503, "service_unavailable");
        }
        await RequireStillAsync(scope, MatchPermission, token);
        if (rate is not { State: ExchangeRateResolutionStatesV1.Resolved, BulletinDate: { } bulletin, SellingRate: { } text })
            throw NotReady([new("officialInvoiceNo", SalesInvoiceReasonCodesV1.RateUnresolved)]);
        if (rate.SourceCurrencyId != currencyId || rate.TargetCurrencyId != SettingsCurrencyIdentitiesV1.TurkishLira
            || rate.RateDay != invoiceDate || !SalesLinePriceCalculator.TryParseExact(text, out var value) || value <= 0m)
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.ReferenceProviderInvalid);
        MoneyProfileV1 profile;
        try { profile = await settings.GetMoneyProfileAsync(SettingsCurrencyIdentitiesV1.TurkishLira, SalesMoneyOperations.OrderDraftV1, invoiceDate, token); }
        catch (SettingsPrerequisiteException)
        {
            await RequireStillAsync(scope, MatchPermission, token);
            throw new SalesDraftServiceException(503, "service_unavailable");
        }
        await RequireStillAsync(scope, MatchPermission, token);
        if (profile is null || profile.CompanyId != scope.CompanyId || profile.CurrencyId != SettingsCurrencyIdentitiesV1.TurkishLira
            || !profile.IsActive || profile.Scale is < 0 or > Money.MaximumScale)
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.ReferenceProviderInvalid);
        return new(value, invoiceDate, bulletin, SettingsCurrencyIdentitiesV1.TurkishLira, profile.Scale);
    }

    private static string SubcontractHash(string command, Guid invoiceId, object request) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { command, invoiceId, request }, SalesDraftJson.Options)))).ToLowerInvariant();

    private static SalesSubcontractServiceInvoiceCommandResult SubcontractReplay(SalesSubcontractInvoiceReplayV1 replay) =>
        replay.Refusal is { } refusal ? throw new SalesDraftServiceException(refusal.Status, refusal.Code, refusal.Errors)
            : SubcontractResult(new(replay.Invoice ?? throw new InvalidOperationException("Fason fatura yeniden yanıtı eksik."),
                SalesDraftWriteOutcome.Replayed, replay.OriginalCorrelationId), true);
}
