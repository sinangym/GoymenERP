import { useEffect, useRef, useState, type FormEvent } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link, useParams } from 'react-router'
import { salesDetailPath } from '../sales/capabilities'
import { PURCHASING_CAPABILITY, PURCHASING_ROUTE } from '../purchasing/capabilities'
import { parseQuantityInput } from '../sales/wirePrimitives'
import { StateBlock } from '../shell/StateBlock'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { Button } from '../ui/Button'
import { INVENTORY_FUTURE_ALLOCATION_WRITE_CAPABILITY } from './capabilities'
import { fetchFutureAllocations, fetchFutureSupplyOptions, sendFutureAllocation, type FutureAllocationPending } from './futureAllocationApi'
import { futureGuid, type FutureAllocation, type FutureAllocationList, type FutureSupplyOptions } from './futureAllocationContract'
import { FUTURE_ALLOCATION_TEXT as T } from './futureAllocationText'
import styles from './inventory.module.css'

function recall(key: string | null, order: string, line: string): FutureAllocationPending | null {
  if (key === null) return null
  try {
    const value: unknown = JSON.parse(sessionStorage.getItem(key) ?? 'null')
    if (typeof value !== 'object' || value === null || !('kind' in value) || !('key' in value) || !('payload' in value)
      || typeof value.key !== 'string' || value.key.length === 0 || value.key.length > 128 || typeof value.payload !== 'object' || value.payload === null) return null
    // Exact key sets: a stored attempt carrying any other member is not replayed.
    const keys = Object.keys(value).length; const payloadKeys = Object.keys(value.payload).length
    if (value.kind === 'create' && keys === 3 && payloadKeys === 8 && 'allocationId' in value.payload && futureGuid(value.payload.allocationId)
      && 'salesOrderId' in value.payload && value.payload.salesOrderId === order
      && 'salesOrderLineId' in value.payload && value.payload.salesOrderLineId === line
      && 'purchaseOrderId' in value.payload && futureGuid(value.payload.purchaseOrderId)
      && 'purchaseOrderLineId' in value.payload && futureGuid(value.payload.purchaseOrderLineId)
      && 'unitId' in value.payload && futureGuid(value.payload.unitId) && 'quantityText' in value.payload && typeof value.payload.quantityText === 'string'
      && 'reason' in value.payload && typeof value.payload.reason === 'string') return value as FutureAllocationPending
    if (value.kind === 'release' && keys === 4 && payloadKeys === 4 && 'allocationId' in value && futureGuid(value.allocationId)
      && 'expectedAllocationVersion' in value.payload && typeof value.payload.expectedAllocationVersion === 'string'
      && 'quantityText' in value.payload && value.payload.quantityText === null && 'reasonCode' in value.payload && value.payload.reasonCode === 'manual-release'
      && 'reasonText' in value.payload && typeof value.payload.reasonText === 'string') return value as FutureAllocationPending
  } catch { /* no usable pending transport */ }
  return null
}

export function InventoryFutureAllocationPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const params = useParams()
  const company = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const actor = shell.activeContext?.userId ?? null
  if (namespace === null || company === null || actor === null) return <StateBlock state={{ kind: 'no-authorized-company' }} subject={T.subject} />
  if (!futureGuid(params.salesOrderId) || !futureGuid(params.salesOrderLineId)) return <StateBlock state={{ kind: 'not-found' }} subject={T.subject} />
  return <FutureAllocationView key={`${namespace}:${params.salesOrderId}:${params.salesOrderLineId}:${shell.routeEntryGeneration}`}
    company={company} actor={actor} namespace={namespace} order={params.salesOrderId} line={params.salesOrderLineId} routeEntry={shell.routeEntryGeneration} />
}

