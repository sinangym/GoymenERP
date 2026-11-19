using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Invoices;

namespace GoymenERP.Modules.Purchasing.Application;

/// <summary>ALS-D8/D59/D64: owner commands commit before offering the immutable invoice to Finance.
/// Finance is never enlisted in a Purchasing transaction. Failed admission remains explicitly pending.</summary>
public sealed class PurchaseIncomingInvoiceWorkflow(ICompanyContext company, ICurrentUser user,
    IErpAuthorityRevalidationService authority, ICorrelationContext correlation,
    IPurchasingIncomingInvoiceStoreV1 store, TimeProvider time,
    IPurchaseInvoiceFinanceAdmissionPortV1? finance = null, IPurchaseIncomingInvoiceDiagnosticsV1? diagnostics = null,
    IPurchaseIncomingInvoiceMatchSourceV1? matching = null)
    : IPurchaseIncomingInvoiceWorkflowV1
{
    public async Task<PurchaseIncomingInvoiceSnapshot> ReadAsync(Guid invoiceId, CancellationToken cancellationToken = default)
    {
        var scope = await RequireAsync(PurchaseIncomingInvoicePermissionNamesV1.Read, cancellationToken);
        return await FindAsync(scope, invoiceId, cancellationToken);
    }

    public async Task<PurchaseIncomingInvoiceOperationResultV1> ApproveVarianceAsync(Guid invoiceId,
        PurchaseIncomingInvoiceVarianceApprovalRequestV1 request, PurchasingCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scope = await RequireAsync(PurchaseIncomingInvoicePermissionNamesV1.ApproveVariance, cancellationToken);
        var expected = Version(request.ExpectedVersion);
        return await MutateAsync(scope, invoiceId, PurchaseIncomingInvoiceCommandsV1.ApproveVariance,
            request, metadata, invoice => invoice.ApproveVariance(expected, scope.Actor, request.Reason, time.GetUtcNow()), cancellationToken);
    }

    public async Task<PurchaseIncomingInvoiceOperationResultV1> MatchAsync(Guid invoiceId,
        PurchaseIncomingInvoiceMatchRequestV1 request, PurchasingCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scope = await RequireAsync(PurchaseIncomingInvoicePermissionNamesV1.Match, cancellationToken);
        var expected = Version(request.ExpectedVersion);
        var expenses = request.AdditionalCosts ?? [];
        if (request.Receipts is null || request.Receipts.Count + expenses.Count == 0 || request.Receipts.Any(item => item is null
            || item.InvoiceLineId == Guid.Empty || item.PurchaseOrderId == Guid.Empty || item.AcceptanceId == Guid.Empty
            || !ValidQuantity(item.Quantity))
            || request.Receipts.Select(item => item.AcceptanceId).Distinct().Count() != request.Receipts.Count
            || expenses.Any(item => item is null || item.InvoiceLineId == Guid.Empty || item.PurchaseOrderId == Guid.Empty
                || item.CostId == Guid.Empty || !ValidQuantity(item.NetAmount))
            || expenses.Select(item => (item.InvoiceLineId, item.CostId)).Distinct().Count() != expenses.Count)
            throw new PurchasingServiceException(400, "validation_failed");
        return await MutateAsync(scope, invoiceId, PurchaseIncomingInvoiceCommandsV1.Match, request, metadata,
            request.Receipts.Select(item => item.PurchaseOrderId).Concat(expenses.Select(item => item.PurchaseOrderId)).Distinct().Order().ToArray(), async (invoice, token) =>
            {
                if (invoice.Version != expected) throw new PurchasingVersionConflictException(expected, invoice.Version);
                if (invoice.Status != PurchaseIncomingInvoiceStatus.Recorded)
                    throw new PurchasingRuleException(PurchaseIncomingInvoice.StateConflict, "Fatura eşleştirme durumunda değil.");
                if (matching is null) throw new PurchasingServiceException(503, "service_unavailable");
                PurchaseIncomingInvoiceMatchSourceV1 source;
                try { source = await matching.ReadWithAdditionalCostsAsync(invoice, request.Receipts, expenses, token); }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception)
                {
                    await FenceAsync(scope, token);
                    if (exception is PurchasingServiceException or PurchasingRuleException) throw;
                    throw new PurchasingServiceException(503, "service_unavailable");
                }
                await FenceAsync(scope, token);
                if (source is null) throw new PurchasingServiceException(503, "service_unavailable");
                invoice.Match(expected, source.Receipts, source.Policy, scope.Actor, source.AdditionalCosts);
            }, cancellationToken);
    }

    public async Task<PurchaseIncomingInvoiceOperationResultV1> FinalizeAsync(Guid invoiceId,
        PurchaseIncomingInvoiceVersionRequestV1 request, PurchasingCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scope = await RequireAsync(PurchaseIncomingInvoicePermissionNamesV1.Finalize, cancellationToken);
        var expected = Version(request.ExpectedVersion);
        var committed = await MutateAsync(scope, invoiceId, PurchaseIncomingInvoiceCommandsV1.Finalize, request, metadata,
            invoice => invoice.Finalize(expected, Guid.NewGuid(), scope.Actor, time.GetUtcNow()), cancellationToken);
        // A second owner operation starts only after the preceding store call has committed and disposed its transaction.
        var offer = PurchaseIncomingInvoiceFinalizationV1.Finance(committed.Invoice.Restore(), scope.Actor,
            metadata.CorrelationId, metadata.CausationId);
        var acknowledgementMetadata = metadata with { IdempotencyKey = offer.IdempotencyKey };
        try
        {
            var admitted = await AdmitAsync(scope, committed.Invoice,
                new(committed.Invoice.Version.ToString(CultureInfo.InvariantCulture)), acknowledgementMetadata, cancellationToken);
            return committed with { Invoice = admitted.Invoice, FinanceOutcome = admitted.FinanceOutcome };
        }
        catch (OperationCanceledException) { throw; }
        catch (PurchasingServiceException exception) when (exception.Status is 401 or 403) { throw; }
        catch (Exception exception)
        {
            diagnostics?.FinanceHandoffFailed(scope.Company, invoiceId, "after_finalization", exception);
            // The invoice commit stands. No failed/unknown Finance answer is represented as accepted.
            return committed with { FinanceOutcome = "unavailable" };
        }
    }

    public async Task<PurchaseIncomingInvoiceOperationResultV1> RetryFinanceAsync(Guid invoiceId,
        PurchaseIncomingInvoiceVersionRequestV1 request, PurchasingCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scope = await RequireAsync(PurchaseIncomingInvoicePermissionNamesV1.Finalize, cancellationToken);
        _ = Version(request.ExpectedVersion);
        // Acknowledgement is the retry command's only Purchasing write; the same key is read before any Finance call.
        var replay = await ReplayAsync(scope, invoiceId, PurchaseIncomingInvoiceCommandsV1.AcknowledgeFinance,
            request, metadata, cancellationToken);
        if (replay is not null) return replay with { FinanceOutcome = "admitted" };
        var invoice = await FindAsync(scope, invoiceId, cancellationToken);
        return await AdmitAsync(scope, invoice, request, metadata, cancellationToken);
    }

    private async Task<PurchaseIncomingInvoiceOperationResultV1> AdmitAsync(Scope scope, PurchaseIncomingInvoiceSnapshot snapshot,
        PurchaseIncomingInvoiceVersionRequestV1 request, PurchasingCommandMetadata metadata, CancellationToken token)
    {
        var replay = await ReplayAsync(scope, snapshot.Id, PurchaseIncomingInvoiceCommandsV1.AcknowledgeFinance, request, metadata, token);
        if (replay is not null) return replay with { FinanceOutcome = "admitted" };
        var expected = Version(request.ExpectedVersion);
        if (snapshot.Version != expected)
            return await RefuseAsync(scope, snapshot.Id, PurchaseIncomingInvoiceCommandsV1.AcknowledgeFinance, request, metadata,
                PurchasingProblemCodesV1.VersionConflict, token);
        if (snapshot.Status != PurchaseIncomingInvoiceStatus.FinancePending)
            return await RefuseAsync(scope, snapshot.Id, PurchaseIncomingInvoiceCommandsV1.AcknowledgeFinance, request, metadata,
                PurchaseIncomingInvoice.StateConflict, token);
        await FenceAsync(scope, token);
        var offer = PurchaseIncomingInvoiceFinalizationV1.Finance(snapshot.Restore(), scope.Actor, metadata.CorrelationId, metadata.CausationId);
        PurchaseInvoiceFinanceAdmissionAnswerV1 answer;
        try { answer = finance is null ? PurchaseInvoiceFinanceAdmissionAnswerV1.Unavailable
            : await finance.AdmitAsync(offer, token) ?? PurchaseInvoiceFinanceAdmissionAnswerV1.Unavailable; }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            diagnostics?.FinanceHandoffFailed(scope.Company, snapshot.Id, "finance_offer", exception);
            answer = PurchaseInvoiceFinanceAdmissionAnswerV1.Unavailable;
        }
        await FenceAsync(scope, token);
        if (answer.Outcome != PurchaseInvoiceFinanceAdmissionOutcomeV1.Admitted)
            return new(snapshot, false, metadata.CorrelationId, Outcome(answer.Outcome));
        var result = await MutateAsync(scope, snapshot.Id, PurchaseIncomingInvoiceCommandsV1.AcknowledgeFinance, request, metadata,
            invoice => invoice.AcknowledgeFinance(expected, snapshot.AdmissionId!.Value, time.GetUtcNow()), token);
        return result with { FinanceOutcome = "admitted" };
    }

    private Task<PurchaseIncomingInvoiceOperationResultV1> MutateAsync(Scope scope, Guid invoiceId, string command,
        object request, PurchasingCommandMetadata metadata, Action<PurchaseIncomingInvoice> mutate, CancellationToken token)
        => MutateAsync(scope, invoiceId, command, request, metadata, [], (invoice, _) => { mutate(invoice); return Task.CompletedTask; }, token);

    private async Task<PurchaseIncomingInvoiceOperationResultV1> MutateAsync(Scope scope, Guid invoiceId, string command,
        object request, PurchasingCommandMetadata metadata, IReadOnlyList<Guid> ordersToLock,
        Func<PurchaseIncomingInvoice, CancellationToken, Task> mutate, CancellationToken token)
    {
        var replay = await ReplayAsync(scope, invoiceId, command, request, metadata, token);
        if (replay is not null) return replay;
        if (invoiceId == Guid.Empty) throw new PurchasingServiceException(404, "not_found");
        try
        {
            var result = await store.MutateIncomingInvoiceAsync(invoiceId, command, Envelope(scope, metadata),
                PurchasingRequestHash.Compute(command, invoiceId, request), ordersToLock, (invoice, cancellationToken) =>
                {
                    if (invoice.CompanyId != scope.Company) throw new PurchasingServiceException(503, "service_unavailable");
                    return mutate(invoice, cancellationToken);
                }, cancellationToken => FenceAsync(scope, cancellationToken), token);
            return Result(result ?? throw new PurchasingServiceException(404, "not_found"), scope.Company, invoiceId);
        }
        catch (PurchasingVersionConflictException)
        { return await RefuseAsync(scope, invoiceId, command, request, metadata, PurchasingProblemCodesV1.VersionConflict, token); }
        catch (PurchasingRuleException exception)
        { return await RefuseAsync(scope, invoiceId, command, request, metadata, exception.Code, token); }
        catch (ArgumentException) { throw new PurchasingServiceException(400, "validation_failed"); }
        catch (PurchasingStoreException exception) { await FenceAsync(scope, token); throw Map(exception); }
    }

    private async Task<PurchaseIncomingInvoiceOperationResultV1?> ReplayAsync(Scope scope, Guid invoiceId, string command,
        object request, PurchasingCommandMetadata metadata, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        await FenceAsync(scope, token);
        try
        {
            var replay = await store.FindIncomingInvoiceAttemptAsync(command, Envelope(scope, metadata),
                PurchasingRequestHash.Compute(command, invoiceId, request), token);
            await FenceAsync(scope, token);
            return replay is null ? null : Result(replay, scope.Company, invoiceId);
        }
        catch (PurchasingStoreException exception) { await FenceAsync(scope, token); throw Map(exception); }
    }

    private async Task<PurchaseIncomingInvoiceOperationResultV1> RefuseAsync(Scope scope, Guid invoiceId, string command,
        object request, PurchasingCommandMetadata metadata, string code, CancellationToken token)
    {
        try { return Result(await store.RecordIncomingInvoiceRefusalAsync(command, Envelope(scope, metadata),
            PurchasingRequestHash.Compute(command, invoiceId, request), new(409, code, [new("invoice", code)]),
            cancellationToken => FenceAsync(scope, cancellationToken), token), scope.Company, invoiceId); }
        catch (PurchasingStoreException exception) { throw Map(exception); }
    }

    private async Task<PurchaseIncomingInvoiceSnapshot> FindAsync(Scope scope, Guid invoiceId, CancellationToken token)
    {
        if (invoiceId == Guid.Empty) throw new PurchasingServiceException(404, "not_found");
        PurchaseIncomingInvoiceSnapshot? invoice;
        try { invoice = await store.FindIncomingInvoiceAsync(invoiceId, token); }
        catch (PurchasingStoreException exception) { await FenceAsync(scope, token); throw Map(exception); }
        catch (Exception exception) when (exception is InvalidOperationException or JsonException)
        { await FenceAsync(scope, token); throw new PurchasingServiceException(503, "service_unavailable"); }
        await FenceAsync(scope, token);
        if (invoice is null) throw new PurchasingServiceException(404, "not_found");
        if (invoice.CompanyId != scope.Company || invoice.Id != invoiceId) throw new PurchasingServiceException(503, "service_unavailable");
        ValidateSnapshot(invoice);
        return invoice;
    }

    private async Task<Scope> RequireAsync(string permission, CancellationToken token)
    {
        if (!user.IsAuthenticated || string.IsNullOrWhiteSpace(user.UserId)) throw new PurchasingServiceException(401, "unauthenticated");
        if (!company.IsResolved || company.CompanyId == Guid.Empty || !user.CanAccessCompany(company.CompanyId))
            throw new PurchasingServiceException(403, "forbidden");
        var companyId = company.CompanyId; var actor = user.UserId;
        var measured = await authority.RevalidatePermissionScopeAsync(permission, companyId, token);
        if (!measured.IsGranted || !measured.IsCompanyWide || !Same(companyId, actor)) throw new PurchasingServiceException(403, "forbidden");
        var price = await authority.RevalidatePermissionScopeAsync(PurchasingPermissionManifestV1.OrdersPriceRead, companyId, token);
        if (!price.IsGranted || !price.IsCompanyWide || !Same(companyId, actor)) throw new PurchasingServiceException(403, "forbidden");
        return new(companyId, actor, permission, measured.RoleCodes.Order(StringComparer.Ordinal).ToArray());
    }

    private async Task FenceAsync(Scope scope, CancellationToken token)
    {
        var current = await RequireAsync(scope.Permission, token);
        if (current.Company != scope.Company || current.Actor != scope.Actor) throw new PurchasingServiceException(403, "forbidden");
    }
    private bool Same(Guid companyId, string actor) => company.IsResolved && company.CompanyId == companyId
        && user.IsAuthenticated && user.UserId == actor && user.CanAccessCompany(companyId);
    private PurchasingWriteEnvelope Envelope(Scope scope, PurchasingCommandMetadata metadata) => new(scope.Actor, scope.Permission,
        scope.Roles, metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey, correlation.SourceChannel,
        correlation.ClientIpAddress, correlation.UserAgent);
    private static long Version(string text) => long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0
        && text == value.ToString(CultureInfo.InvariantCulture)
        ? value : throw new PurchasingServiceException(400, "validation_failed");
    private static bool ValidQuantity(string text) => decimal.TryParse(text, NumberStyles.AllowDecimalPoint,
        CultureInfo.InvariantCulture, out var value) && value > 0 && value <= PurchaseIncomingInvoice.MaximumStoredQuantity
        && decimal.Round(value, PurchaseIncomingInvoice.MaximumStoredScale) == value
        && PurchaseAcceptanceParticipantContractV1.FormatQuantity(value) == text;
    private static PurchaseIncomingInvoiceOperationResultV1 Result(PurchasingIncomingInvoiceWriteResult result, Guid companyId, Guid invoiceId)
    {
        if (result.Refusal is { } refusal) throw new PurchasingServiceException(refusal.Status, refusal.Code, refusal.Errors,
            result.IsReplay, result.OriginalCorrelationId);
        if (result.Invoice is not { } invoice || invoice.CompanyId != companyId || invoice.Id != invoiceId)
            throw new PurchasingServiceException(503, "service_unavailable");
        ValidateSnapshot(invoice);
        return new(invoice, result.IsReplay, result.OriginalCorrelationId);
    }
    private static void ValidateSnapshot(PurchaseIncomingInvoiceSnapshot invoice)
    {
        try { _ = invoice.Restore(); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or PurchasingRuleException or OverflowException)
        { throw new PurchasingServiceException(503, "service_unavailable"); }
    }
    private static PurchasingServiceException Map(PurchasingStoreException exception) => exception.Code switch
    {
        PurchasingStoreCodes.SameKeyPayloadMismatch => new(409, PurchasingProblemCodesV1.SameKeyPayloadMismatch),
        PurchasingStoreCodes.ConcurrentChange => new(409, PurchasingProblemCodesV1.ConcurrentChange),
        PurchasingStoreCodes.LineBusy => new(409, PurchasingProblemCodesV1.LineBusy),
        PurchasingStoreCodes.CompanyContextMismatch => new(403, "forbidden"),
        _ => new(503, "service_unavailable"),
    };
    private static string Outcome(PurchaseInvoiceFinanceAdmissionOutcomeV1 outcome) => outcome switch
    {
        PurchaseInvoiceFinanceAdmissionOutcomeV1.Refused => "refused",
        PurchaseInvoiceFinanceAdmissionOutcomeV1.KeyConflict => "key_conflict",
        PurchaseInvoiceFinanceAdmissionOutcomeV1.Retry => "retry",
        _ => "unavailable",
    };
    private sealed record Scope(Guid Company, string Actor, string Permission, IReadOnlyCollection<string> Roles);
}
