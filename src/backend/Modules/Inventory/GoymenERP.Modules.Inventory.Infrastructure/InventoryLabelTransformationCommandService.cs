using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;

namespace GoymenERP.Modules.Inventory.Infrastructure;

/// <summary>
/// The R19 split/merge command over the accepted #438 writer. See
/// <see cref="IInventoryLabelTransformationCommandV1"/> for the authority contract.
/// <list type="bullet">
/// <item><b>Authority:</b> <see cref="InventoryPermissionManifestV1.LabelTransformationRecord"/>, company-wide,
/// measured fresh before the writer is called and again immediately before handing over the derived command.
/// An unregistered name is 503 <c>inventory_permission_unregistered</c>; a denied, site-scoped or lost grant,
/// another actor or another company is 403; no identity is 401. The writer is never reached in those cases.</item>
/// <item><b>Metadata:</b> actor, permission and role codes come from the measured scope, never from the caller.
/// A transport actor that is not the authenticated actor is refused.</item>
/// <item><b>Writer answers:</b> passed through unchanged, except the kilogram-profile provider states the
/// writer reports as 400 (<see cref="InventoryLabelTransformationHttpCodesV1.Status503Codes"/>): those are not
/// the caller's input and are answered 503 with the same code.</item>
/// </list>
/// This service holds no database handle and performs no read of its own; it adds no permission registration,
/// grant, route or shell capability.
/// </summary>
public sealed class InventoryLabelTransformationCommandService : IInventoryLabelTransformationCommandV1
{
    private readonly ICompanyContext companyContext;
    private readonly ICurrentUser currentUser;
    private readonly IErpAuthorityRevalidationService authority;
    private readonly IInventoryLabelGenealogyWriterV1 writer;
    private readonly Func<string, bool> isKnownPermission;

    public InventoryLabelTransformationCommandService(
        ICompanyContext companyContext,
        ICurrentUser currentUser,
        IErpAuthorityRevalidationService authority,
        IInventoryLabelGenealogyWriterV1 writer)
        : this(companyContext, currentUser, authority, writer, ErpPermissions.IsKnown)
    {
    }

    /// <summary>Test seam: the central catalogue's "is this name registered" answer.</summary>
    internal InventoryLabelTransformationCommandService(
        ICompanyContext companyContext,
        ICurrentUser currentUser,
        IErpAuthorityRevalidationService authority,
        IInventoryLabelGenealogyWriterV1 writer,
        Func<string, bool> isKnownPermission)
    {
        this.companyContext = companyContext ?? throw new ArgumentNullException(nameof(companyContext));
        this.currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
        this.authority = authority ?? throw new ArgumentNullException(nameof(authority));
        this.writer = writer ?? throw new ArgumentNullException(nameof(writer));
        this.isKnownPermission = isKnownPermission ?? throw new ArgumentNullException(nameof(isKnownPermission));
    }

    public async Task<InventoryLabelTransformationResultV1> RecordAsync(
        TransformInventoryLabelsV1 request,
        InventoryCommandMetadata transport,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(transport);
        cancellationToken.ThrowIfCancellationRequested();

        var scope = await RequireAsync(cancellationToken);
        if (!string.IsNullOrEmpty(transport.ActorId)
            && !string.Equals(transport.ActorId, scope.ActorId, StringComparison.Ordinal))
        {
            throw Forbidden();
        }

        var finalScope = await RequireStillAsync(scope, cancellationToken);
        var command = transport with
        {
            ActorId = finalScope.ActorId,
            PermissionCode = InventoryPermissionManifestV1.LabelTransformationRecord,
            RoleCodes = finalScope.RoleCodes,
        };

        try
        {
            return await writer.TransformAsync(request, command, cancellationToken);
        }
        catch (InventoryLabelGenealogyException exception)
            when (exception.Status == 400
                && InventoryLabelTransformationHttpCodesV1.Status503Codes.Contains(exception.Code))
        {
            throw new InventoryLabelGenealogyException(503, exception.Code, exception);
        }
    }

    private async Task<Scope> RequireAsync(CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            throw new InventoryLabelGenealogyException(401, InventoryLabelTransformationCommandCodesV1.Unauthenticated);
        }

        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty
            || !currentUser.CanAccessCompany(companyContext.CompanyId))
        {
            throw Forbidden();
        }

        const string permission = InventoryPermissionManifestV1.LabelTransformationRecord;
        if (!isKnownPermission(permission))
        {
            // A name the central catalogue does not know is a setup gap, never a grant.
            throw new InventoryLabelGenealogyException(503, InventoryLabelTransformationCommandCodesV1.PermissionUnregistered);
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
    private async Task<Scope> RequireStillAsync(Scope scope, CancellationToken cancellationToken)
    {
        if (!Matches(scope))
        {
            throw Forbidden();
        }

        var measured = await authority.RevalidatePermissionScopeAsync(
            InventoryPermissionManifestV1.LabelTransformationRecord, scope.CompanyId, cancellationToken);
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

    private static InventoryLabelGenealogyException Forbidden() =>
        new(403, InventoryLabelTransformationCommandCodesV1.Forbidden);

    private sealed record Scope(Guid CompanyId, string ActorId, IReadOnlyCollection<string> RoleCodes);
}
