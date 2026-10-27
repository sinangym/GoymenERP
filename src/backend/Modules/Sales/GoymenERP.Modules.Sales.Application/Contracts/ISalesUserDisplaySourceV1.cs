namespace GoymenERP.Modules.Sales.Application.Contracts;

/// <summary>
/// MOD-08: Sales-owned consumer port for the Identity owner's display name of the people a Sales record already names
/// (the approval submitter and decider, the customer acceptance recorder). The central adapter validates the supplied
/// server context and delegates one bounded batch to Identity's user directory; Sales never reads Identity
/// persistence. A person the owner does not answer keeps the raw id the record already shows: no name is invented.
/// </summary>
public interface ISalesUserDisplaySourceV1
{
    Task<SalesUserDisplayBatchAnswerV1> GetAuthorizedBatchAsync(
        SalesUserDisplayBatchRequestV1 request,
        CancellationToken cancellationToken = default);
}

/// <summary><c>CompanyId</c> and <c>ActorId</c> are copies of the server-resolved context; <c>UserIds</c> come from authorized Sales records.</summary>
public sealed record SalesUserDisplayBatchRequestV1(
    Guid CompanyId,
    string ActorId,
    IReadOnlyList<Guid> UserIds);

public enum SalesUserDisplayBatchOutcomeV1
{
    Success,
    Denied,
    Unavailable,
}

public sealed record SalesUserDisplayBatchAnswerV1(
    SalesUserDisplayBatchOutcomeV1 Outcome,
    IReadOnlyList<SalesUserDisplayV1> Items);

/// <summary>
/// One person a Sales read names, as the Identity owner shows them in the record's company. <c>UserId</c> is the same
/// id the record carries (for example <c>approval.submittedBy</c>); <c>IsActiveInCompany</c> is false for a former
/// member, whose name is still shown for the facts they recorded.
/// </summary>
public sealed record SalesUserDisplayV1(
    Guid UserId,
    string DisplayName,
    bool IsActiveInCompany);
