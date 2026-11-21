using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Modules.Inventory.Infrastructure;

/// <summary>
/// Fresh company-wide authority around the KLN-D22 reverse transfer writer. A reverse transfer is a new transfer plan,
/// so it runs under the ordinary transfer write permission (no new permission name). Its destination is the original
/// transfer's source location and custodian, which the owner port must still report active, exactly as a plain plan
/// re-checks its destination. Registering nothing here grants nothing.
/// </summary>
public sealed class InventoryTransferReverseCommandService : IInventoryTransferReverseCommandV1
{
    private readonly ICompanyContext companyContext;
    private readonly ICurrentUser currentUser;
    private readonly IErpAuthorityRevalidationService authority;
    private readonly IInventoryTransferDestinationPortV1 destination;
    private readonly IInventoryTransferReaderV1 reader;
    private readonly IInventoryTransferReverseWriterV1 writer;
    private readonly Func<string, bool> isKnownPermission;

    public InventoryTransferReverseCommandService(
        ICompanyContext companyContext,
        ICurrentUser currentUser,
        IErpAuthorityRevalidationService authority,
        IInventoryTransferDestinationPortV1 destination,
        IInventoryTransferReaderV1 reader,
        IInventoryTransferReverseWriterV1 writer)
        : this(companyContext, currentUser, authority, destination, reader, writer, ErpPermissions.IsKnown)
    {
    }

    internal InventoryTransferReverseCommandService(
        ICompanyContext companyContext,
        ICurrentUser currentUser,
        IErpAuthorityRevalidationService authority,
        IInventoryTransferDestinationPortV1 destination,
        IInventoryTransferReaderV1 reader,
        IInventoryTransferReverseWriterV1 writer,
        Func<string, bool> isKnownPermission)
    {
        this.companyContext = companyContext ?? throw new ArgumentNullException(nameof(companyContext));
        this.currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
        this.authority = authority ?? throw new ArgumentNullException(nameof(authority));
        this.destination = destination ?? throw new ArgumentNullException(nameof(destination));
        this.reader = reader ?? throw new ArgumentNullException(nameof(reader));
        this.writer = writer ?? throw new ArgumentNullException(nameof(writer));
        this.isKnownPermission = isKnownPermission ?? throw new ArgumentNullException(nameof(isKnownPermission));
    }

    public async Task<InventoryTransferReverseResultV1> ReverseAsync(
        Guid originalTransferId,
        ReverseInventoryTransferV1 request,
        InventoryCommandMetadata transport,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scope = await RequireAsync(InventoryPermissionManifestV1.StockTransferWrite, cancellationToken);
        var replayMetadata = await FinalizeAsync(scope, transport, cancellationToken);
        if (await writer.ReplayReverseAsync(originalTransferId, request, replayMetadata,
                token => FreshAuthorityFenceAsync(scope, token), cancellationToken) is { } replay)
        {
            return replay;
        }

        var original = await ReadTransferAsync(scope, originalTransferId, cancellationToken);
        await RequireReturnDestinationAsync(scope, original, cancellationToken);
        var metadata = await FinalizeAsync(scope, transport, cancellationToken);
        return await writer.ReverseAsync(originalTransferId, request, metadata,
            token => FreshAuthorityFenceAsync(scope, token), cancellationToken);
    }

    private async Task<InventoryTransferViewV1> ReadTransferAsync(
        Scope scope,
        Guid transferId,
        CancellationToken cancellationToken)
    {
        InventoryTransferViewV1? transfer;
        try
        {
            transfer = await reader.ReadAsync(transferId, cancellationToken);
        }
        catch (InventoryTransferException)
        {
            await MeasureAsync(scope, cancellationToken);
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await MeasureAsync(scope, cancellationToken);
            throw new InventoryTransferException(503, InventoryTransferCodesV1.Unavailable, exception);
        }

        scope = await MeasureAsync(scope, cancellationToken);
        if (transfer is null)
        {
            throw new InventoryTransferException(404, InventoryTransferCodesV1.TransferNotFound);
        }

        if (transfer.CompanyId != scope.CompanyId)
        {
            throw new InventoryTransferException(503, InventoryTransferCodesV1.Unavailable);
        }

        return transfer;
    }

    /// <summary>The reverse transfer's destination is the original transfer's source location and custodian.</summary>
    private async Task RequireReturnDestinationAsync(
        Scope scope,
        InventoryTransferViewV1 original,
        CancellationToken cancellationToken)
    {
        InventoryTransferDestinationStatusV1 destinationStatus;
        try
        {
            destinationStatus = await destination.CheckAsync(scope.CompanyId, original.SourceLocationId,
                original.SourceCustodianKind, original.SourceCustodianId, cancellationToken);
        }
        catch (InventoryTransferException)
        {
            await MeasureAsync(scope, cancellationToken);
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await MeasureAsync(scope, cancellationToken);
            throw new InventoryTransferException(503, InventoryTransferCodesV1.Unavailable, exception);
        }

        await MeasureAsync(scope, cancellationToken);
        if (destinationStatus != InventoryTransferDestinationStatusV1.Active)
        {
            throw destinationStatus switch
            {
                InventoryTransferDestinationStatusV1.NotFound =>
                    new InventoryTransferException(422, InventoryTransferCodesV1.DestinationNotFound),
                InventoryTransferDestinationStatusV1.Inactive =>
                    new InventoryTransferException(422, InventoryTransferCodesV1.DestinationInactive),
                InventoryTransferDestinationStatusV1.ContextMismatch =>
                    new InventoryTransferException(422, InventoryTransferCodesV1.DestinationContextMismatch),
                _ => new InventoryTransferException(503, InventoryTransferCodesV1.Unavailable),
            };
        }
    }

