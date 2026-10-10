using System.Globalization;
using System.Text;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Modules.Inventory.Infrastructure;

public sealed class InventoryPendingApprovalReadServiceV1(
    IInventoryPendingApprovalReaderV1 reader,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority)
    : IInventoryStockCountPendingApprovalReadServiceV1,
      IInventoryStockScrapPendingApprovalReadServiceV1
{
    private const int PageSize = 50;
    internal const int ReadLimit = PageSize + 1;
    internal const int MaximumOffset = 1_000_000;

    Task<InventoryPendingApprovalReadResultV1<InventoryStockCountPendingApprovalPageV1>>
        IInventoryStockCountPendingApprovalReadServiceV1.ListAsync(
            InventoryPendingApprovalReadContextV1 context,
            string? cursor,
            CancellationToken cancellationToken) => ListStockCountsAsync(context, cursor, cancellationToken);

    Task<InventoryPendingApprovalReadResultV1<InventoryStockCountPendingApprovalDetailV1>>
        IInventoryStockCountPendingApprovalReadServiceV1.GetAsync(
            InventoryPendingApprovalReadContextV1 context,
            Guid countId,
            CancellationToken cancellationToken) => GetStockCountAsync(context, countId, cancellationToken);

    Task<InventoryPendingApprovalReadResultV1<InventoryStockScrapPendingApprovalPageV1>>
        IInventoryStockScrapPendingApprovalReadServiceV1.ListAsync(
            InventoryPendingApprovalReadContextV1 context,
            string? cursor,
            CancellationToken cancellationToken) => ListStockScrapsAsync(context, cursor, cancellationToken);

    Task<InventoryPendingApprovalReadResultV1<InventoryStockScrapPendingApprovalDetailV1>>
        IInventoryStockScrapPendingApprovalReadServiceV1.GetAsync(
            InventoryPendingApprovalReadContextV1 context,
            Guid scrapId,
            CancellationToken cancellationToken) => GetStockScrapAsync(context, scrapId, cancellationToken);

    private async Task<InventoryPendingApprovalReadResultV1<InventoryStockCountPendingApprovalPageV1>> ListStockCountsAsync(
        InventoryPendingApprovalReadContextV1 context,
        string? cursor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryCursor(context.CompanyId, InventoryPendingApprovalKindsV1.StockCount, cursor, out var offset))
        {
            return NotFound<InventoryStockCountPendingApprovalPageV1>();
        }
        if (await FenceAsync<InventoryStockCountPendingApprovalPageV1>(context,
                InventoryPermissionManifestV1.StockCountApprove, cancellationToken) is { } refused)
        {
            return refused;
        }

        IReadOnlyList<InventoryStockCountPendingApprovalItemV1> rows;
        try
        {
            rows = await reader.ReadStockCountsAsync(context.ActorId, offset, ReadLimit, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return await FailureAfterReadAsync<InventoryStockCountPendingApprovalPageV1>(context,
                InventoryPermissionManifestV1.StockCountApprove, cancellationToken);
        }

        if (await FenceAsync<InventoryStockCountPendingApprovalPageV1>(context,
                InventoryPermissionManifestV1.StockCountApprove, cancellationToken) is { } afterRead)
        {
            return afterRead;
        }
        if (!ValidCounts(context, rows))
        {
            return Unavailable<InventoryStockCountPendingApprovalPageV1>();
        }

        var hasMore = rows.Count > PageSize;
        return new(InventoryPendingApprovalReadOutcomeV1.Found,
            new InventoryStockCountPendingApprovalPageV1(
                context.CompanyId,
                rows.Take(PageSize).ToArray(),
                hasMore
                    ? EncodeCursor(context.CompanyId, InventoryPendingApprovalKindsV1.StockCount,
                        checked(offset + PageSize))
                    : null));
    }

    private async Task<InventoryPendingApprovalReadResultV1<InventoryStockCountPendingApprovalDetailV1>> GetStockCountAsync(
        InventoryPendingApprovalReadContextV1 context,
        Guid countId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (countId == Guid.Empty)
        {
            return NotFound<InventoryStockCountPendingApprovalDetailV1>();
        }
        if (await FenceAsync<InventoryStockCountPendingApprovalDetailV1>(context,
                InventoryPermissionManifestV1.StockCountApprove, cancellationToken) is { } refused)
        {
            return refused;
        }

        InventoryStockCountPendingApprovalItemV1? item;
        try
        {
            item = await reader.ReadStockCountAsync(countId, context.ActorId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return await FailureAfterReadAsync<InventoryStockCountPendingApprovalDetailV1>(context,
                InventoryPermissionManifestV1.StockCountApprove, cancellationToken);
        }

        if (await FenceAsync<InventoryStockCountPendingApprovalDetailV1>(context,
                InventoryPermissionManifestV1.StockCountApprove, cancellationToken) is { } afterRead)
        {
            return afterRead;
        }
        if (item is null)
        {
            return NotFound<InventoryStockCountPendingApprovalDetailV1>();
        }
        return ValidCount(context, item) && item.CountId == countId
            ? new(InventoryPendingApprovalReadOutcomeV1.Found,
                new InventoryStockCountPendingApprovalDetailV1(item))
            : Unavailable<InventoryStockCountPendingApprovalDetailV1>();
    }

    private async Task<InventoryPendingApprovalReadResultV1<InventoryStockScrapPendingApprovalPageV1>> ListStockScrapsAsync(
        InventoryPendingApprovalReadContextV1 context,
        string? cursor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryCursor(context.CompanyId, InventoryPendingApprovalKindsV1.StockScrap, cursor, out var offset))
        {
            return NotFound<InventoryStockScrapPendingApprovalPageV1>();
        }
        if (await FenceAsync<InventoryStockScrapPendingApprovalPageV1>(context,
                InventoryPermissionManifestV1.StockScrapApprove, cancellationToken) is { } refused)
        {
            return refused;
        }

        IReadOnlyList<InventoryStockScrapPendingApprovalItemV1> rows;
        try
        {
            rows = await reader.ReadStockScrapsAsync(context.ActorId, offset, ReadLimit, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return await FailureAfterReadAsync<InventoryStockScrapPendingApprovalPageV1>(context,
                InventoryPermissionManifestV1.StockScrapApprove, cancellationToken);
        }

        if (await FenceAsync<InventoryStockScrapPendingApprovalPageV1>(context,
                InventoryPermissionManifestV1.StockScrapApprove, cancellationToken) is { } afterRead)
        {
            return afterRead;
        }
        if (!ValidScraps(context, rows))
        {
            return Unavailable<InventoryStockScrapPendingApprovalPageV1>();
        }

        var hasMore = rows.Count > PageSize;
        return new(InventoryPendingApprovalReadOutcomeV1.Found,
            new InventoryStockScrapPendingApprovalPageV1(
                context.CompanyId,
                rows.Take(PageSize).ToArray(),
                hasMore
                    ? EncodeCursor(context.CompanyId, InventoryPendingApprovalKindsV1.StockScrap,
                        checked(offset + PageSize))
                    : null));
    }

    private async Task<InventoryPendingApprovalReadResultV1<InventoryStockScrapPendingApprovalDetailV1>> GetStockScrapAsync(
        InventoryPendingApprovalReadContextV1 context,
        Guid scrapId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (scrapId == Guid.Empty)
        {
            return NotFound<InventoryStockScrapPendingApprovalDetailV1>();
        }
        if (await FenceAsync<InventoryStockScrapPendingApprovalDetailV1>(context,
                InventoryPermissionManifestV1.StockScrapApprove, cancellationToken) is { } refused)
        {
            return refused;
        }

        InventoryStockScrapPendingApprovalItemV1? item;
        try
        {
            item = await reader.ReadStockScrapAsync(scrapId, context.ActorId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return await FailureAfterReadAsync<InventoryStockScrapPendingApprovalDetailV1>(context,
                InventoryPermissionManifestV1.StockScrapApprove, cancellationToken);
        }

        if (await FenceAsync<InventoryStockScrapPendingApprovalDetailV1>(context,
                InventoryPermissionManifestV1.StockScrapApprove, cancellationToken) is { } afterRead)
        {
            return afterRead;
        }
        if (item is null)
        {
            return NotFound<InventoryStockScrapPendingApprovalDetailV1>();
        }
        return ValidScrap(context, item) && item.ScrapId == scrapId
            ? new(InventoryPendingApprovalReadOutcomeV1.Found,
                new InventoryStockScrapPendingApprovalDetailV1(item))
            : Unavailable<InventoryStockScrapPendingApprovalDetailV1>();
    }

    private async Task<InventoryPendingApprovalReadResultV1<T>?> FenceAsync<T>(
        InventoryPendingApprovalReadContextV1 context,
        string approvePermission,
        CancellationToken cancellationToken) where T : class
    {
        if (!SameContext(context))
        {
            return Denied<T>();
        }

        foreach (var permission in new[]
                 {
                     ErpPermissions.InventoryLabelsRead,
                     ErpPermissions.InventoryBalancesRead,
                     approvePermission,
                 })
        {
            try
            {
                var scope = await authority.RevalidatePermissionScopeAsync(
                    permission, context.CompanyId, cancellationToken);
                if (!scope.IsGranted || !scope.IsCompanyWide || !SameContext(context))
                {
                    return Denied<T>();
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return Unavailable<T>();
            }
        }

        return null;
    }

    private async Task<InventoryPendingApprovalReadResultV1<T>> FailureAfterReadAsync<T>(
        InventoryPendingApprovalReadContextV1 context,
        string approvePermission,
        CancellationToken cancellationToken) where T : class =>
        await FenceAsync<T>(context, approvePermission, cancellationToken) ?? Unavailable<T>();

    private bool SameContext(InventoryPendingApprovalReadContextV1 context) =>
        context.CompanyId != Guid.Empty
        && !string.IsNullOrWhiteSpace(context.ActorId)
        && companyContext.IsResolved
        && companyContext.CompanyId == context.CompanyId
        && currentUser.IsAuthenticated
        && string.Equals(currentUser.UserId, context.ActorId, StringComparison.Ordinal)
        && currentUser.CanAccessCompany(context.CompanyId);

    private static bool ValidCounts(
        InventoryPendingApprovalReadContextV1 context,
        IReadOnlyList<InventoryStockCountPendingApprovalItemV1>? rows) =>
        rows is not null
        && rows.Count <= ReadLimit
        && rows.Select(row => row?.CountId).Distinct().Count() == rows.Count
        && rows.All(row => ValidCount(context, row));

    private static bool ValidScraps(
        InventoryPendingApprovalReadContextV1 context,
        IReadOnlyList<InventoryStockScrapPendingApprovalItemV1>? rows) =>
        rows is not null
        && rows.Count <= ReadLimit
        && rows.Select(row => row?.ScrapId).Distinct().Count() == rows.Count
        && rows.All(row => ValidScrap(context, row));

    private static bool ValidCount(
        InventoryPendingApprovalReadContextV1 context,
        InventoryStockCountPendingApprovalItemV1? item) =>
        item is not null
        && item.CountId != Guid.Empty
        && item.CompanyId == context.CompanyId
        && item.LabelId != Guid.Empty
        && item.ProductId != Guid.Empty
        && item.LocationId != Guid.Empty
        && item.OwnerId != Guid.Empty
        && item.CustodianId != Guid.Empty
        && !string.IsNullOrWhiteSpace(item.LabelVersion)
        && !string.IsNullOrWhiteSpace(item.OwnerKind)
        && !string.IsNullOrWhiteSpace(item.CustodianKind)
        && !string.IsNullOrWhiteSpace(item.Block)
        && !string.IsNullOrWhiteSpace(item.BookKg)
        && !string.IsNullOrWhiteSpace(item.CountedKg)
        && !string.IsNullOrWhiteSpace(item.DifferenceKg)
        && item.DifferenceKg != "0"
        && !string.IsNullOrWhiteSpace(item.Reason)
        && !string.IsNullOrWhiteSpace(item.SubmittedBy)
        && !string.Equals(item.SubmittedBy, context.ActorId, StringComparison.Ordinal);

    private static bool ValidScrap(
        InventoryPendingApprovalReadContextV1 context,
        InventoryStockScrapPendingApprovalItemV1? item) =>
        item is not null
        && item.ScrapId != Guid.Empty
        && item.CompanyId == context.CompanyId
        && item.LabelId != Guid.Empty
        && item.ProductId != Guid.Empty
        && item.LocationId != Guid.Empty
        && item.OwnerId != Guid.Empty
        && item.CustodianId != Guid.Empty
        && !string.IsNullOrWhiteSpace(item.LabelVersion)
        && !string.IsNullOrWhiteSpace(item.OwnerKind)
        && !string.IsNullOrWhiteSpace(item.CustodianKind)
        && !string.IsNullOrWhiteSpace(item.Block)
        && !string.IsNullOrWhiteSpace(item.BookKg)
        && !string.IsNullOrWhiteSpace(item.ScrapKg)
        && !string.IsNullOrWhiteSpace(item.RemainingKg)
        && !string.IsNullOrWhiteSpace(item.Reason)
        && !string.IsNullOrWhiteSpace(item.SubmittedBy)
        && !string.Equals(item.SubmittedBy, context.ActorId, StringComparison.Ordinal);

    private static string EncodeCursor(Guid companyId, string kind, int offset)
    {
        var bytes = Encoding.UTF8.GetBytes(
            $"{companyId:N}:{kind}:{offset.ToString(CultureInfo.InvariantCulture)}");
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static bool TryCursor(Guid companyId, string kind, string? cursor, out int offset)
    {
        offset = 0;
        if (cursor is null)
        {
            return true;
        }
        if (cursor.Length is 0 or > 256)
        {
            return false;
        }

        try
        {
            var value = cursor.Replace('-', '+').Replace('_', '/');
            value = value.PadRight(value.Length + ((4 - value.Length % 4) % 4), '=');
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(value));
            var prefix = $"{companyId:N}:{kind}:";
            if (!decoded.StartsWith(prefix, StringComparison.Ordinal)
                || !int.TryParse(decoded[prefix.Length..], NumberStyles.None,
                    CultureInfo.InvariantCulture, out offset)
                || offset < 0 || offset > MaximumOffset)
            {
                return false;
            }
            return string.Equals(cursor, EncodeCursor(companyId, kind, offset), StringComparison.Ordinal);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static InventoryPendingApprovalReadResultV1<T> Denied<T>() where T : class =>
        new(InventoryPendingApprovalReadOutcomeV1.Denied, null);

    private static InventoryPendingApprovalReadResultV1<T> NotFound<T>() where T : class =>
        new(InventoryPendingApprovalReadOutcomeV1.NotFound, null);

    private static InventoryPendingApprovalReadResultV1<T> Unavailable<T>() where T : class =>
        new(InventoryPendingApprovalReadOutcomeV1.Unavailable, null);
}