function FutureAllocationView({ company, actor, namespace, order, line, routeEntry }: {
  readonly company: string; readonly actor: string; readonly namespace: string; readonly order: string; readonly line: string; readonly routeEntry: number
}) {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const canWrite = hasCapability(shell, INVENTORY_FUTURE_ALLOCATION_WRITE_CAPABILITY)
  const canOpenPurchase = hasCapability(shell, PURCHASING_CAPABILITY.orderDetail)
  const [cursor, setCursor] = useState<string | null>(null)
  const [selected, setSelected] = useState('')
  const [quantity, setQuantity] = useState('')
  const [reason, setReason] = useState('')
  const [release, setRelease] = useState<FutureAllocation | null>(null)
  // The pending key must outlive an authority refresh: the context namespace carries the capability generation, so an unknown
  // create would be forgotten (and a retry could mint a second allocation). Session, actor, company and line only.
  // It is PERSISTED, so the session is the server's binding (`ShellState.sessionBinding`), never `sessionEpoch`, which restarts
  // at 0 in every document: after an F5 the same session would miss its attempt and another session could meet it. While the
  // binding is null nothing is persisted or recalled; the mounted screen still holds the same key.
  const storeKey = typeof shell.sessionBinding === 'string' && shell.sessionBinding.length > 0
    ? `goymen.inventory.future-allocation.v1:${shell.sessionBinding}:${actor}:${company}:${order}:${line}` : null
  const [pending, setPending] = useState<FutureAllocationPending | null>(() => recall(storeKey, order, line))
  const [busy, setBusy] = useState(false)
  const [commandState, setCommandState] = useState<RemoteViewState<never> | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const generation = useRef(0)
  const flight = useRef(false)
  useEffect(() => () => { generation.current++ }, [])
  const list = useQuery({ queryKey: ['inventory-future-allocations', namespace, order, line, routeEntry], retry: false,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const result = await fetchFutureAllocations(company, order, line, signal)
      if (result.type === 'http' && [401, 403].includes(result.status)) boundary.report(result.status, snapshot)
      return result
    } })
  const options = useQuery({ queryKey: ['inventory-future-options', namespace, order, line, cursor, routeEntry], retry: false, enabled: canWrite,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const result = await fetchFutureSupplyOptions(company, order, line, cursor, signal)
      if (result.type === 'http' && [401, 403].includes(result.status)) boundary.report(result.status, snapshot)
      return result
    } })
  const listState = (list.isPending ? { kind: 'initial-loading' } : classifyOutcome(list.data ?? { type: 'malformed' })) as RemoteViewState<FutureAllocationList>
  const optionsState = (options.isPending ? { kind: 'initial-loading' } : classifyOutcome(options.data ?? { type: 'malformed' })) as RemoteViewState<FutureSupplyOptions>
  const choice = optionsState.kind === 'ready' ? optionsState.data.items.find(item => item.purchaseOrderLineId === selected) : undefined

  async function send(attempt: FutureAllocationPending) {
    if (flight.current) return
    flight.current = true
    const current = ++generation.current
    const snapshot = boundary.capture()
    setPending(attempt); setBusy(true); setCommandState(null); setMessage(null)
    try { if (storeKey !== null) sessionStorage.setItem(storeKey, JSON.stringify(attempt)) } catch { /* the mounted transport still keeps the same key */ }
    const result = await sendFutureAllocation(company, order, line, attempt)
    if (result.type !== 'unknown' && !(result.type === 'failed' && [401, 403].includes(result.status))) {
      try { if (storeKey !== null) sessionStorage.removeItem(storeKey) } catch { /* definite result */ }
    }
    if (current !== generation.current) return
    flight.current = false; setBusy(false)
    if (result.type === 'unknown') { setCommandState({ kind: result.reason }); setMessage(T.unknown); return }
    if (result.type === 'failed' && [401, 403].includes(result.status)) {
      boundary.report(result.status, snapshot); setCommandState({ kind: result.status === 401 ? 'unauthenticated' : 'forbidden' }); return
    }
    setPending(null); setRelease(null); setReason(''); setSelected(''); setQuantity('')
    void list.refetch(); if (canWrite) void options.refetch()
    if (result.type === 'applied') { setMessage(result.replayed ? T.replayed : T.changed); return }
    if (result.status === 404) setCommandState({ kind: 'not-found' })
    else if (result.status === 409) { setCommandState({ kind: 'conflict' }); setMessage(T.conflict) }
    else setMessage(result.code === 'inventory_future_allocation_unit_conversion_unavailable' ? T.noConversion : T.refused)
  }
  function submit(event: FormEvent) {
    event.preventDefault()
    if (!canWrite || pending !== null || reason.trim().length === 0 || reason.length > 1000) { setMessage(T.invalid); return }
    if (release !== null) {
      void send({ kind: 'release', key: crypto.randomUUID(), allocationId: release.allocationId,
        payload: { expectedAllocationVersion: release.version, quantityText: null, reasonCode: 'manual-release', reasonText: reason } })
    } else {
      const parsed = parseQuantityInput(quantity)
      if (!parsed.ok || choice === undefined) { setMessage(T.invalid); return }
      void send({ kind: 'create', key: crypto.randomUUID(), payload: { allocationId: crypto.randomUUID(), salesOrderId: order, salesOrderLineId: line,
        purchaseOrderId: choice.purchaseOrderId, purchaseOrderLineId: choice.purchaseOrderLineId, unitId: choice.unitId, quantityText: parsed.value, reason } })
    }
  }
  return <section className={styles.page}>
    <nav><Link to={salesDetailPath(order)}>{T.back}</Link></nav><h1>{T.title}</h1><p>{T.explanation}</p>
    {listState.kind !== 'ready' ? <StateBlock state={listState} subject={T.subject} onRetry={() => void list.refetch()} /> : <>
      <p>{T.evaluatedOn}: {listState.data.evaluatedOn}</p>
      {listState.data.windowFull ? <p role="status">{T.window}</p> : null}
      {listState.data.items.length === 0 ? <StateBlock state={{ kind: 'empty-no-data' }} subject={T.subject} /> : <div className={styles.tableWrap}>
        <table><caption>{T.title}</caption><thead><tr><th scope="col">{T.allocation}</th><th scope="col">{T.allocated}</th><th scope="col">{T.remaining}</th><th scope="col">{T.consumed}</th><th scope="col">{T.released}</th><th scope="col">{T.unit}</th><th scope="col">{T.status}</th><th scope="col">{T.delivery}</th><th scope="col">{T.release}</th></tr></thead>
          <tbody>{listState.data.items.map((item, index) => <tr key={item.allocationId}><td>{index + 1}</td><td>{item.allocatedQuantity}</td><td>{item.remainingQuantity}</td>
            <td>{item.consumedQuantity}</td><td>{item.releasedQuantity}</td><td>{listState.data.unitCodes[item.unitId]}</td><td>{T.statuses[item.status]}</td>
            <td>{listState.data.schedules[item.allocationId].status === 'Scheduled'
              ? <>{listState.data.schedules[item.allocationId].deliveryDate} — {listState.data.schedules[item.allocationId].isOverdue ? T.overdue : T.onTime}</>
              : listState.data.schedules[item.allocationId].status === 'Undated' ? T.undated
                : listState.data.schedules[item.allocationId].status === 'Inactive' ? T.inactiveSchedule : T.scheduleUnavailable}</td>
            <td>{canWrite && item.status === 'Active' ? <Button type="button" disabled={busy || pending !== null} onClick={() => { setRelease(item); setReason(''); setMessage(null) }}>{T.release}</Button> : null}</td></tr>)}</tbody>
        </table></div>}
    </>}
    {!canWrite ? null : <section aria-labelledby="future-allocation-form"><h2 id="future-allocation-form">{release === null ? T.create : T.releaseHeading}</h2>
      {release === null && optionsState.kind !== 'ready' ? <StateBlock state={optionsState} subject={T.source} onRetry={() => void options.refetch()} /> : null}
      {release === null && optionsState.kind === 'ready' ? <>
        <p>{T.openDemand}: {optionsState.data.openDemandQuantity} {optionsState.data.demandUnitCode}</p>
        {optionsState.data.items.length === 0 ? <p role="status">{T.empty}</p> : null}
        <div className={styles.actions}><Button type="button" disabled={busy || pending !== null || cursor === null} onClick={() => { setCursor(null); setSelected('') }}>{T.first}</Button>
          <Button type="button" disabled={busy || pending !== null || optionsState.data.nextCursor === null} onClick={() => { setCursor(optionsState.data.nextCursor); setSelected('') }}>{T.more}</Button></div>
      </> : null}
      <form className={styles.form} onSubmit={submit}><fieldset disabled={busy || pending !== null}><legend>{release === null ? T.create : T.releaseHeading}</legend>
        {release !== null ? <p>{T.remaining}: {release.remainingQuantity} {listState.kind === 'ready' ? listState.data.unitCodes[release.unitId] : ''}</p> : <>
          <label>{T.source}<select value={selected} onChange={event => setSelected(event.target.value)} required><option value="">{T.sourcePrompt}</option>
            {optionsState.kind === 'ready' ? optionsState.data.items.map(item => <option key={item.purchaseOrderLineId} value={item.purchaseOrderLineId} disabled={item.availableQuantity === '0'}>
              {item.supplierCode} — {item.supplierName} / {T.line} {item.lineNo} / {T.available}: {item.availableQuantity} {item.unitCode}</option>) : null}</select></label>
          {choice !== undefined && canOpenPurchase ? <Link to={PURCHASING_ROUTE[PURCHASING_CAPABILITY.orderDetail].replace(':orderId', choice.purchaseOrderId)}>{T.sourceOrder}</Link> : null}
          <label>{T.quantity}{choice === undefined ? '' : ` (${choice.unitCode})`}<input inputMode="decimal" value={quantity} onChange={event => setQuantity(event.target.value)} required /></label>
        </>}
        <label>{T.reason}<textarea value={reason} maxLength={1000} onChange={event => setReason(event.target.value)} required /></label>
        <div className={styles.actions}><Button type="submit">{release === null ? T.create : T.releaseConfirm}</Button>
          {release === null ? null : <Button type="button" onClick={() => { setRelease(null); setReason('') }}>{T.cancel}</Button>}</div>
      </fieldset></form>
      {pending === null ? null : <Button type="button" onClick={() => void send(pending)} disabled={busy}>{busy ? T.busy : T.retry}</Button>}
    </section>}
    {commandState === null ? null : <StateBlock state={commandState} subject={T.subject} />}
    {message === null ? null : <p role="status">{message}</p>}
  </section>
}
