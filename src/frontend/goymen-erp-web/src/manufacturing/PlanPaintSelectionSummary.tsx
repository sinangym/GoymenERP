import type { PlanProcessSpecView } from './contract'
import { PAINT_SELECTION_TEXT as T } from './paintSelectionText'

/** Stored owner references only. Reading history never substitutes a master's current version for the chosen one. */
export function PlanPaintSelectionSummary({ value }: { value: PlanProcessSpecView | null | undefined }) {
  const selection = value?.paintSelection
  if (selection === undefined) return null
  return <dl aria-label={T.heading}>
    <div><dt>{T.product}</dt><dd>{selection.productId} · {T.productVersion} {selection.productVersion} · {T.membershipVersion} {selection.membershipVersion}</dd></div>
    <div><dt>{T.supplier}</dt><dd>{selection.supplierPartyId} / {selection.supplierCompanyAccountId} · {T.sourceVersions} {selection.supplierPartyVersion} / {selection.supplierAccountVersion}</dd></div>
    <div><dt>{T.color}</dt><dd>{selection.colorId === null ? T.none : `${selection.colorId} · ${T.sourceVersions} ${selection.colorVersion}`}</dd></div>
    {value?.paintColorCode == null ? null : <div><dt>{T.note}</dt><dd>{value.paintColorCode}</dd></div>}
  </dl>
}
