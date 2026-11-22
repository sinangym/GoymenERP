/**
 * F-009 D6: the one outstanding output correction of an order screen, kept OUTSIDE the component (the `pendingFactStore`
 * pattern). A correction whose outcome is unknown — no response, a 5xx, an unreadable answer, or a request still in flight
 * when the section unmounted — may have taken the kilograms back already. Its frozen snapshot (reversal id, movement id and
 * Idempotency-Key) is the only safe way to find out: re-sending it is answered as the same command. Losing it on a remount
 * would give the user a fresh form, and a fresh submit would take the mass back a SECOND time.
 *
 * Keyed like the pending fact (session epoch, actor, company, order — `pendingFactKey`): it survives a remount and a
 * capability refresh and never crosses into another session, actor, company or order. Memory only; a reload ends it.
 */
import type { FactUnknown } from './commandApi'
import type { OutputCorrectionSnapshot } from './contract'

export interface PendingCorrection {
  readonly snapshot: OutputCorrectionSnapshot
  /** The last unknown outcome; an attempt still in flight when the section unmounted reads as aborted. */
  readonly unknown: FactUnknown
}

const store = new Map<string, PendingCorrection>()

export function recallPendingCorrection(key: string | null): PendingCorrection | null {
  return key === null ? null : (store.get(key) ?? null)
}

/** Reserved BEFORE the request leaves, so an unmount mid-flight cannot lose it. */
export function reservePendingCorrection(key: string | null, pending: PendingCorrection): void {
  if (key !== null) store.set(key, pending)
}

/** Releases exactly this snapshot, never whatever newer one holds the slot. */
export function releasePendingCorrection(key: string | null, idempotencyKey: string): void {
  if (key === null) return
  if (store.get(key)?.snapshot.idempotencyKey === idempotencyKey) store.delete(key)
}

/** Test isolation only. */
export function clearPendingCorrections(): void {
  store.clear()
}
