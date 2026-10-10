namespace GoymenERP.Modules.Counterparty.Application.Contracts;

/// <summary>
/// Cari-owned projection for a central Inventory stock-owner adapter that has already authorized the Inventory stock-owner records
/// from which every requested Party id was obtained. This is not a general Cari read, search, list,
/// or card contract and it must never accept arbitrary client-provided Party ids. Same-company historical
/// records remain displayable after Party or account deactivation; this contract grants no selection or
/// new-transaction eligibility.
/// </summary>
public interface ICounterpartyInventoryStockOwnerDisplaySourceV1
{
    Task<CounterpartyInventoryStockOwnerDisplayAnswerV1> ReadForAuthorizedInventoryStockOwnersAsync(
        CounterpartyInventoryStockOwnerDisplayRequestV1 request,
        CancellationToken cancellationToken = default);
}

public enum CounterpartyInventoryStockOwnerDisplayOutcomeV1
{
    Unavailable = 0,
    Denied = 1,
    Found = 2,
}

/// <summary>
/// CompanyId and ActorId are copies of the server-resolved context, never caller overrides.
/// PartyIds must be the distinct ids extracted from already-authorized Inventory stock-owner records.
/// </summary>
public sealed record CounterpartyInventoryStockOwnerDisplayRequestV1(
    Guid CompanyId,
    string ActorId,
    IReadOnlyList<Guid> PartyIds);

public sealed record CounterpartyInventoryStockOwnerDisplayAnswerV1(
    CounterpartyInventoryStockOwnerDisplayOutcomeV1 Outcome,
    IReadOnlyList<CounterpartyInventoryStockOwnerDisplayV1> Items);

public sealed record CounterpartyInventoryStockOwnerDisplayV1(
    Guid PartyId,
    string Code,
    string LegalName,
    string? TradeName);
