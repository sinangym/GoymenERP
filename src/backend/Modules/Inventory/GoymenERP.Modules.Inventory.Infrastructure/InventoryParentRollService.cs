using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Balances;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Lots;
using GoymenERP.Modules.Inventory.Domain.Stock;

namespace GoymenERP.Modules.Inventory.Infrastructure;

internal sealed record InventoryParentRollRecord(long Version, MaterialLabel Label, MaterialLot Lot,
    IReadOnlyList<StockBalance> Balances);
internal sealed record InventoryParentRollWindow(IReadOnlyList<InventoryParentRollRecord> Records, bool HasMore);

internal interface IInventoryParentRollReader
{
    Task<InventoryParentRollWindow> SearchAsync(Guid companyId, string q, int limit, CancellationToken ct);
    Task<InventoryParentRollRecord?> ReadAsync(Guid companyId, Guid labelId, CancellationToken ct);
}

internal sealed class InventoryParentRollService(ICompanyContext company, ICurrentUser user,
    IErpAuthorityRevalidationService authority, IInventoryParentRollReader? reader)
    : IInventoryParentRollCandidatesV1, IInventoryParentRollValidationV1,
        IInventoryContextualParentRollReferencesV1
{
    public async Task<InventoryParentRollCandidatesV1> SearchAsync(InventoryParentRollQueryV1 query,
        CancellationToken cancellationToken = default)
    {
        var scope = await RequireAsync(null, cancellationToken);
        if (query is null || query.Limit is < 1 or > 100 || query.Q?.Length > MaterialLabel.MaximumCodeLength)
            throw Error(400, InventoryParentRollCodesV1.InvalidQuery);
        return await SearchAsync(scope, query.Q, query.Limit, null, cancellationToken);
    }

    async Task<InventoryParentRollCandidatesV1> IInventoryContextualParentRollReferencesV1.SearchAsync(
        InventoryContextualParentRollQueryV1 query, CancellationToken cancellationToken)
    {
        var scope = await RequireAsync(null, cancellationToken);
        if (query is null || query.Limit is < 1 or > 100 || query.Q?.Length > MaterialLabel.MaximumCodeLength
            || !IsValid(query.PositionContext))
            throw Error(400, InventoryParentRollCodesV1.InvalidQuery);
        return await SearchAsync(scope, query.Q, query.Limit, query.PositionContext, cancellationToken);
    }

    private async Task<InventoryParentRollCandidatesV1> SearchAsync(Scope scope, string? q, int limit,
        InventoryParentRollPositionContextV1? context, CancellationToken cancellationToken)
    {
        var source = reader ?? throw Error(503, InventoryParentRollCodesV1.Unavailable);
        var window = await FencedAsync(() => source.SearchAsync(scope.CompanyId, q?.Trim() ?? "",
            limit, cancellationToken), scope, cancellationToken);
        return new([.. window.Records.Select(record => View(record, scope.CompanyId, context))], window.HasMore);
    }

    public async Task<InventoryParentRollReferenceV1> ValidateAsync(InventoryParentRollSelectionV1 selection,
        CancellationToken cancellationToken = default)
    {
        var scope = await RequireAsync(null, cancellationToken);
        if (selection is null || selection.LabelId == Guid.Empty)
            throw Error(404, InventoryParentRollCodesV1.InvalidReference);
        return await ValidateAsync(scope, selection.LabelId, selection.ExpectedLabelVersion, null, cancellationToken);
    }

    async Task<InventoryParentRollReferenceV1> IInventoryContextualParentRollReferencesV1.ValidateAsync(
        InventoryContextualParentRollSelectionV1 selection, CancellationToken cancellationToken)
    {
        var scope = await RequireAsync(null, cancellationToken);
        if (selection is null || selection.LabelId == Guid.Empty)
            throw Error(404, InventoryParentRollCodesV1.InvalidReference);
        if (!IsValid(selection.PositionContext))
            throw Error(422, InventoryParentRollCodesV1.ContextRequired);
        return await ValidateAsync(scope, selection.LabelId, selection.ExpectedLabelVersion,
            selection.PositionContext, cancellationToken);
    }

    private async Task<InventoryParentRollReferenceV1> ValidateAsync(Scope scope, Guid labelId,
        string expectedLabelVersion, InventoryParentRollPositionContextV1? context,
        CancellationToken cancellationToken)
    {
        if (!long.TryParse(expectedLabelVersion, NumberStyles.None, CultureInfo.InvariantCulture,
            out var expected) || expected <= 0 || expected.ToString(CultureInfo.InvariantCulture) != expectedLabelVersion)
            throw Error(400, InventoryParentRollCodesV1.InvalidVersion);
        var source = reader ?? throw Error(503, InventoryParentRollCodesV1.Unavailable);
        var record = await FencedAsync(() => source.ReadAsync(scope.CompanyId, labelId, cancellationToken),
            scope, cancellationToken);
        if (record is null || record.Label.CompanyId != scope.CompanyId || record.Label.Id != labelId)
            throw Error(404, InventoryParentRollCodesV1.InvalidReference);
        if (record.Version != expected) throw Error(409, InventoryParentRollCodesV1.VersionConflict);
        var view = View(record, scope.CompanyId, context);
        if (!view.DraftSelectable) throw Error(422, view.DraftRefusalCode!);
        return view;
    }

    private static InventoryParentRollReferenceV1 View(InventoryParentRollRecord record, Guid companyId,
        InventoryParentRollPositionContextV1? context)
    {
        var label = record.Label;
        if (label.CompanyId != companyId || record.Lot.CompanyId != companyId || record.Version <= 0
            || record.Lot.Id != label.LotId || record.Lot.ProductId != label.ProductId
            || label.ProductVersion is null
            || record.Balances.Any(b => b.Position.CompanyId != companyId || b.Position.LabelId != label.Id
                || b.Position.ProductId != label.ProductId)) throw Error(503, InventoryParentRollCodesV1.Unavailable);
        // Reuse INV-1's existing stored-state invariant; never silently reinterpret a corrupt label
        // spanning physical places. Multiple Free/Blocked buckets at its actual place remain intact.
        try { _ = StockLedger.FromStoredBalances(record.Balances); }
        catch (InventoryRuleException) { throw Error(503, InventoryParentRollCodesV1.Unavailable); }
        var balances = InventoryReadModelsV1.Balances(label.Id, record.Balances);
        var refusal = balances.Positions.Count == 0
            ? InventoryParentRollCodesV1.NoPhysicalPosition
            : context is null
                ? balances.Positions.Any(p =>
                    p.OwnerKind != nameof(StockPartyKind.Company) || p.OwnerId != companyId)
                    ? InventoryParentRollCodesV1.ContextRequired
                    : null
                : balances.Positions.Any(position => !Matches(position, context))
                    ? InventoryParentRollCodesV1.ContextRequired
                    : null;
        return new(companyId, label.Id, record.Version.ToString(CultureInfo.InvariantCulture), label.Code,
            label.ProductId, new(record.Lot.Id, record.Lot.HeatNumber, record.Lot.CoilNumber), label.HeatNumber,
            label.CoilNumber, label.DocumentStatus.ToString(), [.. label.RequiredDocuments.Order(StringComparer.Ordinal)],
            [.. label.AttachedDocuments.Order(StringComparer.Ordinal)], balances.PhysicalKg, balances.FreeKg,
            balances.BlockedKg, balances.Positions, refusal is null, refusal, ProductionUseRequiresRevalidation: true)
        {
            ProductVersion = label.ProductVersion,
        };
    }

    private static bool Matches(InventoryPositionV1 position, InventoryParentRollPositionContextV1 context) =>
        string.Equals(position.OwnerKind, context.OwnerKind.ToString(), StringComparison.Ordinal)
        && position.OwnerId == context.OwnerId
        && string.Equals(position.CustodianKind, context.CustodianKind.ToString(), StringComparison.Ordinal)
        && position.CustodianId == context.CustodianId;

    private static bool IsValid(InventoryParentRollPositionContextV1 context) =>
        Enum.IsDefined(context.OwnerKind) && context.OwnerId != Guid.Empty
        && Enum.IsDefined(context.CustodianKind) && context.CustodianId != Guid.Empty;

    private async Task<T> FencedAsync<T>(Func<Task<T>> read, Scope scope, CancellationToken ct)
    {
        T value;
        try { value = await read(); }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            await RequireAsync(scope, ct);
            if (exception is InventoryParentRollException) throw;
            throw Error(503, InventoryParentRollCodesV1.Unavailable);
        }
        await RequireAsync(scope, ct);
        return value;
    }

    private async Task<Scope> RequireAsync(Scope? expected, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!user.IsAuthenticated || string.IsNullOrWhiteSpace(user.UserId))
            throw Error(expected is null ? 401 : 403, expected is null
                ? InventoryParentRollCodesV1.Unauthenticated : InventoryParentRollCodesV1.Forbidden);
        if (!company.IsResolved) throw Error(403, InventoryParentRollCodesV1.Forbidden);
        var scope = new Scope(company.CompanyId, user.UserId);
        if (expected is not null && expected != scope) throw Error(403, InventoryParentRollCodesV1.Forbidden);
        foreach (var permission in new[] { ErpPermissions.InventoryLabelsRead, ErpPermissions.InventoryBalancesRead })
        {
            RequireIdentity(scope);
            PermissionScope grant;
            try { grant = await authority.RevalidatePermissionScopeAsync(permission, scope.CompanyId, ct); }
            catch (OperationCanceledException) { throw; }
            catch { RequireIdentity(scope); throw Error(503, InventoryParentRollCodesV1.Unavailable); }
            ct.ThrowIfCancellationRequested();
            RequireIdentity(scope);
            if (!grant.IsGranted || !grant.IsCompanyWide) throw Error(403, InventoryParentRollCodesV1.Forbidden);
        }
        return scope;
    }

    private void RequireIdentity(Scope scope)
    {
        if (!company.IsResolved || scope.CompanyId == Guid.Empty || company.CompanyId != scope.CompanyId
            || !user.IsAuthenticated || user.UserId != scope.ActorId || !user.CanAccessCompany(scope.CompanyId))
            throw Error(403, InventoryParentRollCodesV1.Forbidden);
    }

    private static InventoryParentRollException Error(int status, string code) => new(status, code);
    private sealed record Scope(Guid CompanyId, string ActorId);
}
