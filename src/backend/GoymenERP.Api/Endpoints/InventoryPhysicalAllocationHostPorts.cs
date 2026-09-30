using System.Text.Json.Serialization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

/// <summary>
/// The public create shape. Product identity and version are intentionally absent: the host obtains
/// both from the approved Sales line and never accepts those authoritative facts from the caller.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateInventoryPhysicalAllocationRequestV1(
    [property: JsonRequired] Guid AllocationId,
    [property: JsonRequired] Guid SalesOrderId,
    [property: JsonRequired] Guid SalesOrderLineId,
    [property: JsonRequired] Guid LabelId,
    [property: JsonRequired] string ExpectedLabelVersion,
    [property: JsonRequired] string QuantityKg,
    [property: JsonRequired] string Reason,
    DateTimeOffset? ReservedUntilUtc);

/// <summary>
/// INV-AL-1 change request. The Sales order/line name the demand the allocation already serves; the host re-reads
/// that line through the Sales owner port (it must still be approved) and supplies its product identity and version.
/// <see cref="TargetLabelId"/> equal to the allocation's own label changes the kilograms in place (no
/// <see cref="NewAllocationId"/>); another label of the same product moves the reservation (a new allocation id).
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ChangeInventoryPhysicalAllocationRequestV1(
    [property: JsonRequired] string ExpectedAllocationVersion,
    [property: JsonRequired] Guid SalesOrderId,
    [property: JsonRequired] Guid SalesOrderLineId,
    [property: JsonRequired] Guid TargetLabelId,
    [property: JsonRequired] string ExpectedTargetLabelVersion,
    [property: JsonRequired] string QuantityKg,
    [property: JsonRequired] string Reason,
    Guid? NewAllocationId);

