import type { GoodsReceiptRequest } from './goodsReceiptContract'
import type { ShellState } from '../shell/shellState'

export interface ReceiptAttempt { readonly binding: string; readonly key: string; readonly payload: GoodsReceiptRequest }
// Bound to the durable actor/session/company identity below plus the order and line. No raw key/payload in URL,
// storage or logs: memory only, so a full page reload ends the session's in-flight work (BLG-09 is separate).
const pending = new Map<string, ReceiptAttempt>()

/**
 * BLG-01 — the DURABLE identity a pending receipt is bound to: the actor, the shell's real `sessionEpoch` and the
 * committed single company. The API origin is not part of it: it is fixed per built document and this store lives
 * in memory for one document only, so an attempt can never meet another server (the order approval on the same page
 * keys the same way).
 *
 * The capability generation and the committed context revision are read NOWHERE here, deliberately. They belong to
 * the query namespace (`contextKeyString`), which fences DATA ACCESS and changes on a 403 re-fence or a fresh
 * authorized entry while the same person, in the same session, is still looking at the same receipt. Binding the
 * attempt to that namespace lost it on such a refresh and invited a second idempotency key for a receipt the server
 * may already have applied — the flaw `product/pendingAttempt.ts` records and fixed for Product, and the reason the
 * order approval on the same page already keys by session/actor/company (`pendingApprovalKey`).
 *
 * Fresh ACCESS stays a separate fence: the panel mounts only under the published capability, and a retry goes
 * through the same auth boundary as the first send. Recalling an attempt shows it and waits for a person; nothing is
 * retried on its own. A logout or actor change advances `sessionEpoch`, so a new session never reaches an old attempt.
 */
export function receiptAttemptBinding(shell: Pick<ShellState, 'activeContext' | 'sessionEpoch'>): string | null {
  const context = shell.activeContext
  if (context === null || context.scopeMode !== 'single' || context.companyId === null) return null
  return JSON.stringify(['goymen.purchasing.receipt.v1', context.userId, String(shell.sessionEpoch), context.companyId])
}

/** One order line's slot under a durable binding (JSON, so no two (binding, order, line) triples collide). */
export function receiptAttemptScope(attemptBinding: string, orderId: string, lineId: string): string {
  return JSON.stringify([attemptBinding, 'receipt', orderId, lineId])
}

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
