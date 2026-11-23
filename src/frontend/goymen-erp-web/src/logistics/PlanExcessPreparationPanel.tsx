import { useEffect, useRef, useState, type FormEvent } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { useShellState } from '../shell/shellContext'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StateBlock } from '../shell/StateBlock'
import { hasCapability } from '../shell/shellState'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import { parseQuantityInput } from './contract'
import { abandon, applyOutcome, attemptBinding, beginSend, decideSubmit, generateAttemptKey, generateClientId, isOutcomeStillUnknown, liveSnapshot,
  type AttemptSnapshot, type AttemptState } from './instructionAttempt'
import { PLAN_EXCESS_CAPABILITY, proposePlanExcessPath, sendPlanExcess, type PlanExcessCommand, type PlanExcessResult } from './planExcessApi'
import { PLAN_EXCESS_TEXT as T } from './planExcessText'
import styles from './LogisticsPages.module.css'

type PlanAttempt = AttemptState<PlanExcessCommand, PlanExcessResult>
const pending = new Map<string, PlanAttempt>()
const settled = new Set<string>()
function hold(binding: string, state: PlanAttempt) {
  if (state.phase === 'idle') return
  const token = `${binding}|${state.snapshot.key}`
  if (settled.has(token)) return
  if (isOutcomeStillUnknown(state)) pending.set(binding, state)
  else { settled.add(token); const held = pending.get(binding); if (held !== undefined && held.phase !== 'idle' && held.snapshot.key === state.snapshot.key) pending.delete(binding) }
}
/** Memory only, actor/session/company/scope bound. A lost response never opens a fresh key. */
export function usePlanExcessCommand(scope: string, onRecorded: () => void) {
  const shell = useShellState(), boundary = useAuthBoundary(), queries = useQueryClient()
  const binding = attemptBinding(shell, scope), company = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const [state, setState] = useState<PlanAttempt>(() => {
    const held = binding === null ? undefined : pending.get(binding)
    return held?.phase === 'submitting' ? { phase: 'unknown', snapshot: held.snapshot, reason: 'network-error' } : held ?? { phase: 'idle' }
  })
  const current = useRef(state), latest = useRef({ binding, onRecorded }), alive = useRef(false)
  latest.current = { binding, onRecorded }
  useEffect(() => { alive.current = true; return () => { alive.current = false } }, [])
  async function send(snapshot: AttemptSnapshot<PlanExcessCommand>) {
    if (current.current.phase === 'submitting' || latest.current.binding !== snapshot.binding) return
    const previous = current.current, captured = boundary.capture(), flight = beginSend<PlanExcessCommand, PlanExcessResult>(snapshot)
    current.current = flight; setState(flight); hold(snapshot.binding, flight)
    const result = await sendPlanExcess(snapshot)
    if (result.type === 'http') boundary.report(result.status, captured)
    const next = applyOutcome(flight, snapshot, snapshot.binding, result, previous)
    hold(snapshot.binding, next)
    if (!alive.current || latest.current.binding !== snapshot.binding) return
    current.current = next; setState(next)
    if (next.phase === 'completed') { void queries.invalidateQueries({ queryKey: ['logistics-plan-excess'] }); latest.current.onRecorded() }
  }
  function submit(path: string, payload: PlanExcessCommand) {
    if (binding === null || company === null) return
    const old = current.current.phase === 'completed' ? { phase: 'idle' as const } : current.current
    const decision = decideSubmit(old, { binding, companyId: company, path, payload }, generateAttemptKey)
    if (decision.type === 'send') void send(decision.snapshot)
  }
  return { state, submit, frozen: state.phase === 'submitting' || isOutcomeStillUnknown(state) && state.phase !== 'abandoned', initialPayload: state.phase === 'idle' ? null : state.snapshot.payload,
    retry: () => { const snapshot = liveSnapshot(current.current); if (snapshot !== null) void send(snapshot) },
    leave: () => { const next = abandon(current.current); current.current = next; setState(next); if (binding !== null) hold(binding, next) } }
}
export function PlanExcessCommandNotice({ command }: { readonly command: ReturnType<typeof usePlanExcessCommand> }) {
  const { state } = command
  if (state.phase === 'idle') return null
  const unknown = state.phase === 'unknown' || state.phase === 'abandoned' || state.phase === 'failed' && state.unresolvedSince !== undefined
  const reason = state.phase === 'unknown' || state.phase === 'abandoned' ? state.reason : state.phase === 'failed' ? state.unresolvedSince : undefined
  return <div aria-live="polite">
    <p role="status">{state.phase === 'submitting' ? T.sending : unknown ? T.unknown : state.phase === 'completed' ? state.result.isReplay ? T.replayed : T.completed : T.refused}</p>
    {reason !== undefined ? <StateBlock subject={T.subject} state={{ kind: reason }} /> : null}
    {state.phase === 'failed' && !unknown && state.failure !== 'validation' && state.failure !== 'unexpected'
      ? <StateBlock subject={T.subject} state={{ kind: state.failure }} /> : null}
    {unknown ? <><Button type="button" onClick={command.retry}>{T.retry}</Button><Button type="button" onClick={command.leave}>{T.abandon}</Button></> : null}
  </div>
}
export function PlanExcessPreparationPanel(props: { readonly instructionId: string; readonly instructionVersion: string; readonly lineId: string; readonly lineNo: number; readonly instructed: string; readonly onRecorded: () => void }) {
  const shell = useShellState(), binding = attemptBinding(shell, `plan-excess.prepare:${props.instructionId}:${props.lineId}`)
  if (binding === null || !hasCapability(shell, PLAN_EXCESS_CAPABILITY.prepare)) return null
  return <Preparation key={`${binding}|${shell.requestFenceGeneration}|${shell.routeEntryGeneration}`} {...props} />
}
function Preparation(props: Parameters<typeof PlanExcessPreparationPanel>[0]) {
  const command = usePlanExcessCommand(`plan-excess.prepare:${props.instructionId}:${props.lineId}`, props.onRecorded)
  const initial = command.initialPayload !== null && 'exceptionId' in command.initialPayload ? command.initialPayload : null
  const [quantity, setQuantity] = useState(initial?.quantity ?? ''), [reason, setReason] = useState(initial?.reason ?? ''), [error, setError] = useState(false)
  function submit(event: FormEvent) {
    event.preventDefault(); const parsed = parseQuantityInput(quantity), note = reason.trim()
    if (!parsed.ok || note.length === 0 || note.length > 500 || /[\u0000-\u001f\u007f]/.test(note)) { setError(true); return }
    setError(false)
    const original = command.initialPayload !== null && 'exceptionId' in command.initialPayload ? command.initialPayload : null
    const payload = command.state.phase !== 'completed' && original !== null && original.expectedInstructionVersion === props.instructionVersion && original.quantity === parsed.value && original.reason === note
      ? original : { exceptionId: generateClientId(), expectedInstructionVersion: props.instructionVersion, quantity: parsed.value, reason: note }
    command.submit(proposePlanExcessPath(props.instructionId, props.lineId), payload)
  }
  return <section className={styles.section} aria-label={`${T.prepare} ${props.lineNo}`}>
    <h3>{T.prepare} · {T.line} {props.lineNo}</h3><p>{T.explanation}</p><p role="note">{T.warning}</p><p>{T.instructed}: {props.instructed}</p>
    <PlanExcessCommandNotice command={command} />
    <form onSubmit={submit} noValidate><fieldset disabled={command.frozen}><legend>{T.prepare}</legend>
      <Field label={T.quantity} error={error ? T.invalid : undefined}>{control => <input {...control} inputMode="decimal" value={quantity} onChange={event => setQuantity(event.target.value)} />}</Field>
      <Field label={T.reason}>{control => <textarea {...control} value={reason} maxLength={500} onChange={event => setReason(event.target.value)} />}</Field>
      <Button type="submit">{T.save}</Button>
    </fieldset></form>
  </section>
}
/** Synthetic tests only: no keys or payloads are written to browser storage. */
export function resetPlanExcessAttempts() { pending.clear(); settled.clear() }
