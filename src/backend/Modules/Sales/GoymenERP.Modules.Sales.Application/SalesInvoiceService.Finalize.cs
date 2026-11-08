using System.Globalization;
using System.Text;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Invoices;

namespace GoymenERP.Modules.Sales.Application;

// S5 API-36 — Fatura çekirdeği 4: the finalisation (SAT-D208, Sinan S6) and the Finance hand-off. The K3 skeleton of DecideAsync with
// two differences: a missing official match is data (409, not recorded: the same key asks again once the match exists), and the
// commit is followed by the hand-off to Finance, which is NEVER part of the commit — the invoice is committed "Finans kabulü
// bekliyor" first, the answer is a second, separate save of the ledger, and nothing here ever calls a waiting invoice admitted.
public sealed partial class SalesInvoiceService
{
    private const string FinalizePermission = SalesPermissionManifestV1.InvoicesFinalize;
    private const string AdmissionKeyPrefix = "sales-invoice";
    private const int MaximumOutcomeReasonLength = 40;

    public async Task<SalesInvoiceCommandResult> FinalizeInvoiceAsync(Guid invoiceId, SalesDraftStateCommandRequest request,
        SalesCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        var scope = await RequireAsync(FinalizePermission, cancellationToken);
        await RequirePriceReadAsync(scope, cancellationToken);
        var expectedVersion = ParseExpectedVersion(request.ExpectedVersion);
        var requestHash = SalesInvoiceStateHash.Compute(SalesInvoiceCommands.FinalizeV1, invoiceId, request);
        var envelope = Envelope(scope, metadata);
        var replay = await FindStateReplayAsync(scope, FinalizePermission, envelope, requestHash, SalesInvoiceCommands.FinalizeV1,
            cancellationToken);
        if (replay is not null)
        {
            return Replayed(replay);
        }

        if (invoiceId == Guid.Empty)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        // The last fences that can still prevent the write; none after the commit.
        await RequireStillAsync(scope, FinalizePermission, cancellationToken);
        await RequirePriceReadAsync(scope, cancellationToken);
        SalesInvoiceStoreResult? committed;
        try
        {
            committed = await invoices.FinalizeInvoiceAsync(envelope, requestHash, invoiceId,
                (target, now) => target.Finalize(expectedVersion, scope.ActorId, Guid.NewGuid, now),
                new SalesDraftStateAudit("sales.invoice.finalized.v1", "Finalize", "Satış faturası kesinleştirildi"), cancellationToken);
        }
        catch (SalesVersionConflictException exception)
        {
            return await TerminalAsync(scope, FinalizePermission, envelope, requestHash, SalesInvoiceCommands.FinalizeV1, invoiceId,
                new SalesDraftTerminalRefusal(409, SalesDraftProblemCodesV1.VersionConflict, [new("expectedVersion", exception.Code)]),
                cancellationToken);
        }
        catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.StateConflict)
        {
            return await TerminalAsync(scope, FinalizePermission, envelope, requestHash, SalesInvoiceCommands.FinalizeV1, invoiceId,
                new SalesDraftTerminalRefusal(409, SalesDraftProblemCodesV1.StateConflict, [new("state", exception.Code)]),
                cancellationToken);
        }
        catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.InvoiceMatchRequired)
        {
            // Data, not a permanent refusal: the match can be recorded and the same key asks again.
            throw new SalesDraftServiceException(409, SalesInvoiceProblemCodesV1.MatchRequired, [new("match", "required")]);
        }
        catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.InvoiceInstalmentFunctionalInvalid)
        {
            throw new SalesDraftServiceException(422, SalesInvoiceProblemCodesV1.InstalmentFunctionalInvalid,
                [new("instalments", "functional_invalid")]);
        }
        catch (SalesRuleException exception)
        {
            throw new SalesDraftServiceException(422, "validation_failed", [new("request", exception.Code)]);
        }
        catch (ArgumentException)
        {
            throw new SalesDraftServiceException(400, "validation_failed");
        }
        catch (SalesDraftStoreException exception) when (exception.Code == SalesDraftStoreCodes.ConcurrentChange)
        {
            // ADR-011 §8: a lost same-key race answers the winner; a lost version race is the caller's concurrent_change.
            var won = await FindStateReplayAsync(scope, FinalizePermission, envelope, requestHash, SalesInvoiceCommands.FinalizeV1,
                cancellationToken);
            return won is null ? throw Store(exception) : Replayed(won);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        if (committed is null)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        if (committed.Outcome == SalesDraftWriteOutcome.Replayed || committed.Invoice.Finalization is not { } finalization)
        {
            return Result(committed, committed.Outcome == SalesDraftWriteOutcome.Replayed);
        }

        // SAT-D208: committed first; the hand-off is the second, separate step. Its failure never undoes or hides the commit.
        var offered = await HandOffAsync(scope, committed, envelope, metadata, FirstAdmissionKey(committed.Invoice.Id, finalization),
            requestHash: null, commandName: null, cancellationToken);
        return Result(offered ?? committed, isReplay: false);
    }

    /// <summary>
    /// SAT-D208: re-drives the Finance hand-off of a finalised invoice that still waits. The same admission id with a key per attempt;
    /// Finance replays what it already holds. An admitted invoice is never offered again.
    /// </summary>
    public async Task<SalesInvoiceCommandResult> ResendFinanceAdmissionAsync(Guid invoiceId, SalesDraftStateCommandRequest request,
        SalesCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        var scope = await RequireAsync(FinalizePermission, cancellationToken);
        await RequirePriceReadAsync(scope, cancellationToken);
        var expectedVersion = ParseExpectedVersion(request.ExpectedVersion);
        var requestHash = SalesInvoiceStateHash.Compute(SalesInvoiceCommands.ResendFinanceV1, invoiceId, request);
        var envelope = Envelope(scope, metadata);
        var replay = await FindStateReplayAsync(scope, FinalizePermission, envelope, requestHash, SalesInvoiceCommands.ResendFinanceV1,
            cancellationToken);
        if (replay is not null)
        {
            return Replayed(replay);
        }

        if (invoiceId == Guid.Empty)
        {
            throw new SalesDraftServiceException(404, "not_found");
        }

        var invoice = await StoreAsync(scope, () => invoices.FindInvoiceAsync(invoiceId, cancellationToken), cancellationToken,
            FinalizePermission) ?? throw new SalesDraftServiceException(404, "not_found");
        if (invoice.Version != expectedVersion)
        {
            return await TerminalAsync(scope, FinalizePermission, envelope, requestHash, SalesInvoiceCommands.ResendFinanceV1, invoiceId,
                new SalesDraftTerminalRefusal(409, SalesDraftProblemCodesV1.VersionConflict,
                    [new("expectedVersion", SalesDraftProblemCodesV1.VersionConflict)]), cancellationToken);
        }

        if (invoice.State != SalesInvoiceState.Finalized || invoice.Finalization is not { FinanceStatus: SalesInvoiceFinanceStatus.Waiting })
        {
            return await TerminalAsync(scope, FinalizePermission, envelope, requestHash, SalesInvoiceCommands.ResendFinanceV1, invoiceId,
                new SalesDraftTerminalRefusal(409, SalesDraftProblemCodesV1.StateConflict, [new("state", SalesRuleCodes.StateConflict)]),
                cancellationToken);
        }

        var stored = new SalesInvoiceStoreResult(invoice, SalesDraftWriteOutcome.Recorded, envelope.CorrelationId.Trim());
        try
        {
            var offered = await HandOffAsync(scope, stored, envelope, metadata, RedriveKey(invoice.Id, invoice.Finalization),
                requestHash, SalesInvoiceCommands.ResendFinanceV1, cancellationToken, throwOnStoreFailure: true);
            return offered is null
                ? throw new SalesDraftServiceException(503, "service_unavailable")
                : Result(offered, offered.Outcome == SalesDraftWriteOutcome.Replayed);
        }
        catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.StateConflict)
        {
            return await TerminalAsync(scope, FinalizePermission, envelope, requestHash, SalesInvoiceCommands.ResendFinanceV1, invoiceId,
                new SalesDraftTerminalRefusal(409, SalesDraftProblemCodesV1.StateConflict, [new("state", exception.Code)]),
                cancellationToken);
        }
        catch (SalesDraftStoreException exception) when (exception.Code == SalesDraftStoreCodes.ConcurrentChange)
        {
            var won = await FindStateReplayAsync(scope, FinalizePermission, envelope, requestHash, SalesInvoiceCommands.ResendFinanceV1,
                cancellationToken);
            return won is null ? throw Store(exception) : Replayed(won);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }
    }

    /// <summary>
    /// Offers the finalised plan to Finance and records the answer in the ledger (a second save, no version step). Without a registered
    /// port nothing is offered and nothing is recorded (the invoice stays waiting). After the finalisation commit a failure here is
    /// swallowed (the commit stands, the re-drive repairs); for the explicit re-drive the store failure is the caller's.
    /// </summary>
    private async Task<SalesInvoiceStoreResult?> HandOffAsync(Scope scope, SalesInvoiceStoreResult committed, SalesDraftWriteEnvelope envelope,
        SalesCommandMetadata metadata, string admissionKey, string? requestHash, string? commandName, CancellationToken cancellationToken,
        bool throwOnStoreFailure = false)
    {
        if (financeAdmission is null || committed.Invoice.Finalization is not { } finalization)
        {
            return null;
        }

        var answer = await OfferAsync(scope, committed.Invoice, finalization, metadata, admissionKey, cancellationToken);
        if (throwOnStoreFailure)
        {
            // The re-drive is the caller's command: the fence is measured again before its only save.
            await RequireStillAsync(scope, FinalizePermission, cancellationToken);
        }

        try
        {
            var outcome = OutcomeText(answer);
            return await invoices.RecordFinanceAttemptAsync(envelope, requestHash, commandName, committed.Invoice.Id,
                (target, now) => target.RecordFinanceAttempt(answer.Outcome == SalesFinanceAdmissionOutcomeV1.Admitted, outcome, now),
                new SalesDraftStateAudit("sales.invoice.finance-offered.v1", "FinanceOffer", "Satış faturası Finans'a sunuldu"),
                cancellationToken);
        }
        catch (Exception exception) when (!throwOnStoreFailure && exception is not OperationCanceledException)
        {
            return null;
        }
    }

    private async Task<SalesFinanceAdmissionAnswerV1> OfferAsync(Scope scope, SalesInvoice invoice, SalesInvoiceFinalization finalization,
        SalesCommandMetadata metadata, string admissionKey, CancellationToken cancellationToken)
    {
        var header = invoice.Header;
        var scale = header.AmountScale;
        var functionalScale = header.Rate?.FunctionalScale ?? 0;
        var request = new SalesFinanceAdmissionRequestV1(scope.CompanyId, scope.ActorId, metadata.CorrelationId, metadata.CausationId,
            admissionKey, finalization.AdmissionId, invoice.Id, invoice.Version.ToString(CultureInfo.InvariantCulture), header.PartyId,
            header.CompanyAccountId, header.CurrencyId, header.Rate is { } rate ? Decimal(rate.Rate) : null,
            [.. finalization.Plan.Select(item => new SalesFinanceInstalmentV1(item.OpenItemId, item.InstallmentId, item.SequenceNo,
                item.DueDate, Text(item.Amount, scale), item.FunctionalAmount is { } functional ? Text(functional, functionalScale) : null))]);
        try
        {
            return await financeAdmission!.AdmitAsync(request, cancellationToken) ?? SalesFinanceAdmissionAnswerV1.Unavailable;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Fail closed: a throwing owner is "nothing is known", never an admission.
            return SalesFinanceAdmissionAnswerV1.Unavailable;
        }
    }

    private static string FirstAdmissionKey(Guid invoiceId, SalesInvoiceFinalization finalization) => string.Create(CultureInfo.InvariantCulture,
        $"{AdmissionKeyPrefix}:{invoiceId:D}:{finalization.FinalizedVersion}");

    private static string RedriveKey(Guid invoiceId, SalesInvoiceFinalization finalization) => string.Create(CultureInfo.InvariantCulture,
        $"{AdmissionKeyPrefix}:{invoiceId:D}:{finalization.FinalizedVersion}:{finalization.AttemptCount + 1}");

    /// <summary>The ledger's last outcome: the outcome name and a short ASCII reason, within the column.</summary>
    private static string OutcomeText(SalesFinanceAdmissionAnswerV1 answer)
    {
        var builder = new StringBuilder(answer.Outcome.ToString().ToLowerInvariant());
        var reason = new StringBuilder();
        foreach (var character in answer.Reason ?? string.Empty)
        {
            if (reason.Length >= MaximumOutcomeReasonLength)
            {
                break;
            }

            if (character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-' or '.')
            {
                reason.Append(character);
            }
            else if (character is >= 'A' and <= 'Z')
            {
                reason.Append(char.ToLowerInvariant(character));
            }
        }

        return reason.Length == 0 ? builder.ToString() : builder.Append(':').Append(reason).ToString();
    }

    private static long ParseExpectedVersion(string? text) =>
        long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var version) && version >= 1
            ? version
            : throw new SalesDraftServiceException(400, "validation_failed", [new("expectedVersion", "validation_failed")]);

    private SalesDraftWriteEnvelope Envelope(Scope scope, SalesCommandMetadata metadata) => new(scope.ActorId, FinalizePermission,
        scope.RoleCodes, metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey, correlation.SourceChannel,
        correlation.ClientIpAddress, correlation.UserAgent);

    private static SalesInvoiceFinalizationView? FinalizationView(SalesInvoice invoice)
    {
        if (invoice.Finalization is not { } facts)
        {
            return null;
        }

        var scale = invoice.Header.AmountScale;
        var functionalScale = invoice.Header.Rate?.FunctionalScale ?? 0;
        return new SalesInvoiceFinalizationView(facts.AdmissionId, facts.FinalizedVersion.ToString(CultureInfo.InvariantCulture),
            facts.FinalizedBy, facts.FinalizedAtUtc,
            facts.FinanceStatus == SalesInvoiceFinanceStatus.Admitted
                ? SalesInvoiceFinanceStatusesV1.Admitted
                : SalesInvoiceFinanceStatusesV1.Waiting,
            facts.AdmittedAtUtc, facts.AttemptCount, facts.LastAttemptAtUtc, facts.LastOutcome, SalesInvoiceAccountingStatusesV1.NoSource,
            [.. facts.Plan.Select(item => new SalesInvoiceFinalInstalmentView(item.SequenceNo, item.DueDate, Text(item.Amount, scale),
                item.FunctionalAmount is { } functional ? Text(functional, functionalScale) : null, item.OpenItemId, item.InstallmentId))]);
    }
}
