import type { PaymentAllocationRequest } from './PaymentAllocationHttp'
export interface PaymentAllocationAttempt { readonly key: string; readonly request: PaymentAllocationRequest; readonly phase: 'sending' | 'unknown' }
const attempts = new Map<string, PaymentAllocationAttempt>()
export const recallPaymentAllocation = (slot: string) => attempts.get(slot) ?? null
export function recoverPaymentAllocation(slot: string): PaymentAllocationAttempt | null {
  const prior = attempts.get(slot)
  if (!prior) return null
  const result = { ...prior, phase: 'unknown' as const }; attempts.set(slot, result); return result
}
export function reservePaymentAllocation(slot: string, request: PaymentAllocationRequest): PaymentAllocationAttempt | null {
  if (attempts.has(slot)) return null
  const result: PaymentAllocationAttempt = { key: crypto.randomUUID(), request: Object.freeze({ ...request, body: Object.freeze({ ...request.body }) }) as PaymentAllocationRequest, phase: 'sending' }
  attempts.set(slot, result); return result
}
export function retryPaymentAllocation(slot: string): PaymentAllocationAttempt | null {
  const prior = attempts.get(slot)
  if (prior?.phase !== 'unknown') return null
  const result = { ...prior, phase: 'sending' as const }; attempts.set(slot, result); return result
}
export function unknownPaymentAllocation(slot: string, key: string): PaymentAllocationAttempt | null {
  const prior = attempts.get(slot)
  if (prior?.key !== key) return null
  const result = { ...prior, phase: 'unknown' as const }; attempts.set(slot, result); return result
}
export function settlePaymentAllocation(slot: string, key: string) { if (attempts.get(slot)?.key === key) attempts.delete(slot) }
