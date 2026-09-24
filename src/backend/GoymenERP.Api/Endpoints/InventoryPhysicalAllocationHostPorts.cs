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

        SalesOrderLineReferenceAnswerV1 answer;
        try
        {
            answer = await orderLines.GetApprovedLineAsync(
                request.SalesOrderId, request.SalesOrderLineId, cancellationToken);
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

        await RequireStillAsync(scope, cancellationToken);
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
            throw new InventoryPhysicalAllocationHostException(
                answer.Outcome == SalesOrderLineReferenceOutcomeV1.Approved
                    ? StatusCodes.Status503ServiceUnavailable
                    : StatusCodes.Status404NotFound);
        }

        if (line.OrderId != request.SalesOrderId || line.LineId != request.SalesOrderLineId
            || line.ProductId == Guid.Empty || string.IsNullOrWhiteSpace(line.ProductVersion))
        {
            throw new InventoryPhysicalAllocationHostException(StatusCodes.Status503ServiceUnavailable);
        }

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
        return await writer.CreateAsync(command, Metadata(scope, transport), cancellationToken);
    }

    public async Task<InventoryPhysicalAllocationResultV1> ReleaseAsync(
        Guid allocationId,
        ReleasePhysicalAllocationV1 request,
        InventoryCommandMetadata transport,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var scope = await RequireAsync(cancellationToken);
        await RequireStillAsync(scope, cancellationToken);
        return await writer.ReleaseAsync(allocationId, request, Metadata(scope, transport), cancellationToken);
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

    private async Task RequireStillAsync(Scope scope, CancellationToken cancellationToken)
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

    public static IReadOnlySet<string> Status422Codes { get; } = Set(InventoryAllocationCodesV1.InvalidReference);

    private static IReadOnlySet<string> Set(params string[] codes) => codes.ToHashSet(StringComparer.Ordinal);
}
