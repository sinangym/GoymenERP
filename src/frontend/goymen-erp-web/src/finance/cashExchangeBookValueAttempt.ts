import type { BookValueAttempt } from './cashExchangeBookValue'

export interface PendingBookValueAttempt {
  readonly transferId: string
  readonly attempt: BookValueAttempt
}

// Tab memory only. No source document, amount or command is written to browser storage.
const pending = new Map<string, PendingBookValueAttempt>()
let sessionOwner: string | null = null

export const bookValueAttemptBinding = (namespace: string, sessionEpoch: number, actorId: string, companyId: string) =>
  JSON.stringify([namespace, sessionEpoch, actorId, companyId])

export function synchronizeBookValueAttemptSession(sessionEpoch: number, actorId: string | null): void {
  const current = actorId === null ? null : JSON.stringify([sessionEpoch, actorId])
  if (current === null || sessionOwner !== current) pending.clear()
  sessionOwner = current
}

export const recallBookValueAttempt = (binding: string) => pending.get(binding) ?? null
export function rememberBookValueAttempt(binding: string, value: PendingBookValueAttempt): void {
  pending.set(binding, structuredClone(value))
}
export function forgetBookValueAttempt(binding: string, key: string): void {
  if (pending.get(binding)?.attempt.key === key) pending.delete(binding)
}
export function clearBookValueAttempts(): void {
  pending.clear()
  sessionOwner = null
}
