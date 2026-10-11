namespace GoymenERP.Modules.Inventory.Application;

public static class InventoryPendingApprovalKindsV1
{
    public const string StockCount = "inventory.stock-count.v1";
    public const string StockScrap = "inventory.stock-scrap.v1";
}

public enum InventoryPendingApprovalReadOutcomeV1
{
    Found,
    NotFound,
    Denied,
    Unavailable,
}

public sealed record InventoryPendingApprovalReadContextV1(
    Guid CompanyId,
    string ActorId);

public sealed record InventoryStockCountPendingApprovalItemV1(
    Guid CountId,
    Guid CompanyId,
    Guid LabelId,
    string LabelCode,
    string LabelVersion,
    string OwnerKind,
    Guid OwnerId,
    string CustodianKind,
    Guid CustodianId,
    Guid LocationId,
    Guid ProductId,
    string Block,
    string BookKg,
    string CountedKg,
    string DifferenceKg,
    string Reason,
    string SubmittedBy,
    DateTimeOffset SubmittedAtUtc);

public sealed record InventoryStockScrapPendingApprovalItemV1(
    Guid ScrapId,
    Guid CompanyId,
    Guid LabelId,
    string LabelCode,
    string LabelVersion,
    string OwnerKind,
    Guid OwnerId,
    string CustodianKind,
    Guid CustodianId,
    Guid LocationId,
    Guid ProductId,
    string Block,
    string BookKg,
    string ScrapKg,
    string RemainingKg,
    string Reason,
    string SubmittedBy,
    DateTimeOffset SubmittedAtUtc);

public sealed record InventoryStockCountPendingApprovalPageV1(
    Guid CompanyId,
    IReadOnlyList<InventoryStockCountPendingApprovalItemV1> Items,
    string? NextCursor);

public sealed record InventoryStockScrapPendingApprovalPageV1(
    Guid CompanyId,
    IReadOnlyList<InventoryStockScrapPendingApprovalItemV1> Items,
    string? NextCursor);

public sealed record InventoryStockCountPendingApprovalDetailV1(
    InventoryStockCountPendingApprovalItemV1 Item);

public sealed record InventoryStockScrapPendingApprovalDetailV1(
    InventoryStockScrapPendingApprovalItemV1 Item);

public sealed record InventoryPendingApprovalReadResultV1<T>(
    InventoryPendingApprovalReadOutcomeV1 Outcome,
    T? Resource) where T : class;

/// <summary>
/// Inventory-owned, company-filtered persistence read. Implementations return only undecided rows submitted by
/// another actor and never write, approve or reject anything.
/// </summary>
public interface IInventoryPendingApprovalReaderV1
{
    Task<IReadOnlyList<InventoryStockCountPendingApprovalItemV1>> ReadStockCountsAsync(
        string excludingActorId,
        DateTimeOffset? afterSubmittedAtUtc,
        Guid? afterCountId,
        int limit,
        CancellationToken cancellationToken);

    Task<InventoryStockCountPendingApprovalItemV1?> ReadStockCountAsync(
        Guid countId,
        string excludingActorId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<InventoryStockScrapPendingApprovalItemV1>> ReadStockScrapsAsync(
        string excludingActorId,
        DateTimeOffset? afterSubmittedAtUtc,
        Guid? afterScrapId,
        int limit,
        CancellationToken cancellationToken);

    Task<InventoryStockScrapPendingApprovalItemV1?> ReadStockScrapAsync(
        Guid scrapId,
        string excludingActorId,
        CancellationToken cancellationToken);
}

/// <summary>
/// D-05 read side for non-zero stock counts that still await the existing stock-count decision. The service
/// revalidates label/balance read and stock-count approve authority before and after every owner read.
/// </summary>
public interface IInventoryStockCountPendingApprovalReadServiceV1
{
    Task<InventoryPendingApprovalReadResultV1<InventoryStockCountPendingApprovalPageV1>> ListAsync(
        InventoryPendingApprovalReadContextV1 context,
        string? cursor,
        CancellationToken cancellationToken);

    Task<InventoryPendingApprovalReadResultV1<InventoryStockCountPendingApprovalDetailV1>> GetAsync(
        InventoryPendingApprovalReadContextV1 context,
        Guid countId,
        CancellationToken cancellationToken);
}

/// <summary>
/// D-05 read side for stock scraps that still await the existing stock-scrap decision. The service revalidates
/// label/balance read and stock-scrap approve authority before and after every owner read.
/// </summary>
public interface IInventoryStockScrapPendingApprovalReadServiceV1
{
    Task<InventoryPendingApprovalReadResultV1<InventoryStockScrapPendingApprovalPageV1>> ListAsync(
        InventoryPendingApprovalReadContextV1 context,
        string? cursor,
        CancellationToken cancellationToken);

    Task<InventoryPendingApprovalReadResultV1<InventoryStockScrapPendingApprovalDetailV1>> GetAsync(
        InventoryPendingApprovalReadContextV1 context,
        Guid scrapId,
        CancellationToken cancellationToken);
}
