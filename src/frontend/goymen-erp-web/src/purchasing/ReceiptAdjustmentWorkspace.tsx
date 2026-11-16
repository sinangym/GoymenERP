import { useId, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import type { PurchaseOrderDetail } from './contract'
import { isPurchasingVersion } from './contract'
import { fetchGoodsReceiptOptions, fetchGoodsReceipts } from './goodsReceiptApi'
import { receiptQuantityInput, type GoodsReceipt } from './goodsReceiptContract'
import { fetchSupplierReturns } from './supplierReturnApi'
import type { SupplierReturn } from './supplierReturnContract'
import { createSupplierReturn, decideSupplierReturn, reverseReceipt, type AdjustmentOutcome } from './receiptAdjustmentApi'
import type { ReceiptReversalRequest, ReceiptReversalResponse, SupplierReturnDecision, SupplierReturnRequest, SupplierReturnResponse } from './receiptAdjustmentContract'
import { useReceiptAdjustmentAttempt } from './useReceiptAdjustmentAttempt'
import { receiptIdentity } from './goodsReceiptAttempt'
import { RECEIPT_ADJUSTMENT_TEXT as T } from './receiptAdjustmentText'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { classifyOutcome } from '../shell/uiState'
import { StateBlock } from '../shell/StateBlock'
import { Button } from '../ui/Button'
import styles from './purchasing.module.css'

interface Props {
  readonly detail: PurchaseOrderDetail; readonly binding: string; readonly attemptBinding: string
  readonly canAdjust: boolean; readonly canReturn: boolean; readonly canDecide: boolean; readonly onSettled: () => void
}
export function ReceiptAdjustmentWorkspace(props: Props) {
  const [open, setOpen] = useState(false), id = useId()
  if (!props.canAdjust && !props.canReturn && !props.canDecide) return null
  return <section aria-labelledby={`${id}-heading`} data-testid="receipt-adjustment-workspace">
    <h2 id={`${id}-heading`}>{T.heading}</h2><p>{T.note}</p>
    <Button type="button" variant="secondary" aria-expanded={open} aria-controls={`${id}-body`} onClick={() => setOpen(!open)}>{open ? T.hide : T.show}</Button>
    <div id={`${id}-body`}>{open ? <WorkspaceBody {...props} /> : null}</div>
  </section>
}
function WorkspaceBody(props: Props) {
  const boundary = useAuthBoundary(), company = props.detail.order.companyId, order = props.detail.order.id
  const receipts = useQuery({ queryKey: [props.binding, 'receipt-adjustment-sources', order, props.detail.order.version],
    enabled: props.canAdjust || props.canReturn, queryFn: async ({ signal }) => {
      const auth = boundary.capture(), result = await fetchGoodsReceipts(props.detail, signal)
      if (result.type === 'http') boundary.report(result.status, auth)
      return result
    } })
  const returns = useQuery({ queryKey: [props.binding, 'receipt-adjustment-pending-returns', order, props.detail.order.version],
    enabled: props.canDecide, queryFn: async ({ signal }) => {
      const auth = boundary.capture(), result = await fetchSupplierReturns(company, order, signal)
      if (result.type === 'http') boundary.report(result.status, auth)
      return result
    } })
  const refresh = () => { props.onSettled(); void receipts.refetch(); if (props.canDecide) void returns.refetch() }
  return <>
    {props.canAdjust || props.canReturn ? <>
      <h3>{T.receipts}</h3>
      {receipts.data?.type === 'ok-single' ? receipts.data.data.items.length === 0 ? <p>{T.empty}</p>
        : receipts.data.data.items.map((receipt) => <ReceiptOperation key={receipt.acceptanceId} {...props} receipt={receipt} onSettled={refresh} />)
        : <StateBlock subject={T.receipts} state={receipts.data === undefined ? { kind: 'initial-loading' } : classifyOutcome(receipts.data)} onRetry={() => void receipts.refetch()} />}
    </> : null}
    {props.canDecide ? <><h3>{T.returns}</h3>
      {returns.data?.type === 'ok-single' ? <>
        {returns.data.data.items.filter((item) => item.status === 'PendingCostException').length === 0 ? <p>{T.noPending}</p> : null}
        {returns.data.data.items.filter((item) => item.status === 'PendingCostException').map((item) =>
          <ReturnDecision key={item.returnId} {...props} item={item} onSettled={refresh} />)}
      </> : <StateBlock subject={T.returns} state={returns.data === undefined ? { kind: 'initial-loading' } : classifyOutcome(returns.data)} onRetry={() => void returns.refetch()} />}
    </> : null}
  </>
}

type Operation = { readonly mode: 'undo' | 'correction'; readonly request: ReceiptReversalRequest }
  | { readonly mode: 'return'; readonly request: SupplierReturnRequest }
type Answer = ReceiptReversalResponse | SupplierReturnResponse
function ReceiptOperation(props: Props & { readonly receipt: GoodsReceipt }) {
  const { receipt, detail } = props, id = useId(), boundary = useAuthBoundary()
  const [mode, setMode] = useState<'undo' | 'correction' | 'return'>(props.canAdjust ? 'undo' : 'return')
  const [quantity, setQuantity] = useState(''), [kg, setKg] = useState(''), [block, setBlock] = useState<'Free' | 'Blocked'>(receipt.initialBlock)
  const [reason, setReason] = useState(''), [cost, setCost] = useState(''), [replacement, setReplacement] = useState(false)
  const [nextQuantity, setNextQuantity] = useState(''), [nextKg, setNextKg] = useState(''), [label, setLabel] = useState('')
  const [location, setLocation] = useState(receipt.locationId), [invalid, setInvalid] = useState(false)
  const line = detail.lines.find((candidate) => candidate.lineId === receipt.purchaseOrderLineId)
  const options = useQuery({ queryKey: [props.binding, 'receipt-correction-locations', receipt.acceptanceId, detail.order.version],
    enabled: mode === 'correction' && line !== undefined && detail.order.status === 'Approved', queryFn: async ({ signal }) => {
      const auth = boundary.capture(), result = await fetchGoodsReceiptOptions(detail, line!, signal)
      if (result.type === 'http') boundary.report(result.status, auth)
      return result
    } })
  const locations = options.data?.type === 'ok-single' ? options.data.data.locations : []
  const attempt = useReceiptAdjustmentAttempt<Operation, Answer>(JSON.stringify([props.attemptBinding, 'receipt-adjustment', detail.order.id, receipt.acceptanceId]),
    props.binding, (payload, key) => payload.mode === 'return'
      ? createSupplierReturn(detail.order.companyId, detail.order.id, payload.request, key)
      : reverseReceipt(detail.order.companyId, detail.order.id, receipt.acceptanceId, payload.request, key), props.onSettled)
  function submit(event: React.FormEvent) {
    event.preventDefault()
    if (attempt.locked || (mode === 'return' ? !props.canReturn : !props.canAdjust)) return
    const undo = receiptQuantityInput(quantity), measured = receiptQuantityInput(kg)
    const newQuantity = receiptQuantityInput(nextQuantity), newKg = receiptQuantityInput(nextKg)
    const manualCost = cost.trim() === '' ? null : costInput(cost)
    if (undo === null || measured === null || reason.trim() === '' || reason.trim().length > 1000
      || mode === 'return' && cost.trim() !== '' && manualCost === null
      || mode === 'correction' && (newQuantity === null || newKg === null || label.trim() === '' || label.trim().length > 64
        || location !== receipt.locationId && !locations.some((item) => item.locationId === location))) { setInvalid(true); return }
    setInvalid(false)
    const at = new Date().toISOString()
    if (mode === 'return') {
      attempt.submit({ mode, request: { returnId: receiptIdentity(), returnLineId: receiptIdentity(), goodsAcceptanceId: receipt.acceptanceId,
        inventoryMovementId: receiptIdentity(), sourceBlock: block, quantityKg: measured, manualUnitCostTryPerKg: manualCost,
        replacementExpected: replacement, reason: reason.trim(), occurredAtUtc: at, expectedPurchaseOrderVersion: detail.order.version, undoQuantity: undo } })
    } else {
      attempt.submit({ mode, request: { expectedPurchaseOrderVersion: detail.order.version, adjustmentId: receiptIdentity(),
        reversalLineId: receiptIdentity(), inventoryMovementId: receiptIdentity(), undoQuantity: undo, measuredUndoQuantityKg: measured,
        sourceBlock: block, reason: reason.trim(), occurredAtUtc: at, replacementReceipt: mode === 'correction' ? {
          expectedPurchaseOrderVersion: detail.order.version, expectedProductVersion: receipt.productVersion,
          acceptanceId: receiptIdentity(), acceptedQuantity: newQuantity!, measuredQuantityKg: newKg!, locationId: location,
          lotId: receiptIdentity(), labelId: receiptIdentity(), labelCode: label.trim(), heatNumber: null, coilNumber: null,
          movementId: receiptIdentity(), occurredAtUtc: at } : null } })
    }
  }
  return <article className={styles.actionCard} aria-labelledby={`${id}-heading`}>
    <h4 id={`${id}-heading`}>{receipt.labelCode}</h4>
    <p>{T.originalQuantity}: {receipt.acceptedQuantity}; {T.originalKg}: {receipt.measuredQuantityKg}</p>
    <AttemptFeedback attempt={attempt} />
    {attempt.snapshot !== null ? <PendingOperationSummary operation={attempt.snapshot.payload as Operation} /> : null}
    {attempt.outcome?.type === 'applied' ? <AdjustmentResult answer={attempt.outcome.response} /> : null}
    <form className={styles.createForm} onSubmit={submit} noValidate><fieldset disabled={attempt.locked}>
      <legend>{T.mode}</legend><div className={styles.formGrid}>
        <label htmlFor={`${id}-mode`}>{T.mode}</label><select id={`${id}-mode`} value={mode} onChange={(e) => setMode(e.target.value as typeof mode)}>
          {props.canAdjust ? <><option value="undo">{T.undo}</option><option value="correction">{T.correction}</option></> : null}
          {props.canReturn ? <option value="return">{T.supplierReturn}</option> : null}
        </select>
        <label htmlFor={`${id}-quantity`}>{T.purchaseQuantity}</label><input id={`${id}-quantity`} inputMode="decimal" value={quantity} onChange={(e) => setQuantity(e.target.value)} />
        <label htmlFor={`${id}-kg`}>{T.measuredKg}</label><input id={`${id}-kg`} inputMode="decimal" value={kg} onChange={(e) => setKg(e.target.value)} />
        <label htmlFor={`${id}-block`}>{T.block}</label><select id={`${id}-block`} value={block} onChange={(e) => setBlock(e.target.value as typeof block)}>
          <option value="Free">{T.free}</option><option value="Blocked">{T.blocked}</option></select>
        <label htmlFor={`${id}-reason`}>{T.reason}</label><textarea id={`${id}-reason`} maxLength={1000} value={reason} onChange={(e) => setReason(e.target.value)} />
        {mode === 'return' ? <>
          <label htmlFor={`${id}-cost`}>{T.manualCost}</label><input id={`${id}-cost`} inputMode="decimal" value={cost} onChange={(e) => setCost(e.target.value)} />
          <label htmlFor={`${id}-replacement`}>{T.replacement}</label><input id={`${id}-replacement`} type="checkbox" checked={replacement} onChange={(e) => setReplacement(e.target.checked)} />
        </> : null}
        {mode === 'correction' ? <>
          <label htmlFor={`${id}-next-quantity`}>{T.newQuantity}</label><input id={`${id}-next-quantity`} inputMode="decimal" value={nextQuantity} onChange={(e) => setNextQuantity(e.target.value)} />
          <label htmlFor={`${id}-next-kg`}>{T.newKg}</label><input id={`${id}-next-kg`} inputMode="decimal" value={nextKg} onChange={(e) => setNextKg(e.target.value)} />
          <label htmlFor={`${id}-label`}>{T.newLabel}</label><input id={`${id}-label`} maxLength={64} value={label} onChange={(e) => setLabel(e.target.value)} />
          <label htmlFor={`${id}-location`}>{T.newLocation}</label><select id={`${id}-location`} value={location} onChange={(e) => setLocation(e.target.value)}>
            {!locations.some((item) => item.locationId === receipt.locationId) ? <option value={receipt.locationId}>{T.originalLocation}</option> : null}
            {locations.map((item) => <option key={item.locationId} value={item.locationId}>{item.siteName} / {item.locationName}</option>)}
          </select>
        </> : null}
      </div>
      {mode === 'return' ? <p>{T.definitive}</p> : null}
      {invalid ? <p role="alert">{T.invalid}</p> : null}<Button type="submit">{T.save}</Button>
    </fieldset></form>
  </article>
}
function ReturnDecision(props: Props & { readonly item: SupplierReturn }) {
  const { item, detail } = props, id = useId()
  const [reason, setReason] = useState(''), [version, setVersion] = useState(detail.order.version), [invalid, setInvalid] = useState(false)
  const attempt = useReceiptAdjustmentAttempt<{ approve: boolean; request: SupplierReturnDecision }, SupplierReturnResponse>(
    JSON.stringify([props.attemptBinding, 'return-decision', detail.order.id, item.returnId]), props.binding,
    (payload, key) => decideSupplierReturn(detail.order.companyId, detail.order.id, item.returnId, payload.approve, payload.request, key), props.onSettled)
  function decide(approve: boolean) {
    if (attempt.locked) return
    if (approve && item.expectedPurchaseOrderVersion !== undefined && !isPurchasingVersion(version)
      || !approve && reason.trim() === '' || reason.trim().length > 1000) { setInvalid(true); return }
    setInvalid(false)
    attempt.submit({ approve, request: { expectedVersion: item.version, reason: approve ? null : reason.trim(),
      ...(approve && item.expectedPurchaseOrderVersion !== undefined ? { expectedPurchaseOrderVersion: version } : {}) } })
  }
  return <article className={styles.actionCard} aria-labelledby={`${id}-heading`}>
    <h4 id={`${id}-heading`}>{T.returns}: {item.quantityKg} kg</h4><p>{item.reason}</p><p>{T.manualCost}: {item.unitCostTryPerKg}</p>
    <AttemptFeedback attempt={attempt} />
    {attempt.snapshot !== null ? <p>{(attempt.snapshot.payload as { approve: boolean }).approve ? T.approve : T.reject};
      {' '}{T.expectedOrderVersion}: {(attempt.snapshot.payload as { request: SupplierReturnDecision }).request.expectedPurchaseOrderVersion ?? item.expectedPurchaseOrderVersion}</p> : null}
    {attempt.outcome?.type === 'applied' ? <p role="status">{attempt.outcome.response.return.status === 'Recorded' ? T.approved : T.rejected}</p> : null}
    <fieldset disabled={attempt.locked}><legend>{T.returns}</legend>
      {item.expectedPurchaseOrderVersion !== undefined ? <>
        <label htmlFor={`${id}-version`}>{T.expectedOrderVersion}</label><input id={`${id}-version`} inputMode="numeric" value={version} onChange={(e) => setVersion(e.target.value)} />
        <p>{T.expectedOrderHelp}</p></> : null}
      <label htmlFor={`${id}-reason`}>{T.decisionReason}</label><textarea id={`${id}-reason`} maxLength={1000} value={reason} onChange={(e) => setReason(e.target.value)} />
      {invalid ? <p role="alert">{T.invalid}</p> : null}
      <Button type="button" onClick={() => decide(true)}>{T.approve}</Button><Button type="button" variant="secondary" onClick={() => decide(false)}>{T.reject}</Button>
    </fieldset>
  </article>
}
function AttemptFeedback<TAnswer>({ attempt }: { readonly attempt: { readonly sending: boolean; readonly snapshot: unknown;
  readonly outcome: AdjustmentOutcome<TAnswer> | null; retry(): void } }) {
  const result = attempt.outcome
  return <>
    {attempt.sending ? <p role="status">{T.sending}</p> : null}
    {!attempt.sending && attempt.snapshot !== null ? <div role="alert"><p>{T.unknown}</p><Button type="button" onClick={attempt.retry}>{T.retry}</Button></div> : null}
    {result?.type === 'failed' ? <StateBlock subject={T.heading} state={classifyOutcome({ type: 'http', status: result.status, body: { code: result.code } })} /> : null}
    {result?.type === 'unknown' ? <StateBlock subject={T.heading} state={classifyOutcome(result.reason === 'service-unavailable'
      ? { type: 'http', status: result.status ?? 503, body: null } : { type: result.reason })} /> : null}
  </>
}
function AdjustmentResult({ answer }: { readonly answer: Answer }) {
  if ('return' in answer) return <p role="status">{answer.return.status === 'PendingCostException' ? T.pending : answer.isReplay ? T.replayed : T.saved}</p>
  return <div role="status"><p>{answer.purchase.isReplay ? T.replayed : T.saved}</p>
    <p>{T.netOpened}: {answer.purchase.netOpenedQuantity}; {T.allocatable}: {answer.purchase.allocatableQuantity}</p>
    {answer.stock.conversions.map((item) => <p key={item.conversionId}>{T.reopened}: {item.reopenedQuantity}; {T.released}: {item.overflowReleasedQuantity}; {T.unheld}: {item.unheldQuantity}</p>)}
    {answer.correctionStock !== null ? <><p>{T.newLabelSaved}</p>
      {answer.correctionStock.futureSupplyReleases.map((release) => <p key={release.futureAllocationId}>{T.released}: {release.releasedQuantity}</p>)}
    </> : null}
  </div>
}
function PendingOperationSummary({ operation }: { readonly operation: Operation }) {
  const quantity = operation.mode === 'return' ? operation.request.undoQuantity : operation.request.undoQuantity
  const kg = operation.mode === 'return' ? operation.request.quantityKg : operation.request.measuredUndoQuantityKg
  return <p>{operation.mode === 'return' ? T.supplierReturn : operation.mode === 'correction' ? T.correction : T.undo};
    {' '}{T.purchaseQuantity}: {quantity}; {T.measuredKg}: {kg}
    {operation.mode === 'correction' && operation.request.replacementReceipt != null
      ? <>; {T.newLabel}: {operation.request.replacementReceipt.labelCode}; {T.newQuantity}: {operation.request.replacementReceipt.acceptedQuantity};
          {' '}{T.newKg}: {operation.request.replacementReceipt.measuredQuantityKg}</> : null}
  </p>
}
function costInput(value: string): string | null {
  const raw = value.trim().replace(',', '.')
  if (!/^[0-9]+(\.[0-9]{1,4})?$/.test(raw)) return null
  const [whole, fraction = ''] = raw.split('.'), integer = whole.replace(/^0+(?=\d)/, ''), decimal = fraction.replace(/0+$/, '')
  const canonical = integer + (decimal === '' ? '' : `.${decimal}`)
  return /^(0|[1-9]\d{0,13})(\.[0-9]{0,3}[1-9])?$/.test(canonical) && canonical !== '0' ? canonical : null
}
