import { ORGANIZATION_CAPABILITY, ORGANIZATION_ROUTE, workCenterListPath } from './capabilities'

export const DETAIL_ORIGIN = {
  locations: 'locations',
  workCenters: 'work-centers',
} as const

export type DetailOrigin = (typeof DETAIL_ORIGIN)[keyof typeof DETAIL_ORIGIN]

export interface OrganizationDetailReturn {
  readonly ns: string
  readonly from?: DetailOrigin
  readonly page?: number
}

export interface DetailBackTarget {
  readonly path: string
  readonly label: string
}

const TARGETS: Record<DetailOrigin, DetailBackTarget> = {
  [DETAIL_ORIGIN.locations]: {
    path: ORGANIZATION_ROUTE[ORGANIZATION_CAPABILITY.settingsList],
    label: 'Organizasyon',
  },
  [DETAIL_ORIGIN.workCenters]: {
    path: ORGANIZATION_ROUTE[ORGANIZATION_CAPABILITY.workCentersList],
    label: 'İş Merkezleri',
  },
}

export const DEFAULT_DETAIL_BACK_TARGET = TARGETS[DETAIL_ORIGIN.locations]

function isDetailOrigin(value: unknown): value is DetailOrigin {
  return value === DETAIL_ORIGIN.locations || value === DETAIL_ORIGIN.workCenters
}

/**
 * Resolve only a known Organization origin recorded under the current context.
 * Router state is caller-controlled, so it supplies a token and never a path.
 */
export function detailBackTarget(state: unknown, namespace: string | null): DetailBackTarget {
  if (
    typeof state !== 'object' ||
    state === null ||
    Array.isArray(state) ||
    namespace === null
  ) {
    return DEFAULT_DETAIL_BACK_TARGET
  }

  const candidate = state as Partial<OrganizationDetailReturn>
  if (candidate.ns !== namespace || !isDetailOrigin(candidate.from)) {
    return DEFAULT_DETAIL_BACK_TARGET
  }

  return TARGETS[candidate.from]
}

export function workCenterDetailReturn(
  namespace: string | null,
): OrganizationDetailReturn | undefined {
  return namespace === null
    ? undefined
    : { ns: namespace, from: DETAIL_ORIGIN.workCenters }
}

/** Lowercase canonical id, the form the Organization routes emit (the work-centre filter's own rule). */
const CANONICAL_ID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/
const NIL_ID = '00000000-0000-0000-0000-000000000000'

/** The ORG-03 origin an ORG-04 card may honour: the list, optionally narrowed to one location. */
export interface WorkCenterCardOrigin {
  readonly ns: string
  readonly from: typeof DETAIL_ORIGIN.workCenters
  readonly locationId?: string
}

/**
 * ORG-04: the list origin the card was opened with, or undefined.
 *
 * Router state is caller-controlled, so only the exact tokens the ORG-03 list writes are honoured — the plain
 * `{ ns, from }` and the filtered `{ ns, from, locationId }` with a canonical id — and only under the CURRENT
 * committed context. Anything else (another context's token, an extra key, a path) is ignored rather than
 * interpreted: the card then returns to the whole list.
 */
export function workCenterCardOrigin(state: unknown, namespace: string | null): WorkCenterCardOrigin | undefined {
  if (namespace === null || typeof state !== 'object' || state === null || Array.isArray(state)) return undefined
  const value = state as Record<string, unknown>
  const keys = Object.keys(value)
  if (value.ns !== namespace || value.from !== DETAIL_ORIGIN.workCenters) return undefined
  if (keys.length === 2 && keys.every((key) => key === 'ns' || key === 'from')) {
    return { ns: namespace, from: DETAIL_ORIGIN.workCenters }
  }
  if (
    keys.length === 3 &&
    keys.every((key) => key === 'ns' || key === 'from' || key === 'locationId') &&
    typeof value.locationId === 'string' &&
    CANONICAL_ID.test(value.locationId) &&
    value.locationId !== NIL_ID
  ) {
    return { ns: namespace, from: DETAIL_ORIGIN.workCenters, locationId: value.locationId }
  }
  return undefined
}

/**
 * Where the ORG-04 card's back link goes: the list it came from, with its location filter restored.
 *
 * The filter is restored ONLY when it is this card's own location — the rule the location card applies to the
 * same token (`filteredWorkCenterBackPath`). A token naming another location (a forged or carried-over state)
 * returns to the whole list rather than to a filter the record does not belong to. The card's location is known
 * only from the loaded record, so until it is (loading, not-found, an error) the back link is the whole list.
 */
export function workCenterCardBackTarget(
  origin: WorkCenterCardOrigin | undefined,
  cardLocationId: string | undefined,
): DetailBackTarget {
  const list = TARGETS[DETAIL_ORIGIN.workCenters]
  return origin?.locationId === undefined || origin.locationId !== cardLocationId
    ? list
    : { path: workCenterListPath(origin.locationId), label: list.label }
}
