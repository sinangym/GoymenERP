using System.Globalization;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Invoices;

namespace GoymenERP.Modules.Sales.Application;

// S5 API-35 — Fatura çekirdeği 3: the official document match (SAT-D89, SAT-D106, SAT-D229, Sinan S1). The K3 skeleton of
// DecideAsync with one owner read in the middle: the official document comes from Sales' own consumer port (fail-closed), the
// permanent refusals are recorded under the key, the data-dependent ones (not found, mismatch, difference without approval) are
// not — the official document can change, so the same key asks again.
public sealed partial class SalesInvoiceService
{
    private const string MatchPermission = SalesPermissionManifestV1.InvoicesMatch;

    public async Task<SalesInvoiceCommandResult> MatchInvoiceAsync(Guid invoiceId, MatchSalesInvoiceRequest request,
        SalesCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        var scope = await RequireAsync(MatchPermission, cancellationToken);
        await RequirePriceReadAsync(scope, cancellationToken);
        if (request.ApproveDifference)
        {
            // SAT-D106/D204: approving an excess is an approval — the approver's own name is measured too.
            await RequireStillAsync(scope, ApprovePermission, cancellationToken);
        }

        if (!long.TryParse(request.ExpectedVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var expectedVersion)
            || expectedVersion < 1)
        {
            throw new SalesDraftServiceException(400, "validation_failed", [new("expectedVersion", "validation_failed")]);
        }

        if (string.IsNullOrWhiteSpace(request.OfficialInvoiceNo) || request.OfficialInvoiceNo.Length > SalesInvoice.MaximumOfficialNoLength
            || request.Ettn is null || !Guid.TryParseExact(request.Ettn, "D", out var ettnGuid) || ettnGuid == Guid.Empty)
        {
            throw new SalesDraftServiceException(400, "validation_failed");
        }

        var ettn = ettnGuid.ToString("D");
        var requestHash = SalesInvoiceMatchHash.Compute(invoiceId, request);
        var envelope = new SalesDraftWriteEnvelope(scope.ActorId, MatchPermission, scope.RoleCodes, metadata.CorrelationId,
            metadata.CausationId, metadata.IdempotencyKey, correlation.SourceChannel, correlation.ClientIpAddress, correlation.UserAgent);
        var replay = await FindStateReplayAsync(scope, MatchPermission, envelope, requestHash, SalesInvoiceCommands.MatchV1,
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
            MatchPermission) ?? throw new SalesDraftServiceException(404, "not_found");
        var tolerance = (matchTolerance ?? new SalesInvoiceMatchTolerancePolicy()).ToleranceAmount;
        var official = await ReadOfficialAsync(scope, request.OfficialInvoiceNo, ettn, cancellationToken);

        // The reads that explain a refusal: an official document used by another invoice is permanent (recorded under the key);
        // a customer, currency or amount disagreement is data and is answered without recording.
        var usedBy = await StoreAsync(scope, () => invoices.FindMatchedInvoiceIdAsync(official.OfficialNo, official.Ettn, cancellationToken),
            cancellationToken, MatchPermission);
        if (usedBy is { } other && other != invoiceId)
        {
            return await TerminalAsync(scope, MatchPermission, envelope, requestHash, SalesInvoiceCommands.MatchV1, invoiceId,
                new SalesDraftTerminalRefusal(409, SalesInvoiceProblemCodesV1.OfficialDocumentAlreadyMatched,
                    [new("officialInvoiceNo", "already_matched")]), cancellationToken);
        }

        if (invoice.State == SalesInvoiceState.Approved && invoice.Match is null && invoice.Version == expectedVersion)
        {
            ThrowIfDataRefusal(invoice, official, tolerance, request.ApproveDifference);
        }

        // The last fences that can still prevent the write; none after the commit.
        await RequireStillAsync(scope, MatchPermission, cancellationToken);
        await RequirePriceReadAsync(scope, cancellationToken);
        if (request.ApproveDifference)
        {
            await RequireStillAsync(scope, ApprovePermission, cancellationToken);
        }

        SalesInvoiceStoreResult? result;
        try
        {
            result = await invoices.MatchInvoiceAsync(envelope, requestHash, invoiceId,
                (target, now) => target.RecordMatch(expectedVersion, scope.ActorId, official, tolerance, request.ApproveDifference,
                    request.DifferenceReason, now),
                new SalesDraftStateAudit("sales.invoice.matched.v1", "Match", "Satış faturası resmî belgeyle eşleştirildi"),
                cancellationToken);
        }
        catch (SalesVersionConflictException exception)
        {
            return await TerminalAsync(scope, MatchPermission, envelope, requestHash, SalesInvoiceCommands.MatchV1, invoiceId,
                new SalesDraftTerminalRefusal(409, SalesDraftProblemCodesV1.VersionConflict, [new("expectedVersion", exception.Code)]),
                cancellationToken);
        }
        catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.StateConflict)
        {
            return await TerminalAsync(scope, MatchPermission, envelope, requestHash, SalesInvoiceCommands.MatchV1, invoiceId,
                new SalesDraftTerminalRefusal(409, SalesDraftProblemCodesV1.StateConflict, [new("state", exception.Code)]),
                cancellationToken);
        }
        catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.InvoiceSelfApproval)
        {
            return await TerminalAsync(scope, MatchPermission, envelope, requestHash, SalesInvoiceCommands.MatchV1, invoiceId,
                new SalesDraftTerminalRefusal(409, SalesInvoiceProblemCodesV1.SelfApproval, [new("actor", exception.Code)]),
                cancellationToken);
        }
        catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.InvoiceMatchMismatch)
        {
            throw new SalesDraftServiceException(409, SalesInvoiceProblemCodesV1.OfficialMatchMismatch, [new("official", "mismatch")]);
        }
        catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.InvoiceMatchDifferenceNotApproved)
        {
            throw new SalesDraftServiceException(409, SalesInvoiceProblemCodesV1.DifferenceApprovalRequired,
                [new("difference", "exceeds_tolerance")]);
        }
        catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.InvoiceMatchDifferenceReasonRequired)
        {
            throw new SalesDraftServiceException(422, exception.Code, [new("differenceReason", "required")]);
        }
        catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.InvoiceMatchDifferenceNotApplicable)
        {
            throw new SalesDraftServiceException(422, exception.Code, [new("approveDifference", "not_applicable")]);
        }
        catch (SalesRuleException exception) when (exception.Code == SalesRuleCodes.ApprovalReasonTooLong)
        {
            throw new SalesDraftServiceException(422, exception.Code, [new("differenceReason", exception.Code)]);
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
            // ADR-011 §8: a lost same-key race answers the winner; a lost official-document or invoice race is concurrent_change.
            var won = await FindStateReplayAsync(scope, MatchPermission, envelope, requestHash, SalesInvoiceCommands.MatchV1,
                cancellationToken);
            return won is null ? throw Store(exception) : Replayed(won);
        }
        catch (SalesDraftStoreException exception)
        {
            throw Store(exception);
        }

        return result is null
            ? throw new SalesDraftServiceException(404, "not_found")
            : Result(result, result.Outcome == SalesDraftWriteOutcome.Replayed);
    }

    /// <summary>
    /// The official document under the fence. An unwired, failing or inconsistent provider is 503 (never "not found" and never a
    /// default); "not found" is the provider's own answer (409, not recorded).
    /// </summary>
    private async Task<SalesOfficialInvoiceFacts> ReadOfficialAsync(Scope scope, string officialNo, string ettn,
        CancellationToken cancellationToken)
    {
        SalesOfficialInvoiceAnswerV1 answer;
        try
        {
            answer = officialInvoices is null
                ? SalesOfficialInvoiceAnswerV1.Unavailable
                : await officialInvoices.LookupAsync(scope.CompanyId, officialNo, ettn, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            answer = SalesOfficialInvoiceAnswerV1.Unavailable;
        }

        await RequireStillAsync(scope, MatchPermission, cancellationToken);
        switch (answer?.Outcome)
        {
            case SalesOfficialInvoiceOutcomeV1.NotFound:
                throw new SalesDraftServiceException(409, SalesInvoiceProblemCodesV1.OfficialDocumentNotFound,
                    [new("officialInvoiceNo", "not_found")]);
            case SalesOfficialInvoiceOutcomeV1.Found when answer.Invoice is { } found
                && string.Equals(found.OfficialNo, officialNo, StringComparison.Ordinal)
                && string.Equals(found.Ettn, ettn, StringComparison.OrdinalIgnoreCase)
                && found.PayableFunctionalAmount >= 0m && found.PayableAmount >= 0m
                && found.CustomerPartyId != Guid.Empty && found.CurrencyId != Guid.Empty:
                return found with { Ettn = ettn };
            default:
                throw new SalesDraftServiceException(503, SalesInvoiceProblemCodesV1.OfficialInvoiceProviderUnavailable);
        }
    }

    private static void ThrowIfDataRefusal(SalesInvoice invoice, SalesOfficialInvoiceFacts official, decimal tolerance, bool approveDifference)
    {
        var evaluation = invoice.EvaluateMatch(official);
        if (!evaluation.ExactFieldsMatch)
        {
            var errors = new List<SalesDraftFieldError>();
            if (!evaluation.CustomerMatches)
            {
                errors.Add(new("customer", "mismatch"));
            }

            if (!evaluation.CurrencyMatches)
            {
                errors.Add(new("currency", "mismatch"));
            }

            throw new SalesDraftServiceException(409, SalesInvoiceProblemCodesV1.OfficialMatchMismatch, errors);
        }

        if (evaluation.Difference > tolerance && !approveDifference)
        {
            throw new SalesDraftServiceException(409, SalesInvoiceProblemCodesV1.DifferenceApprovalRequired,
                [new("difference", "exceeds_tolerance")]);
        }
    }

    private static SalesInvoiceMatchView? MatchView(SalesInvoiceMatchFacts? match) => match is null
        ? null
        : new SalesInvoiceMatchView(match.OfficialNo, match.Ettn, ExactText(match.InvoiceFunctionalAmount),
            ExactText(match.OfficialFunctionalAmount), ExactText(match.Difference), ExactText(match.Tolerance), match.MatchedBy,
            match.MatchedAtUtc, match.DifferenceApprovedBy, match.DifferenceReason);

    private static string ExactText(decimal value) => value.ToString("0.00######", CultureInfo.InvariantCulture);
}
