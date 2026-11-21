import type { ShellState } from '../shell/shellState'
import { API_BASE } from './api'
import { isTransferGuid, isTransferInstant, isTransferVersion } from './transferContract'

export const TRANSFER_CANCEL_REASON_LIMIT = 500
export interface TransferCancelRequest {
  readonly expectedTransferVersion: string
  readonly cancelledAtUtc: string
  readonly reason: string
}
export interface TransferCancelAttempt {
  readonly binding: string
  readonly companyId: string
  readonly transferId: string
  readonly key: string
  readonly payload: TransferCancelRequest
}
export type TransferCancelRecall = { readonly type: 'none' } | { readonly type: 'pending'; readonly attempt: TransferCancelAttempt }
  | { readonly type: 'unavailable' }

export function transferCancelBinding(shell: Pick<ShellState, 'activeContext' | 'sessionBinding'>, transferId: string): string | null {
  const context = shell.activeContext
  if (context === null || context.scopeMode !== 'single' || context.companyId === null || !isTransferGuid(transferId)
    || typeof shell.sessionBinding !== 'string' || shell.sessionBinding.length === 0) return null
  // Persisted commands use the SERVER session identity; sessionEpoch restarts at zero after an F5. The namespace that fences
  // reads changes on a capability refresh and must not invite a second key for an unresolved cancellation.
  return JSON.stringify([API_BASE, context.userId, shell.sessionBinding, context.companyId, 'cancel-dispatch', transferId])
}
export function isTransferCancelRequest(value: unknown): value is TransferCancelRequest {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return false
  const item = value as Partial<TransferCancelRequest>
  return Object.keys(item).length === 3 && isTransferVersion(item.expectedTransferVersion)
    && isTransferInstant(item.cancelledAtUtc) && /(?:Z|\+00:00)$/.test(item.cancelledAtUtc)
    && typeof item.reason === 'string' && item.reason === item.reason.trim() && item.reason.length > 0
    && item.reason.length <= TRANSFER_CANCEL_REASON_LIMIT && !/[\u0000-\u001f\u007f-\u009f]/.test(item.reason)
}
function storageKey(binding: string): string { return `goymen.inventory.transfer-cancel.v1:${encodeURIComponent(binding)}` }
export function recallTransferCancel(binding: string, companyId: string, transferId: string): TransferCancelRecall {
  try {
    const raw = sessionStorage.getItem(storageKey(binding))
    if (raw === null) return { type: 'none' }
    const item: unknown = JSON.parse(raw)
    if (typeof item !== 'object' || item === null || Array.isArray(item)) return { type: 'unavailable' }
    const value = item as Partial<TransferCancelAttempt>
    return Object.keys(value).length === 5 && value.binding === binding && value.companyId === companyId
      && value.transferId === transferId && isTransferGuid(value.key) && isTransferCancelRequest(value.payload)
      ? { type: 'pending', attempt: value as TransferCancelAttempt } : { type: 'unavailable' }
  } catch { return { type: 'unavailable' } }
}
/** Do not send a command if its unresolved attempt cannot be retained, or overwrite another unresolved key. */
export function reserveTransferCancel(attempt: TransferCancelAttempt): boolean {
  const stored = recallTransferCancel(attempt.binding, attempt.companyId, attempt.transferId)
  if (stored.type === 'unavailable' || stored.type === 'pending' && JSON.stringify(stored.attempt) !== JSON.stringify(attempt)) return false
  try {
    sessionStorage.setItem(storageKey(attempt.binding), JSON.stringify(attempt))
    const saved = recallTransferCancel(attempt.binding, attempt.companyId, attempt.transferId)
    return saved.type === 'pending' && JSON.stringify(saved.attempt) === JSON.stringify(attempt)
  } catch { return false }
}
export function settleTransferCancel(attempt: TransferCancelAttempt): boolean {
  const current = recallTransferCancel(attempt.binding, attempt.companyId, attempt.transferId)
  if (current.type === 'none') return true
  if (current.type !== 'pending' || current.attempt.key !== attempt.key) return false
  try {
    sessionStorage.removeItem(storageKey(attempt.binding))
    return recallTransferCancel(attempt.binding, attempt.companyId, attempt.transferId).type === 'none'
  } catch { return false /* A later explicit retry still uses this same key. */ }
}
