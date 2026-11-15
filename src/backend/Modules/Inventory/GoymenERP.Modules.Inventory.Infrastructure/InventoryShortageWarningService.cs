using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Shortages;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Inventory.Infrastructure;

/// <summary>
/// KLN-D42 (STK-S31): the dated shortage warning read. It gathers the open Sales lines (Sales ports), the open purchase lines
/// (Purchasing port through the host), the company's free unreserved stock and the Sales lines' active reservations (Inventory's
/// own rows), then hands each product to the one <see cref="ShortageWarningCalculator"/>. The screen shows these figures and
/// recomputes nothing.
/// <para>
/// Authority: the read rides the stock read names (labels + balances, company-wide), measured before and after the reads like the
/// movement timeline; no new permission name. Fail-closed: any source that cannot answer, an answer for another company, or a
/// malformed quantity is 503 for the whole read — a partial picture would hide a shortage. A product whose demand or purchase
/// line is not in kilograms (Settings unit identity) is answered <see cref="InventoryShortageStatusNamesV1.Unconvertible"/>
/// with no figures, never converted by guess. Production orders are not read; nothing is written or requested.
/// </para>
/// </summary>
internal sealed class InventoryShortageWarningService(
    ICompanyContext company,
    ICurrentUser user,
    IErpAuthorityRevalidationService authority,
    IInventoryShortageDemandSourceV1? demandSource,
    IInventoryShortageSupplySourceV1? supplySource,
    IInventoryShortageSimilarStockSourceV1? similarSource,
    ISettingsUnitIdentityCatalogV1? units,
    IInventoryShortageStockReader? stockReader) : IInventoryShortageWarningsV1
{
    private static readonly InventoryShortageSimilarStockV1 SimilarUnavailable =
        new(InventoryShortageSimilarStatusNamesV1.Unavailable, []);

    public async Task<InventoryShortageWarningsV1> ReadAsync(
        InventoryShortageWarningQueryV1 query,
        CancellationToken cancellationToken = default)
    {
        var scope = await RequireAsync(null, cancellationToken);
        if (query is null || query.ProductId == Guid.Empty)
        {
            throw Error(400, InventoryShortageWarningCodesV1.InvalidQuery);
        }

        if (demandSource is null || supplySource is null || units is null || stockReader is null)
        {
            throw Error(503, InventoryShortageWarningCodesV1.Unavailable);
        }

        var result = await FencedAsync(() => ComputeAsync(scope.CompanyId, query, cancellationToken), scope, cancellationToken);
        return result;
    }

    private async Task<InventoryShortageWarningsV1> ComputeAsync(Guid companyId, InventoryShortageWarningQueryV1 query,
        CancellationToken cancellationToken)
    {
        var demand = await demandSource!.ListOpenDemandAsync(cancellationToken);
        if (demand?.Lines is not { } demandLines || demand.Outcome != InventoryShortageSourceOutcomeV1.Answered)
        {
            throw Error(503, InventoryShortageWarningCodesV1.Unavailable);
        }

        if (demandLines.Any(line => line is null || line.CompanyId != companyId || line.ProductId == Guid.Empty
                || line.UnitId == Guid.Empty || line.OrderId == Guid.Empty || line.LineId == Guid.Empty))
        {
            throw Error(503, InventoryShortageWarningCodesV1.Unavailable);
        }

        var supply = await supplySource!.ListOpenSupplyAsync(cancellationToken);
        if (supply?.Lines is not { } supplyLines || supply.Outcome != InventoryShortageSourceOutcomeV1.Answered)
        {
            throw Error(503, InventoryShortageWarningCodesV1.Unavailable);
        }

        var datesAnswered = supply.DatesAnswered;
        if (supplyLines.Any(line => line is null || line.CompanyId != companyId || line.ProductId == Guid.Empty
                || line.UnitId == Guid.Empty || line.OrderId == Guid.Empty || line.LineId == Guid.Empty
                || !datesAnswered && line.ExpectedDate is not null))
        {
            throw Error(503, InventoryShortageWarningCodesV1.Unavailable);
        }

        var wantedDemand = query.ProductId is { } only
            ? demandLines.Where(line => line.ProductId == only).ToArray()
            : demandLines.ToArray();
        var productIds = wantedDemand.Select(line => line.ProductId).Distinct().ToArray();
        var wantedProducts = productIds.ToHashSet();
        var wantedSupply = supplyLines.Where(line => wantedProducts.Contains(line.ProductId)).ToArray();

        var kilogramUnits = await KilogramUnitsAsync(
            wantedDemand.Select(line => line.UnitId).Concat(wantedSupply.Select(line => line.UnitId)), cancellationToken);
        var stock = await stockReader!.ReadAsync(companyId, productIds,
            wantedDemand.Select(line => line.OrderId).Distinct().ToArray(), cancellationToken);
        if (stock?.FreeUnreservedKgByProduct is null || stock.ReservedKgBySalesLine is null)
        {
            throw Error(503, InventoryShortageWarningCodesV1.Unavailable);
        }

        var demandByProduct = wantedDemand.ToLookup(line => line.ProductId);
        var supplyByProduct = wantedSupply.ToLookup(line => line.ProductId);
        var items = new List<InventoryShortageProductV1>();
        foreach (var productId in productIds)
        {
            var productDemand = demandByProduct[productId].ToArray();
            var productSupply = supplyByProduct[productId].ToArray();
            var first = productDemand
                .OrderBy(line => line.DocumentNo, StringComparer.Ordinal)
                .ThenBy(line => line.LineNo)
                .First();
            InventoryShortageProductV1 item;
            if (productDemand.Any(line => !kilogramUnits.Contains(line.UnitId))
                || productSupply.Any(line => !kilogramUnits.Contains(line.UnitId)))
            {
                item = new InventoryShortageProductV1(productId, first.ProductCode, first.ProductName,
                    InventoryShortageStatusNamesV1.Unconvertible, null, null, null, null, null, null, null, [],
                    await SimilarAsync(productId, cancellationToken));
            }
            else
            {
                var calculated = ShortageWarningCalculator.Calculate(new ShortageProductInput(
                    productId,
                    stock.FreeUnreservedKgByProduct.GetValueOrDefault(productId),
                    productDemand.Select(line => new ShortageDemandLine(line.OrderId, line.LineId, line.DocumentNo, line.LineNo,
                        line.DueDate, Quantity(line.OpenQuantityText),
                        stock.ReservedKgBySalesLine.GetValueOrDefault((line.OrderId, line.LineId)))).ToArray(),
                    productSupply.Select(line => new ShortageSupplyLine(line.OrderId, line.LineId, line.ExpectedDate,
                        Quantity(line.RemainingQuantityText))).ToArray()));
                if (query.OnlyWarnings && calculated.Status == ShortageProductStatus.Covered)
                {
                    continue;
                }

                item = Map(first, calculated, await SimilarAsync(productId, cancellationToken));
            }

            items.Add(item);
        }

        var ordered = items
            .OrderByDescending(item => Severity(item.Status))
            .ThenBy(item => item.EarliestDueDate.HasValue ? 0 : 1)
            .ThenBy(item => item.EarliestDueDate)
            .ThenBy(item => item.ProductCode, StringComparer.Ordinal)
            .ThenBy(item => item.ProductId)
            .ToArray();
        return new InventoryShortageWarningsV1(companyId,
            datesAnswered ? InventoryShortageSupplyDatesV1.Answered : InventoryShortageSupplyDatesV1.NotAnswered,
            ordered);
    }

    private async Task<HashSet<Guid>> KilogramUnitsAsync(IEnumerable<Guid> unitIds, CancellationToken cancellationToken)
    {
        var kilograms = new HashSet<Guid>();
        foreach (var unitId in unitIds.Distinct())
        {
            var identity = await units!.ReadAsync(unitId, cancellationToken);
            if (identity is { IsKilogram: true } && identity.UnitId == unitId)
            {
                kilograms.Add(unitId);
            }
        }

        return kilograms;
    }

    private async Task<InventoryShortageSimilarStockV1> SimilarAsync(Guid productId, CancellationToken cancellationToken)
    {
        if (similarSource is null)
        {
            return SimilarUnavailable;
        }

        try
        {
            var answer = await similarSource.ReadAsync(productId, cancellationToken);
            return ValidSimilar(answer, productId) ? answer : SimilarUnavailable;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Information only (KLN-D42 (3)): a similar-product answer that fails never hides the shortage itself.
            return SimilarUnavailable;
        }
    }

    private static bool ValidSimilar(InventoryShortageSimilarStockV1? answer, Guid productId)
    {
        if (answer?.Items is null)
        {
            return false;
        }

        return answer.Status switch
        {
            InventoryShortageSimilarStatusNamesV1.NotConfigured or InventoryShortageSimilarStatusNamesV1.Unavailable =>
                answer.Items.Count == 0,
            InventoryShortageSimilarStatusNamesV1.Answered => answer.Items.All(item => item is not null
                && item.ProductId != Guid.Empty && item.ProductId != productId && IsKilogramText(item.FreeStockKg)),
            _ => false,
        };
    }

    private static InventoryShortageProductV1 Map(InventoryShortageDemandLineV1 first, ShortageProductResult result,
        InventoryShortageSimilarStockV1 similar) =>
        new(result.ProductId, first.ProductCode, first.ProductName, StatusName(result.Status),
            Text(result.OpenDemandKg), Text(result.ReservedKg), Text(result.NetDemandKg), Text(result.FreeStockKg),
            Text(result.OpenSupplyKg), Text(result.ShortageKg), result.EarliestDueDate,
            result.Lines.Select(line => new InventoryShortageLineV1(line.Line.OrderId, line.Line.DocumentNo, line.Line.LineId,
                line.Line.LineNo, line.Line.DueDate, Text(line.Line.OpenKg), Text(line.Line.OpenKg - line.NetKg),
                Text(line.FromStockKg), Text(line.FromSupplyKg), Text(line.UncoveredKg), CoverageName(line.Coverage),
                line.LatestSupplyDate)).ToArray(),
            similar);

    private static string StatusName(ShortageProductStatus status) => status switch
    {
        ShortageProductStatus.Covered => InventoryShortageStatusNamesV1.Covered,
        ShortageProductStatus.CoverageDateUnknown => InventoryShortageStatusNamesV1.CoverageDateUnknown,
        ShortageProductStatus.LateCovered => InventoryShortageStatusNamesV1.LateCovered,
        ShortageProductStatus.Shortage => InventoryShortageStatusNamesV1.Shortage,
        _ => throw Error(503, InventoryShortageWarningCodesV1.Unavailable),
    };

    private static string CoverageName(ShortageLineCoverage coverage) => coverage switch
    {
        ShortageLineCoverage.Covered => InventoryShortageCoverageNamesV1.Covered,
        ShortageLineCoverage.CoverageDateUnknown => InventoryShortageCoverageNamesV1.CoverageDateUnknown,
        ShortageLineCoverage.LateCovered => InventoryShortageCoverageNamesV1.LateCovered,
        ShortageLineCoverage.Uncovered => InventoryShortageCoverageNamesV1.Uncovered,
        _ => throw Error(503, InventoryShortageWarningCodesV1.Unavailable),
    };

    /// <summary>Display order only: the worst first; unconvertible products sit with the shortages because nothing covers them yet.</summary>
    private static int Severity(string status) => status switch
    {
        InventoryShortageStatusNamesV1.Shortage or InventoryShortageStatusNamesV1.Unconvertible => 4,
        InventoryShortageStatusNamesV1.LateCovered => 3,
        InventoryShortageStatusNamesV1.CoverageDateUnknown => 2,
        _ => 1,
    };

    private static decimal Quantity(string? text)
    {
        if (string.IsNullOrEmpty(text)
            || !decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            || value < 0m)
        {
            throw Error(503, InventoryShortageWarningCodesV1.Unavailable);
        }

        return value;
    }

    private static bool IsKilogramText(string? text) =>
        !string.IsNullOrEmpty(text)
        && decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
        && value >= 0m;

    /// <summary>Exact invariant text without trailing zeros (1.500 is "1.5"); kilograms are never rounded here.</summary>
    internal static string Text(decimal value) =>
        (value / 1.0000000000000000000000000000m).ToString(CultureInfo.InvariantCulture);

    private async Task<T> FencedAsync<T>(Func<Task<T>> read, Scope scope, CancellationToken cancellationToken)
    {
        T result;
        try
        {
            result = await read();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            await RequireAsync(scope, cancellationToken);
            if (exception is InventoryShortageWarningException)
            {
                throw;
            }

            throw Error(503, InventoryShortageWarningCodesV1.Unavailable, exception);
        }

        await RequireAsync(scope, cancellationToken);
        return result;
    }

    private async Task<Scope> RequireAsync(Scope? expected, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!user.IsAuthenticated || string.IsNullOrWhiteSpace(user.UserId))
        {
            throw Error(expected is null ? 401 : 403, expected is null
                ? InventoryShortageWarningCodesV1.Unauthenticated
                : InventoryShortageWarningCodesV1.Forbidden);
        }

        if (!company.IsResolved)
        {
            throw Error(403, InventoryShortageWarningCodesV1.Forbidden);
        }

        var scope = new Scope(company.CompanyId, user.UserId);
        if (expected is not null && expected != scope)
        {
            throw Error(403, InventoryShortageWarningCodesV1.Forbidden);
        }

        foreach (var permission in new[] { ErpPermissions.InventoryLabelsRead, ErpPermissions.InventoryBalancesRead })
        {
            RequireIdentity(scope);
            PermissionScope grant;
            try
            {
                grant = await authority.RevalidatePermissionScopeAsync(permission, scope.CompanyId, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                RequireIdentity(scope);
                throw Error(503, InventoryShortageWarningCodesV1.Unavailable, exception);
            }

            cancellationToken.ThrowIfCancellationRequested();
            RequireIdentity(scope);
            if (!grant.IsGranted || !grant.IsCompanyWide)
            {
                throw Error(403, InventoryShortageWarningCodesV1.Forbidden);
            }
        }

        return scope;
    }

    private void RequireIdentity(Scope scope)
    {
        if (!company.IsResolved || company.CompanyId != scope.CompanyId || scope.CompanyId == Guid.Empty
            || !user.IsAuthenticated || user.UserId != scope.ActorId || !user.CanAccessCompany(scope.CompanyId))
        {
            throw Error(403, InventoryShortageWarningCodesV1.Forbidden);
        }
    }

    private static InventoryShortageWarningException Error(int statusCode, string code, Exception? exception = null) =>
        new(statusCode, code, exception);

    private sealed record Scope(Guid CompanyId, string ActorId);
}
