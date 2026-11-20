import type { ReturnSourceSelection, SupplierReturnCostAllocation, SupplierReturnSourceLabel } from './supplierReturnSourceContract'
import { RECEIPT_ADJUSTMENT_TEXT as T } from './receiptAdjustmentText'

/** Display the frozen command/receipt allocation without deriving quantities or monetary values. */
export function SupplierReturnSourceSummary({ allocation, labels = [] }: {
  readonly allocation: SupplierReturnCostAllocation
  readonly labels?: readonly SupplierReturnSourceLabel[]
}) {
  function share(value: ReturnSourceSelection) {
    return value.takeAll ? value.undoQuantity === null ? T.selectedWholeSource : `${T.selectedWholeKg}; ${T.purchaseQuantity}: ${value.undoQuantity}`
      : `${T.measuredKg}: ${value.quantityKg}; ${T.purchaseQuantity}: ${value.undoQuantity}`
  }
  return <section aria-label={T.selectedSources}>
    <h5>{T.selectedSources}</h5>
    <ul>
      {allocation.pending.takeAll || allocation.pending.quantityKg !== '0' || allocation.pending.undoQuantity !== '0'
        ? <li>{T.pendingSource}: {share(allocation.pending)}</li> : null}
      {allocation.invoicedSources.map(source => {
        const label = labels.find(item => item.sourceEventId.toLowerCase() === source.sourceEventId.toLowerCase())
        return <li key={source.sourceEventId}>{label === undefined ? `${T.sourceReference}: ${source.sourceEventId}` : T.invoiceSource(label.invoiceNumber, label.invoiceLineNo)}: {share(source)}</li>
      })}
    </ul>
  </section>
}
