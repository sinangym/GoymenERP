/**
 * The one outstanding period command of a screen, kept OUTSIDE the component.
 *
 * A command whose outcome is unknown (no response, a 5xx, an unreadable answer, or a request still
 * in flight when the screen unmounted) may have been stored. Its frozen snapshot — key AND period
 * id — is the only safe way to find out: re-sending it is answered as the same command (ADR-011).
 * Losing it on an unmount would leave a fresh form, and a fresh proposal would be a second attempt.
 *
 * So the snapshot lives here until the server settles it, keyed by session epoch, actor, company and
 * screen ("propose" or "periods"): it survives a remount and never crosses into another session,
 * actor or company. Module state, memory only: nothing is written to web storage, and a page reload
 * ends it (the period list then shows whether the command took effect).
 */

import type { PeriodCommandSnapshot } from './contract'

export type PeriodCommandScreen = 'propose' | 'periods'

export interface PendingPeriodCommand {
  readonly snapshot: PeriodCommandSnapshot
  readonly resends: number
}

const store = new Map<string, PendingPeriodCommand>()

export function pendingPeriodKey(
  sessionEpoch: number,
  userId: string | null,
  companyId: string | null,
  screen: PeriodCommandScreen,
): string | null {
  if (userId === null || companyId === null) return null
  return JSON.stringify([sessionEpoch, userId, companyId, screen])
}

export function recallPendingPeriodCommand(key: string | null): PendingPeriodCommand | null {
  return key === null ? null : (store.get(key) ?? null)
}

/** Reserved BEFORE the request leaves, so an unmount mid-flight cannot lose it. */
export function reservePendingPeriodCommand(key: string | null, pending: PendingPeriodCommand): void {
  if (key !== null) store.set(key, pending)
}

/** Releases exactly this snapshot, never whatever newer one holds the slot. */
export function releasePendingPeriodCommand(key: string | null, idempotencyKey: string): void {
  if (key === null) return
  if (store.get(key)?.snapshot.idempotencyKey === idempotencyKey) store.delete(key)
}

/** Test isolation only. */
export function clearPendingPeriodCommands(): void {
  store.clear()
}
