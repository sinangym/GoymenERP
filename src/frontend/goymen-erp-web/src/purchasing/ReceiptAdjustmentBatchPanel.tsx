import { useId, useState } from 'react'
import type { PurchaseOrderDetail } from './contract'
import { receiptQuantityInput, type GoodsReceipt } from './goodsReceiptContract'
import { receiptIdentity } from './goodsReceiptAttempt'
import { reverseReceiptBatch } from './receiptAdjustmentBatchApi'
import type { ReceiptReversalBatchRequest, ReceiptReversalBatchResponse } from './receiptAdjustmentBatchContract'
import { useReceiptAdjustmentAttempt } from './useReceiptAdjustmentAttempt'
import { RECEIPT_BATCH_ADJUSTMENT_TEXT as T } from './receiptAdjustmentBatchText'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome } from '../shell/uiState'
import { Button } from '../ui/Button'
import styles from './purchasing.module.css'

interface Props {
  readonly detail: PurchaseOrderDetail; readonly receipts: readonly GoodsReceipt[]; readonly binding: string
  readonly attemptBinding: string; readonly onSettled: () => void
}
interface Row { readonly selected: boolean; readonly quantity: string; readonly kg: string; readonly block: 'Free' | 'Blocked'
  readonly correction: boolean; readonly nextQuantity: string; readonly nextKg: string; readonly label: string }
