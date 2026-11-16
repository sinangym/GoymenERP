using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Modules.Inventory.Infrastructure;

/// <summary>
/// SAT-D304 / SAT-D372 opening-stock cost commands over the Inventory writer, in the R19 command shape (the ST-02
/// scrap twin: <see cref="InventoryStockScrapCommandService"/>).
/// <list type="bullet">
/// <item><b>Authority:</b> submit = <see cref="InventoryPermissionManifestV1.OpeningCostSubmit"/>, decide =
/// <see cref="InventoryPermissionManifestV1.OpeningCostApprove"/>; each company-wide, measured fresh before the writer
/// is called and again immediately before handing over the derived command. An unregistered name is 503
/// <c>inventory_permission_unregistered</c>; a denied, site-scoped or lost grant, another actor or another company
/// is 403; no identity is 401. The writer is never reached in those cases. The same captured scope is measured
/// after every owner read and after the transaction flush, before commit. Registering the names grants nothing.</item>
/// <item><b>Metadata:</b> actor, permission and role codes come from the measured scope, never from the caller.</item>
/// <item><b>Writer answers:</b> passed through; a setup state reported as 400 is answered 503 with the same code.</item>
/// </list>
/// This service holds no database handle and performs no read of its own.
/// </summary>
public sealed class InventoryOpeningCostCommandService : IInventoryOpeningCostCommandV1
{
    private readonly ICompanyContext companyContext;
    private readonly ICurrentUser currentUser;
    private readonly IErpAuthorityRevalidationService authority;
    private readonly IInventoryOpeningCostWriterV1 writer;
    private readonly Func<string, bool> isKnownPermission;

    public InventoryOpeningCostCommandService(
        ICompanyContext companyContext,
        ICurrentUser currentUser,
        IErpAuthorityRevalidationService authority,
        IInventoryOpeningCostWriterV1 writer)
        : this(companyContext, currentUser, authority, writer, ErpPermissions.IsKnown)
    {
    }

    /// <summary>Test seam: the central catalogue's "is this name registered" answer.</summary>
    internal InventoryOpeningCostCommandService(
        ICompanyContext companyContext,
        ICurrentUser currentUser,
        IErpAuthorityRevalidationService authority,
        IInventoryOpeningCostWriterV1 writer,
        Func<string, bool> isKnownPermission)
    {
        this.companyContext = companyContext ?? throw new ArgumentNullException(nameof(companyContext));
        this.currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
        this.authority = authority ?? throw new ArgumentNullException(nameof(authority));
        this.writer = writer ?? throw new ArgumentNullException(nameof(writer));
        this.isKnownPermission = isKnownPermission ?? throw new ArgumentNullException(nameof(isKnownPermission));
    }

    public async Task<InventoryOpeningCostResultV1> SubmitAsync(
        SubmitInventoryOpeningCostV1 request,
        InventoryCommandMetadata transport,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        const string permission = InventoryPermissionManifestV1.OpeningCostSubmit;
        var authorized = await AuthorizeAsync(permission, transport, cancellationToken);
        return await PassThroughAsync(() => writer.SubmitOpeningCostAsync(request, authorized.Metadata,
            token => FreshAuthorityFenceAsync(permission, authorized.Scope, token), cancellationToken));
    }

    public async Task<InventoryOpeningCostResultV1> DecideAsync(
        Guid openingCostId,
        DecideInventoryOpeningCostV1 request,
        InventoryCommandMetadata transport,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        const string permission = InventoryPermissionManifestV1.OpeningCostApprove;
        var authorized = await AuthorizeAsync(permission, transport, cancellationToken);
        return await PassThroughAsync(() => writer.DecideOpeningCostAsync(openingCostId, request, authorized.Metadata,
            token => FreshAuthorityFenceAsync(permission, authorized.Scope, token), cancellationToken));
    }

    private async Task<AuthorizedCommand> AuthorizeAsync(
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
        return new(transport with
        {
            ActorId = finalScope.ActorId,
            PermissionCode = permission,
            RoleCodes = finalScope.RoleCodes,
        }, scope);
    }

    private static async Task<InventoryOpeningCostResultV1> PassThroughAsync(Func<Task<InventoryOpeningCostResultV1>> call)
    {
        try
        {
            return await call();
        }
        catch (InventoryOpeningCostException exception)
            when (exception.Status == 400 && InventoryOpeningCostHttpCodesV1.Status503Codes.Contains(exception.Code))
        {
            throw new InventoryOpeningCostException(503, exception.Code, exception);
        }
    }

    private async Task<Scope> RequireAsync(string permission, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            throw new InventoryOpeningCostException(401, InventoryOpeningCostCodesV1.Unauthenticated);
        }

        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty
            || !currentUser.CanAccessCompany(companyContext.CompanyId))
        {
            throw Forbidden();
        }

        if (!isKnownPermission(permission))
        {
            // A name the central catalogue does not know is a setup gap, never a grant.
            throw new InventoryOpeningCostException(503, InventoryOpeningCostCodesV1.PermissionUnregistered);
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

    private async Task FreshAuthorityFenceAsync(string permission, Scope scope, CancellationToken cancellationToken) =>
        _ = await RequireStillAsync(permission, scope, cancellationToken);

    private bool Matches(Scope scope) => currentUser.IsAuthenticated
        && string.Equals(currentUser.UserId, scope.ActorId, StringComparison.Ordinal)
        && companyContext.IsResolved && companyContext.CompanyId == scope.CompanyId
        && currentUser.CanAccessCompany(scope.CompanyId);

    private static InventoryOpeningCostException Forbidden() => new(403, InventoryOpeningCostCodesV1.Forbidden);

    private sealed record AuthorizedCommand(InventoryCommandMetadata Metadata, Scope Scope);
    private sealed record Scope(Guid CompanyId, string ActorId, IReadOnlyCollection<string> RoleCodes);
}
