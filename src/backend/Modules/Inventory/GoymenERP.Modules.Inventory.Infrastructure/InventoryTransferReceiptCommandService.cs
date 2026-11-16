using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Modules.Inventory.Infrastructure;

/// <summary>
/// Fresh company-wide authority around the B-22c receipt writer (KLN-D21/KLN-D23/KLN-D24). A receipt runs under the
/// transfer write permission and, like a plain acceptance, re-checks the destination owner first. An excess decision,
/// a loss request and a loss decision each run under their own registered permission; the maker≠approver rule is the
/// writer's comparison of the stored recorder with the freshly authorized actor, never a role check. Registering the
/// permission names grants nothing.
/// </summary>
public sealed class InventoryTransferReceiptCommandService : IInventoryTransferReceiptCommandV1
{
    private readonly ICompanyContext companyContext;
    private readonly ICurrentUser currentUser;
    private readonly IErpAuthorityRevalidationService authority;
    private readonly IInventoryTransferDestinationPortV1 destination;
    private readonly IInventoryTransferReaderV1 reader;
    private readonly IInventoryTransferReceiptWriterV1 writer;
    private readonly Func<string, bool> isKnownPermission;

    public InventoryTransferReceiptCommandService(
        ICompanyContext companyContext,
        ICurrentUser currentUser,
        IErpAuthorityRevalidationService authority,
        IInventoryTransferDestinationPortV1 destination,
        IInventoryTransferReaderV1 reader,
        IInventoryTransferReceiptWriterV1 writer)
        : this(companyContext, currentUser, authority, destination, reader, writer, ErpPermissions.IsKnown)
    {
    }

