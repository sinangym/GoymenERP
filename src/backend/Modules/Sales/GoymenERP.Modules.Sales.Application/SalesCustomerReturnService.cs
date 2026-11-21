using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.CustomerReturns;
using GoymenERP.Modules.Sales.Domain.Invoices;

namespace GoymenERP.Modules.Sales.Application;

/// <summary>
/// API-40 — müşteri iade faturası (EK57 SAT-D288…D291, EK58 SAT-D292…D294; Sinan 2026-11-10). The K3 skeleton: the operation's own
/// name and the price read are measured before anything is read, after every awaited read and right before the single save; the
/// replay comes before any read; one save writes the return, its attempt and the audit event; a terminal refusal (stale version,
/// state conflict, self-approval) is recorded under the key and answered again on retry.
/// <list type="bullet">
/// <item><b>Create</b> (<c>sales.returns.write</c>): the original invoice must be the company's FINALISED invoice; the optional
/// shipment must be its shipment; amounts are the calculator's against the finalised returns of the invoice (SAT-D292).</item>
/// <item><b>Submit</b> (<c>sales.returns.write</c>), <b>approve</b> (<c>sales.returns.approve</c>, never the preparer or the
/// submitter, SAT-D290 (3)).</item>
/// <item><b>Finalise</b> (<c>sales.returns.finalize</c>): without the Finance acceptance port nothing is read or written (503,
/// SAT-D294); with it, the return is recomputed against the finalised returns of now, offered to Finance (API-11 v2) under its own
/// id as the acceptance identity and the command's own key, and committed only when Finance admitted it. The commit writes the
/// <c>sales.return-invoice.finalized.v1</c> outbox event in the same save. FNS-D81: a partial return carries the installments the
/// user chose; finalising one without a selection is refused (<c>sales_customer_return_installments_required</c>) instead of
/// letting Finance apply its automatic nearest-due order (FNS-D54).</item>
/// <item><b>Installments</b> (<c>sales.returns.finalize</c>): the approved return's original invoice installments and Finance's
/// nearest-due prefill (FNS-D81), read through the same port; nothing is written.</item>
/// </list>
/// No stock movement is written and no other module's table is touched (SAT-D293 (2)).
/// </summary>
public sealed class SalesCustomerReturnService(
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority,
    ICorrelationContext correlation,
    ISalesCustomerReturnStore returns,
    ISalesInvoiceStore invoices,
    TimeProvider timeProvider,
    ISalesCustomerReturnFinancePortV1? finance = null) : ISalesCustomerReturnService
{
    private const string ReadPermission = SalesPermissionManifestV1.ReturnsRead;
    private const string WritePermission = SalesPermissionManifestV1.ReturnsWrite;
    private const string ApprovePermission = SalesPermissionManifestV1.ReturnsApprove;
    private const string FinalizePermission = SalesPermissionManifestV1.ReturnsFinalize;
    private const string PriceReadPermission = SalesPermissionManifestV1.OrdersPriceRead;

    /// <summary>The prefix of Finance's stable installment selection refusals (FNS-D44), answered on the <c>allocations</c> field.</summary>
    private const string FinanceAllocationReasonPrefix = "finance_customer_return_allocation_";

    /// <summary>Technical bound (not a policy): the longest Finance reason code passed through to the caller.</summary>
    private const int MaximumReasonCodeLength = 128;

    private static readonly SalesDraftStateAudit CreatedAudit =
        new("sales.customer-return.created.v1", "CreateCustomerReturn", "Müşteri iade faturası kaydedildi");

    private static readonly SalesDraftStateAudit SubmittedAudit =
        new("sales.customer-return.submitted.v1", "SubmitCustomerReturn", "Müşteri iade faturası onaya gönderildi");

    private static readonly SalesDraftStateAudit ApprovedAudit =
        new("sales.customer-return.approved.v1", "ApproveCustomerReturn", "Müşteri iade faturası onaylandı");

    private static readonly SalesDraftStateAudit RejectedAudit =
        new("sales.customer-return.rejected.v1", "RejectCustomerReturn", "Müşteri iade faturası reddedildi");

    private static readonly SalesDraftStateAudit CancelledAudit =
        new("sales.customer-return.cancelled.v1", "CancelCustomerReturn", "Müşteri iade faturası iptal edildi");

    private static readonly SalesDraftStateAudit FinalizedAudit =
        new("sales.customer-return.finalized.v1", "FinalizeCustomerReturn", "Müşteri iade faturası kesinleşti");

    public async Task<SalesCustomerReturnCommandResult> CreateCustomerReturnAsync(CreateSalesCustomerReturnRequest request,
        SalesCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        var scope = await RequireAsync(WritePermission, cancellationToken);
        var inputs = Validate(request);
        var requestHash = SalesCustomerReturnHash.Compute(request);
        var envelope = Envelope(scope, metadata);
        var replay = await FindReplayAsync(scope, envelope, requestHash, SalesCustomerReturnCommands.CreateV1, cancellationToken);
        if (replay is not null)
        {
            return Replayed(replay);
        }

        // Another company's invoice is null under the tenant filter, like a missing one.
        var invoice = await StoreAsync(scope, () => invoices.FindInvoiceAsync(request.OriginalInvoiceId, cancellationToken),
            cancellationToken)
            ?? throw new SalesDraftServiceException(422, SalesCustomerReturnProblemCodesV1.InvoiceNotFound,
                [new("originalInvoiceId", SalesCustomerReturnProblemCodesV1.InvoiceNotFound)]);
        var finalized = await StoreAsync(scope, () => returns.ListFinalizedCustomerReturnsAsync(invoice.Id, cancellationToken),
            cancellationToken);
        var priors = Priors(finalized, exceptId: null);
        var documentNo = request.CustomerDocumentNo.Trim();
        if (await StoreAsync(scope, () => returns.CustomerDocumentExistsAsync(invoice.Header.PartyId, documentNo, cancellationToken),
                cancellationToken))
        {
            throw Duplicate();
        }

        // The rules are asked before the last fence, so a refusal is answered without a write.
        var customerReturnId = Guid.NewGuid();
        SalesCustomerReturn Build(DateTimeOffset now) => SalesCustomerReturn.Create(customerReturnId, scope.CompanyId, invoice,
            request.ShipmentId, request.CustomerDocumentNo, request.CustomerDocumentDate, inputs, priors, scope.ActorId, now);
        Rules(() => Build(timeProvider.GetUtcNow()));

        await RequireStillAsync(scope, WritePermission, cancellationToken);
        SalesCustomerReturnStoreResult stored;
        try
        {
            stored = await returns.StoreCustomerReturnAsync(envelope, requestHash, Build, CreatedAudit, cancellationToken);
        }
        catch (SalesRuleException exception)
        {
            throw Rule(exception);
        }
        catch (SalesDraftStoreException exception) when (exception.Code == SalesDraftStoreCodes.ConcurrentChange)
        {
            // ADR-011 §8: a racing caller of the same key answers the winner; a return of the same customer document that won the
            // unique index leaves nothing under this key — 409 concurrent_change, the retry names the duplicate.
            var won = await FindReplayAsync(scope, envelope, requestHash, SalesCustomerReturnCommands.CreateV1, cancellationToken);
            return won is null ? throw Store(exception) : Replayed(won);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        return Result(stored);
    }

    public Task<SalesCustomerReturnCommandResult> SubmitCustomerReturnAsync(Guid customerReturnId, SalesDraftStateCommandRequest request,
        SalesCommandMetadata metadata, CancellationToken cancellationToken = default) =>
        DecideAsync(customerReturnId, request, SalesCustomerReturnCommands.SubmitV1, WritePermission, metadata,
            (target, actor, version, now) => target.Submit(version, actor, now), SubmittedAudit, cancellationToken);

    public Task<SalesCustomerReturnCommandResult> ApproveCustomerReturnAsync(Guid customerReturnId, SalesDraftStateCommandRequest request,
        SalesCommandMetadata metadata, CancellationToken cancellationToken = default) =>
        DecideAsync(customerReturnId, request, SalesCustomerReturnCommands.ApproveV1, ApprovePermission, metadata,
            (target, actor, version, now) => target.Approve(version, actor, now), ApprovedAudit, cancellationToken);

    /// <summary>SAT-D290 (2, 3): the invoice's rejection — the approver's authority, never the preparer or the submitter, optional reason.</summary>
    public Task<SalesCustomerReturnCommandResult> RejectCustomerReturnAsync(Guid customerReturnId, SalesDraftStateCommandRequest request,
        SalesCommandMetadata metadata, CancellationToken cancellationToken = default) =>
        DecideAsync(customerReturnId, request, SalesCustomerReturnCommands.RejectV1, ApprovePermission, metadata,
            (target, actor, version, now) => target.Reject(version, actor, request.Reason, now), RejectedAudit, cancellationToken,
            withReason: true);

    /// <summary>SAT-D290 (2): the invoice's cancellation — the writer's authority, before approval only, optional reason.</summary>
    public Task<SalesCustomerReturnCommandResult> CancelCustomerReturnAsync(Guid customerReturnId, SalesDraftStateCommandRequest request,
        SalesCommandMetadata metadata, CancellationToken cancellationToken = default) =>
        DecideAsync(customerReturnId, request, SalesCustomerReturnCommands.CancelV1, WritePermission, metadata,
            (target, actor, version, now) => target.Cancel(version, actor, request.Reason, now), CancelledAudit, cancellationToken,
            withReason: true);

    public async Task<SalesCustomerReturnCommandResult> FinalizeCustomerReturnAsync(Guid customerReturnId,
        FinalizeSalesCustomerReturnRequest request, SalesCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        var scope = await RequireAsync(FinalizePermission, cancellationToken);
        var expectedVersion = ExpectedVersion(new SalesDraftStateCommandRequest(request.ExpectedVersion));
        var allocations = Validate(request);

        // SAT-D294: the receivable effect is never skipped; without the Finance acceptance port nothing is read, offered or written.
        var port = finance ?? throw new SalesDraftServiceException(503, SalesCustomerReturnProblemCodesV1.FinancePortUnavailable);
        const string commandName = SalesCustomerReturnCommands.FinalizeV1;
        var requestHash = SalesCustomerReturnHash.ComputeFinalize(customerReturnId, request);
        var envelope = Envelope(scope, metadata, FinalizePermission);
        var replay = await FindReplayAsync(scope, envelope, requestHash, commandName, cancellationToken, FinalizePermission);
        if (replay is not null)
        {
            return Replayed(replay);
        }

        if (customerReturnId == Guid.Empty)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        var target = await StoreAsync(scope, () => returns.FindCustomerReturnAsync(customerReturnId, cancellationToken), cancellationToken,
            FinalizePermission) ?? throw new SalesDraftServiceException(404, "not_found");
        var invoice = await StoreAsync(scope, () => invoices.FindInvoiceAsync(target.Header.OriginalInvoiceId, cancellationToken),
            cancellationToken, FinalizePermission) ?? throw new SalesDraftServiceException(503, "service_unavailable");
        var finalized = await StoreAsync(scope,
            () => returns.ListFinalizedCustomerReturnsAsync(invoice.Id, cancellationToken), cancellationToken, FinalizePermission);
        var priors = Priors(finalized, exceptId: target.Id);
        var finalizedBefore = finalized.Count(item => item.Id != target.Id);
        void Finalize(SalesCustomerReturn item, DateTimeOffset now) =>
            item.Finalize(expectedVersion, scope.ActorId, now, invoice, priors);

        // The decision is asked on a copy before Finance is: a stale version, a wrong state or changed amounts never reach Finance.
        var refusal = await DryRunAsync(scope, envelope, requestHash, commandName, target.Id, FinalizePermission,
            () => Finalize(Copy(target), timeProvider.GetUtcNow()), cancellationToken);
        if (refusal is not null)
        {
            return refusal;
        }

        var invoiceVersion = InvoiceVersion(invoice);
        if (allocations is null)
        {
            // FNS-D81: Finance's automatic nearest-due order (FNS-D54) is never applied to a Sales return. Without a selection
            // the return is finalised only when Finance answers that no selection applies (the whole outstanding amount).
            var prefill = await InstallmentsAsync(scope, port, target, invoiceVersion, cancellationToken);
            if (prefill.SelectionAllowed)
            {
                throw new SalesDraftServiceException(422, SalesCustomerReturnProblemCodesV1.InstallmentsRequired,
                    [new("allocations", SalesCustomerReturnProblemCodesV1.InstallmentsRequired)]);
            }
        }

        await RequireStillAsync(scope, FinalizePermission, cancellationToken);
        SalesCustomerReturnFinanceAnswerV1 answer;
        try
        {
            answer = await port.OfferAsync(Offer(scope, target, invoiceVersion, allocations, metadata), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new SalesDraftServiceException(503, "service_unavailable");
        }

        await RequireStillAsync(scope, FinalizePermission, cancellationToken);
        if (answer is null || answer.CustomerReturnId != target.Id || !Enum.IsDefined(answer.Outcome))
        {
            throw new SalesDraftServiceException(503, "service_unavailable");
        }

        if (answer.Outcome != SalesCustomerReturnFinanceOutcomeV1.Admitted)
        {
            // Not recorded: Finance may admit the same return later (a corrected selection, a retry after its own race).
            throw Finance(answer.Outcome, answer.ReasonCode);
        }

        SalesCustomerReturnStoreResult? result = null;
        var stored = await DecisionAsync(scope, envelope, requestHash, commandName, target.Id, FinalizePermission, async () =>
            result = await returns.FinalizeCustomerReturnAsync(envelope, requestHash, target.Id, finalizedBefore, Finalize, FinalizedAudit,
                cancellationToken), cancellationToken);
        if (stored is not null)
        {
            return stored;
        }

        return result is null ? throw new SalesDraftServiceException(404, "not_found") : Result(result);
    }

    public async Task<SalesCustomerReturnInstallmentsResponse> GetCustomerReturnInstallmentsAsync(Guid customerReturnId,
        CancellationToken cancellationToken = default)
    {
        var scope = await RequireAsync(FinalizePermission, cancellationToken);
        var port = finance ?? throw new SalesDraftServiceException(503, SalesCustomerReturnProblemCodesV1.FinancePortUnavailable);
        if (customerReturnId == Guid.Empty)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        var target = await StoreAsync(scope, () => returns.FindCustomerReturnAsync(customerReturnId, cancellationToken), cancellationToken,
            FinalizePermission) ?? throw new SalesDraftServiceException(404, "not_found");
        if (target.State != SalesCustomerReturnState.Approved)
        {
            // Only an approved return is finalised; the prefill of any other state would be a question nobody can act on.
            throw new SalesDraftServiceException(409, SalesDraftProblemCodesV1.StateConflict, [new("state", SalesRuleCodes.StateConflict)]);
        }

        var invoice = await StoreAsync(scope, () => invoices.FindInvoiceAsync(target.Header.OriginalInvoiceId, cancellationToken),
            cancellationToken, FinalizePermission) ?? throw new SalesDraftServiceException(503, "service_unavailable");
        var answer = await InstallmentsAsync(scope, port, target, InvoiceVersion(invoice), cancellationToken);
        return new SalesCustomerReturnInstallmentsResponse(
            target.Id,
            target.Version.ToString(CultureInfo.InvariantCulture),
            ReturnAmount(target),
            answer.SelectionAllowed,
            answer.AppliedAmount!,
            answer.AdvanceAmount!,
            [.. answer.Installments.Select(item => new SalesCustomerReturnInstallmentView(item.OpenItemId, item.DueOn, item.OriginalAmount,
                item.OutstandingAmount, item.PrefillAmount))]);
    }

    /// <summary>
    /// Finance's installment read under the fence: a refusal or an unusable answer is mapped and never answered as a prefill; the
    /// fence is measured again after the await.
    /// </summary>
    private async Task<SalesCustomerReturnInstallmentAnswerV1> InstallmentsAsync(Scope scope, ISalesCustomerReturnFinancePortV1 port,
        SalesCustomerReturn target, string invoiceVersion, CancellationToken cancellationToken)
    {
        await RequireStillAsync(scope, FinalizePermission, cancellationToken);
        SalesCustomerReturnInstallmentAnswerV1 answer;
        try
        {
            answer = await port.ReadInstallmentsAsync(new SalesCustomerReturnInstallmentQueryV1(scope.CompanyId, scope.ActorId,
                correlation.CorrelationId, correlation.CausationId, target.Id, target.Header.OriginalInvoiceId, invoiceVersion,
                ReturnAmount(target)), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new SalesDraftServiceException(503, "service_unavailable");
        }

        await RequireStillAsync(scope, FinalizePermission, cancellationToken);
        if (answer is null || !Enum.IsDefined(answer.Outcome))
        {
            throw new SalesDraftServiceException(503, "service_unavailable");
        }

        if (answer.Outcome != SalesCustomerReturnFinanceOutcomeV1.Admitted)
        {
            throw Finance(answer.Outcome, answer.ReasonCode);
        }

        if (answer.Installments is null || answer.AppliedAmount is null || answer.AdvanceAmount is null
            || answer.Installments.Any(item => item is null || item.OpenItemId == Guid.Empty || item.OriginalAmount is null
                || item.OutstandingAmount is null || item.PrefillAmount is null))
        {
            throw new SalesDraftServiceException(503, "service_unavailable");
        }

        return answer;
    }

    /// <summary>
    /// Finance's refusal as the caller's problem (never recorded under the key): an installment selection refusal names the
    /// selection with Finance's own stable code; any other refusal is <c>sales_customer_return_finance_refused</c>.
    /// </summary>
    private static SalesDraftServiceException Finance(SalesCustomerReturnFinanceOutcomeV1 outcome, string? reasonCode) => outcome switch
    {
        SalesCustomerReturnFinanceOutcomeV1.Refused when reasonCode is not null
            && reasonCode.StartsWith(FinanceAllocationReasonPrefix, StringComparison.Ordinal)
            && reasonCode.Length <= MaximumReasonCodeLength
            => new SalesDraftServiceException(422, SalesCustomerReturnProblemCodesV1.FinanceRefused, [new("allocations", reasonCode)]),
        SalesCustomerReturnFinanceOutcomeV1.Refused => new SalesDraftServiceException(422, SalesCustomerReturnProblemCodesV1.FinanceRefused,
            [new("finance", SalesCustomerReturnProblemCodesV1.FinanceRefused)]),
        SalesCustomerReturnFinanceOutcomeV1.KeyConflict => new SalesDraftServiceException(409, SalesDraftProblemCodesV1.SameKeyPayloadMismatch),
        SalesCustomerReturnFinanceOutcomeV1.Retry => new SalesDraftServiceException(409, SalesDraftProblemCodesV1.ConcurrentChange),
        SalesCustomerReturnFinanceOutcomeV1.Denied => new SalesDraftServiceException(403, "forbidden"),
        _ => new SalesDraftServiceException(503, "service_unavailable"),
    };

    public async Task<SalesCustomerReturnView> GetCustomerReturnAsync(Guid customerReturnId, CancellationToken cancellationToken = default)
    {
        var scope = await RequireAsync(ReadPermission, cancellationToken);
        if (customerReturnId == Guid.Empty)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        var item = await StoreAsync(scope, () => returns.FindCustomerReturnAsync(customerReturnId, cancellationToken), cancellationToken,
            ReadPermission) ?? throw new SalesDraftServiceException(404, "not_found");
        return View(item);
    }

    public async Task<SalesCustomerReturnListResponse> ListCustomerReturnsAsync(Guid? originalInvoiceId,
        CancellationToken cancellationToken = default)
    {
        var scope = await RequireAsync(ReadPermission, cancellationToken);
        if (originalInvoiceId == Guid.Empty)
        {
            throw new SalesDraftServiceException(400, "validation_failed", [new("invoiceId", "validation_failed")]);
        }

        var items = await StoreAsync(scope,
            () => returns.ListCustomerReturnsAsync(originalInvoiceId, SalesCustomerReturnApiV1.MaximumListed, cancellationToken),
            cancellationToken, ReadPermission);
        return new SalesCustomerReturnListResponse([.. items.Select(Summary)]);
    }

    private async Task<SalesCustomerReturnCommandResult> DecideAsync(Guid customerReturnId, SalesDraftStateCommandRequest request,
        string commandName, string permission, SalesCommandMetadata metadata,
        Action<SalesCustomerReturn, string, long, DateTimeOffset> decide, SalesDraftStateAudit audit, CancellationToken cancellationToken,
        bool withReason = false)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        var scope = await RequireAsync(permission, cancellationToken);
        var expectedVersion = ExpectedVersion(request);
        var requestHash = withReason
            ? SalesCustomerReturnHash.ComputeDecision(commandName, customerReturnId, request)
            : SalesCustomerReturnHash.Compute(commandName, customerReturnId, request);
        var envelope = Envelope(scope, metadata, permission);
        var replay = await FindReplayAsync(scope, envelope, requestHash, commandName, cancellationToken, permission);
        if (replay is not null)
        {
            return Replayed(replay);
        }

        if (customerReturnId == Guid.Empty)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        // The last fence that can still prevent the write; none after the commit.
        await RequireStillAsync(scope, permission, cancellationToken);
        SalesCustomerReturnStoreResult? result = null;
        var refusal = await DecisionAsync(scope, envelope, requestHash, commandName, customerReturnId, permission, async () =>
            result = await returns.DecideCustomerReturnAsync(envelope, requestHash, commandName, customerReturnId,
                (target, now) => decide(target, scope.ActorId, expectedVersion, now), audit, cancellationToken), cancellationToken);
        if (refusal is not null)
        {
            return refusal;
        }

        return result is null ? throw new SalesDraftServiceException(404, "not_found") : Result(result);
    }

    /// <summary>
    /// Runs a state decision (a dry run or the store's save) and maps its refusals: a stale version, a wrong state and a
    /// self-approval are recorded under the key (ADR-011 §7); a rule of the calculator is answered without a record; a lost race
    /// answers the winner. Null when the decision went through.
    /// </summary>
    private async Task<SalesCustomerReturnCommandResult?> DecisionAsync(Scope scope, SalesDraftWriteEnvelope envelope, string requestHash,
        string commandName, Guid customerReturnId, string permission, Func<Task> decide, CancellationToken cancellationToken)
    {
        SalesDraftTerminalRefusal terminal;
        try
        {
            await decide();
            return null;
        }
        catch (SalesVersionConflictException exception)
        {
            terminal = new SalesDraftTerminalRefusal(409, SalesDraftProblemCodesV1.VersionConflict, [new("expectedVersion", exception.Code)]);
        }
        catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.StateConflict)
        {
            terminal = new SalesDraftTerminalRefusal(409, SalesDraftProblemCodesV1.StateConflict, [new("state", exception.Code)]);
        }
        catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.CustomerReturnSelfApproval)
        {
            // SAT-D290 (3): the preparer or the submitter approves or rejects; the same actor under the same key stays refused.
            terminal = new SalesDraftTerminalRefusal(409, SalesCustomerReturnProblemCodesV1.SelfApproval, [new("actor", exception.Code)]);
        }
        catch (SalesRuleException exception)
        {
            throw Rule(exception);
        }
        catch (ArgumentException)
        {
            throw new SalesDraftServiceException(400, "validation_failed");
        }
        catch (SalesDraftStoreException exception) when (exception.Code == SalesDraftStoreCodes.ConcurrentChange)
        {
            // ADR-011 §8: a lost same-key race answers the winner; a lost version or finalisation sequence race is the caller's
            // concurrent_change (the retry recomputes against the return finalised meanwhile).
            var won = await FindReplayAsync(scope, envelope, requestHash, commandName, cancellationToken, permission);
            return won is null ? throw Store(exception) : Replayed(won);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        return await TerminalAsync(scope, envelope, requestHash, commandName, customerReturnId, permission, terminal, cancellationToken);
    }

    private Task<SalesCustomerReturnCommandResult?> DryRunAsync(Scope scope, SalesDraftWriteEnvelope envelope, string requestHash,
        string commandName, Guid customerReturnId, string permission, Action decide, CancellationToken cancellationToken) =>
        DecisionAsync(scope, envelope, requestHash, commandName, customerReturnId, permission, () =>
        {
            decide();
            return Task.CompletedTask;
        }, cancellationToken);

    /// <summary>ADR-011 §7: the refusal is recorded under the key and then answered; a lost record race answers the winner.</summary>
    private async Task<SalesCustomerReturnCommandResult> TerminalAsync(Scope scope, SalesDraftWriteEnvelope envelope, string requestHash,
        string commandName, Guid customerReturnId, string permission, SalesDraftTerminalRefusal refusal, CancellationToken cancellationToken)
    {
        await RequireStillAsync(scope, permission, cancellationToken);
        try
        {
            await returns.RecordCustomerReturnRefusalAsync(envelope, requestHash, commandName, customerReturnId, refusal, cancellationToken);
        }
        catch (SalesDraftStoreException exception) when (exception.Code == SalesDraftStoreCodes.ConcurrentChange)
        {
            var won = await FindReplayAsync(scope, envelope, requestHash, commandName, cancellationToken, permission);
            if (won is not null)
            {
                return Replayed(won);
            }
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        throw new SalesDraftServiceException(refusal.Status, refusal.Code, refusal.Errors);
    }

    /// <summary>The body the reader let through, measured again for a direct caller: a malformed field is a 400.</summary>
    private static IReadOnlyList<SalesCustomerReturnLineInput> Validate(CreateSalesCustomerReturnRequest request)
    {
        try
        {
            if (request.OriginalInvoiceId == Guid.Empty || request.ShipmentId == Guid.Empty || request.CustomerDocumentNo is null
                || request.Lines is null || request.Lines.Count == 0 || request.Lines.Count > SalesCustomerReturn.MaximumLines)
            {
                throw new FormatException();
            }

            return [.. request.Lines.Select(line =>
            {
                if (line is null || line.OriginalInvoiceLineId == Guid.Empty || line.Quantity is null
                    || line.Quantity.Length > SalesCustomerReturnApiV1.MaximumQuantityTextLength)
                {
                    throw new FormatException();
                }

                SalesDraftJson.RequireDecimalShape(line.Quantity, positive: true);
                return new SalesCustomerReturnLineInput(line.OriginalInvoiceLineId, SalesCustomerReturnJson.Quantity(line.Quantity));
            })];
        }
        catch (Exception exception) when (exception is FormatException or System.Text.Json.JsonException or OverflowException)
        {
            throw new SalesDraftServiceException(400, "validation_failed");
        }
    }

    /// <summary>The finalisation body the reader let through, measured again for a direct caller: a malformed field is a 400.</summary>
    private static IReadOnlyList<SalesCustomerReturnFinanceAllocationV1>? Validate(FinalizeSalesCustomerReturnRequest request)
    {
        try
        {
            SalesCustomerReturnJson.RequireFinalize(request);
        }
        catch (System.Text.Json.JsonException)
        {
            throw new SalesDraftServiceException(400, "validation_failed", [new("allocations", "validation_failed")]);
        }

        return request.Allocations is null
            ? null
            : [.. request.Allocations.Select(item => new SalesCustomerReturnFinanceAllocationV1(item.OpenItemId, item.Amount))];
    }

    private static long ExpectedVersion(SalesDraftStateCommandRequest request) =>
        long.TryParse(request.ExpectedVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var expectedVersion)
        && expectedVersion >= 1
            ? expectedVersion
            : throw new SalesDraftServiceException(400, "validation_failed", [new("expectedVersion", "validation_failed")]);

    /// <summary>SAT-D292 (6): the priors of every line are the FINALISED returns of the invoice (never drafts or approvals).</summary>
    private static IReadOnlyDictionary<Guid, IReadOnlyList<SalesCustomerReturnPrior>> Priors(IEnumerable<SalesCustomerReturn> finalized,
        Guid? exceptId) =>
        finalized
            .Where(item => item.State == SalesCustomerReturnState.Finalized && item.Id != exceptId)
            .SelectMany(item => item.Lines.Select(line => (line.OriginalInvoiceLineId,
                Prior: new SalesCustomerReturnPrior(line.Quantity, line.Amounts, line.Functional, item.IsShipmentLinked))))
            .GroupBy(entry => entry.OriginalInvoiceLineId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<SalesCustomerReturnPrior>)[.. group.Select(entry => entry.Prior)]);

    private static void Rules(Action build)
    {
        try
        {
            build();
        }
        catch (SalesRuleException exception)
        {
            throw Rule(exception);
        }
        catch (ArgumentException)
        {
            throw new SalesDraftServiceException(400, "validation_failed");
        }
    }

    /// <summary>A rule of the return answers its own stable code (not recorded): the request or the finalised returns must change.</summary>
    private static SalesDraftServiceException Rule(SalesRuleException exception)
    {
        var field = exception.Code switch
        {
            SalesRuleCodes.CustomerReturnInvoiceNotFinalized => "originalInvoiceId",
            SalesRuleCodes.CustomerReturnShipmentNotOnInvoice => "shipmentId",
            SalesRuleCodes.CustomerReturnDocumentInvalid => "customerDocumentNo",
            SalesRuleCodes.CustomerReturnDocumentDuplicate => "customerDocumentNo",
            SalesRuleCodes.ApprovalReasonTooLong => "reason",
            _ => "lines",
        };
        var status = exception.Code is SalesRuleCodes.CustomerReturnAmountsChanged or SalesRuleCodes.CustomerReturnDocumentDuplicate
            ? 409
            : 422;
        var code = exception.Code switch
        {
            SalesRuleCodes.ApprovalReasonTooLong => SalesDraftProblemCodesV1.ApprovalReasonTooLong,
            _ when exception.Code.StartsWith("sales_customer_return_", StringComparison.Ordinal) => exception.Code,
            _ => "validation_failed",
        };
        return new SalesDraftServiceException(status, code, [new(field, exception.Code)]);
    }

    private static SalesDraftServiceException Duplicate() => new(409, SalesCustomerReturnProblemCodesV1.DocumentDuplicate,
        [new("customerDocumentNo", SalesCustomerReturnProblemCodesV1.DocumentDuplicate)]);

    /// <summary>
    /// The acceptance offer: the command's own actor, correlation and key (Finance replays the same key, payload and actor), the
    /// return at the version being finalised, the admitted original invoice version and the user's selection. The business date is
    /// the finalisation's Istanbul day (SAT-D294 as implemented; see the slice notes for the open question on a retry across days).
    /// </summary>
    private SalesCustomerReturnFinanceOfferV1 Offer(Scope scope, SalesCustomerReturn item, string invoiceVersion,
        IReadOnlyList<SalesCustomerReturnFinanceAllocationV1>? allocations, SalesCommandMetadata metadata) => new(
        scope.CompanyId,
        scope.ActorId,
        metadata.CorrelationId,
        metadata.CausationId,
        metadata.IdempotencyKey,
        item.Id,
        item.Version.ToString(CultureInfo.InvariantCulture),
        item.Header.OriginalInvoiceId,
        invoiceVersion,
        item.Header.PartyId,
        item.Header.CompanyAccountId,
        item.Header.CurrencyId,
        ReturnAmount(item),
        item.Header.FunctionalCurrencyId,
        item.FunctionalTotals is { } functional ? Text(functional.LineTotal.Amount, item.Header.FunctionalScale!.Value) : null,
        Day(timeProvider.GetUtcNow()),
        allocations);

    /// <summary>The return amount Finance is told: the sum of the line totals at the invoice currency's scale (SAT-D292).</summary>
    private static string ReturnAmount(SalesCustomerReturn item) => Text(item.Totals.LineTotal.Amount, item.Header.AmountScale);

    /// <summary>
    /// The original invoice's version Finance admitted: the version the finalisation recorded (later bookkeeping on the invoice
    /// never changes what Finance knows); an invoice without the finalisation facts answers its own version and Finance refuses it.
    /// </summary>
    private static string InvoiceVersion(SalesInvoice invoice) =>
        (invoice.Finalization?.FinalizedVersion ?? invoice.Version).ToString(CultureInfo.InvariantCulture);

    private static SalesCustomerReturn Copy(SalesCustomerReturn item) => SalesCustomerReturn.Restore(item.Id, item.CompanyId, item.Header,
        item.ShipmentId, item.CustomerDocumentNo, item.CustomerDocumentDate, item.State, item.Facts, item.CreatedBy, item.CreatedAtUtc,
        item.Version, item.UpdatedAtUtc, item.Lines);

    private SalesDraftWriteEnvelope Envelope(Scope scope, SalesCommandMetadata metadata, string permission = WritePermission) =>
        new(scope.ActorId, permission, scope.RoleCodes, metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey,
            correlation.SourceChannel, correlation.ClientIpAddress, correlation.UserAgent);

    private async Task<SalesCustomerReturnReplay?> FindReplayAsync(Scope scope, SalesDraftWriteEnvelope envelope, string requestHash,
        string commandName, CancellationToken cancellationToken, string permission = WritePermission) =>
        await StoreAsync(scope, () => returns.FindCustomerReturnReplayAsync(envelope, requestHash, commandName, cancellationToken),
            cancellationToken, permission);

    /// <summary>ADR-011 §4/§7: the stored return as the replay, or the recorded refusal as the same refusal.</summary>
    private static SalesCustomerReturnCommandResult Replayed(SalesCustomerReturnReplay replay)
    {
        if (replay.Refusal is { } refusal)
        {
            throw new SalesDraftServiceException(refusal.Status, refusal.Code, refusal.Errors, isReplay: true,
                originalCorrelationId: replay.OriginalCorrelationId);
        }

        return new SalesCustomerReturnCommandResult(View(replay.CustomerReturn!), true, replay.OriginalCorrelationId);
    }

    private static SalesCustomerReturnCommandResult Result(SalesCustomerReturnStoreResult stored) =>
        new(View(stored.CustomerReturn), stored.Outcome == SalesDraftWriteOutcome.Replayed, stored.OriginalCorrelationId);

    internal static SalesCustomerReturnView View(SalesCustomerReturn item)
    {
        var scale = item.Header.AmountScale;
        var functionalScale = item.Header.FunctionalScale;
        return new SalesCustomerReturnView(
            item.Id,
            item.State.ToString(),
            item.Version.ToString(CultureInfo.InvariantCulture),
            item.Header.OriginalInvoiceId,
            item.Header.OriginalDocumentNo,
            item.ShipmentId,
            item.CustomerDocumentNo,
            item.CustomerDocumentDate,
            item.Header.PartyId,
            item.Header.CompanyAccountId,
            item.Header.CurrencyId,
            scale,
            item.Header.Rate is { } rate ? Exact(rate) : null,
            item.Header.FunctionalCurrencyId,
            functionalScale,
            Amounts(item.Totals, scale),
            item.FunctionalTotals is { } tl ? Amounts(tl, functionalScale!.Value) : null,
            item.NeedsReview,
            item.CreatedBy,
            item.CreatedAtUtc,
            item.Facts.SubmittedBy,
            item.Facts.SubmittedAtUtc,
            item.Facts.ApprovedBy,
            item.Facts.ApprovedAtUtc,
            item.Facts.FinalizedBy,
            item.Facts.FinalizedAtUtc,
            item.Facts.RejectedBy,
            item.Facts.RejectedAtUtc,
            item.Facts.RejectionReason,
            item.Facts.CancelledBy,
            item.Facts.CancelledAtUtc,
            item.Facts.CancellationReason,
            item.UpdatedAtUtc,
            [.. item.Lines.Select(line => new SalesCustomerReturnLineView(
                line.LineNo,
                line.OriginalInvoiceLineId,
                line.OriginalLineNo,
                line.ShipmentId,
                line.ProductId,
                line.UnitId,
                Exact(line.Quantity),
                Exact(line.NetUnitPrice),
                Exact(line.VatPercent),
                line.WithholdingShare is { } share ? Exact(share) : null,
                line.IsLastReturn,
                line.NeedsReview,
                Amounts(line.Amounts, scale),
                line.Functional is { } functional ? Amounts(functional, functionalScale!.Value) : null))]);
    }

    internal static SalesCustomerReturnSummaryView Summary(SalesCustomerReturn item) => new(
        item.Id,
        item.State.ToString(),
        item.Version.ToString(CultureInfo.InvariantCulture),
        item.Header.OriginalInvoiceId,
        item.Header.OriginalDocumentNo,
        item.CustomerDocumentNo,
        item.CustomerDocumentDate,
        item.Header.PartyId,
        item.Header.CurrencyId,
        Text(item.Totals.LineTotal.Amount, item.Header.AmountScale),
        item.IsShipmentLinked,
        item.NeedsReview,
        item.Lines.Count,
        item.CreatedAtUtc);

    private static SalesCustomerReturnAmountsView Amounts(SalesCustomerReturnAmounts amounts, int scale) => new(
        Text(amounts.Net.Amount, scale), Text(amounts.Vat.Amount, scale), Text(amounts.Withholding.Amount, scale),
        Text(amounts.LineTotal.Amount, scale), Text(amounts.DerivedPayable.Amount, scale), Text(amounts.Drift.Amount, scale));

    private static string Text(decimal amount, int scale) =>
        amount.ToString("F" + scale.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    /// <summary>An exact decimal without trailing zeros (quantities, prices, rates and percents are never rounded for display).</summary>
    private static string Exact(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);

    /// <summary>SAT-D10: the Europe/Istanbul day; without the zone nothing is guessed (503, never UTC).</summary>
    private static DateOnly Day(DateTimeOffset instant)
    {
        try
        {
            return SalesBusinessDay.DateOf(instant);
        }
        catch (SalesBusinessDayUnavailableException)
        {
            throw new SalesDraftServiceException(503, "service_unavailable");
        }
    }

    // ---- fences -------------------------------------------------------------------------------------------------------------

    /// <summary>The operation's own name and the price read (the answer carries amounts), measured together.</summary>
    private async Task<Scope> RequireAsync(string permission, CancellationToken cancellationToken)
    {
        var scope = await RequireOneAsync(permission, cancellationToken);
        var price = await RequireOneAsync(PriceReadPermission, cancellationToken);
        if (price.CompanyId != scope.CompanyId || !string.Equals(price.ActorId, scope.ActorId, StringComparison.Ordinal))
        {
            throw new SalesDraftServiceException(403, "forbidden");
        }

        return scope;
    }

    private async Task<Scope> RequireOneAsync(string permission, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            throw new SalesDraftServiceException(401, "unauthenticated");
        }

        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty || !currentUser.CanAccessCompany(companyContext.CompanyId))
        {
            throw new SalesDraftServiceException(403, "forbidden");
        }

        if (!ErpPermissions.IsKnown(permission))
        {
            throw new SalesDraftServiceException(503, SalesDraftProblemCodesV1.PermissionUnregistered);
        }

        var companyId = companyContext.CompanyId;
        var actorId = currentUser.UserId;
        var measured = await authority.RevalidatePermissionScopeAsync(permission, companyId, cancellationToken);
        if (!measured.IsGranted || !measured.IsCompanyWide || !Matches(companyId, actorId))
        {
            throw new SalesDraftServiceException(403, "forbidden");
        }

        return new Scope(companyId, actorId, [.. measured.RoleCodes.Order(StringComparer.Ordinal)]);
    }

    private async Task RequireStillAsync(Scope scope, string permission, CancellationToken cancellationToken)
    {
        var current = await RequireAsync(permission, cancellationToken);
        if (current.CompanyId != scope.CompanyId || !string.Equals(current.ActorId, scope.ActorId, StringComparison.Ordinal))
        {
            throw new SalesDraftServiceException(403, "forbidden");
        }
    }

    private bool Matches(Guid companyId, string actorId) =>
        currentUser.IsAuthenticated
        && string.Equals(currentUser.UserId, actorId, StringComparison.Ordinal)
        && companyContext.IsResolved
        && companyContext.CompanyId == companyId
        && currentUser.CanAccessCompany(companyId);

    /// <summary>A Sales store read under the fence: a store refusal is mapped; the fence is measured again after the await.</summary>
    private async Task<T> StoreAsync<T>(Scope scope, Func<Task<T>> read, CancellationToken cancellationToken,
        string permission = WritePermission)
    {
        T result;
        try
        {
            result = await read();
        }
        catch (SalesDraftStoreException exception)
        {
            await RequireStillAsync(scope, permission, cancellationToken);
            throw Store(exception);
        }

        await RequireStillAsync(scope, permission, cancellationToken);
        return result;
    }

    private static SalesDraftServiceException Store(SalesDraftStoreException exception) => exception.Code switch
    {
        SalesDraftStoreCodes.SameKeyPayloadMismatch => new SalesDraftServiceException(409, SalesDraftProblemCodesV1.SameKeyPayloadMismatch),
        SalesDraftStoreCodes.ConcurrentChange => new SalesDraftServiceException(409, SalesDraftProblemCodesV1.ConcurrentChange),
        SalesDraftStoreCodes.CompanyContextMismatch => new SalesDraftServiceException(403, "forbidden"),
        _ => new SalesDraftServiceException(503, "service_unavailable"),
    };

    private sealed record Scope(Guid CompanyId, string ActorId, IReadOnlyCollection<string> RoleCodes);
}
