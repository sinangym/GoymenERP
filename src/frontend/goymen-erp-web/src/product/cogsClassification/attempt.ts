import type { ShellState } from '../../shell/shellState'
import { COGS_API_BASE, type CogsCommandOutcome } from './api'
import type { CogsMutation, SetCogsRequest } from './contract'

export interface CogsSnapshot {
  readonly binding: string; readonly companyId: string; readonly productId: string
  readonly request: SetCogsRequest; readonly fingerprint: string; readonly key: string
}
export type CogsAttempt =
  | { readonly phase: 'idle' }
  | { readonly phase: 'sending'; readonly snapshot: CogsSnapshot; readonly unresolved: boolean }
  | { readonly phase: 'unknown'; readonly snapshot: CogsSnapshot; readonly reason: Extract<CogsCommandOutcome, { type: 'unknown' }>['reason'] }
  | { readonly phase: 'failed'; readonly snapshot: CogsSnapshot; readonly status: number; readonly unresolved: boolean }
  | { readonly phase: 'completed'; readonly snapshot: CogsSnapshot; readonly result: CogsMutation }
const idle: CogsAttempt = Object.freeze({ phase: 'idle' })
const records = new Map<string, CogsAttempt>()
const listeners = new Map<string, Set<() => void>>()
export function cogsBinding(shell: ShellState, productId: string): string | null {
  const context = shell.activeContext
  return context?.scopeMode === 'single' && context.companyId !== null && !shell.sessionInvalid
    ? JSON.stringify([COGS_API_BASE, shell.sessionEpoch, context.userId, context.companyId.toLowerCase(), productId.toLowerCase(), 'cogs-classification']) : null
}
export function cogsAttempt(binding: string | null): CogsAttempt { return binding === null ? idle : records.get(binding) ?? idle }
export function subscribeCogsAttempt(binding: string | null, listener: () => void): () => void {
  if (binding === null) return () => {}
  const set = listeners.get(binding) ?? new Set<() => void>()
  set.add(listener); listeners.set(binding, set)
  return () => { set.delete(listener); if (set.size === 0) listeners.delete(binding) }
}
function publish(binding: string, value: CogsAttempt) { records.set(binding, value); listeners.get(binding)?.forEach((listener) => listener()) }
export function cogsAttemptFrozen(value: CogsAttempt): boolean {
  return value.phase === 'sending' || value.phase === 'unknown' || (value.phase === 'failed' && value.unresolved)
}
export function beginCogsAttempt(binding: string | null, companyId: string, productId: string, request: SetCogsRequest,
  newKey: () => string = () => `cogs-${crypto.randomUUID()}`): CogsSnapshot | null {
  if (binding === null) return null
  const current = cogsAttempt(binding)
  if (current.phase === 'sending') return null
  const fingerprint = JSON.stringify(request)
  if (cogsAttemptFrozen(current) && current.phase !== 'idle') {
    if (current.snapshot.fingerprint !== fingerprint || current.snapshot.companyId.toLowerCase() !== companyId.toLowerCase()
      || current.snapshot.productId.toLowerCase() !== productId.toLowerCase()) return null
    publish(binding, { phase: 'sending', snapshot: current.snapshot, unresolved: true })
    return current.snapshot
  }
  const snapshot = Object.freeze({ binding, companyId, productId, request: Object.freeze({ ...request }), fingerprint, key: newKey() })
  publish(binding, { phase: 'sending', snapshot, unresolved: false })
  return snapshot
}
export function finishCogsAttempt(snapshot: CogsSnapshot, outcome: CogsCommandOutcome) {
  const current = cogsAttempt(snapshot.binding)
  if (current.phase === 'idle' || current.phase === 'completed' || current.snapshot.key !== snapshot.key
    || (current.phase === 'failed' && !current.unresolved)) return
  const unresolved = current.phase === 'unknown' || current.unresolved
  if (outcome.type === 'applied') publish(snapshot.binding, { phase: 'completed', snapshot, result: outcome.data })
  else if (outcome.type === 'unknown') publish(snapshot.binding, { phase: 'unknown', snapshot, reason: outcome.reason })
  else publish(snapshot.binding, { phase: 'failed', snapshot, status: outcome.status, unresolved: unresolved && !outcome.terminalReplay })
}
export function leaveCogsAttempt(binding: string | null) {
  const current = cogsAttempt(binding)
  if (binding !== null && current.phase === 'sending') publish(binding, { phase: 'unknown', snapshot: current.snapshot, reason: 'network-error' })
}
export function resetCogsAttemptsForTests() { records.clear(); listeners.clear() }
