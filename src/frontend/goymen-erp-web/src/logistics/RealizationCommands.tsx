import { useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { fetchShipmentAllocations, recordDelivery, recordShipment } from './api'
import { parseQuantityInput, type InstructionDetail, type InstructionLine, type InstructionMutation } from './contract'
import { REALIZATION_CAPABILITY, shipmentRemainder, type RecordDeliveryRequest, type RecordShipmentRequest } from './realizationContract'
import { abandon, applyOutcome, attemptBinding, beginSend, decideSubmit, generateAttemptKey, generateClientId,
  isOutcomeStillUnknown, liveSnapshot, type AttemptSnapshot, type AttemptState, type CommandOutcome } from './instructionAttempt'
import { commandNotice } from './instructionAttemptNotice'
import { recallInstructionAttempt, rememberInstructionAttempt } from './pendingInstructionAttempts'
import { INSTRUCTION_DETAIL_TEXT, REALIZATION_TEXT as T } from './logisticsText'
import { formatInstant } from './format'
import { hasCapability, type ShellState } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { classifyOutcome } from '../shell/uiState'
import { StateBlock } from '../shell/StateBlock'
import styles from './LogisticsPages.module.css'

interface Props {
  readonly shell: ShellState
  readonly detail: InstructionDetail
  readonly companyId: string
  readonly onRecorded: () => void
}
type LineProps = Props & { readonly line: InstructionLine }

export function RealizationCommands(props: Props) {
  const shipment = hasCapability(props.shell, REALIZATION_CAPABILITY.shipment) && hasCapability(props.shell, REALIZATION_CAPABILITY.allocation)
  const delivery = hasCapability(props.shell, REALIZATION_CAPABILITY.delivery)
  if (!shipment && !delivery) return null
  return <>{props.detail.lines.map((line) => <div key={line.lineId}>
    {shipment ? <ShipmentForm {...props} line={line} /> : null}
    {delivery ? <DeliveryForm {...props} line={line} /> : null}
  </div>)}</>
}

function useRealizationAttempt<P>(props: LineProps, kind: 'shipment' | 'delivery',
  command: (snapshot: AttemptSnapshot<P>) => Promise<CommandOutcome<InstructionMutation>>) {
  const scope = `logistics.instruction.${kind}:${props.detail.instruction.id}:${props.line.lineId}`
  const slot = attemptBinding(props.shell, scope)
  const [initial] = useState(() => recallInstructionAttempt<P, null>(slot))
  const [attempt, setAttempt] = useState<AttemptState<P, InstructionMutation>>(() => initial?.attempt ?? { phase: 'idle' })
  const latest = useRef(props)
  latest.current = props
  const current = useRef(attempt)
  current.current = attempt
  const boundary = useAuthBoundary()

  async function send(snapshot: AttemptSnapshot<P>) {
    if (attemptBinding(latest.current.shell, scope) !== snapshot.binding) return
    const previous = current.current
    const inFlight = beginSend<P, InstructionMutation>(snapshot)
    const auth = boundary.capture()
    current.current = inFlight
    setAttempt(inFlight)
    rememberInstructionAttempt(snapshot.binding, inFlight, null)
    const outcome = await command(snapshot)
    if (outcome.type === 'http') boundary.report(outcome.status, auth)
    const binding = attemptBinding(latest.current.shell, scope)
    if (binding !== snapshot.binding) return
    const next = applyOutcome(current.current, snapshot, binding, outcome, previous)
    current.current = next
    setAttempt(next)
    rememberInstructionAttempt(snapshot.binding, next, null)
    if (next.phase === 'completed' || next.phase === 'failed' && next.failure === 'conflict') latest.current.onRecorded()
  }

  function submit(payload: P) {
    const binding = attemptBinding(latest.current.shell, scope)
    if (binding === null) return
    const state = current.current.phase === 'completed' ? { phase: 'idle' as const } : current.current
    const decision = decideSubmit(state, { path: scope, binding, companyId: props.companyId, payload }, generateAttemptKey)
    if (decision.type === 'send') void send(decision.snapshot)
  }

  function retry() {
    const snapshot = liveSnapshot(current.current)
    if (snapshot !== null && current.current.phase !== 'submitting') void send(snapshot)
  }

  function leave() {
    const next = abandon(current.current)
    current.current = next
    setAttempt(next)
    if (next.phase !== 'idle') rememberInstructionAttempt(next.snapshot.binding, next, null)
  }

  return { attempt, submit, retry, leave, live: liveSnapshot(attempt),
    initialPayload: initial === null || initial.attempt.phase === 'idle' ? null : initial.attempt.snapshot.payload,
    frozen: attempt.phase === 'submitting' || isOutcomeStillUnknown(attempt) && attempt.phase !== 'abandoned' }
}

function AttemptNotice({ attempt, retry, leave, testId }: {
  readonly attempt: AttemptState<unknown, InstructionMutation>
  readonly retry: () => void
  readonly leave: () => void
  readonly testId: string
}) {
  const notice = commandNotice(attempt)
  if (notice === null) return null
  return <div className={styles.notice} data-tone={notice.tone} role={notice.live === 'assertive' ? 'alert' : 'status'}>
    <p data-testid={testId}>{notice.message}</p>
    {notice.actions.length > 0 ? <div className={styles.actions}>
      <button type="button" onClick={retry} disabled={attempt.phase === 'submitting'}>{INSTRUCTION_DETAIL_TEXT.command.retry}</button>
      <button type="button" onClick={leave} disabled={attempt.phase === 'submitting'}>{INSTRUCTION_DETAIL_TEXT.command.abandon}</button>
    </div> : null}
  </div>
}

function ShipmentForm(props: LineProps) {
  const { line, detail, companyId, shell } = props
  const boundary = useAuthBoundary()
  const active = detail.instruction.status === 'Issued' && !line.isBlocked && line.loadedNotShipped !== '0'
  const binding = attemptBinding(shell, `shipment-allocations:${detail.instruction.id}:${line.lineId}`)
  const query = useQuery({
    queryKey: [binding, shell.routeEntryGeneration, 'shipment-allocations', line.sourceOrderId, line.sourceOrderLineId, line.productId, detail.instruction.version],
    enabled: active && binding !== null,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchShipmentAllocations(line, companyId, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
  })
  const attempt = useRealizationAttempt<RecordShipmentRequest>({ ...props, onRecorded: () => { props.onRecorded(); void query.refetch() } }, 'shipment',
    (snapshot) => recordShipment(line.lineId, snapshot.payload, { companyId: snapshot.companyId,
      instructionId: detail.instruction.id, idempotencyKey: snapshot.key }))
  const [allocationId, setAllocationId] = useState(attempt.initialPayload?.allocationId ?? '')
  const [quantity, setQuantity] = useState(attempt.initialPayload?.quantity ?? '')
  const [error, setError] = useState(false)
  const allocations = query.data?.type === 'ok-single' ? query.data.data.items.filter((item) => item.status === 'Active') : []
  const chosen = allocations.find((item) => item.allocationId === allocationId)
  const id = `shipment-${line.lineNo}`

  function submit(event: React.FormEvent) {
    event.preventDefault()
    const parsed = parseQuantityInput(quantity)
    if (!active || chosen === undefined || !parsed.ok) { setError(true); return }
    setError(false)
    const live = attempt.live?.payload
    const payload = live !== undefined && live.allocationId === allocationId && live.quantity === parsed.value ? live : {
      expectedVersion: detail.instruction.version, shipmentId: generateClientId(), allocationId,
      expectedAllocationVersion: chosen.allocationVersion, expectedLabelVersion: chosen.labelVersion,
      movementId: generateClientId(), quantity: parsed.value, occurredAtUtc: new Date().toISOString(),
    }
    attempt.submit(payload)
  }

  return <section className={styles.section} aria-labelledby={`${id}-heading`} data-testid={id}>
    <h2 id={`${id}-heading`}>{T.shipment} ({T.line} {line.lineNo})</h2>
    <p className={styles.muted}>{T.shipmentNote}</p>
    <AttemptNotice {...attempt} testId={`${id}-notice`} />
    {active && (query.data === undefined || query.data.type !== 'ok-single') ? <StateBlock
      subject={T.allocation} state={query.data === undefined ? { kind: 'initial-loading' } : classifyOutcome(query.data)} onRetry={() => void query.refetch()} /> : null}
    {query.data?.type === 'ok-single' && query.data.data.searchWindowFull ? <p role="status">{T.windowFull}</p> : null}
    {active && query.data?.type === 'ok-single' && allocations.length === 0 ? <p>{T.noAllocation}</p> : null}
    <form onSubmit={submit} noValidate>
      <fieldset className={styles.fieldset} disabled={!active || attempt.frozen}>
        <legend>{T.shipment}</legend>
        <div className={styles.grid}>
          <label htmlFor={`${id}-allocation`}>{T.allocation}</label>
          <select id={`${id}-allocation`} value={allocationId} onChange={(event) => setAllocationId(event.target.value)}>
            <option value="">{T.choose}</option>
            {allocations.map((item) => <option key={item.allocationId} value={item.allocationId}>
              {T.labelReference} {item.labelId.slice(-8)} / {T.allocationReference} {item.allocationId.slice(-8)} ({T.remaining}: {item.remainingKg} kg)
            </option>)}
          </select>
          <label htmlFor={`${id}-quantity`}>{T.quantity}</label>
          <input id={`${id}-quantity`} inputMode="decimal" value={quantity} onChange={(event) => setQuantity(event.target.value)} aria-invalid={error || undefined} />
        </div>
        {error ? <p role="alert">{T.invalid}</p> : null}
        <button className={styles.primaryButton} type="submit" disabled={chosen === undefined} data-testid={`${id}-submit`}>{T.shipment}</button>
      </fieldset>
    </form>
  </section>
}

function DeliveryForm(props: LineProps) {
  const { detail, line } = props
  const attempt = useRealizationAttempt<RecordDeliveryRequest>(props, 'delivery', (snapshot) =>
    recordDelivery(line.lineId, snapshot.payload, { companyId: snapshot.companyId,
      instructionId: detail.instruction.id, idempotencyKey: snapshot.key }))
  const [sourceShipmentId, setSourceShipmentId] = useState(attempt.initialPayload?.sourceShipmentId ?? '')
  const [quantity, setQuantity] = useState(attempt.initialPayload?.quantity ?? '')
  const [error, setError] = useState(false)
  const shipments = detail.shares.filter((share) => share.kind === 'Shipped' && share.lineId === line.lineId
    && shipmentRemainder(share, detail.shares) !== '0')
  const id = `delivery-${line.lineNo}`
  const active = detail.instruction.status === 'Issued'

  function submit(event: React.FormEvent) {
    event.preventDefault()
    const parsed = parseQuantityInput(quantity)
    if (!active || !shipments.some((share) => share.shareId === sourceShipmentId) || !parsed.ok) { setError(true); return }
    setError(false)
    const live = attempt.live?.payload
    attempt.submit(live !== undefined && live.sourceShipmentId === sourceShipmentId && live.quantity === parsed.value ? live : {
      expectedVersion: detail.instruction.version, deliveryId: generateClientId(), sourceShipmentId,
      quantity: parsed.value, occurredAtUtc: new Date().toISOString(),
    })
  }

  return <section className={styles.section} aria-labelledby={`${id}-heading`} data-testid={id}>
    <h2 id={`${id}-heading`}>{T.delivery} ({T.line} {line.lineNo})</h2>
    <p className={styles.muted}>{T.deliveryNote}</p>
    <AttemptNotice {...attempt} testId={`${id}-notice`} />
    {shipments.length === 0 ? <p>{T.noShipment}</p> : null}
    <form onSubmit={submit} noValidate>
      <fieldset className={styles.fieldset} disabled={!active || attempt.frozen}>
        <legend>{T.delivery}</legend>
        <div className={styles.grid}>
          <label htmlFor={`${id}-source`}>{T.sourceShipment}</label>
          <select id={`${id}-source`} value={sourceShipmentId} onChange={(event) => setSourceShipmentId(event.target.value)}>
            <option value="">{T.choose}</option>
            {shipments.map((share) => <option key={share.shareId} value={share.shareId}>
              {T.shipmentReference} {share.shareId.slice(-8)} / {formatInstant(share.recordedAtUtc)} ({T.remaining}: {shipmentRemainder(share, detail.shares)} kg)
            </option>)}
          </select>
          <label htmlFor={`${id}-quantity`}>{T.quantity}</label>
          <input id={`${id}-quantity`} inputMode="decimal" value={quantity} onChange={(event) => setQuantity(event.target.value)} aria-invalid={error || undefined} />
        </div>
        {error ? <p role="alert">{T.invalid}</p> : null}
        <button className={styles.primaryButton} type="submit" disabled={!shipments.some((share) => share.shareId === sourceShipmentId)}
          data-testid={`${id}-submit`}>{T.delivery}</button>
      </fieldset>
    </form>
  </section>
}
