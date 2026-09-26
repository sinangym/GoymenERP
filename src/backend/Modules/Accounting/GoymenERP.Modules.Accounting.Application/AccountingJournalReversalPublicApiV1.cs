using System.Globalization;

namespace GoymenERP.Modules.Accounting.Application;

public sealed record ProposeAccountingJournalReversalRequestV1(
    Guid ProposalId,
    Guid SourceJournalId,
    Guid PeriodId,
    string TransactionDate,
    string AccountingDate,
    string? ValueDate,
    string Reason);

public sealed record ApproveAccountingJournalReversalRequestV1(int ExpectedVersion);

/// <summary>
/// Strict public adapter. JSON dates are accepted only as exact ISO calendar dates; permission,
/// idempotency, authority fences and maker-checker remain in the owner lifecycle.
/// </summary>
public sealed class AccountingJournalReversalPublicServiceV1(
    IAccountingJournalReversalLifecycleV1 lifecycle)
{
    public Task<AccountingApiResult<AccountingJournalReversalProposalViewV1>> ProposeAsync(
        ProposeAccountingJournalReversalRequestV1? request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return Task.FromResult(Invalid("request"));
        }

        if (request.ProposalId == Guid.Empty)
        {
            return Task.FromResult(Invalid("proposalId"));
        }

        if (request.SourceJournalId == Guid.Empty)
        {
            return Task.FromResult(Invalid("sourceJournalId"));
        }

        if (request.PeriodId == Guid.Empty)
        {
            return Task.FromResult(Invalid("periodId"));
        }

        if (!TryDate(request.TransactionDate, out var transactionDate))
        {
            return Task.FromResult(Invalid("transactionDate"));
        }

        if (!TryDate(request.AccountingDate, out var accountingDate))
        {
            return Task.FromResult(Invalid("accountingDate"));
        }

        DateOnly? valueDate = null;
        if (request.ValueDate is not null)
        {
            if (!TryDate(request.ValueDate, out var parsedValueDate))
            {
                return Task.FromResult(Invalid("valueDate"));
            }

            valueDate = parsedValueDate;
        }

        // The policy's own bound (trimmed, at most ReasonMaximumLength) is checked here too, so an over-long reason
        // is this route's declared 400 accounting_request_invalid rather than the policy's internal code.
        if (string.IsNullOrWhiteSpace(request.Reason)
            || request.Reason.Trim().Length > AccountingJournalReversalPolicyV1.ReasonMaximumLength)
        {
            return Task.FromResult(Invalid("reason"));
        }

        return lifecycle.ProposeAsync(
            new ProposeAccountingJournalReversalV1(
                request.ProposalId,
                request.SourceJournalId,
                request.PeriodId,
                transactionDate,
                accountingDate,
                valueDate,
                request.Reason),
            idempotencyKey,
            cancellationToken);
    }

    public Task<AccountingApiResult<AccountingJournalReversalProposalViewV1>> ApproveAsync(
        Guid proposalId,
        ApproveAccountingJournalReversalRequestV1? request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return Task.FromResult(Invalid("request"));
        }

        return request.ExpectedVersion <= 0
            ? Task.FromResult(Invalid("expectedVersion"))
            : lifecycle.ApproveAsync(proposalId, request.ExpectedVersion, idempotencyKey, cancellationToken);
    }

    /// <summary>
    /// An exact ISO calendar date that the policy accepts: <c>0001-01-01</c> is the default/minimum date the policy
    /// refuses, so it is refused here with the route's declared 400 as well.
    /// </summary>
    private static bool TryDate(string? text, out DateOnly value)
    {
        value = default;
        return text is not null
            && DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out value)
            && value != default;
    }

    private static AccountingApiResult<AccountingJournalReversalProposalViewV1> Invalid(string field) =>
        new(AccountingApiStatus.Invalid, null, false, AccountingApiCodes.RequestInvalid, field);
}
