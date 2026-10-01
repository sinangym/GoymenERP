using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Modules.Inventory.Infrastructure;

/// <summary>Fresh company-wide transfer authority around the Inventory writer and its destination-owner read.</summary>
public sealed class InventoryTransferCommandService : IInventoryTransferCommandV1
{
    private readonly ICompanyContext companyContext;
    private readonly ICurrentUser currentUser;
    private readonly IErpAuthorityRevalidationService authority;
    private readonly IInventoryTransferDestinationPortV1 destination;
    private readonly IInventoryTransferReaderV1 reader;
    private readonly IInventoryTransferWriterV1 writer;
    private readonly Func<string, bool> isKnownPermission;

    public InventoryTransferCommandService(
        ICompanyContext companyContext,
        ICurrentUser currentUser,
        IErpAuthorityRevalidationService authority,
        IInventoryTransferDestinationPortV1 destination,
        IInventoryTransferReaderV1 reader,
        IInventoryTransferWriterV1 writer)
        : this(companyContext, currentUser, authority, destination, reader, writer, ErpPermissions.IsKnown)
    {
    }

    internal InventoryTransferCommandService(
        ICompanyContext companyContext,
        ICurrentUser currentUser,
        IErpAuthorityRevalidationService authority,
        IInventoryTransferDestinationPortV1 destination,
        IInventoryTransferReaderV1 reader,
        IInventoryTransferWriterV1 writer,
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

    public async Task<InventoryTransferResultV1> PlanAsync(
        PlanInventoryTransferV1 request,
        InventoryCommandMetadata transport,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scope = await RequireAsync(cancellationToken);
        var replayMetadata = await FinalizeAsync(scope, transport, cancellationToken);
        if (await writer.ReplayPlanAsync(request, replayMetadata,
                token => FreshAuthorityFenceAsync(scope, token), cancellationToken) is { } replay)
        {
            return replay;
        }

        await RequireDestinationAsync(scope, request.DestinationLocationId, request.DestinationCustodianKind,
            request.DestinationCustodianId, cancellationToken);
        var metadata = await FinalizeAsync(scope, transport, cancellationToken);
        return await writer.PlanAsync(request, metadata, token => FreshAuthorityFenceAsync(scope, token),
            cancellationToken);
    }

    public async Task<InventoryTransferResultV1> DispatchAsync(
        Guid transferId,
        DispatchInventoryTransferV1 request,
        InventoryCommandMetadata transport,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scope = await RequireAsync(cancellationToken);
        var metadata = await FinalizeAsync(scope, transport, cancellationToken);
        return await writer.DispatchAsync(transferId, request, metadata, token => FreshAuthorityFenceAsync(scope, token),
            cancellationToken);
    }

    public async Task<InventoryTransferResultV1> AcceptLineAsync(
        Guid transferId,
        Guid lineId,
        AcceptInventoryTransferLineV1 request,
        InventoryCommandMetadata transport,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scope = await RequireAsync(cancellationToken);
        var replayMetadata = await FinalizeAsync(scope, transport, cancellationToken);
        if (await writer.ReplayAcceptLineAsync(transferId, lineId, request, replayMetadata,
                token => FreshAuthorityFenceAsync(scope, token), cancellationToken) is { } replay)
        {
            return replay;
        }

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

        await RequireDestinationAsync(scope, transfer.DestinationLocationId, transfer.DestinationCustodianKind,
            transfer.DestinationCustodianId, cancellationToken);
        var metadata = await FinalizeAsync(scope, transport, cancellationToken);
        return await writer.AcceptLineAsync(transferId, lineId, request, metadata,
            token => FreshAuthorityFenceAsync(scope, token), cancellationToken);
    }

    private async Task RequireDestinationAsync(
        Scope scope,
        Guid locationId,
        string custodianKind,
        Guid custodianId,
        CancellationToken cancellationToken)
    {
        InventoryTransferDestinationStatusV1 destinationStatus;
        try
        {
            destinationStatus = await destination.CheckAsync(scope.CompanyId, locationId, custodianKind,
                custodianId, cancellationToken);
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

        if (!isKnownPermission(InventoryPermissionManifestV1.StockTransferWrite))
        {
            throw new InventoryTransferException(503, InventoryTransferCodesV1.PermissionUnregistered);
        }

        var scope = new Scope(companyContext.CompanyId, currentUser.UserId, []);
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
            PermissionCode = InventoryPermissionManifestV1.StockTransferWrite,
            RoleCodes = finalScope.RoleCodes,
        };
    }

    private async Task<Scope> MeasureAsync(Scope scope, CancellationToken cancellationToken)
    {
        if (!Matches(scope)) throw Forbidden();
        var measured = await authority.RevalidatePermissionScopeAsync(
            InventoryPermissionManifestV1.StockTransferWrite, scope.CompanyId, cancellationToken);
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

    private sealed record Scope(Guid CompanyId, string ActorId, IReadOnlyCollection<string> RoleCodes);
}