/// <summary>
/// Central host boundary for the Inventory-owned writer. It performs no Inventory or Sales table read:
/// the approved demand comes through the Sales owner port and persistence remains behind the Inventory
/// writer. Fresh authority is measured before the owner read and again after it, immediately before the
/// derived command is handed to the writer.
/// </summary>
public sealed class InventoryPhysicalAllocationHostService(
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IErpAuthorityRevalidationService authority,
    ISalesOrderLineReferencePortV1 orderLines,
    IInventoryPhysicalAllocationWriterV1 writer)
{
    public async Task<InventoryPhysicalAllocationResultV1> CreateAsync(
        CreateInventoryPhysicalAllocationRequestV1 request,
        InventoryCommandMetadata transport,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scope = await RequireAsync(cancellationToken);
        if (request.AllocationId == Guid.Empty || request.SalesOrderId == Guid.Empty
            || request.SalesOrderLineId == Guid.Empty || request.LabelId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.ExpectedLabelVersion)
            || string.IsNullOrWhiteSpace(request.QuantityKg) || string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new InventoryPhysicalAllocationHostException(
                StatusCodes.Status400BadRequest, InventoryAllocationCodesV1.ValidationFailed);
        }

        var (line, finalScope) = await ApprovedLineAsync(scope, request.SalesOrderId, request.SalesOrderLineId,
            cancellationToken);
        var command = new CreatePhysicalSalesAllocationV1(
            request.AllocationId,
            request.SalesOrderId,
            request.SalesOrderLineId,
            line.ProductId,
            line.ProductVersion,
            request.LabelId,
            request.ExpectedLabelVersion,
            request.QuantityKg,
            request.Reason,
            request.ReservedUntilUtc);
        return await writer.CreateAsync(command, Metadata(finalScope, transport), cancellationToken);
    }

    public async Task<InventoryPhysicalAllocationResultV1> ReleaseAsync(
        Guid allocationId,
        ReleasePhysicalAllocationV1 request,
        InventoryCommandMetadata transport,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scope = await RequireAsync(cancellationToken);
        var finalScope = await RequireStillAsync(scope, cancellationToken);
        return await writer.ReleaseAsync(allocationId, request, Metadata(finalScope, transport), cancellationToken);
    }

    /// <summary>
    /// INV-AL-1 change: the same fences as a create (fresh authority before and after the Sales owner read, and the
    /// line must still be approved; a cancelled or unapproved order is 422 and a full release stays available), then
    /// the Inventory writer changes its own rows in one save.
    /// </summary>
    public async Task<InventoryPhysicalAllocationChangeResultV1> ChangeAsync(
        Guid allocationId,
        ChangeInventoryPhysicalAllocationRequestV1 request,
        InventoryCommandMetadata transport,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scope = await RequireAsync(cancellationToken);
        if (allocationId == Guid.Empty || request.SalesOrderId == Guid.Empty || request.SalesOrderLineId == Guid.Empty
            || request.TargetLabelId == Guid.Empty || request.NewAllocationId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.ExpectedAllocationVersion)
            || string.IsNullOrWhiteSpace(request.ExpectedTargetLabelVersion)
            || string.IsNullOrWhiteSpace(request.QuantityKg) || string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new InventoryPhysicalAllocationHostException(
                StatusCodes.Status400BadRequest, InventoryAllocationCodesV1.ValidationFailed);
        }

        var (line, finalScope) = await ApprovedLineAsync(scope, request.SalesOrderId, request.SalesOrderLineId,
            cancellationToken);
        var command = new ChangePhysicalSalesAllocationV1(
            request.ExpectedAllocationVersion,
            request.SalesOrderId,
            request.SalesOrderLineId,
            line.ProductId,
            line.ProductVersion,
            request.TargetLabelId,
            request.ExpectedTargetLabelVersion,
            request.QuantityKg,
            request.Reason,
            request.NewAllocationId);
        return await writer.ChangeAsync(allocationId, command, Metadata(finalScope, transport), cancellationToken);
    }

    private async Task<(SalesOrderLineReferenceV1 Line, Scope FinalScope)> ApprovedLineAsync(
        Scope scope,
        Guid salesOrderId,
        Guid salesOrderLineId,
        CancellationToken cancellationToken)
    {
        SalesOrderLineReferenceAnswerV1 answer;
        try
        {
            answer = await orderLines.GetApprovedLineAsync(salesOrderId, salesOrderLineId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InventoryPhysicalAllocationHostException(
                StatusCodes.Status503ServiceUnavailable, innerException: exception);
        }

        var finalScope = await RequireStillAsync(scope, cancellationToken);
        if (answer.Outcome == SalesOrderLineReferenceOutcomeV1.NotFound)
        {
            throw new InventoryPhysicalAllocationHostException(StatusCodes.Status404NotFound);
        }

        if (answer.Outcome == SalesOrderLineReferenceOutcomeV1.NotApproved)
        {
            throw new InventoryPhysicalAllocationHostException(
                StatusCodes.Status422UnprocessableEntity, InventoryAllocationCodesV1.InvalidReference);
        }

        var line = answer.Line;
        if (answer.Outcome != SalesOrderLineReferenceOutcomeV1.Approved || line is null
            || line.CompanyId != scope.CompanyId)
        {
            throw new InventoryPhysicalAllocationHostException(StatusCodes.Status503ServiceUnavailable);
        }

        if (line.OrderId != salesOrderId || line.LineId != salesOrderLineId
            || line.ProductId == Guid.Empty || string.IsNullOrWhiteSpace(line.ProductVersion))
        {
            throw new InventoryPhysicalAllocationHostException(StatusCodes.Status503ServiceUnavailable);
        }

        return (line, finalScope);
    }

    private async Task<Scope> RequireAsync(CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            throw new InventoryPhysicalAllocationHostException(StatusCodes.Status401Unauthorized);
        }

        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty
            || !currentUser.CanAccessCompany(companyContext.CompanyId))
        {
            throw new InventoryPhysicalAllocationHostException(StatusCodes.Status403Forbidden);
        }

        const string permission = InventoryPermissionManifestV1.PhysicalAllocationWrite;
        if (!ErpPermissions.IsKnown(permission))
        {
            throw new InventoryPhysicalAllocationHostException(StatusCodes.Status503ServiceUnavailable);
        }

        var scope = new Scope(companyContext.CompanyId, currentUser.UserId, []);
        var measured = await authority.RevalidatePermissionScopeAsync(permission, scope.CompanyId, cancellationToken);
        if (!measured.IsGranted || !measured.IsCompanyWide || !Matches(scope))
        {
            throw new InventoryPhysicalAllocationHostException(StatusCodes.Status403Forbidden);
        }

        return scope with { RoleCodes = [.. measured.RoleCodes.Order(StringComparer.Ordinal)] };
    }

    private async Task<Scope> RequireStillAsync(Scope scope, CancellationToken cancellationToken)
    {
        if (!Matches(scope))
        {
            throw new InventoryPhysicalAllocationHostException(StatusCodes.Status403Forbidden);
        }

        var measured = await authority.RevalidatePermissionScopeAsync(
            InventoryPermissionManifestV1.PhysicalAllocationWrite, scope.CompanyId, cancellationToken);
        if (!measured.IsGranted || !measured.IsCompanyWide || !Matches(scope))
        {
            throw new InventoryPhysicalAllocationHostException(StatusCodes.Status403Forbidden);
        }

        return scope with { RoleCodes = [.. measured.RoleCodes.Order(StringComparer.Ordinal)] };
    }

    private bool Matches(Scope scope) => currentUser.IsAuthenticated
        && string.Equals(currentUser.UserId, scope.ActorId, StringComparison.Ordinal)
        && companyContext.IsResolved && companyContext.CompanyId == scope.CompanyId
        && currentUser.CanAccessCompany(scope.CompanyId);

    private static InventoryCommandMetadata Metadata(Scope scope, InventoryCommandMetadata transport) => transport with
    {
        ActorId = scope.ActorId,
        PermissionCode = InventoryPermissionManifestV1.PhysicalAllocationWrite,
        RoleCodes = scope.RoleCodes,
    };

    private sealed record Scope(Guid CompanyId, string ActorId, IReadOnlyCollection<string> RoleCodes);
}

