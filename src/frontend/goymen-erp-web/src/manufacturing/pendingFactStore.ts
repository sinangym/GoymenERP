/**
 * The one outstanding fact of an order screen, kept OUTSIDE the component.
 *
 * A fact whose outcome is unknown (no response, a 5xx, an unreadable answer, or a
 * request still in flight when the screen unmounted) may have been recorded. Its frozen
 * snapshot — fact id AND Idempotency-Key — is the only safe way to find out: re-sending
 * it is answered as the same command (ADR-011) and the same fact (R3). Losing it on an
 * unmount would leave the user a fresh form, and a fresh submit would be a SECOND fact.
 *
 * So the snapshot lives here until the server settles it, keyed by session epoch, actor,
 * company and order: it survives a remount and a capability refence, and never crosses
 * into another session, actor, company or order.
 *
 * Module state, memory only: nothing is written to web storage, and a page reload ends
 * it (the order read then shows whether the fact is there).
 */

import type { FactSnapshot } from './contract'

export interface PendingFact {
  readonly snapshot: FactSnapshot
  /** How many times the same snapshot was re-sent. */
  readonly resends: number
}

const store = new Map<string, PendingFact>()

export function pendingFactKey(
  sessionEpoch: number,
  userId: string | null,
  companyId: string | null,
  orderId: string,
): string | null {
  if (userId === null || companyId === null || orderId.length === 0) return null
  return JSON.stringify([sessionEpoch, userId, companyId, orderId])
}

export function recallPendingFact(key: string | null): PendingFact | null {
  return key === null ? null : (store.get(key) ?? null)
}

/** Reserved BEFORE the request leaves, so an unmount mid-flight cannot lose it. */
export function reservePendingFact(key: string | null, pending: PendingFact): void {
  if (key !== null) store.set(key, pending)
}

/** Releases exactly this snapshot, never whatever newer one holds the slot. */
export function releasePendingFact(key: string | null, idempotencyKey: string): void {
  if (key === null) return
  if (store.get(key)?.snapshot.idempotencyKey === idempotencyKey) store.delete(key)
}

/** Test isolation only. */
export function clearPendingFacts(): void {
  store.clear()
}
