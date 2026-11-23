using GoymenERP.Modules.Manufacturing.Application;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace GoymenERP.Api.OpenApi;

public static partial class ErpOpenApiContract
{
    private const string RemnantClassificationProperty = "remnantClassification";

    public static RouteHandlerBuilder ProducesManufacturingPlanAllocationConflict(
        this RouteHandlerBuilder builder, params string[] stableCodes)
    {
        if (stableCodes.Length == 0) throw new InvalidOperationException("A plan-allocation conflict requires stable codes.");
        builder.Produces<ManufacturingPlanAllocationConflictProblemContract>(StatusCodes.Status409Conflict, "application/problem+json");
        AddProblemCodeMetadata(builder, StatusCodes.Status409Conflict, stableCodes);
        return builder;
    }

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
        else if (type == typeof(ManufacturingPlanAllocationConflictProblemContract))
        {
            SetIntegerProperty(schema, "status", nullable: false);
            schema.Description = "The approval/reapproval 409 problem, with unchanged stable codes. "
                + "planAllocationShortfall is optional and appears only for a fresh Inventory insufficient-free-stock "
                + "refusal attributed to the authorized company and plan, after the attempted write was rolled back. "
                + "It is absent from stored answers and replays; older servers may omit it.";
        }
        else if (type == typeof(ManufacturingPlanAllocationShortfallV1))
        {
            schema.Description = "Inventory's verified single-position refusal-time measurements, in canonical kilogram text. "
                + "availableKg and missingKg are computed by Inventory; clients must not recompute them. "
                + "This optional snapshot grants no authority and is not a current availability promise.";
        }
        else if (type == typeof(ManufacturingStockShortfallViewV1))
        {
            schema.Description = "The input issue Inventory refused because it would take physical stock below "
                + "zero (SAT-D318). movementKind is Inventory's movement kind name; sourceKind, sourceId and "
                + "sourceLineId name the order and its issue; labelId and productId name the parent label and its "
                + "product; locationId is the stored location of the label's stock, null when the label holds none. "
                + "requestedKg is the issue's lossless invariant kilogram text. availableKg is, in the same text "
                + "form, the usable kilograms the issue could have taken there: the free physical kilograms less "
                + "other demands' active reservations, never negative and always less than requestedKg. Reserved "
                + "kilograms are not part of this view. It records nothing: the refusal is not stored.";
        }
        else if (type == typeof(DeclareInventoryOutputRequestV1))
        {
            // URT-S16 (URT-D43, BS-14): the usable remnant's label classification is an additive optional member;
            // a client that never sends it is answered as before.
            schema.Required?.Remove(RemnantClassificationProperty);
            if (schema.Properties?.TryGetValue(RemnantClassificationProperty, out var remnant) != true
                || remnant is not OpenApiSchema concreteRemnant)
            {
                throw new InvalidOperationException(
                    "Manufacturing output remnantClassification is not a concrete OpenAPI schema.");
            }

            concreteRemnant.Description = "Optional. Only with outputClass UsableRemnant: the label classification "
                + "the operator chose, exactly Standard or SecondQuality. Absent or null opens a SecondQuality label. "
                + "The label always carries the separate UsableRemnant production status.";
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

public sealed class ManufacturingPlanAllocationConflictProblemContract
{
    public required string Type { get; init; }
    public required string Title { get; init; }
    public required int Status { get; init; }
    public required string Detail { get; init; }
    public required string Code { get; init; }
    public string? CorrelationId { get; init; }
    public ErpVersionConflictContract? Conflict { get; init; }
    public ManufacturingPlanAllocationShortfallV1? PlanAllocationShortfall { get; init; }
}
