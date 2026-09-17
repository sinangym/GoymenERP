/**
 * The per-item coverage record of a browser journey, and how a finished case settles it.
 *
 * A PURE MODULE ON PURPOSE. Nothing here imports Playwright, so the one rule that decides
 * whether `coverage.json` can say MEASURED about a red case is checkable without a browser.
 *
 * THE RULE. The Playwright outcome of the case wins over anything the case wrote into
 * coverage while it ran: a case that did not end as expected turns every item it claimed
 * into FAILED — including an item it had already marked MEASURED before a later step
 * (a screenshot, an evidence file) failed. Business #66 5705730345 reads `status`; a red
 * case must never leave a MEASURED behind for that reader.
 */
import type { ActorGrant } from './manifest.js'

/** A closed set: a gate reads `status`, never searches `detail` for a word. */
export type ItemStatus = 'MEASURED' | 'SKIPPED' | 'FAILED' | 'NOT_MEASURED'

export interface ItemEvidence {
  status: ItemStatus
  detail: string
  actor: string | null
  /** As declared in the manifest for this actor; null for normal-seed actors. */
  grant: ActorGrant | null
  observed?: Record<string, unknown>
}

/** The placeholder a claimed item carries until the case settles it. */
export const UNSETTLED = 'the case started and did not reach its end'

/** Playwright's own verdict on the case, reduced to what settling needs. */
export interface CaseOutcome {
  readonly status: string
  readonly expectedStatus: string
  readonly skipReason?: string
  readonly error?: string
}

export function settleClaimed<K extends string>(
  coverage: Record<K, ItemEvidence>,
  claimed: readonly K[],
  outcome: CaseOutcome,
): void {
  const skipped = outcome.status === 'skipped'
  const asExpected = outcome.status === outcome.expectedStatus
  for (const key of claimed) {
    const item = coverage[key]
    if (skipped) {
      if (item.detail === UNSETTLED) {
        coverage[key] = { ...item, status: 'SKIPPED', detail: outcome.skipReason ?? 'skipped' }
      }
    } else if (!asExpected) {
      // Unconditional: whatever the case settled before it failed does not survive.
      coverage[key] = { ...item, status: 'FAILED', detail: outcome.error ?? UNSETTLED, observed: undefined }
    } else if (item.detail === UNSETTLED) {
      coverage[key] = { ...item, status: 'FAILED', detail: 'the case passed without settling this item' }
    }
  }
}