    private async Task<Scope> RequireAsync(string permission, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            throw new InventoryTransferException(401, InventoryTransferCodesV1.Unauthenticated);
        }

        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty
            || !currentUser.CanAccessCompany(companyContext.CompanyId))
        {
            throw Forbidden();
        }

        if (!isKnownPermission(permission))
        {
            throw new InventoryTransferException(503, InventoryTransferCodesV1.PermissionUnregistered);
        }

        var scope = new Scope(companyContext.CompanyId, currentUser.UserId, permission, []);
        return await MeasureAsync(scope, cancellationToken);
    }

    private async Task<InventoryCommandMetadata> FinalizeAsync(
        Scope scope,
        InventoryCommandMetadata transport,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transport);
        if (!string.IsNullOrEmpty(transport.ActorId)
            && !string.Equals(transport.ActorId, scope.ActorId, StringComparison.Ordinal))
        {
            throw Forbidden();
        }

        var finalScope = await MeasureAsync(scope, cancellationToken);
        return transport with
        {
            ActorId = finalScope.ActorId,
            PermissionCode = finalScope.Permission,
            RoleCodes = finalScope.RoleCodes,
        };
    }

    private async Task<Scope> MeasureAsync(Scope scope, CancellationToken cancellationToken)
    {
        if (!Matches(scope)) throw Forbidden();
        var measured = await authority.RevalidatePermissionScopeAsync(
            scope.Permission, scope.CompanyId, cancellationToken);
        if (!measured.IsGranted || !measured.IsCompanyWide || !Matches(scope)) throw Forbidden();
        return scope with { RoleCodes = [.. measured.RoleCodes.Order(StringComparer.Ordinal)] };
    }

    private async Task FreshAuthorityFenceAsync(Scope scope, CancellationToken cancellationToken) =>
        _ = await MeasureAsync(scope, cancellationToken);

    private bool Matches(Scope scope) => currentUser.IsAuthenticated
        && string.Equals(currentUser.UserId, scope.ActorId, StringComparison.Ordinal)
        && companyContext.IsResolved && companyContext.CompanyId == scope.CompanyId
        && currentUser.CanAccessCompany(scope.CompanyId);

    private static InventoryTransferException Forbidden() =>
        new(403, InventoryTransferCodesV1.Forbidden);

    private sealed record Scope(
        Guid CompanyId,
        string ActorId,
        string Permission,
        IReadOnlyCollection<string> RoleCodes);
}

public sealed class InventoryTransferReverseQueryService(
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority,
    IInventoryTransferReverseReaderV1 reader) : IInventoryTransferReverseQueryV1
{
    public async Task<InventoryTransferReversalFactsViewV1> GetReversalFactsAsync(
        Guid transferId,
        CancellationToken cancellationToken = default)
    {
        var scope = await RequireAsync(cancellationToken);
        InventoryTransferReversalFactsViewV1? result;
        try
        {
            result = await reader.ReadReversalFactsAsync(transferId, cancellationToken);
        }
        catch (InventoryTransferException)
        {
            await RequireStillAsync(scope, cancellationToken);
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RequireStillAsync(scope, cancellationToken);
            throw new InventoryTransferException(503, InventoryTransferCodesV1.Unavailable, exception);
        }

        await RequireStillAsync(scope, cancellationToken);
        if (result is null) throw new InventoryTransferException(404, InventoryTransferCodesV1.TransferNotFound);
        if (result.CompanyId != scope.CompanyId)
        {
            throw new InventoryTransferException(503, InventoryTransferCodesV1.Unavailable);
        }

        return result;
    }

    private async Task<Scope> RequireAsync(CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            throw new InventoryTransferException(401, InventoryTransferCodesV1.Unauthenticated);
        }

        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty
            || !currentUser.CanAccessCompany(companyContext.CompanyId))
        {
            throw Forbidden();
        }

        var scope = new Scope(companyContext.CompanyId, currentUser.UserId);
        await RequireStillAsync(scope, cancellationToken);
        return scope;
    }

    private async Task RequireStillAsync(Scope scope, CancellationToken cancellationToken)
    {
        if (!Matches(scope)) throw Forbidden();
        var measured = await authority.RevalidatePermissionScopeAsync(
            ErpPermissions.InventoryBalancesRead, scope.CompanyId, cancellationToken);
        if (!measured.IsGranted || !measured.IsCompanyWide || !Matches(scope)) throw Forbidden();
    }

    private bool Matches(Scope scope) => currentUser.IsAuthenticated
        && string.Equals(currentUser.UserId, scope.ActorId, StringComparison.Ordinal)
        && companyContext.IsResolved && companyContext.CompanyId == scope.CompanyId
        && currentUser.CanAccessCompany(scope.CompanyId);

    private static InventoryTransferException Forbidden() => new(403, InventoryTransferCodesV1.Forbidden);

    private sealed record Scope(Guid CompanyId, string ActorId);
}
