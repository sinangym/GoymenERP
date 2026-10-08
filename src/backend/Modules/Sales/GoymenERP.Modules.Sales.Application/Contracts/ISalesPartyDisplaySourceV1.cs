namespace GoymenERP.Modules.Sales.Application.Contracts;

/// <summary>
/// Sales-owned consumer port for the Cari owner's current canonical party presentation.
/// The central adapter validates the supplied server context and delegates one bounded batch
/// to Cari; Sales never reads Cari persistence or invents a display fallback.
/// </summary>
public interface ISalesPartyDisplaySourceV1
{
    Task<SalesPartyDisplayBatchAnswerV1> GetAuthorizedBatchAsync(
        SalesPartyDisplayBatchRequestV1 request,
        CancellationToken cancellationToken = default);
}

public sealed record SalesPartyDisplayBatchRequestV1(
    Guid CompanyId,
    string ActorId,
    IReadOnlyList<Guid> PartyIds);

public enum SalesPartyDisplayBatchOutcomeV1
{
    Success,
    Denied,
    Unavailable,
}

public sealed record SalesPartyDisplayBatchAnswerV1(
    SalesPartyDisplayBatchOutcomeV1 Outcome,
    IReadOnlyList<SalesPartyDisplayV1> Items);

public sealed record SalesPartyDisplayV1(
    Guid PartyId,
    string Code,
    string LegalName,
    string? TradeName);
