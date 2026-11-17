namespace GoymenERP.Modules.Finance.Contracts;

/// <summary>FNS-D95: one lender, credit group and currency. Changes use existing credit proposal/approval authority.</summary>
public sealed record CreditLimitProposeV1(Guid CreditAccountId, string ExpectedCreditVersion, Guid? ExpectedCurrentLimitId,
    string? ExpectedCurrentLimitVersion, string Amount, CreditSourceDocumentV1 SourceDocument, string Reason);
public sealed record CreditLimitDecisionV1(Guid LimitId, string ExpectedVersion, bool Approve, string? Reason);
public sealed record CreditLimitDecisionBodyV1(string ExpectedVersion, bool Approve, string? Reason);
public sealed record CreditLimitViewV1(Guid Id, Guid CompanyId, Guid LenderPartyId, Guid LenderCompanyPartyAccountId,
    CreditAccountGroup Group, Guid CurrencyId, string Version, CreditAccountStatus Status, CreditLimitProposeV1 Source,
    bool IsOwnProposal, DateTimeOffset ProposedAtUtc, DateTimeOffset? DecidedAtUtc, string? RejectionReason, Guid? SupersededById);
/// <summary>Only approved, explicitly recorded principal facts; not a claim that historical bank exposure is complete.</summary>
public sealed record CreditLimitAssessmentV1(string RecordedPrincipalNet, bool? ExceedsLimit,
    IReadOnlyList<CreditLimitMovementAssessmentV1> ProposedMovements);
public sealed record CreditLimitMovementAssessmentV1(Guid MovementId, string SourceReference, string ProjectedRecordedPrincipalNet, bool? ExceedsLimit);
public sealed record CreditLimitWorkspaceV1(Guid CompanyId, Guid CreditAccountId, IReadOnlyList<CreditLimitViewV1> Items,
    CreditLimitAssessmentV1 Assessment);
public sealed record CreditLimitResultV1(CommandOutcome Outcome, CreditLimitViewV1? Resource, bool IsReplay,
    string OriginalCorrelationId, IReadOnlyList<ValidationIssue> Issues);
public interface IFinanceCreditLimitServiceV1
{
    Task<ReadResult<CreditLimitWorkspaceV1>> ReadAsync(FinanceContext context, Guid creditAccountId, CancellationToken cancellationToken);
    Task<CreditLimitResultV1> ProposeAsync(FinanceContext context, CommandMetadata metadata, CreditLimitProposeV1 request, CancellationToken cancellationToken);
    Task<CreditLimitResultV1> DecideAsync(FinanceContext context, CommandMetadata metadata, CreditLimitDecisionV1 request, CancellationToken cancellationToken);
}
