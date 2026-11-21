import { useEffect, useId, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome } from '../shell/uiState'
import { Button } from '../ui/Button'
import { receiptQuantityInput } from './goodsReceiptContract'
import { fetchSupplierReturnCostSources } from './supplierReturnSourceApi'
import type { ExactSourceQuantity, ReturnSourceSelection, SupplierReturnCostAllocation, SupplierReturnCostSources } from './supplierReturnSourceContract'
import { RECEIPT_ADJUSTMENT_TEXT as T } from './receiptAdjustmentText'

interface Props {
  readonly companyId: string; readonly orderId: string; readonly acceptanceId: string; readonly labelId: string
  readonly binding: string; readonly onChange: (allocation: SupplierReturnCostAllocation | null) => void
}
export function SupplierReturnSourceAllocation(props: Props) {
  const boundary = useAuthBoundary()
  const { onChange } = props
  const sources = useQuery({ queryKey: [props.binding, 'supplier-return-cost-sources', props.orderId, props.acceptanceId],
    queryFn: async ({ signal }) => {
      const captured = boundary.capture()
      const result = await fetchSupplierReturnCostSources(props.companyId, props.orderId, props.acceptanceId, props.labelId, signal)
      if (result.type === 'http' && (result.status === 401 || result.status === 403)) boundary.report(result.status, captured)
      return result
    } })
  const data = sources.data?.type === 'ok-single' ? sources.data.data : null
  useEffect(() => { if (data === null) onChange(null) }, [data, onChange])
  return <section aria-label={T.sourceHeading}>
    <h5>{T.sourceHeading}</h5><p>{T.sourceHelp}</p>
    {data === null ? <StateBlock subject={T.sourceHeading} state={sources.data === undefined ? { kind: 'initial-loading' } : classifyOutcome(sources.data)} onRetry={() => void sources.refetch()} />
      : <SourceFields key={data.sourceVersion} data={data} onChange={props.onChange} />}
    {data !== null ? <Button type="button" variant="secondary" disabled={sources.isFetching} onClick={() => void sources.refetch()}>{T.refreshSources}</Button> : null}
  </section>
}
interface Draft { readonly selected: boolean; readonly takeAll: boolean; readonly kg: string; readonly quantity: string }
const empty: Draft = { selected: false, takeAll: false, kg: '', quantity: '' }
const zero: ReturnSourceSelection = { takeAll: false, quantityKg: '0', undoQuantity: '0' }
function selection(draft: Draft, wholePairAvailable: boolean): ReturnSourceSelection | null {
  if (!draft.selected) return zero
  if (draft.takeAll) {
    if (wholePairAvailable) return { takeAll: true, quantityKg: null, undoQuantity: null }
    const quantity = receiptQuantityInput(draft.quantity)
    return quantity === null ? null : { takeAll: true, quantityKg: null, undoQuantity: quantity }
  }
  const kg = receiptQuantityInput(draft.kg), quantity = receiptQuantityInput(draft.quantity)
  return kg === null || quantity === null ? null : { takeAll: false, quantityKg: kg, undoQuantity: quantity }
}
function SourceFields({ data, onChange }: { readonly data: SupplierReturnCostSources; readonly onChange: Props['onChange'] }) {
  const [drafts, setDrafts] = useState<Readonly<Record<string, Draft>>>({})
  useEffect(() => {
    const pending = selection(drafts.pending ?? empty, data.pendingWholePairAvailable)
    const selected = data.invoicedSources.filter(source => drafts[source.sourceEventId]?.selected)
    const invoicedSources: SupplierReturnCostAllocation['invoicedSources'][number][] = []
    if (pending === null || !(drafts.pending?.selected || selected.length > 0)) { onChange(null); return }
    for (const source of selected) {
      const share = selection(drafts[source.sourceEventId], source.wholePairAvailable)
      if (share === null) { onChange(null); return }
      invoicedSources.push({ sourceEventId: source.sourceEventId, ...share })
    }
    onChange({ expectedSourceVersion: data.sourceVersion, pending, invoicedSources })
  }, [data, drafts, onChange])
  function entry(key: string, title: string, kg: ExactSourceQuantity, quantity: ExactSourceQuantity, wholePairAvailable: boolean) {
    return <SourceEntry key={key} title={title} availableKg={kg} availableQuantity={quantity} wholePairAvailable={wholePairAvailable} draft={drafts[key] ?? empty}
      onChange={draft => setDrafts(prior => ({ ...prior, [key]: draft }))} />
  }
  return <>
    {entry('pending', T.pendingSource, data.pendingQuantityKg, data.pendingUndoQuantity, data.pendingWholePairAvailable)}
    {data.invoicedSources.map(source => entry(source.sourceEventId, T.invoiceSource(source.invoiceNumber, source.invoiceLineNo),
      source.availableQuantityKg, source.availableUndoQuantity, source.wholePairAvailable))}
  </>
}
/** Exact source facts are displayed as supplied. No conversion, prorating or rounding takes place in the UI. */
const quantityText = (value: ExactSourceQuantity) => value.denominator === '1' ? value.numerator : `${value.numerator} / ${value.denominator}`
function SourceEntry({ title, availableKg, availableQuantity, wholePairAvailable, draft, onChange }: {
  readonly title: string; readonly availableKg: ExactSourceQuantity; readonly availableQuantity: ExactSourceQuantity
  readonly draft: Draft; readonly onChange: (value: Draft) => void
  readonly wholePairAvailable: boolean
}) {
  const id = useId()
  return <fieldset>
    <legend>{title}</legend>
    <p>{T.availableSourceKg}: {quantityText(availableKg)}; {T.availableSourceQuantity}: {quantityText(availableQuantity)}</p>
    <label><input type="checkbox" checked={draft.selected} onChange={event => onChange({ ...draft, selected: event.target.checked })} />{T.selectSource(title)}</label>
    {draft.selected ? <>
      <label><input type="checkbox" checked={draft.takeAll} onChange={event => onChange({ ...draft, takeAll: event.target.checked })} />{T.takeAllSource(title)}</label>
      {!wholePairAvailable ? <p>{T.purchaseQuantityNeedsEntry}</p> : null}
      {!draft.takeAll ? <>
        <label htmlFor={`${id}-kg`}>{T.sourceKg(title)}</label><input id={`${id}-kg`} inputMode="decimal" value={draft.kg} onChange={event => onChange({ ...draft, kg: event.target.value })} />
      </> : null}
      {!draft.takeAll || !wholePairAvailable ? <>
        <label htmlFor={`${id}-quantity`}>{T.sourceQuantity(title)}</label><input id={`${id}-quantity`} inputMode="decimal" value={draft.quantity} onChange={event => onChange({ ...draft, quantity: event.target.value })} />
      </> : null}
    </> : null}
  </fieldset>
}
