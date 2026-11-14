using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>
/// B-03 (API-22/27, DEC-PRDCT-003/007, URT-D8/URT-D14): the V2 Manufacturing realization participant. Input issue and
/// unused-input return keep their V1 shapes and behaviour. The output declaration takes a V2 request and maps the
/// Manufacturing output class to an Inventory label classification before anything is written; a class without a
/// stockable label classification is refused. V1 stays available unchanged until its consumer moves to V2.
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
    DateTimeOffset OccurredAtUtc)
{
    public InventoryManufacturingOutputRequestV1 ToV1() => new(CompanyId, ManufacturingOrderId, OutputId,
        ManufacturingOrderVersionAfter, ParentLabelId, ExpectedParentLabelVersion, OutputClass, ProductId,
        ExpectedProductVersion, LotId, LabelId, LabelCode, HeatNumber, CoilNumber, RequiredDocuments, OwnerKind,
        OwnerId, CustodianKind, CustodianId, LocationId, InitialBlock, MovementId, QuantityKg, OccurredAtUtc);
}

/// <summary>
/// The V1 output result plus the label classification Inventory created and the Manufacturing genealogy it
/// recorded. A replay returns the stored edges; Inventory never re-derives them.
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
    /// B-03: the output class has no stockable label classification (ProcessLoss, UsableRemnant or an unknown token).
    /// UsableRemnant stays fail-closed until its production status field is decided.
    /// </summary>
    public const string OutputClassNotStockable = "inventory_manufacturing_output_class_not_stockable";

    public static bool IsDefined(int status, string code) =>
        status == 400 && code == OutputClassNotStockable
        || InventoryManufacturingRealizationCodesV1.IsDefined(status, code);
}

/// <summary>
/// DEC-PRDCT-007 / URT-D14: Manufacturing output class token to Inventory label classification. The tokens are the
/// Manufacturing output class names, compared ordinally.
/// </summary>
public static class InventoryManufacturingOutputClassificationV2
{
    public const string GoodProduct = "GoodProduct";
    public const string SecondQuality = "SecondQuality";
    public const string Scrap = "Scrap";

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
    /// </summary>
    public static (Kilograms Quantity, InventoryLabelClassificationV1 Classification) Validate(
        InventoryManufacturingOutputRequestV2 request, InventoryCommandMetadata metadata, Guid enlistedCompanyId)
    {
        ArgumentNullException.ThrowIfNull(request);
        var quantity = InventoryManufacturingRealizationContractV1.Validate(request.ToV1(), metadata,
            enlistedCompanyId);
        if (!InventoryManufacturingOutputClassificationV2.TryMap(request.OutputClass, out var classification))
        {
            throw InventoryManufacturingRealizationException.RefusedV2(400,
                InventoryManufacturingRealizationCodesV2.OutputClassNotStockable);
        }

        return (quantity, classification);
    }
}
