import type { BrandingCommand } from './brandingApi'
import type { UploadAttempt } from './api'

/** Unknown writes keep the same key AND immutable payload/file across route unmounts, bound to actor/session/company/origin. */
export type BrandingAttempt = ({ readonly kind: 'command'; readonly key: string; readonly command: BrandingCommand }
  | { readonly kind: 'upload'; readonly attempt: UploadAttempt; readonly slot: 'logo' | 'letterhead' }) & { readonly sending: boolean }
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
export function endBrandingFlight(binding: string) {
  const current = entries.get(binding)
  if (current !== undefined) { entries.set(binding, { ...current, sending: false }); listeners.forEach((listener) => listener()) }
}
