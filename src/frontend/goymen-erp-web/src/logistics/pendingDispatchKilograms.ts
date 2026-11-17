import { isOutcomeStillUnknown, type AttemptState } from './instructionAttempt'
import type { KgCommand, KgResponse } from './dispatchKilogramsContract'
type State = AttemptState<KgCommand, KgResponse>
const held = new Map<string, State>()
const settled = new Set<string>()
export function rememberKgAttempt(slot: string, state: State): void {
  if (state.phase === 'idle') return
  const token = `${slot}|${state.snapshot.key}`
  if (settled.has(token)) return
  if (isOutcomeStillUnknown(state)) { held.set(slot, state); return }
  settled.add(token)
  if (held.get(slot)?.phase !== 'idle' && (held.get(slot) as Exclude<State, { phase: 'idle' }> | undefined)?.snapshot.key === state.snapshot.key) held.delete(slot)
}
export function recallKgAttempt(slot: string | null): State {
  const state = slot === null ? undefined : held.get(slot)
  return state?.phase === 'submitting' ? { phase: 'unknown', snapshot: state.snapshot, reason: 'network-error' } : state ?? { phase: 'idle' }
}
