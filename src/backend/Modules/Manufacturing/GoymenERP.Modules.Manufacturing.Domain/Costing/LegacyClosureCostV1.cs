using GoymenERP.Modules.Manufacturing.Domain.Orders;

namespace GoymenERP.Modules.Manufacturing.Domain.Costing;

/// <summary>
/// The cost of one order closed with output above input (URT-D52 (1)): always
/// <see cref="ProductionReceiptCostStatus.Unreadable"/> with <see cref="ProductionReceiptCostCodesV1.OutputExceedsInput"/>
/// and its Turkish text, "hesaplanamadı — çıkan giren kg'dan fazla". It carries no amount, never zero.
/// </summary>
public sealed record LegacyClosureCostStateV1(ProductionReceiptCostStatus Status, string Code, string Text);

/// <summary>
/// URT-D52 (SINAN_KARARLARI_2026-11-14_URETIM_EK4; it completes URT-D42 (3) and (5)): an order that a close stored before
/// URT-D41 let close with output above input has no production cost. Its cost shows "hesaplanamadı — çıkan giren
/// kg'dan fazla" (1) and it stays in the URT-D42 (3) report list; the order is not reopened, re-assessed or changed (3).
/// The rule is the close's own: output above input on any planned parent roll for a roll-based process, on the order
/// total for a process without a main roll input (<see cref="ProductionOrder.HasOutputAboveInput"/>, URT-D41 (1),
/// URT-D42 (1), URT-D49). A close under URT-D41 refuses such an order, so only an earlier close can be one; this type
/// reads the order's stored facts and changes nothing. That a weighing correction (URT-D53) can fix the order later is
/// URT-D52 (2); whether that needs the closed order to be reopened was not asked and is not decided here (NEEDS-SINAN,
/// URT-D52 (3)), so nothing here reopens or corrects an order.
/// </summary>
public static class LegacyClosureCostV1
{
    /// <summary>True for a closed order with output above input where the close measures it.</summary>
    public static bool IsOutputAboveInputClosure(ProductionOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);
        return order.Status == OrderStatus.Closed && order.HasOutputAboveInput();
    }

    /// <summary>
    /// The cost input with <see cref="ProductionReceiptCostInput.ClosedWithOutputAboveInput"/> measured on the order the
    /// input was built from, so the calculator answers <c>output_exceeds_input</c> for it even where the order totals
    /// alone would balance (a roll above input beside a short one; rolls never offset each other, URT-D42 (1)). A mark
    /// already set is kept: this only ever adds the refusal, never removes it.
    /// </summary>
    public static ProductionReceiptCostInput WithClosureMeasure(ProductionReceiptCostInput input, ProductionOrder order)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(order);
        return input with
        {
            ClosedWithOutputAboveInput = input.ClosedWithOutputAboveInput || IsOutputAboveInputClosure(order),
        };
    }

    /// <summary>
    /// The URT-D52 (1) cost state of the order, or null when the order is not a closed order with output above input
    /// (its cost is then the calculator's answer on its facts).
    /// </summary>
    public static LegacyClosureCostStateV1? StateOf(ProductionOrder order) =>
        IsOutputAboveInputClosure(order)
            ? new LegacyClosureCostStateV1(ProductionReceiptCostStatus.Unreadable,
                ProductionReceiptCostCodesV1.OutputExceedsInput, LegacyClosureCostTextsV1.OutputExceedsInput)
            : null;
}