public sealed class InventoryPhysicalAllocationHostException(
    int status,
    string? code = null,
    Exception? innerException = null) : Exception(code, innerException)
{
    public int Status { get; } = status;
    public string? Code { get; } = code;
}

public static class InventoryPhysicalAllocationHttpCodesV1
{
    public static IReadOnlySet<string> Status400Codes { get; } = Set(InventoryAllocationCodesV1.ValidationFailed);

    public static IReadOnlySet<string> CreateStatus409Codes { get; } = Set(
        InventoryAllocationCodesV1.IdempotencyConflict,
        InventoryAllocationCodesV1.LabelVersionConflict,
        InventoryAllocationCodesV1.InsufficientFreeStock,
        InventoryAllocationCodesV1.ConcurrentChange);

    public static IReadOnlySet<string> ReleaseStatus409Codes { get; } = Set(
        InventoryAllocationCodesV1.IdempotencyConflict,
        InventoryAllocationCodesV1.AllocationVersionConflict,
        InventoryAllocationCodesV1.AllocationStateConflict,
        InventoryAllocationCodesV1.ConcurrentChange);

    // The Sales ownership gate (InventorySalesOwnershipGuardV1) refuses only a NEW reservation, so only create
    // declares inventory_allocation_owner_not_company; release keeps its single invalid-reference 422.
    public static IReadOnlySet<string> CreateStatus422Codes { get; } = Set(
        InventoryAllocationCodesV1.InvalidReference,
        InventoryAllocationCodesV1.OwnerNotCompany);

    public static IReadOnlySet<string> ReleaseStatus422Codes { get; } = Set(InventoryAllocationCodesV1.InvalidReference);

    // INV-AL-1 change: stale allocation or label version, a non-active allocation, too little free stock, a race.
    public static IReadOnlySet<string> ChangeStatus409Codes { get; } = Set(
        InventoryAllocationCodesV1.IdempotencyConflict,
        InventoryAllocationCodesV1.AllocationVersionConflict,
        InventoryAllocationCodesV1.AllocationStateConflict,
        InventoryAllocationCodesV1.LabelVersionConflict,
        InventoryAllocationCodesV1.InsufficientFreeStock,
        InventoryAllocationCodesV1.ConcurrentChange);

    // A change adds a reservation part, so the Sales ownership gate applies as on create; the same label and kg is no change.
    public static IReadOnlySet<string> ChangeStatus422Codes { get; } = Set(
        InventoryAllocationCodesV1.InvalidReference,
        InventoryAllocationCodesV1.OwnerNotCompany,
        InventoryAllocationCodesV1.ChangeUnchanged);

    private static IReadOnlySet<string> Set(params string[] codes) => codes.ToHashSet(StringComparer.Ordinal);
}
