import { useLayoutEffect, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { fetchGoodsReceiptOptions, fetchGoodsReceiptQuantityComparison, fetchGoodsReceipts, recordGoodsReceipt } from './goodsReceiptApi'
import { receiptQuantityInput, type GoodsReceipt, type GoodsReceiptRequest, type GoodsReceiptResponse } from './goodsReceiptContract'
import { recallReceiptAttempt, receiptIdentity, reserveReceiptAttempt, settleReceiptAttempt, type ReceiptAttempt } from './goodsReceiptAttempt'
import { GoodsReceiptQuantityComparison } from './GoodsReceiptQuantityComparison'
import { GOODS_RECEIPT_TEXT as T } from './goodsReceiptText'
import type { PurchaseOrderDetail, PurchaseOrderLine } from './contract'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import styles from './purchasing.module.css'

interface Props { readonly enabled: boolean; readonly binding: string; readonly detail: PurchaseOrderDetail; readonly onRecorded: () => void }

export function GoodsReceiptPanel(props: Props) {
  if (!props.enabled || props.detail.lines.length === 0) return null
  return <ReceiptWorkspace {...props} />
}

function ReceiptWorkspace(props: Props) {
  const boundary = useAuthBoundary()
  const history = useQuery({
    queryKey: [props.binding, 'purchase-goods-receipts', props.detail.order.id, props.detail.order.version],
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchGoodsReceipts(props.detail, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
  })
  const refresh = () => { props.onRecorded(); void history.refetch() }
  return <section aria-label={T.heading}>
    <h2>{T.heading}</h2><p className={styles.note}>{T.note}</p>
    {props.detail.lines.map((line) => <ReceiptLine key={line.lineId} {...props} line={line} onRecorded={refresh} />)}
    <h3>{T.history}</h3>
    {history.data?.type === 'ok-single' ? <>
      {history.data.data.searchWindowFull ? <p>{T.limited}</p> : null}
      {history.data.data.items.length === 0 ? <p>{T.empty}</p> : <ul>{history.data.data.items.map((receipt) =>
        <ReceiptHistoryItem key={receipt.acceptanceId} binding={props.binding} detail={props.detail} receipt={receipt} />)}</ul>}
    </> : <StateBlock subject={T.history} state={history.data === undefined ? { kind: 'initial-loading' } : classifyOutcome(history.data)}
      onRetry={() => void history.refetch()} />}
  </section>
}

function ReceiptHistoryItem({ binding, detail, receipt }: {
  readonly binding: string
  readonly detail: PurchaseOrderDetail
  readonly receipt: GoodsReceipt
}) {
  const boundary = useAuthBoundary()
  const comparison = useQuery({
    queryKey: [binding, 'purchase-goods-receipt-comparison', detail.order.companyId, detail.order.id,
      receipt.acceptanceId, receipt.purchaseOrderVersion],
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchGoodsReceiptQuantityComparison(detail, receipt, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
  })
  return <li data-testid={`goods-receipt-${receipt.acceptanceId}`}>
    <strong>{receipt.labelCode}</strong>: {T.accepted} {receipt.acceptedQuantity}; {T.measured} {receipt.measuredQuantityKg}. {receipt.initialBlock === 'Free' ? T.initialFree : T.initialBlocked}
    {comparison.data?.type === 'ok-single'
      ? <GoodsReceiptQuantityComparison comparison={comparison.data.data} />
      : <StateBlock subject={T.comparisonHeading}
          state={comparison.data === undefined ? { kind: 'initial-loading' } : classifyOutcome(comparison.data)}
          onRetry={() => void comparison.refetch()} />}
  </li>
}

function ReceiptLine({ binding, detail, line, onRecorded }: Props & { readonly line: PurchaseOrderLine }) {
  const boundary = useAuthBoundary()
  const scope = `${binding}:receipt:${detail.order.id}:${line.lineId}`
  const [initial] = useState(() => recallReceiptAttempt(scope))
  const [pending, setPending] = useState<ReceiptAttempt | null>(initial)
  const [phase, setPhase] = useState<'idle' | 'sending' | 'unknown' | 'failed' | 'done'>(initial === null ? 'idle' : 'unknown')
  const [answer, setAnswer] = useState<GoodsReceiptResponse | null>(null)
  const [locationId, setLocationId] = useState(initial?.payload.locationId ?? '')
  const [accepted, setAccepted] = useState(initial?.payload.acceptedQuantity ?? '')
  const [measured, setMeasured] = useState(initial?.payload.measuredQuantityKg ?? '')
  const [labelCode, setLabelCode] = useState(initial?.payload.labelCode ?? '')
  const [heat, setHeat] = useState(initial?.payload.heatNumber ?? '')
  const [coil, setCoil] = useState(initial?.payload.coilNumber ?? '')
  const [invalid, setInvalid] = useState(false)
  const inFlight = useRef(false)
  const generation = useRef(0)
  useLayoutEffect(() => {
    generation.current += 1
    return () => { generation.current += 1 }
  }, [scope])
  const active = detail.order.status === 'Approved' && line.remaining !== '0'
  const options = useQuery({
    queryKey: [binding, 'purchase-goods-receipt-options', detail.order.id, line.lineId, detail.order.version, line.productVersion],
    enabled: active,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchGoodsReceiptOptions(detail, line, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
  })
  const locations = options.data?.type === 'ok-single' ? options.data.data.locations : []
  const id = `goods-receipt-line-${line.lineNo}`

  async function send(snapshot: ReceiptAttempt) {
    if (inFlight.current || snapshot.binding !== scope) return
    inFlight.current = true
    const live = generation.current
    const wasUnknown = pending !== null
    const security = boundary.capture()
    reserveReceiptAttempt(snapshot)
    setPending(snapshot)
    setPhase('sending')
    const outcome = await recordGoodsReceipt(detail, line.lineId, snapshot.payload, snapshot.key)
    if (generation.current !== live) return
    inFlight.current = false
    if (outcome.type === 'failed') boundary.report(outcome.status, security)
    if (outcome.type === 'unknown' || outcome.type === 'failed' && wasUnknown) { setPhase('unknown'); return }
    settleReceiptAttempt(snapshot)
    setPending(null)
    if (outcome.type === 'applied') {
      setAnswer(outcome.response)
      setPhase('done')
      onRecorded()
      void options.refetch()
    } else {
      setPhase('failed')
      if (outcome.status === 409) { onRecorded(); void options.refetch() }
    }
  }

  function submit(event: React.FormEvent) {
    event.preventDefault()
    if (pending !== null || !active || inFlight.current) return
    const acceptedQuantity = receiptQuantityInput(accepted)
    const measuredQuantityKg = receiptQuantityInput(measured)
    if (acceptedQuantity === null || measuredQuantityKg === null || !locations.some((location) => location.locationId === locationId)
      || labelCode.trim() === '' || labelCode.trim().length > 64 || heat.trim().length > 64 || coil.trim().length > 64) {
      setInvalid(true); return
    }
    setInvalid(false)
    const payload: GoodsReceiptRequest = { expectedPurchaseOrderVersion: detail.order.version, expectedProductVersion: line.productVersion,
      acceptanceId: receiptIdentity(), acceptedQuantity, measuredQuantityKg, locationId,
      lotId: receiptIdentity(), labelId: receiptIdentity(), labelCode: labelCode.trim(), heatNumber: heat.trim() || null,
      coilNumber: coil.trim() || null, movementId: receiptIdentity(), occurredAtUtc: new Date().toISOString() }
    void send({ binding: scope, key: receiptIdentity(), payload })
  }

  return <section className={styles.actionCard} aria-labelledby={`${id}-heading`} data-testid={id}>
    <h3 id={`${id}-heading`}>{T.line} {line.lineNo}</h3>
    {phase === 'sending' ? <p role="status">{T.sending}</p> : null}
    {phase === 'unknown' && pending !== null ? <div className={styles.warningCard}><p role="alert">{T.unknown}</p>
      <button type="button" onClick={() => void send(pending)}>{T.retry}</button></div> : null}
    {phase === 'failed' ? <p role="alert" className={styles.error}>{T.failed}</p> : null}
    {phase === 'done' && answer !== null ? <p role="status" className={styles.success}>
      {answer.isReplay ? T.replayed : T.saved} {answer.receipt.labelCode}: {answer.receipt.initialBlock === 'Free' ? T.initialFree : T.initialBlocked}
    </p> : null}
    {active && options.data?.type !== 'ok-single' ? <StateBlock subject={T.location}
      state={options.data === undefined ? { kind: 'initial-loading' } : classifyOutcome(options.data)} onRetry={() => void options.refetch()} /> : null}
    {options.data?.type === 'ok-single' && options.data.data.searchWindowFull ? <p>{T.limited}</p> : null}
    {active && options.data?.type === 'ok-single' && locations.length === 0 ? <p>{T.noLocation}</p> : null}
    <form className={styles.createForm} onSubmit={submit} noValidate>
      <fieldset disabled={!active || pending !== null || phase === 'sending'}><legend>{T.heading}</legend>
        <div className={styles.formGrid}>
          <label htmlFor={`${id}-location`}>{T.location}</label>
          <select id={`${id}-location`} value={locationId} onChange={(event) => setLocationId(event.target.value)}><option value="">{T.choose}</option>
            {locations.map((location) => <option key={location.locationId} value={location.locationId}>
              {location.siteName} / {location.locationCode} {location.locationName}</option>)}
          </select>
          <label htmlFor={`${id}-accepted`}>{T.accepted}</label><input id={`${id}-accepted`} inputMode="decimal" value={accepted} onChange={(event) => setAccepted(event.target.value)} />
          <label htmlFor={`${id}-measured`}>{T.measured}</label><input id={`${id}-measured`} inputMode="decimal" value={measured} onChange={(event) => setMeasured(event.target.value)} />
          <label htmlFor={`${id}-label`}>{T.label}</label><input id={`${id}-label`} value={labelCode} maxLength={64} onChange={(event) => setLabelCode(event.target.value)} />
          <label htmlFor={`${id}-heat`}>{T.heat}</label><input id={`${id}-heat`} value={heat} maxLength={64} onChange={(event) => setHeat(event.target.value)} />
          <label htmlFor={`${id}-coil`}>{T.coil}</label><input id={`${id}-coil`} value={coil} maxLength={64} onChange={(event) => setCoil(event.target.value)} />
        </div>
        {invalid ? <p role="alert">{T.invalid}</p> : null}
        <button type="submit" disabled={!locations.some((location) => location.locationId === locationId)}>{T.save}</button>
      </fieldset>
    </form>
  </section>
}
