using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Modules.Inventory.Infrastructure;

/// <summary>
/// ST-05 / AT-037 count commands over the Inventory writer, in the R19 command shape
/// (<see cref="InventoryLabelTransformationCommandService"/>).
/// <list type="bullet">
/// <item><b>Authority:</b> record = <see cref="InventoryPermissionManifestV1.StockCountRecord"/>, decide =
/// <see cref="InventoryPermissionManifestV1.StockCountApprove"/>; each company-wide, measured fresh before the writer
/// is called and again immediately before handing over the derived command. An unregistered name is 503
/// <c>inventory_permission_unregistered</c>; a denied, site-scoped or lost grant, another actor or another company
/// is 403; no identity is 401. The writer is never reached in those cases. Nothing is measured after the commit.</item>
/// <item><b>Metadata:</b> actor, permission and role codes come from the measured scope, never from the caller.</item>
/// <item><b>Writer answers:</b> passed through, except the kilogram-profile provider states the writer reports as
/// 400: those are not the caller's input and are answered 503 with the same code.</item>
/// </list>
/// This service holds no database handle and performs no read of its own.
/// </summary>
public sealed class InventoryStockCountCommandService : IInventoryStockCountCommandV1
{
    private readonly ICompanyContext companyContext;
    private readonly ICurrentUser currentUser;
    private readonly IErpAuthorityRevalidationService authority;
    private readonly IInventoryStockCountWriterV1 writer;
    private readonly Func<string, bool> isKnownPermission;

    public InventoryStockCountCommandService(
        ICompanyContext companyContext,
        ICurrentUser currentUser,
        IErpAuthorityRevalidationService authority,
        IInventoryStockCountWriterV1 writer)
        : this(companyContext, currentUser, authority, writer, ErpPermissions.IsKnown)
    {
    }

    /// <summary>Test seam: the central catalogue's "is this name registered" answer.</summary>
    internal InventoryStockCountCommandService(
        ICompanyContext companyContext,
        ICurrentUser currentUser,
        IErpAuthorityRevalidationService authority,
        IInventoryStockCountWriterV1 writer,
        Func<string, bool> isKnownPermission)
    {
        this.companyContext = companyContext ?? throw new ArgumentNullException(nameof(companyContext));
        this.currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
        this.authority = authority ?? throw new ArgumentNullException(nameof(authority));
        this.writer = writer ?? throw new ArgumentNullException(nameof(writer));
        this.isKnownPermission = isKnownPermission ?? throw new ArgumentNullException(nameof(isKnownPermission));
    }

    public async Task<InventoryStockCountResultV1> RecordAsync(
        RecordInventoryStockCountV1 request,
        InventoryCommandMetadata transport,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var command = await AuthorizeAsync(InventoryPermissionManifestV1.StockCountRecord, transport, cancellationToken);
        return await PassThroughAsync(() => writer.RecordStockCountAsync(request, command, cancellationToken));
    }

    public async Task<InventoryStockCountResultV1> DecideAsync(
        Guid countId,
        DecideInventoryStockCountV1 request,
        InventoryCommandMetadata transport,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var command = await AuthorizeAsync(InventoryPermissionManifestV1.StockCountApprove, transport, cancellationToken);
        return await PassThroughAsync(() => writer.DecideStockCountAsync(countId, request, command, cancellationToken));
    }

    private async Task<InventoryCommandMetadata> AuthorizeAsync(
        string permission,
        InventoryCommandMetadata transport,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transport);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = await RequireAsync(permission, cancellationToken);
        if (!string.IsNullOrEmpty(transport.ActorId)
            && !string.Equals(transport.ActorId, scope.ActorId, StringComparison.Ordinal))
        {
            throw Forbidden();
        }

        var finalScope = await RequireStillAsync(permission, scope, cancellationToken);
        return transport with
        {
            ActorId = finalScope.ActorId,
            PermissionCode = permission,
            RoleCodes = finalScope.RoleCodes,
        };
    }

    private static async Task<InventoryStockCountResultV1> PassThroughAsync(Func<Task<InventoryStockCountResultV1>> call)
    {
        try
        {
            return await call();
        }
        catch (InventoryStockCountException exception)
            when (exception.Status == 400 && InventoryStockCountHttpCodesV1.Status503Codes.Contains(exception.Code))
        {
            throw new InventoryStockCountException(503, exception.Code, exception);
        }
    }

    private async Task<Scope> RequireAsync(string permission, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            throw new InventoryStockCountException(401, InventoryStockCountCodesV1.Unauthenticated);
        }

        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty
            || !currentUser.CanAccessCompany(companyContext.CompanyId))
        {
            throw Forbidden();
        }

        if (!isKnownPermission(permission))
        {
            // A name the central catalogue does not know is a setup gap, never a grant.
            throw new InventoryStockCountException(503, InventoryStockCountCodesV1.PermissionUnregistered);
        }

        var scope = new Scope(companyContext.CompanyId, currentUser.UserId, []);
        var measured = await authority.RevalidatePermissionScopeAsync(permission, scope.CompanyId, cancellationToken);
        if (!measured.IsGranted || !measured.IsCompanyWide || !Matches(scope))
        {
            throw Forbidden();
        }

        return scope with { RoleCodes = [.. measured.RoleCodes.Order(StringComparer.Ordinal)] };
    }

    /// <summary>The fence right before the writer: the same actor and company, still granted, measured now.</summary>
    private async Task<Scope> RequireStillAsync(string permission, Scope scope, CancellationToken cancellationToken)
    {
        if (!Matches(scope))
        {
            throw Forbidden();
        }

        var measured = await authority.RevalidatePermissionScopeAsync(permission, scope.CompanyId, cancellationToken);
        if (!measured.IsGranted || !measured.IsCompanyWide || !Matches(scope))
        {
            throw Forbidden();
        }

        return scope with { RoleCodes = [.. measured.RoleCodes.Order(StringComparer.Ordinal)] };
    }

    private bool Matches(Scope scope) => currentUser.IsAuthenticated
        && string.Equals(currentUser.UserId, scope.ActorId, StringComparison.Ordinal)
        && companyContext.IsResolved && companyContext.CompanyId == scope.CompanyId
        && currentUser.CanAccessCompany(scope.CompanyId);

    private static InventoryStockCountException Forbidden() => new(403, InventoryStockCountCodesV1.Forbidden);

    private sealed record Scope(Guid CompanyId, string ActorId, IReadOnlyCollection<string> RoleCodes);
}
