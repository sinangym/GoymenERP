import type { BrandingCommand } from './brandingApi'
import type { UploadAttempt } from './api'

/** Unknown writes keep the same key AND immutable payload/file across route unmounts, bound to actor/session/company/origin. */
export type BrandingAttempt = ({ readonly kind: 'command'; readonly key: string; readonly command: BrandingCommand }
  | { readonly kind: 'upload'; readonly attempt: UploadAttempt; readonly slot: 'logo' | 'letterhead' })
  & { readonly sending: boolean; readonly unresolved?: boolean }
const entries = new Map<string, BrandingAttempt>()
const listeners = new Set<() => void>()
export const brandingAttempt = (binding: string) => entries.get(binding) ?? null
export function setBrandingAttempt(binding: string, attempt: BrandingAttempt | null) {
  if (attempt === null) entries.delete(binding)
  else if (!entries.has(binding)) entries.set(binding, attempt)
  listeners.forEach((listener) => listener())
}
export const subscribeBrandingAttempt = (listener: () => void) => { listeners.add(listener); return () => { listeners.delete(listener) } }
export function beginBrandingFlight(binding: string): BrandingAttempt | null {
  const current = entries.get(binding)
  if (current === undefined || current.sending) return null
  const next = { ...current, sending: true }; entries.set(binding, next); listeners.forEach((listener) => listener()); return next
}
/** `unresolved`: this send's outcome is unknown. Once set it stays set until the attempt is released. */
export function endBrandingFlight(binding: string, unresolved = false) {
  const current = entries.get(binding)
  if (current !== undefined) {
    entries.set(binding, { ...current, sending: false, unresolved: current.unresolved === true || unresolved })
    listeners.forEach((listener) => listener())
  }
}

/**
 * K3 P2-8 (CashTransfer F1 pattern): whether a refusal settles the attempt. A refusal of a FIRST send is definite —
 * 401/403 included — and releases the key. After an unknown send, a 401/403 or a code-less 400/404/415 is answered
 * before the owner reads the idempotency receipt, so it says nothing about the earlier send: the key is kept.
 */
export function refusalSettlesBrandingAttempt(attempt: BrandingAttempt, status: number, code: string | null): boolean {
  if (attempt.unresolved !== true) return true
  if (status === 401 || status === 403) return false
  return !([400, 404, 415].includes(status) && code === null)
}
