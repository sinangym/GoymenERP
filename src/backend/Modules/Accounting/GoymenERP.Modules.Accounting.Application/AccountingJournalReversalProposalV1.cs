namespace GoymenERP.Modules.Accounting.Application;

public enum AccountingJournalReversalProposalStatusV1
{
    Pending = 1,
    Approved = 2,
    Rejected = 3
}

public sealed record ProposeAccountingJournalReversalV1(
    Guid ProposalId,
    Guid SourceJournalId,
    Guid PeriodId,
    DateOnly TransactionDate,
    DateOnly AccountingDate,
    DateOnly? ValueDate,
    string Reason);

public sealed record DecideAccountingJournalReversalV1(
    Guid ProposalId,
    int ExpectedVersion,
    string? Reason = null);

public sealed record AccountingJournalReversalProposalV1(
    Guid ProposalId,
    Guid CompanyId,
    Guid SourceJournalId,
    Guid? ReversalJournalId,
    Guid PeriodId,
    DateOnly TransactionDate,
    DateOnly AccountingDate,
    DateOnly? ValueDate,
    string Reason,
    Guid ProposedBy,
    DateTimeOffset ProposedAtUtc,
    AccountingJournalReversalProposalStatusV1 Status,
    Guid? DecidedBy,
    DateTimeOffset? DecidedAtUtc,
    string? DecisionReason,
    int Version);

public static class AccountingJournalReversalPolicyV1
{
    public static AccountingJournalReversalProposalV1 Propose(
        Guid companyId,
        Guid proposerId,
        ProposeAccountingJournalReversalV1 request,
        DateTimeOffset proposedAtUtc)
    {
        RequireId(companyId, "companyId");
        RequireId(proposerId, "proposerId");
        RequireId(request.ProposalId, "proposalId");
        RequireId(request.SourceJournalId, "sourceJournalId");
        RequireId(request.PeriodId, "periodId");
        RequireDate(request.TransactionDate, "transactionDate");
        RequireDate(request.AccountingDate, "accountingDate");
        if (request.ValueDate is { } valueDate)
        {
            RequireDate(valueDate, "valueDate");
        }

        RequireUtc(proposedAtUtc, "proposedAtUtc");
        var reason = RequireText(request.Reason, "reason");

        return new AccountingJournalReversalProposalV1(
            request.ProposalId,
            companyId,
            request.SourceJournalId,
            ReversalJournalId: null,
            request.PeriodId,
            request.TransactionDate,
            request.AccountingDate,
            request.ValueDate,
            reason,
            proposerId,
            proposedAtUtc,
            AccountingJournalReversalProposalStatusV1.Pending,
            DecidedBy: null,
            DecidedAtUtc: null,
            DecisionReason: null,
            Version: 1);
    }

    public static AccountingJournalReversalProposalV1 Approve(
        AccountingJournalReversalProposalV1 proposal,
        DecideAccountingJournalReversalV1 request,
        Guid checkerId,
        Guid reversalJournalId,
        DateTimeOffset decidedAtUtc)
    {
        ValidateDecision(proposal, request, checkerId, decidedAtUtc);
        RequireId(reversalJournalId, "reversalJournalId");
        if (reversalJournalId == proposal.SourceJournalId)
        {
            throw Invalid("reversalJournalId");
        }

        return proposal with
        {
            ReversalJournalId = reversalJournalId,
            Status = AccountingJournalReversalProposalStatusV1.Approved,
            DecidedBy = checkerId,
            DecidedAtUtc = decidedAtUtc,
            DecisionReason = null,
            Version = checked(proposal.Version + 1)
        };
    }

    public static AccountingJournalReversalProposalV1 Reject(
        AccountingJournalReversalProposalV1 proposal,
        DecideAccountingJournalReversalV1 request,
        Guid checkerId,
        DateTimeOffset decidedAtUtc)
    {
        ValidateDecision(proposal, request, checkerId, decidedAtUtc);
        var reason = RequireText(request.Reason, "reason");

        return proposal with
        {
            ReversalJournalId = null,
            Status = AccountingJournalReversalProposalStatusV1.Rejected,
            DecidedBy = checkerId,
            DecidedAtUtc = decidedAtUtc,
            DecisionReason = reason,
            Version = checked(proposal.Version + 1)
        };
    }

    private static void ValidateDecision(
        AccountingJournalReversalProposalV1 proposal,
        DecideAccountingJournalReversalV1 request,
        Guid checkerId,
        DateTimeOffset decidedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        RequireId(checkerId, "checkerId");
        RequireUtc(decidedAtUtc, "decidedAtUtc");

        if (proposal.Status != AccountingJournalReversalProposalStatusV1.Pending)
        {
            throw new AccountingJournalReversalPolicyExceptionV1(
                AccountingJournalReversalPolicyCodesV1.NotPending,
                "proposalId");
        }

        if (request.ProposalId == Guid.Empty || request.ProposalId != proposal.ProposalId)
        {
            throw Invalid("proposalId");
        }

        if (request.ExpectedVersion <= 0 || request.ExpectedVersion != proposal.Version)
        {
            throw new AccountingJournalReversalPolicyExceptionV1(
                AccountingJournalReversalPolicyCodesV1.VersionConflict,
                "expectedVersion");
        }

        if (checkerId == proposal.ProposedBy)
        {
            throw new AccountingJournalReversalPolicyExceptionV1(
                AccountingJournalReversalPolicyCodesV1.MakerCheckerRequired,
                "checkerId");
        }
    }

    private static void RequireId(Guid value, string field)
    {
        if (value == Guid.Empty)
        {
            throw Invalid(field);
        }
    }

    private static void RequireDate(DateOnly value, string field)
    {
        if (value == default)
        {
            throw Invalid(field);
        }
    }

    private static void RequireUtc(DateTimeOffset value, string field)
    {
        if (value == default || value.Offset != TimeSpan.Zero)
        {
            throw Invalid(field);
        }
    }

    private static string RequireText(string? value, string field)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw Invalid(field);
        }

        return normalized;
    }

    private static AccountingJournalReversalPolicyExceptionV1 Invalid(string field) =>
        new(AccountingJournalReversalPolicyCodesV1.InvalidRequest, field);
}

public static class AccountingJournalReversalPolicyCodesV1
{
    public const string InvalidRequest = "accounting_journal_reversal_invalid";
    public const string NotPending = "accounting_journal_reversal_not_pending";
    public const string VersionConflict = "accounting_journal_reversal_version_conflict";
    public const string MakerCheckerRequired = "accounting_journal_reversal_maker_checker_required";
}

public sealed class AccountingJournalReversalPolicyExceptionV1(string code, string field)
    : InvalidOperationException(code)
{
    public string Code { get; } = code;

    public string Field { get; } = field;
}
