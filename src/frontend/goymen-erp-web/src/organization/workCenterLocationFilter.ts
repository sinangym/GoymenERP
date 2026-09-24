import type { WorkCenterSummary } from './contract'
import { workCenterListPath } from './capabilities'
import { DETAIL_ORIGIN, workCenterDetailReturn } from './detailReturn'

export const WORK_CENTER_LOCATION_TEXT = {
  action: 'Bu lokasyondaki iş merkezleri',
  label: 'Lokasyon filtresi',
  clear: 'Filtreyi temizle',
  filteredSubject: 'bu lokasyondaki iş merkezleri',
  invalidSubject: 'lokasyon filtresi',
} as const

export type WorkCenterLocationFilter =
  | { readonly kind: 'all' }
  | { readonly kind: 'location'; readonly locationId: string }
  | { readonly kind: 'invalid' }

/** Lowercase, hyphenated, nonempty identity as emitted by the canonical Organization routes. */
export function isCanonicalLocationId(value: unknown): value is string {
  return typeof value === 'string' &&
    /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/.test(value) &&
    value !== '00000000-0000-0000-0000-000000000000'
}

/** Invalid input must never fall back to the larger unfiltered set. No server query is constructed. */
export function readWorkCenterLocationFilter(search: string): WorkCenterLocationFilter {
  const pairs = [...new URLSearchParams(search).entries()]
  if (pairs.length === 0) return { kind: 'all' }
  if (pairs.length !== 1 || pairs[0][0] !== 'locationId' || !isCanonicalLocationId(pairs[0][1])) {
    return { kind: 'invalid' }
  }
  return { kind: 'location', locationId: pairs[0][1] }
}

/** Input is the fully decoded authorized server result, not raw JSON or a cross-company cache. */
export function filterWorkCenters(
  rows: readonly WorkCenterSummary[],
  filter: WorkCenterLocationFilter,
): readonly WorkCenterSummary[] {
  if (filter.kind === 'invalid') return []
  return filter.kind === 'all' ? rows : rows.filter((row) => row.locationId === filter.locationId)
}

/** Carry an allowlisted origin token, never a caller-provided URL. Full-list behavior stays unchanged. */
export function filteredWorkCenterDetailReturn(namespace: string | null, locationId: string | null) {
  const origin = workCenterDetailReturn(namespace)
  return origin === undefined || !isCanonicalLocationId(locationId)
    ? origin
    : { ...origin, locationId }
}

/** A location card may restore only its own filter in the same committed context. */
export function filteredWorkCenterBackPath(
  state: unknown,
  namespace: string | null,
  currentLocationId: string | undefined,
): string | null {
  if (namespace === null || typeof state !== 'object' || state === null || Array.isArray(state)) return null
  const value = state as Record<string, unknown>
  const keys = Object.keys(value)
  if (keys.length !== 3 || !keys.every((key) => ['ns', 'from', 'locationId'].includes(key)) ||
    value.ns !== namespace || value.from !== DETAIL_ORIGIN.workCenters ||
    !isCanonicalLocationId(value.locationId) || value.locationId !== currentLocationId) return null
  return workCenterListPath(value.locationId)
}
