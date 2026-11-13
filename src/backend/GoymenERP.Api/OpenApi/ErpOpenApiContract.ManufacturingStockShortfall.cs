using GoymenERP.Modules.Manufacturing.Application;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace GoymenERP.Api.OpenApi;

public static partial class ErpOpenApiContract
{
    /// <summary>
    /// SAT-D318 (owner decision of 2026-11-13): the input issue's 409 is the shared coded problem plus an optional
    /// <c>stockShortfall</c> member. Same status, same stable codes as <see cref="ProducesErpConflict"/>; only the
    /// documented body gains the optional member, so a client that ignores it reads the answer as before.
    /// </summary>
    public static RouteHandlerBuilder ProducesManufacturingInputIssueConflict(
        this RouteHandlerBuilder builder,
        params string[] stableCodes)
    {
        if (stableCodes.Length == 0)
        {
            throw new InvalidOperationException(
                "Coded 409 OpenAPI responses require at least one stable code.");
        }

        builder.Produces<ManufacturingInputIssueConflictProblemContract>(
            StatusCodes.Status409Conflict,
            "application/problem+json");
        AddProblemCodeMetadata(builder, StatusCodes.Status409Conflict, stableCodes);
        return builder;
    }

    private static Task TransformManufacturingStockShortfallSchemaAsync(OpenApiSchema schema,
        OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var type = context.JsonTypeInfo.Type;
        if (type == typeof(ManufacturingInputIssueConflictProblemContract))
        {
            SetIntegerProperty(schema, "status", nullable: false);
            schema.Description = "The input issue's 409 problem: the shared coded problem with the same codes. "
                + "stockShortfall is optional. It is present only on a fresh "
                + "inventory_manufacturing_physical_stock_insufficient refusal that Inventory attributed to this "
                + "issue, and absent on every other code, on a replayed or stored answer and from older servers. "
                + "No problem response establishes success or current authority.";
        }
        else if (type == typeof(ManufacturingStockShortfallViewV1))
        {
            schema.Description = "The input issue Inventory refused because it would take physical stock below "
                + "zero (SAT-D318). movementKind is Inventory's movement kind name; sourceKind, sourceId and "
                + "sourceLineId name the order and its issue; labelId and productId name the parent label and its "
                + "product; locationId is the stored location of the label's stock, null when the label holds none. "
                + "requestedKg is the issue's lossless invariant kilogram text. Available and reserved kilograms "
                + "are not part of this view. It records nothing: the refusal is not stored.";
        }

        return Task.CompletedTask;
    }
}

// Documentation only. Runtime still writes the module's Results.Problem through the host problem
// writer; this is not a second error pipeline. It mirrors ErpProblemDetailsContract member for
// member and adds the optional stockShortfall member of the input issue.
public sealed class ManufacturingInputIssueConflictProblemContract
{
    public required string Type { get; init; }

    public required string Title { get; init; }

    public required int Status { get; init; }

    public required string Detail { get; init; }

    public required string Code { get; init; }

    public string? CorrelationId { get; init; }

    public ErpVersionConflictContract? Conflict { get; init; }

    public ManufacturingStockShortfallViewV1? StockShortfall { get; init; }
}