export function ReceiptAdjustmentBatchPanel(props: Props) {
  const id = useId(), [rows, setRows] = useState<Record<string, Row>>({}), [reason, setReason] = useState(''), [invalid, setInvalid] = useState(false)
  const attempt = useReceiptAdjustmentAttempt<ReceiptReversalBatchRequest, ReceiptReversalBatchResponse>(
    JSON.stringify([props.attemptBinding, 'receipt-adjustment-batch', props.detail.order.id]), props.binding,
    (payload, key) => reverseReceiptBatch(props.detail.order.companyId, props.detail.order.id, payload, key), props.onSettled)
  const row = (receipt: GoodsReceipt): Row => rows[receipt.acceptanceId] ?? { selected: false, quantity: '', kg: '', block: receipt.initialBlock,
    correction: false, nextQuantity: '', nextKg: '', label: '' }
  const update = (receipt: GoodsReceipt, patch: Partial<Row>) => setRows((current) => ({ ...current, [receipt.acceptanceId]: { ...row(receipt), ...patch } }))
  function submit(event: React.FormEvent) {
    event.preventDefault(); if (attempt.locked) return
    const selected = props.receipts.filter((receipt) => row(receipt).selected)
    if (selected.length < 1 || selected.length > 100 || reason.trim() === '' || reason.trim().length > 1000) { setInvalid(true); return }
    const occurredAtUtc = new Date().toISOString(), lines: ReceiptReversalBatchRequest['lines'][number][] = []
    for (const receipt of selected) {
      const values = row(receipt), undoQuantity = receiptQuantityInput(values.quantity), measuredUndoQuantityKg = receiptQuantityInput(values.kg)
      const nextQuantity = receiptQuantityInput(values.nextQuantity), nextKg = receiptQuantityInput(values.nextKg)
      if (undoQuantity === null || measuredUndoQuantityKg === null || values.correction && (nextQuantity === null || nextKg === null
        || values.label.trim() === '' || values.label.trim().length > 64)) { setInvalid(true); return }
      lines.push({ acceptanceId: receipt.acceptanceId, adjustmentId: receiptIdentity(), reversalLineId: receiptIdentity(), inventoryMovementId: receiptIdentity(),
        undoQuantity, measuredUndoQuantityKg, sourceBlock: values.block, replacementReceipt: values.correction ? {
          expectedPurchaseOrderVersion: props.detail.order.version, expectedProductVersion: receipt.productVersion, acceptanceId: receiptIdentity(),
          acceptedQuantity: nextQuantity!, measuredQuantityKg: nextKg!, locationId: receipt.locationId, lotId: receiptIdentity(), labelId: receiptIdentity(),
          labelCode: values.label.trim(), heatNumber: null, coilNumber: null, movementId: receiptIdentity(), occurredAtUtc } : null })
    }
    setInvalid(false); attempt.submit({ batchId: receiptIdentity(), expectedPurchaseOrderVersion: props.detail.order.version,
      reason: reason.trim(), occurredAtUtc, lines })
  }
  const outcome = attempt.outcome
  return <section aria-labelledby={`${id}-heading`}><h3 id={`${id}-heading`}>{T.heading}</h3><p>{T.note}</p>
    {attempt.sending ? <p role="status">{T.sending}</p> : null}
    {!attempt.sending && attempt.snapshot !== null ? <div role="alert"><p>{T.unknown}</p><Button type="button" onClick={attempt.retry}>{T.retry}</Button></div> : null}
    {attempt.snapshot !== null ? <div><p>{T.pending}</p>{(attempt.snapshot.payload as ReceiptReversalBatchRequest).lines.map((line) =>
      <p key={line.acceptanceId}>{T.original}: {line.acceptanceId}; {T.quantity}: {line.undoQuantity}; {T.kg}: {line.measuredUndoQuantityKg}
        {line.replacementReceipt == null ? null : <>; {T.label}: {line.replacementReceipt.labelCode}; {T.nextQuantity}: {line.replacementReceipt.acceptedQuantity}; {T.nextKg}: {line.replacementReceipt.measuredQuantityKg}</>}</p>)}</div> : null}
    {outcome?.type === 'failed' ? <StateBlock subject={T.heading} state={classifyOutcome({ type: 'http', status: outcome.status, body: { code: outcome.code } })} /> : null}
    {outcome?.type === 'unknown' ? <StateBlock subject={T.heading} state={classifyOutcome(outcome.reason === 'service-unavailable'
      ? { type: 'http', status: outcome.status ?? 503, body: null } : { type: outcome.reason })} /> : null}
    {outcome?.type === 'applied' ? <div role="status"><p>{outcome.response.isReplay ? T.replayed : T.saved}</p><p>{T.version}: {outcome.response.orderVersion}</p>
      {outcome.response.supplies.map((fact) => <p key={fact.generalLineId}>{T.general}: {fact.generalLineId}; {T.opened}: {fact.netOpenedQuantity}; {T.allocatable}: {fact.allocatableQuantity}</p>)}</div> : null}
    <form onSubmit={submit} noValidate className={styles.createForm}><fieldset disabled={attempt.locked}>
      <legend>{T.heading}</legend>{props.receipts.map((receipt) => { const value = row(receipt), prefix = `${id}-${receipt.acceptanceId}`; return <fieldset key={receipt.acceptanceId}>
        <legend>{receipt.labelCode}</legend><label htmlFor={`${prefix}-selected`}>{T.choose}</label><input id={`${prefix}-selected`} type="checkbox" checked={value.selected} onChange={(e) => update(receipt, { selected: e.target.checked })} />
        {value.selected ? <div className={styles.formGrid}>
          <label htmlFor={`${prefix}-quantity`}>{T.quantity}</label><input id={`${prefix}-quantity`} inputMode="decimal" value={value.quantity} onChange={(e) => update(receipt, { quantity: e.target.value })} />
          <label htmlFor={`${prefix}-kg`}>{T.kg}</label><input id={`${prefix}-kg`} inputMode="decimal" value={value.kg} onChange={(e) => update(receipt, { kg: e.target.value })} />
          <label htmlFor={`${prefix}-block`}>{T.block}</label><select id={`${prefix}-block`} value={value.block} onChange={(e) => update(receipt, { block: e.target.value as Row['block'] })}><option value="Free">{T.free}</option><option value="Blocked">{T.blocked}</option></select>
          <label htmlFor={`${prefix}-correction`}>{T.correction}</label><input id={`${prefix}-correction`} type="checkbox" checked={value.correction} onChange={(e) => update(receipt, { correction: e.target.checked })} />
          {value.correction ? <><label htmlFor={`${prefix}-next-quantity`}>{T.nextQuantity}</label><input id={`${prefix}-next-quantity`} inputMode="decimal" value={value.nextQuantity} onChange={(e) => update(receipt, { nextQuantity: e.target.value })} />
            <label htmlFor={`${prefix}-next-kg`}>{T.nextKg}</label><input id={`${prefix}-next-kg`} inputMode="decimal" value={value.nextKg} onChange={(e) => update(receipt, { nextKg: e.target.value })} />
            <label htmlFor={`${prefix}-label`}>{T.label}</label><input id={`${prefix}-label`} maxLength={64} value={value.label} onChange={(e) => update(receipt, { label: e.target.value })} /><p>{T.location}</p></> : null}
        </div> : null}</fieldset> })}
      <label htmlFor={`${id}-reason`}>{T.reason}</label><textarea id={`${id}-reason`} maxLength={1000} value={reason} onChange={(e) => setReason(e.target.value)} />
      {invalid ? <p role="alert">{T.invalid}</p> : null}<Button type="submit">{T.save}</Button>
    </fieldset></form>
  </section>
}
