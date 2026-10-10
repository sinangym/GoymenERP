namespace GoymenERP.Modules.Inventory.Application;

/// <summary>
/// Inventory-owned consumer seam for the current display of Counterparty owners already present on
/// an authorized, bounded stock-position page. It is not a Party search or transaction-eligibility API.
/// </summary>
public interface IInventoryStockOwnerDisplaySourceV1
{
    Task<InventoryStockOwnerDisplayBatchAnswerV1> GetAuthorizedBatchAsync(
        InventoryStockOwnerDisplayBatchRequestV1 request,
        CancellationToken cancellationToken = default);
}

public enum InventoryStockOwnerDisplayBatchOutcomeV1
{
    Unavailable = 0,
    Denied = 1,
    Found = 2,
}

public sealed record InventoryStockOwnerDisplayBatchRequestV1(
    Guid CompanyId,
    string ActorId,
    IReadOnlyList<Guid> PartyIds);

public sealed record InventoryStockOwnerDisplayBatchAnswerV1(
    InventoryStockOwnerDisplayBatchOutcomeV1 Outcome,
    IReadOnlyList<InventoryStockOwnerDisplayV1> Items);

public sealed record InventoryStockOwnerDisplayV1(
    Guid PartyId,
    string Code,
    string LegalName,
    string? TradeName);
