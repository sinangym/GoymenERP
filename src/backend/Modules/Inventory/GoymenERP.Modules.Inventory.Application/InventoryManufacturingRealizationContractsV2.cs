using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>
/// B-03 (API-22/27, DEC-PRDCT-003/007, URT-D8/URT-D14): the V2 Manufacturing realization participant. Input issue and
/// unused-input return keep their V1 shapes and behaviour. The output declaration takes a V2 request and maps the
/// Manufacturing output class to an Inventory label classification before anything is written; a class without a
/// stockable label classification is refused. V1 stays available unchanged until its consumer moves to V2.
/// STK-S9 (BS-5/BS-14): a <c>UsableRemnant</c> output is stockable; its label classification is the one the operator
/// chose in production (Standard or SecondQuality, default SecondQuality) and the label carries the separate
/// production status <see cref="InventoryLabelProductionStatusV1.UsableRemnant"/>.
/// Both versions record the output label's Manufacturing genealogy (kind 3) to the order's net-issued input labels.
/// </summary>
public interface IInventoryManufacturingRealizationParticipantV2 : IAsyncDisposable
{
    Task<InventoryManufacturingInputIssueResultV1> StageInputIssueAsync(
        InventoryManufacturingInputIssueRequestV1 request,
        InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    Task<InventoryManufacturingInputReturnResultV1> StageInputReturnAsync(
        InventoryManufacturingInputReturnRequestV1 request,
        InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    Task<InventoryManufacturingOutputResultV2> StageOutputAsync(
        InventoryManufacturingOutputRequestV2 request,
        InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The V1 output request with the same fields. <see cref="OutputClass"/> must be a Manufacturing output class token
/// that has a stockable label classification (see <see cref="InventoryManufacturingOutputClassificationV2"/>).
/// <para>
/// STK-S9 (BS-14): <see cref="RemnantClassification"/> is the label classification the operator selected for a
/// <c>UsableRemnant</c> output: Standard or SecondQuality. Null means the production default, SecondQuality. Any other
/// output class must leave it null; its classification follows the fixed URT-D43 mapping.
/// </para>
/// </summary>
public sealed record InventoryManufacturingOutputRequestV2(
    Guid CompanyId,
    Guid ManufacturingOrderId,
    Guid OutputId,
    string ManufacturingOrderVersionAfter,
    Guid ParentLabelId,
    string ExpectedParentLabelVersion,
    string OutputClass,
    Guid ProductId,
    string ExpectedProductVersion,
    Guid LotId,
    Guid LabelId,
    string LabelCode,
    string? HeatNumber,
    string? CoilNumber,
    IReadOnlyList<string> RequiredDocuments,
    StockPartyKind OwnerKind,
    Guid OwnerId,
    StockPartyKind CustodianKind,
    Guid CustodianId,
    Guid LocationId,
    BlockState InitialBlock,
    Guid MovementId,
    string QuantityKg,
    DateTimeOffset OccurredAtUtc,
    InventoryLabelClassificationV1? RemnantClassification = null,
    int? PieceCount = null)
{
    public InventoryManufacturingOutputRequestV1 ToV1() => new(CompanyId, ManufacturingOrderId, OutputId,
        ManufacturingOrderVersionAfter, ParentLabelId, ExpectedParentLabelVersion, OutputClass, ProductId,
        ExpectedProductVersion, LotId, LabelId, LabelCode, HeatNumber, CoilNumber, RequiredDocuments, OwnerKind,
        OwnerId, CustodianKind, CustodianId, LocationId, InitialBlock, MovementId, QuantityKg, OccurredAtUtc, PieceCount);
}

/// <summary>
/// The V1 output result plus the label classification Inventory created and the Manufacturing genealogy it
/// recorded. A replay returns the stored edges; Inventory never re-derives them.
/// <para>
/// STK-S9 (BS-5): <see cref="ProductionStatus"/> is the production status the output label was born with:
/// <see cref="InventoryLabelProductionStatusV1.UsableRemnant"/> for a usable remnant, otherwise null.
/// </para>
/// </summary>
public sealed record InventoryManufacturingOutputResultV2(
    Guid CompanyId,
    Guid ManufacturingOrderId,
    Guid OutputId,
    string ManufacturingOrderVersion,
    Guid ParentLabelId,
    string ParentLabelVersion,
    string OutputClass,
    InventoryLabelClassificationV1 Classification,
    InventoryLabelProductionStatusV1? ProductionStatus,
    Guid ProductId,
    string ProductVersion,
    Guid LotId,
    Guid LabelId,
    string LabelVersion,
    Guid MovementId,
    string QuantityKg,
    Guid GenealogyTransformationId,
    IReadOnlyList<InventoryManufacturingGenealogyInputV2> GenealogyInputs,
    bool IsReplay,
    string OriginalCorrelationId);

/// <summary>One input label of the output's genealogy: its version and net issued kilograms at output time.</summary>
public sealed record InventoryManufacturingGenealogyInputV2(Guid LabelId, string LabelVersion, string QuantityKg);

public static class InventoryManufacturingRealizationCodesV2
{
    /// <summary>
    /// B-03: the output class has no stockable label classification (ProcessLoss or an unknown token).
    /// STK-S9 (BS-5/BS-14): UsableRemnant is stockable and no longer refused with this code.
    /// </summary>
    public const string OutputClassNotStockable = "inventory_manufacturing_output_class_not_stockable";

    /// <summary>
    /// STK-S9 (BS-14): the remnant classification is not one a usable remnant may take (only Standard or
    /// SecondQuality), or it was sent with an output class other than UsableRemnant.
    /// </summary>
    public const string RemnantClassificationInvalid = "inventory_manufacturing_remnant_classification_invalid";

    public static bool IsDefined(int status, string code) =>
        status == 400 && code is OutputClassNotStockable or RemnantClassificationInvalid
        || InventoryManufacturingRealizationCodesV1.IsDefined(status, code);
}

/// <summary>
/// DEC-PRDCT-007 / URT-D14 / URT-D43: Manufacturing output class token to Inventory label classification. The tokens
/// are the Manufacturing output class names, compared ordinally. <see cref="TryMap"/> is the fixed 1:1 mapping of the
/// classes whose classification does not depend on an operator choice; <see cref="UsableRemnant"/> is resolved by
/// <see cref="TryResolveRemnant"/> (BS-14).
/// </summary>
public static class InventoryManufacturingOutputClassificationV2
{
    public const string GoodProduct = "GoodProduct";
    public const string SecondQuality = "SecondQuality";
    public const string Scrap = "Scrap";
    public const string UsableRemnant = "UsableRemnant";

    /// <summary>BS-14: the classification a usable remnant takes when the operator did not choose another one.</summary>
    public const InventoryLabelClassificationV1 DefaultRemnantClassification = InventoryLabelClassificationV1.SecondQuality;

    /// <summary>
    /// BS-14: a usable remnant takes the operator's choice, Standard or SecondQuality, or
    /// <see cref="DefaultRemnantClassification"/> when none was sent. Scrap or an undefined value is refused.
    /// </summary>
    public static bool TryResolveRemnant(InventoryLabelClassificationV1? selected,
        out InventoryLabelClassificationV1 classification)
    {
        classification = selected ?? DefaultRemnantClassification;
        return classification is InventoryLabelClassificationV1.Standard or InventoryLabelClassificationV1.SecondQuality;
    }

    public static bool TryMap(string? outputClass, out InventoryLabelClassificationV1 classification)
    {
        classification = outputClass switch
        {
            GoodProduct => InventoryLabelClassificationV1.Standard,
            SecondQuality => InventoryLabelClassificationV1.SecondQuality,
            Scrap => InventoryLabelClassificationV1.Scrap,
            _ => default,
        };
        return classification != default;
    }
}

public static class InventoryManufacturingRealizationContractV2
{
    public const string OutputCommandName = "inventory.manufacturing-output-declaration.stage.v2";

    /// <summary>
    /// Runs the V1 output validation and then maps the output class. Every refusal happens before any read or write.
    /// A usable remnant resolves to the operator's classification (BS-14) and the UsableRemnant production status
    /// (BS-5); every other stockable class keeps its fixed classification and no production status.
    /// </summary>
    public static (Kilograms Quantity, InventoryLabelClassificationV1 Classification,
        InventoryLabelProductionStatusV1? ProductionStatus) Validate(
        InventoryManufacturingOutputRequestV2 request, InventoryCommandMetadata metadata, Guid enlistedCompanyId)
    {
        ArgumentNullException.ThrowIfNull(request);
        var quantity = InventoryManufacturingRealizationContractV1.Validate(request.ToV1(), metadata,
            enlistedCompanyId);
        if (string.Equals(request.OutputClass, InventoryManufacturingOutputClassificationV2.UsableRemnant,
                StringComparison.Ordinal))
        {
            if (!InventoryManufacturingOutputClassificationV2.TryResolveRemnant(request.RemnantClassification,
                    out var remnantClassification))
            {
                throw InventoryManufacturingRealizationException.RefusedV2(400,
                    InventoryManufacturingRealizationCodesV2.RemnantClassificationInvalid);
            }

            return (quantity, remnantClassification, InventoryLabelProductionStatusV1.UsableRemnant);
        }

        if (!InventoryManufacturingOutputClassificationV2.TryMap(request.OutputClass, out var classification))
        {
            throw InventoryManufacturingRealizationException.RefusedV2(400,
                InventoryManufacturingRealizationCodesV2.OutputClassNotStockable);
        }

        if (request.RemnantClassification is not null)
        {
            throw InventoryManufacturingRealizationException.RefusedV2(400,
                InventoryManufacturingRealizationCodesV2.RemnantClassificationInvalid);
        }

        return (quantity, classification, ProductionStatus: null);
    }
}
