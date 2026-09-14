/**
 * Where the location card goes BACK to, and how it learns that.
 *
 * Two screens now open the same card: ORG-01's location list and ORG-03's work-center
 * list. "Back" has to mean the list the user actually came from, or the work-center
 * journey dead-ends on a screen the user may not even be entitled to — a
 * `work-centers.read`-only holder sent to `/ayarlar/organizasyon` lands on the neutral
 * not-found block, which reads as a broken app rather than as a scope boundary.
 *
 * THE ORIGIN IS A TOKEN, NEVER A PATH. React Router's `location.state` survives history
 * entries and is attacker-influenceable in the same way a URL is: it is whatever the
 * previous entry put there, and a back/forward or a hand-crafted `pushState` can put
 * anything there. So this module NEVER navigates to a value it read out of state. It
 * reads a token, looks the token up in the frozen table below, and anything it does not
 * recognise falls back to the location list. An unknown origin is a default, not an
 * error: the card still works, it just goes to the canonical list.
 *
 * The namespace check is the same rule the page number already follows
 * (`OrganizationSettingsPage.returnedPage`): a company switch keeps the history entry,
 * so an origin recorded in company A must not steer a card opened in company B.
 */

import {
  ORGANIZATION_ROUTE,
  ORGANIZATION_CAPABILITY,
  workCenterListPath,
} from './capabilities'

export const DETAIL_ORIGIN = {
  /** ORG-01, the paged location list. The default for everything unrecognised. */
  locations: 'locations',
  /** ORG-03, the work-center list. */
  workCenters: 'work-centers',
} as const

export type DetailOrigin = (typeof DETAIL_ORIGIN)[keyof typeof DETAIL_ORIGIN]

/**
 * What a list puts in `location.state` when it opens the card.
 *
 * `page` belongs to ORG-01 only — the work-center endpoint is unpaged, so there is no
 * page to come back to and the field is simply absent rather than faked as 1.
 */
export interface OrganizationDetailReturn {
  readonly ns: string
  readonly from?: DetailOrigin
  readonly page?: number
}

export interface BackTarget {
  readonly path: string
  readonly label: string
}

/**
 * The one place a token becomes a path. Frozen and exhaustive over `DetailOrigin`, so a
 * new origin is a type error here rather than a silent fallback at runtime.
 */
const TARGETS: Record<DetailOrigin, BackTarget> = {
  [DETAIL_ORIGIN.locations]: {
    path: ORGANIZATION_ROUTE[ORGANIZATION_CAPABILITY.settingsList],
    label: 'Organizasyon',
  },
  [DETAIL_ORIGIN.workCenters]: {
    path: workCenterListPath(),
    label: 'İş Merkezleri',
  },
}

export const DEFAULT_BACK_TARGET: BackTarget = TARGETS[DETAIL_ORIGIN.locations]

function isOrigin(value: unknown): value is DetailOrigin {
  return value === DETAIL_ORIGIN.locations || value === DETAIL_ORIGIN.workCenters
}

/**
 * Resolves the card's back link.
 *
 * Falls back to the location list whenever the state is not a record, carries no
 * origin, carries an origin this build does not know, or was recorded under a different
 * context namespace.
 */
export function backTarget(state: unknown, namespace: string | null): BackTarget {
  if (typeof state !== 'object' || state === null || namespace === null) {
    return DEFAULT_BACK_TARGET
  }
  const candidate = state as Partial<OrganizationDetailReturn>
  if (candidate.ns !== namespace || !isOrigin(candidate.from)) return DEFAULT_BACK_TARGET
  return TARGETS[candidate.from]
}

/**
 * The state ORG-03 attaches to a row link.
 *
 * Returns `undefined` with no namespace rather than a state that claims a context it
 * cannot name — an unnamespaced origin would be honoured after a company switch, which
 * is precisely what the check above exists to stop.
 */
export function workCenterReturnState(
  namespace: string | null,
): OrganizationDetailReturn | undefined {
  return namespace === null
    ? undefined
    : { ns: namespace, from: DETAIL_ORIGIN.workCenters }
}
