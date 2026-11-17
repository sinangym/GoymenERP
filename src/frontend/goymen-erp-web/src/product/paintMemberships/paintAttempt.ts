/** In-memory retained command identity. No file bytes, keys, actor or preview token enter browser storage or a URL. */
import type { ShellState } from '../../shell/shellState'
import { PAINT_API_BASE, type PaintCommandOutcome } from './api'
import type { PaintCommand, PaintMutation } from './contract'

export interface PaintSnapshot {
  readonly binding: string
  readonly companyId: string
  readonly command: PaintCommand
  readonly fingerprint: string
  readonly key: string
}
export type PaintAttempt =
  | { readonly phase: 'idle' }
  | { readonly phase: 'sending'; readonly snapshot: PaintSnapshot; readonly unresolved: boolean }
  | { readonly phase: 'unknown'; readonly snapshot: PaintSnapshot; readonly reason: Extract<PaintCommandOutcome, { type: 'unknown' }>['reason'] }
  | { readonly phase: 'failed'; readonly snapshot: PaintSnapshot; readonly status: number; readonly body: unknown; readonly unresolved: boolean }
  | { readonly phase: 'completed'; readonly snapshot: PaintSnapshot; readonly result: PaintMutation }
const idle: PaintAttempt = Object.freeze({ phase: 'idle' })
const attempts = new Map<string, PaintAttempt>()
const listeners = new Map<string, Set<() => void>>()

export function paintBinding(shell: ShellState): string | null {
  const context = shell.activeContext
  return context?.scopeMode === 'single' && context.companyId !== null && !shell.sessionInvalid
    ? JSON.stringify([PAINT_API_BASE, shell.sessionEpoch, context.userId, context.companyId, 'paint-memberships']) : null
}
export function paintAttempt(binding: string | null): PaintAttempt { return binding === null ? idle : attempts.get(binding) ?? idle }
export function subscribePaintAttempt(binding: string | null, listener: () => void): () => void {
  if (binding === null) return () => {}
  const set = listeners.get(binding) ?? new Set<() => void>()
  set.add(listener)
  listeners.set(binding, set)
  return () => { set.delete(listener); if (set.size === 0) listeners.delete(binding) }
}
function publish(binding: string, value: PaintAttempt) {
  attempts.set(binding, value)
  listeners.get(binding)?.forEach((listener) => listener())
}
export function paintAttemptFrozen(value: PaintAttempt): boolean {
  return value.phase === 'sending' || value.phase === 'unknown' || (value.phase === 'failed' && value.unresolved)
}
/** A different payload cannot hide a prior unknown command. Retrying takes the frozen snapshot, never a live form. */
export function beginPaintAttempt(binding: string | null, companyId: string | null, command: PaintCommand,
  newKey: () => string = () => `paint-${crypto.randomUUID()}`): PaintSnapshot | null {
  if (binding === null || companyId === null) return null
  const current = paintAttempt(binding)
  if (current.phase === 'sending') return null
  const fingerprint = JSON.stringify(command)
  if (paintAttemptFrozen(current) && current.phase !== 'idle') {
    if (current.snapshot.fingerprint !== fingerprint) return null
    publish(binding, { phase: 'sending', snapshot: current.snapshot, unresolved: true })
    return current.snapshot
  }
  const payload = Object.freeze({ ...command.payload })
  const immutable: PaintCommand = command.kind === 'import'
    ? Object.freeze({ ...command, payload: payload as typeof command.payload, expectedProductIds: Object.freeze([...command.expectedProductIds]) })
    : Object.freeze({ ...command, payload: payload as typeof command.payload })
  const snapshot = Object.freeze({ binding, companyId, command: immutable, fingerprint, key: newKey() })
  publish(binding, { phase: 'sending', snapshot, unresolved: false })
  return snapshot
}
export function finishPaintAttempt(snapshot: PaintSnapshot, outcome: PaintCommandOutcome): void {
  const current = paintAttempt(snapshot.binding)
  if (current.phase === 'idle' || current.phase === 'completed' || current.snapshot.key !== snapshot.key
    || (current.phase === 'failed' && !current.unresolved)) return
  const unresolved = current.phase === 'unknown' || current.unresolved
  if (outcome.type === 'applied') publish(snapshot.binding, { phase: 'completed', snapshot, result: outcome.data })
  else if (outcome.type === 'unknown') publish(snapshot.binding, { phase: 'unknown', snapshot, reason: outcome.reason })
  else publish(snapshot.binding, { phase: 'failed', snapshot, status: outcome.status, body: outcome.body,
    unresolved: unresolved && outcome.terminalReplay !== true })
}
export function leavePaintAttempt(binding: string | null): void {
  const current = paintAttempt(binding)
  if (binding !== null && current.phase === 'sending') publish(binding, { phase: 'unknown', snapshot: current.snapshot, reason: 'network-error' })
}
export function resetPaintAttemptsForTests(): void { attempts.clear(); listeners.clear() }