    internal InventoryTransferReceiptCommandService(
        ICompanyContext companyContext,
        ICurrentUser currentUser,
        IErpAuthorityRevalidationService authority,
        IInventoryTransferDestinationPortV1 destination,
        IInventoryTransferReaderV1 reader,
        IInventoryTransferReceiptWriterV1 writer,
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

    public async Task<InventoryTransferReceiptResultV1> ReceiveLineAsync(
        Guid transferId,
        Guid lineId,
        ReceiveInventoryTransferLineV1 request,
        InventoryCommandMetadata transport,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scope = await RequireAsync(InventoryPermissionManifestV1.StockTransferWrite, cancellationToken);
        var replayMetadata = await FinalizeAsync(scope, transport, cancellationToken);
        if (await writer.ReplayReceiveLineAsync(transferId, lineId, request, replayMetadata,
                token => FreshAuthorityFenceAsync(scope, token), cancellationToken) is { } replay)
        {
            return replay;
        }

        var transfer = await ReadTransferAsync(scope, transferId, cancellationToken);
        await RequireDestinationAsync(scope, transfer, cancellationToken);
        var metadata = await FinalizeAsync(scope, transport, cancellationToken);
        return await writer.ReceiveLineAsync(transferId, lineId, request, metadata,
            token => FreshAuthorityFenceAsync(scope, token), cancellationToken);
    }

    public async Task<InventoryTransferReceiptResultV1> DecideOverageAsync(
        Guid transferId,
        Guid lineId,
        DecideInventoryTransferOverageV1 request,
        InventoryCommandMetadata transport,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scope = await RequireAsync(InventoryPermissionManifestV1.StockTransferOverageApprove, cancellationToken);
        var replayMetadata = await FinalizeAsync(scope, transport, cancellationToken);
        if (await writer.ReplayDecideOverageAsync(transferId, lineId, request, replayMetadata,
                token => FreshAuthorityFenceAsync(scope, token), cancellationToken) is { } replay)
        {
            return replay;
        }

        var metadata = await FinalizeAsync(scope, transport, cancellationToken);
        return await writer.DecideOverageAsync(transferId, lineId, request, metadata,
            token => FreshAuthorityFenceAsync(scope, token), cancellationToken);
    }

    public async Task<InventoryTransferReceiptResultV1> RequestLossAsync(
        Guid transferId,
        Guid lineId,
        RequestInventoryTransferLossV1 request,
        InventoryCommandMetadata transport,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scope = await RequireAsync(InventoryPermissionManifestV1.StockTransferLossRecord, cancellationToken);
        var replayMetadata = await FinalizeAsync(scope, transport, cancellationToken);
        if (await writer.ReplayRequestLossAsync(transferId, lineId, request, replayMetadata,
                token => FreshAuthorityFenceAsync(scope, token), cancellationToken) is { } replay)
        {
            return replay;
        }

        var metadata = await FinalizeAsync(scope, transport, cancellationToken);
        return await writer.RequestLossAsync(transferId, lineId, request, metadata,
            token => FreshAuthorityFenceAsync(scope, token), cancellationToken);
    }

    public async Task<InventoryTransferReceiptResultV1> DecideLossAsync(
        Guid transferId,
        Guid lineId,
        DecideInventoryTransferLossV1 request,
        InventoryCommandMetadata transport,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scope = await RequireAsync(InventoryPermissionManifestV1.StockTransferLossApprove, cancellationToken);
        var replayMetadata = await FinalizeAsync(scope, transport, cancellationToken);
        if (await writer.ReplayDecideLossAsync(transferId, lineId, request, replayMetadata,
                token => FreshAuthorityFenceAsync(scope, token), cancellationToken) is { } replay)
        {
            return replay;
        }

        var metadata = await FinalizeAsync(scope, transport, cancellationToken);
        return await writer.DecideLossAsync(transferId, lineId, request, metadata,
            token => FreshAuthorityFenceAsync(scope, token), cancellationToken);
    }

    /// <summary>KLN-D107: a corrected excess re-request runs under the receive permission; its decision stays under the
    /// overage approval.</summary>
    public async Task<InventoryTransferReceiptResultV1> RenewOverageAsync(
        Guid transferId,
        Guid lineId,
        RenewInventoryTransferOverageV1 request,
        InventoryCommandMetadata transport,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scope = await RequireAsync(InventoryPermissionManifestV1.StockTransferWrite, cancellationToken);
        var replayMetadata = await FinalizeAsync(scope, transport, cancellationToken);
        if (await writer.ReplayRenewOverageAsync(transferId, lineId, request, replayMetadata,
                token => FreshAuthorityFenceAsync(scope, token), cancellationToken) is { } replay)
        {
            return replay;
        }

        var metadata = await FinalizeAsync(scope, transport, cancellationToken);
        return await writer.RenewOverageAsync(transferId, lineId, request, metadata,
            token => FreshAuthorityFenceAsync(scope, token), cancellationToken);
    }

    /// <summary>KLN-D108: withdrawing an undecided loss request runs under the loss record permission.</summary>
    public async Task<InventoryTransferReceiptResultV1> WithdrawLossAsync(
        Guid transferId,
        Guid lineId,
        WithdrawInventoryTransferLossV1 request,
        InventoryCommandMetadata transport,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scope = await RequireAsync(InventoryPermissionManifestV1.StockTransferLossRecord, cancellationToken);
        var replayMetadata = await FinalizeAsync(scope, transport, cancellationToken);
        if (await writer.ReplayWithdrawLossAsync(transferId, lineId, request, replayMetadata,
                token => FreshAuthorityFenceAsync(scope, token), cancellationToken) is { } replay)
        {
            return replay;
        }

        var metadata = await FinalizeAsync(scope, transport, cancellationToken);
        return await writer.WithdrawLossAsync(transferId, lineId, request, metadata,
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

    private async Task RequireDestinationAsync(
        Scope scope,
        InventoryTransferViewV1 transfer,
        CancellationToken cancellationToken)
    {
        InventoryTransferDestinationStatusV1 destinationStatus;
        try
        {
            destinationStatus = await destination.CheckAsync(scope.CompanyId, transfer.DestinationLocationId,
                transfer.DestinationCustodianKind, transfer.DestinationCustodianId, cancellationToken);
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

public sealed class InventoryTransferReceiptQueryService(
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority,
    IInventoryTransferReceiptReaderV1 reader) : IInventoryTransferReceiptQueryV1
{
    public async Task<InventoryTransferReceiptFactsViewV1> GetReceiptFactsAsync(
        Guid transferId,
        CancellationToken cancellationToken = default)
    {
        var scope = await RequireAsync(cancellationToken);
        InventoryTransferReceiptFactsViewV1? result;
        try
        {
            result = await reader.ReadReceiptFactsAsync(transferId, cancellationToken);
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
