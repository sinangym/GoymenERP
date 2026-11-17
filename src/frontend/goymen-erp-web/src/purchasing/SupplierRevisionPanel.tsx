import { useEffect, useId, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import type { PurchaseOrderLine, PurchaseOrderSummary, PurchasingSupplierOffer } from './contract'
import { fetchPurchasingSupplierOptions } from './api'
import { clearSupplierRevisionAttempt, fetchSupplierRevisions, readSupplierRevisionAttempt, saveSupplierRevisionAttempt,
  sendSupplierRevision, supplierRevisionAttemptKey, type SupplierRevisionAttempt, type SupplierRevisionCommand, type SupplierRevisionHistory, type SupplierRevisionPriceRequest } from './supplierRevisionApi'
import { SUPPLIER_REVISION_TEXT as T } from './supplierRevisionText'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { StateBlock } from '../shell/StateBlock'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { parseDecimalInput } from '../sales/wirePrimitives'
import { Button } from '../ui/Button'

interface Props {
  readonly fenceKey: string; readonly companyId: string; readonly actorId: string | null; readonly sessionEpoch: number
  readonly order: PurchaseOrderSummary; readonly lines: readonly PurchaseOrderLine[]; readonly canEdit: boolean; readonly hasCurrency: boolean
  readonly onSettled: () => Promise<unknown>
}
export function SupplierRevisionPanel(props: Props) {
  const [open, setOpen] = useState(false); const id = useId()
  return <section aria-labelledby={`${id}-heading`}><h2 id={`${id}-heading`}>{T.heading}</h2><p>{T.note}</p>
    <Button type="button" variant="secondary" aria-expanded={open} aria-controls={`${id}-body`} onClick={() => setOpen(!open)}>{open ? T.close : T.open}</Button>
    <div id={`${id}-body`}>{open ? <SupplierRevisionBody key={props.fenceKey} {...props} /> : null}</div>
  </section>
}
function SupplierRevisionBody(props: Props) {
  const { companyId, order, actorId, sessionEpoch, lines, canEdit, hasCurrency, onSettled } = props
  const boundary = useAuthBoundary(); const formId = useId(); const alive = useRef(true); const inFlight = useRef(false)
  useEffect(() => { alive.current = true; return () => { alive.current = false } }, [])
  const storageKey = actorId === null ? null : supplierRevisionAttemptKey(sessionEpoch, actorId, companyId, order.id)
  const [initial] = useState(() => {
    try { return { pending: storageKey === null || actorId === null ? null : readSupplierRevisionAttempt(storageKey, actorId, companyId, order.id), fault: false } }
    catch { return { pending: null, fault: true } }
  })
  const [pending, setPending] = useState<SupplierRevisionAttempt | null>(initial.pending)
  const [storageFault, setStorageFault] = useState(initial.fault)
  const [sending, setSending] = useState(false); const [message, setMessage] = useState(initial.pending === null ? '' : T.unknown)
  const [search, setSearch] = useState(''); const [supplier, setSupplier] = useState(''); const [reason, setReason] = useState('')
  const [suppliers, setSuppliers] = useState<PurchasingSupplierOffer | null>(null); const [supplierStatus, setSupplierStatus] = useState('')
  const supplierGeneration = useRef(0)
  const [lineId, setLineId] = useState(''); const [unitPrice, setUnitPrice] = useState('')
  const [discountKind, setDiscountKind] = useState('none'); const [discount, setDiscount] = useState('')
  const [deferralKind, setDeferralKind] = useState('none'); const [deferral, setDeferral] = useState('')
  const history = useQuery({ queryKey: [props.fenceKey, 'supplier-revisions', order.id, order.version], queryFn: async ({ signal }) => {
    const captured = boundary.capture(); const answer = await fetchSupplierRevisions(companyId, order.id, signal)
    if (answer.type === 'http' && (answer.status === 401 || answer.status === 403)) boundary.report(answer.status, captured)
    return answer
  } })
  const state = (history.isPending ? { kind: 'initial-loading' } : classifyOutcome(history.data ?? { type: 'malformed' })) as RemoteViewState<SupplierRevisionHistory>
  const current = state.kind === 'ready' && state.data.version === order.version
  const frozen = sending || pending !== null || storageFault || !canEdit || actorId === null || !current
  async function readSuppliers(cursor?: string) {
    const generation = ++supplierGeneration.current; const captured = boundary.capture(); setSupplierStatus(T.loadingSuppliers)
    const result = await fetchPurchasingSupplierOptions(companyId, { q: search, cursor })
    if (!alive.current || supplierGeneration.current !== generation) return
    if (result.type === 'http' && (result.status === 401 || result.status === 403)) boundary.report(result.status, captured)
    if (result.type !== 'ok-single') { setSuppliers(null); setSupplierStatus(T.supplierSource); return }
    setSuppliers(previous => cursor === undefined || previous === null ? result.data : {
      items: [...previous.items, ...result.data.items.filter(item => !previous.items.some(old => old.supplierPartyId === item.supplierPartyId && old.companyAccountId === item.companyAccountId))],
      nextCursor: result.data.nextCursor,
    }); setSupplierStatus(result.data.items.length === 0 ? T.noSuppliers : '')
  }
  async function send(attempt: SupplierRevisionAttempt) {
    if (inFlight.current || storageKey === null || !canEdit || actorId === null) return
    inFlight.current = true; setSending(true); setMessage(T.saving); const captured = boundary.capture()
    const result = await sendSupplierRevision(attempt)
    if (!alive.current) return
    inFlight.current = false; setSending(false)
    if (result.type === 'unknown') { setMessage(T.unknown); return }
    if (result.type === 'failed' && (result.status === 401 || result.status === 403)) boundary.report(result.status, captured)
    try { clearSupplierRevisionAttempt(storageKey, attempt); setPending(null) }
    catch { setStorageFault(true); setMessage(T.pendingStorage); return }
    setMessage(result.type === 'applied' ? T.saved : result.status === 401 || result.status === 403 ? T.denied
      : result.status === 404 ? T.missing : result.code === 'purchasing_supplier_revision_blocked' ? T.blocked : result.status === 409 ? T.stale : T.failed)
    if (result.type === 'applied' || result.status === 409) { await onSettled(); if (alive.current) await history.refetch() }
  }
  function prepare(command: SupplierRevisionCommand) {
    if (frozen || actorId === null || storageKey === null) return
    const attempt = { actorId, companyId, orderId: order.id, idempotencyKey: crypto.randomUUID(), command }
    try { saveSupplierRevisionAttempt(storageKey, attempt) } catch { setStorageFault(true); setMessage(T.pendingStorage); return }
    setPending(attempt); void send(attempt)
  }
  function revise() {
    const selected = suppliers?.items.find(item => `${item.supplierPartyId}|${item.companyAccountId}` === supplier)
    if (selected === undefined || selected.supplierPartyId === order.supplierPartyId || reason.trim().length === 0 || reason.trim().length > 2000) { setMessage(T.invalid); return }
    prepare({ kind: 'supplier', expectedVersion: order.version, supplierPartyId: selected.supplierPartyId, companyAccountId: selected.companyAccountId, reason: reason.trim() })
  }
  function price() {
    const line = lines.find(item => item.lineId === lineId && item.kind === 'General')
    const unit = parseDecimalInput(unitPrice); const d = discountKind === 'none' ? null : parseDecimalInput(discount)
    const f = deferralKind === 'none' ? null : parseDecimalInput(deferral)
    if (line === undefined || !unit.ok || d?.ok === false || f?.ok === false) { setMessage(T.invalid); return }
    const value: SupplierRevisionPriceRequest = { unitPrice: unit.value, priceUnitId: line.unitId,
      ...(d?.ok ? discountKind === 'percent' ? { discountPercent: d.value } : { discountAmount: d.value } : {}),
      ...(f?.ok ? deferralKind === 'percent' ? { deferralPercent: f.value } : { deferralAmount: f.value } : {}),
    }
    prepare({ kind: 'price', expectedVersion: order.version, lineId, price: value })
  }
  if (state.kind !== 'ready') return <StateBlock state={state} subject={T.history} onRetry={() => void history.refetch()} />
  return <div>
    {state.data.revisions.length === 0 ? <p data-state="empty-no-data">{T.empty}</p> : <ol aria-label={T.history}>{state.data.revisions.map(item =>
      <li key={item.revision}><strong>{T.revision(item.revision)}</strong> · {item.reason} · <time dateTime={item.recordedAtUtc}>{item.recordedAtUtc}</time></li>)}</ol>}
    {!current ? <p role="alert">{T.stale}</p> : null}
    {message || storageFault ? <p role="status">{storageFault ? T.pendingStorage : message}</p> : null}
    {pending !== null ? <Button type="button" disabled={sending || !canEdit || storageFault} onClick={() => void send(pending)}>{T.retry}</Button> : null}
    <Button type="button" variant="secondary" disabled={sending} onClick={() => void onSettled().then(() => history.refetch())}>{T.reload}</Button>
    {!canEdit || actorId === null ? <p>{T.denied}</p> : order.status === 'Approved' ? <fieldset disabled={frozen || !hasCurrency}><legend>{T.heading}</legend>
      {!hasCurrency ? <p role="alert">{T.noCurrency}</p> : null}
      <label htmlFor={`${formId}-search`}>{T.searchText}</label><input id={`${formId}-search`} value={search} onChange={event => setSearch(event.target.value)} />
      <Button type="button" onClick={() => void readSuppliers()}>{T.search}</Button>{supplierStatus ? <p role="status">{supplierStatus}</p> : null}
      <label htmlFor={`${formId}-supplier`}>{T.supplier}</label><select id={`${formId}-supplier`} value={supplier} onChange={event => setSupplier(event.target.value)}><option value="">{T.choose}</option>
        {suppliers?.items.filter(item => item.supplierPartyId !== order.supplierPartyId).map(item => <option key={`${item.supplierPartyId}|${item.companyAccountId}`} value={`${item.supplierPartyId}|${item.companyAccountId}`}>{item.label}</option>)}</select>
      {suppliers?.nextCursor ? <Button type="button" onClick={() => void readSuppliers(suppliers.nextCursor ?? undefined)}>{T.more}</Button> : null}
      <label htmlFor={`${formId}-reason`}>{T.reason}</label><textarea id={`${formId}-reason`} value={reason} maxLength={2000} onChange={event => setReason(event.target.value)} />
      <Button type="button" onClick={revise}>{T.submit}</Button>
    </fieldset> : order.status === 'Draft' && state.data.revision > 0 ? <fieldset disabled={frozen || !hasCurrency}><legend>{T.price}</legend><p>{T.draftNote}</p>
      {!hasCurrency ? <p role="alert">{T.noCurrency}</p> : null}
      <label htmlFor={`${formId}-line`}>{T.line}</label><select id={`${formId}-line`} value={lineId} onChange={event => setLineId(event.target.value)}><option value="">{T.choose}</option>
        {lines.filter(line => line.kind === 'General').map(line => <option key={line.lineId} value={line.lineId}>{T.priceLine(line.lineNo)}</option>)}</select>
      <label htmlFor={`${formId}-price`}>{T.unitPrice}</label><input id={`${formId}-price`} inputMode="decimal" value={unitPrice} onChange={event => setUnitPrice(event.target.value)} />
      <label htmlFor={`${formId}-discount-kind`}>{T.discountKind}</label><select id={`${formId}-discount-kind`} value={discountKind} onChange={event => setDiscountKind(event.target.value)}>
        <option value="none">{T.none}</option><option value="percent">{T.percent}</option><option value="amount">{T.amount}</option></select>
      {discountKind !== 'none' ? <><label htmlFor={`${formId}-discount`}>{T.discount}</label><input id={`${formId}-discount`} inputMode="decimal" value={discount} onChange={event => setDiscount(event.target.value)} /></> : null}
      <label htmlFor={`${formId}-deferral-kind`}>{T.deferralKind}</label><select id={`${formId}-deferral-kind`} value={deferralKind} onChange={event => setDeferralKind(event.target.value)}>
        <option value="none">{T.none}</option><option value="percent">{T.percent}</option><option value="amount">{T.amount}</option></select>
      {deferralKind !== 'none' ? <><label htmlFor={`${formId}-deferral`}>{T.deferral}</label><input id={`${formId}-deferral`} inputMode="decimal" value={deferral} onChange={event => setDeferral(event.target.value)} /></> : null}
      <Button type="button" onClick={price}>{T.savePrice}</Button>
    </fieldset> : null}
  </div>
}
