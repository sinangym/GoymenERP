import { ORGANIZATION_CAPABILITY, ORGANIZATION_ROUTE } from './capabilities'

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
