import type { GoodsReceiptRequest } from './goodsReceiptContract'

export interface ReceiptAttempt { readonly binding: string; readonly key: string; readonly payload: GoodsReceiptRequest }
// Same actor/session/company/order/line scope supplied by the shell. No raw key/payload in URL, storage or logs.
const pending = new Map<string, ReceiptAttempt>()
export const recallReceiptAttempt = (binding: string): ReceiptAttempt | null => pending.get(binding) ?? null
export function reserveReceiptAttempt(attempt: ReceiptAttempt): void {
  const current = pending.get(attempt.binding)
  if (current !== undefined && current.key !== attempt.key) throw new Error('An unresolved receipt cannot be replaced.')
  pending.set(attempt.binding, attempt)
}
export function settleReceiptAttempt(attempt: ReceiptAttempt): void {
  if (pending.get(attempt.binding)?.key === attempt.key) pending.delete(attempt.binding)
}
export function receiptIdentity(): string {
  const bytes = new Uint8Array(16)
  globalThis.crypto.getRandomValues(bytes)
  bytes[6] = bytes[6] & 0x0f | 0x40
  bytes[8] = bytes[8] & 0x3f | 0x80
  const value = Array.from(bytes, (byte) => byte.toString(16).padStart(2, '0')).join('')
  return `${value.slice(0, 8)}-${value.slice(8, 12)}-${value.slice(12, 16)}-${value.slice(16, 20)}-${value.slice(20)}`
}
export function resetReceiptAttemptsForTests(): void { pending.clear() }
