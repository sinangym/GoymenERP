using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Modules.Inventory.Infrastructure;

/// <summary>
/// Fresh company-wide authority fence for the one-step label classification command. The central
/// catalogue owns the permission name and its grants; this service only consumes the configured name.
/// </summary>
public sealed class InventoryLabelClassificationCommandService : IInventoryLabelClassificationCommandV1
{
    private readonly ICompanyContext companyContext;
    private readonly ICurrentUser currentUser;
    private readonly IErpAuthorityRevalidationService authority;
    private readonly IInventoryLabelClassificationWriterV1 writer;
    private readonly InventoryLabelClassificationAuthorityV1 authorityConfiguration;
    private readonly Func<string, bool> isKnownPermission;

    public InventoryLabelClassificationCommandService(
        ICompanyContext companyContext,
        ICurrentUser currentUser,
        IErpAuthorityRevalidationService authority,
        IInventoryLabelClassificationWriterV1 writer,
        InventoryLabelClassificationAuthorityV1 authorityConfiguration)
        : this(companyContext, currentUser, authority, writer, authorityConfiguration, ErpPermissions.IsKnown)
    {
    }

    internal InventoryLabelClassificationCommandService(
        ICompanyContext companyContext,
        ICurrentUser currentUser,
        IErpAuthorityRevalidationService authority,
        IInventoryLabelClassificationWriterV1 writer,
        InventoryLabelClassificationAuthorityV1 authorityConfiguration,
        Func<string, bool> knownPermission)
    {
        this.companyContext = companyContext ?? throw new ArgumentNullException(nameof(companyContext));
        this.currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
        this.authority = authority ?? throw new ArgumentNullException(nameof(authority));
        this.writer = writer ?? throw new ArgumentNullException(nameof(writer));
        this.authorityConfiguration = authorityConfiguration ?? throw new ArgumentNullException(nameof(authorityConfiguration));
        isKnownPermission = knownPermission ?? throw new ArgumentNullException(nameof(knownPermission));
    }

    public async Task<InventoryLabelClassificationResultV1> ClassifyAsync(
        Guid labelId,
        ClassifyInventoryLabelV1 request,
        InventoryCommandMetadata transport,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(transport);
        cancellationToken.ThrowIfCancellationRequested();

        var permission = authorityConfiguration.PermissionCode?.Trim();
        if (string.IsNullOrWhiteSpace(permission) || !isKnownPermission(permission))
        {
            throw Error(503, InventoryLabelClassificationCodesV1.PermissionUnregistered);
        }

        var scope = await RequireAsync(permission, cancellationToken);
        if (!string.IsNullOrEmpty(transport.ActorId)
            && !string.Equals(transport.ActorId, scope.ActorId, StringComparison.Ordinal))
        {
            throw Forbidden();
        }

        var finalScope = await RequireStillAsync(permission, scope, cancellationToken);
        return await writer.ClassifyAsync(labelId, request, transport with
        {
            ActorId = finalScope.ActorId,
            PermissionCode = permission,
            RoleCodes = finalScope.RoleCodes,
        }, token => FreshAuthorityFenceAsync(permission, scope, token), cancellationToken);
    }

    private async Task<Scope> RequireAsync(string permission, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            throw Error(401, InventoryLabelClassificationCodesV1.Unauthenticated);
        }

        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty
            || !currentUser.CanAccessCompany(companyContext.CompanyId))
        {
            throw Forbidden();
        }

        var scope = new Scope(companyContext.CompanyId, currentUser.UserId, []);
        var measured = await authority.RevalidatePermissionScopeAsync(permission, scope.CompanyId, cancellationToken);
        if (!measured.IsGranted || !measured.IsCompanyWide || !Matches(scope))
        {
            throw Forbidden();
        }

        return scope with { RoleCodes = [.. measured.RoleCodes.Order(StringComparer.Ordinal)] };
    }

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

    private static InventoryLabelClassificationException Forbidden() =>
        Error(403, InventoryLabelClassificationCodesV1.Forbidden);

    private static InventoryLabelClassificationException Error(int status, string code) => new(status, code);

    private sealed record Scope(Guid CompanyId, string ActorId, IReadOnlyCollection<string> RoleCodes);
}
