import type { AttemptBinding } from './receiptCommand'
import type { BalanceTransferRecord } from './PartyBalanceTransferContract'
export interface BalanceTransferAttempt { readonly key: string; readonly request: BalanceTransferRecord; readonly phase: 'sending' | 'unknown' }
const attempts = new Map<string, BalanceTransferAttempt>()
export const balanceTransferSlot = (binding: AttemptBinding) => JSON.stringify([binding.sessionEpoch, binding.actorId, binding.companyId, 'party-balance-transfer'])
export const recallBalanceTransfer = (slot: string) => attempts.get(slot) ?? null
export function recoverBalanceTransfer(slot: string): BalanceTransferAttempt | null {
  const prior = attempts.get(slot)
  if (prior === undefined) return null
  const value = { ...prior, phase: 'unknown' as const }
  attempts.set(slot, value)
  return value
}
export function reserveBalanceTransfer(slot: string, request: BalanceTransferRecord): BalanceTransferAttempt | null {
  if (attempts.has(slot)) return null
  const frozen = Object.freeze({ ...request, selection: Object.freeze({ ...request.selection }), evidence: Object.freeze({ ...request.evidence }) })
  const value = { key: crypto.randomUUID(), request: frozen, phase: 'sending' as const }
  attempts.set(slot, value)
  return value
}
export function retryBalanceTransfer(slot: string): BalanceTransferAttempt | null {
  const prior = attempts.get(slot)
  if (prior?.phase !== 'unknown') return null
  const value = { ...prior, phase: 'sending' as const }
  attempts.set(slot, value)
  return value
}
export function markBalanceTransferUnknown(slot: string, key: string): BalanceTransferAttempt | null {
  const prior = attempts.get(slot)
  if (prior?.key !== key) return null
  const value = { ...prior, phase: 'unknown' as const }
  attempts.set(slot, value)
  return value
}
export function settleBalanceTransfer(slot: string, key: string) {
  if (attempts.get(slot)?.key === key) attempts.delete(slot)
}
