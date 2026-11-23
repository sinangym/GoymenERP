using System.Diagnostics.CodeAnalysis;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Manufacturing.Domain.Orders;

namespace GoymenERP.Modules.Manufacturing.Application;

/// <summary>
/// The stock label a Manufacturing output opens with: the Inventory output-class token, the label classification and
/// the separate label production status (BS-5). <see cref="ProductionStatus"/> is null for every class except
/// <see cref="OutputClass.UsableRemnant"/>.
/// </summary>
public sealed record ManufacturingOutputLabelV1(
    string InventoryOutputClass,
    InventoryLabelClassificationV1 Classification,
    InventoryLabelProductionStatusV1? ProductionStatus);

/// <summary>
/// URT-S16 (URT-D43, BS-5, BS-14): the fixed Manufacturing output class to stock label mapping. It is the same for every
/// company and has no settings table (URT-D43 (1)); Manufacturing holds it because the choice is made in the
/// production output declaration (BS-14, URT-D43 (4)).
/// <list type="bullet">
/// <item><see cref="OutputClass.GoodProduct"/> opens a <c>Standard</c> label.</item>
/// <item><see cref="OutputClass.SecondQuality"/> opens a <c>SecondQuality</c> label.</item>
/// <item><see cref="OutputClass.Scrap"/> opens a <c>Scrap</c> label.</item>
/// <item><see cref="OutputClass.UsableRemnant"/> opens a label with the classification the operator chose (only
/// <c>Standard</c> or <c>SecondQuality</c>; <see cref="DefaultRemnantClassification"/> when none was chosen) and the
/// <c>UsableRemnant</c> production status (BS-5: the classification itself is unchanged, the status is separate).</item>
/// <item><see cref="OutputClass.ProcessLoss"/> is not stock and has no label; it is refused (fail-closed).</item>
/// </list>
/// A remnant classification sent with any class other than <see cref="OutputClass.UsableRemnant"/> is refused.
/// The host checks Inventory's echoed classification and production status against this mapping before it commits.
/// </summary>
public static class ManufacturingOutputClassMappingV1
{
    /// <summary>BS-14: a usable remnant's label classification when the operator did not choose one.</summary>
    public const InventoryLabelClassificationV1 DefaultRemnantClassification =
        InventoryLabelClassificationV1.SecondQuality;

    /// <summary>
    /// Parses the operator's remnant classification token: null stays null (the default applies), otherwise only the
    /// ordinal names <c>Standard</c> and <c>SecondQuality</c> are accepted.
    /// </summary>
    public static bool TryParseRemnantClassification(string? token,
        out InventoryLabelClassificationV1? classification)
    {
        classification = token switch
        {
            null => null,
            nameof(InventoryLabelClassificationV1.Standard) => InventoryLabelClassificationV1.Standard,
            nameof(InventoryLabelClassificationV1.SecondQuality) => InventoryLabelClassificationV1.SecondQuality,
            _ => null,
        };
        return token is null || classification is not null;
    }

    /// <summary>
    /// Maps an output class (and, for a usable remnant, the operator's choice) to the label it opens. Returns false for
    /// a class with no stock label, an undefined class, a remnant classification outside Standard/SecondQuality, or a
    /// remnant classification sent with a class other than <see cref="OutputClass.UsableRemnant"/>.
    /// </summary>
    public static bool TryMap(OutputClass outputClass, InventoryLabelClassificationV1? remnantClassification,
        [NotNullWhen(true)] out ManufacturingOutputLabelV1? label)
    {
        label = null;
        if (outputClass == OutputClass.UsableRemnant)
        {
            var chosen = remnantClassification ?? DefaultRemnantClassification;
            if (chosen is not (InventoryLabelClassificationV1.Standard or InventoryLabelClassificationV1.SecondQuality))
            {
                return false;
            }

            label = new ManufacturingOutputLabelV1(outputClass.ToString(), chosen,
                InventoryLabelProductionStatusV1.UsableRemnant);
            return true;
        }

        if (remnantClassification is not null)
        {
            return false;
        }

        InventoryLabelClassificationV1? classification = outputClass switch
        {
            OutputClass.GoodProduct => InventoryLabelClassificationV1.Standard,
            OutputClass.SecondQuality => InventoryLabelClassificationV1.SecondQuality,
            OutputClass.Scrap => InventoryLabelClassificationV1.Scrap,
            _ => null,
        };
        if (classification is not { } stockable)
        {
            return false;
        }

        label = new ManufacturingOutputLabelV1(outputClass.ToString(), stockable, ProductionStatus: null);
        return true;
    }
}
