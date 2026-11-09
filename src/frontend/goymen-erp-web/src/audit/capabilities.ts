/**
 * Capability id of "Denetim kaydı" (UI-K02, KLN-D2) — a STATEMENT OF INTENT, not a grant.
 *
 * It mirrors `IdentityShellCapabilityProvider` 1.1 exactly and stands on ONE existing server permission,
 * `identity.audit.read`, company-wide, in one company:
 *
 *   identity.audit.list <- identity.audit.read   (SY-07: the company's audit events, read-only)
 *
 * Listing the id here grants nothing. `hasCapability` answers false for an id the server did not grant this caller,
 * `CapabilityRoute` then closes the route onto the neutral not-found block, and the nav entry is not drawn. The
 * endpoint re-authorizes every request regardless (ERP-000K §10.2). There is no write id: an audit record is never
 * edited, deleted or exported from this screen.
 */

export const AUDIT_CAPABILITY = {
  /** Route `/ayarlar/denetim-kaydi`. The only entry point; it carries the sidebar label. */
  list: 'identity.audit.list',
} as const

export type AuditCapabilityId = (typeof AUDIT_CAPABILITY)[keyof typeof AUDIT_CAPABILITY]

export const AUDIT_ROUTE: Record<AuditCapabilityId, string> = {
  [AUDIT_CAPABILITY.list]: '/ayarlar/denetim-kaydi',
}

export const AUDIT_NAV_LABEL: Record<AuditCapabilityId, string> = {
  [AUDIT_CAPABILITY.list]: 'Denetim kaydı',
}

/**
 * The three exact-match filters of `GET /api/v1/audit/events`, their URL parameter names and the server's canonical
 * maximum length after trimming (`AuditEndpoints.IsTooLong`, OpenAPI `x-erp-canonical-max-length`).
 */
export const AUDIT_FILTERS = [
  { key: 'correlationId', param: 'korelasyon', maxLength: 128 },
  { key: 'entityType', param: 'kayitTuru', maxLength: 160 },
  { key: 'entityId', param: 'kayitNo', maxLength: 128 },
] as const

export type AuditFilterKey = (typeof AUDIT_FILTERS)[number]['key']

/** Trimmed filter values; an empty value is absent and never sent. */
export type AuditFilters = Readonly<Record<AuditFilterKey, string>>

export const EMPTY_FILTERS: AuditFilters = { correlationId: '', entityType: '', entityId: '' }

/** Reads the filters from the URL, trimmed. Nothing is defaulted or guessed. */
export function filtersFromSearch(search: URLSearchParams): AuditFilters {
  const filters: Record<AuditFilterKey, string> = { ...EMPTY_FILTERS }
  for (const filter of AUDIT_FILTERS) filters[filter.key] = (search.get(filter.param) ?? '').trim()
  return filters
}

/** The filters that are too long for the server; such a filter is never sent. */
export function overLongFilters(filters: AuditFilters): readonly AuditFilterKey[] {
  return AUDIT_FILTERS.filter((filter) => filters[filter.key].length > filter.maxLength).map((filter) => filter.key)
}

export function hasActiveFilter(filters: AuditFilters): boolean {
  return AUDIT_FILTERS.some((filter) => filters[filter.key] !== '')
}

/** The list path carrying only the non-empty filters, so back, reload and a shared link show the same view. */
export function auditListPath(filters: AuditFilters = EMPTY_FILTERS): string {
  const search = new URLSearchParams()
  for (const filter of AUDIT_FILTERS) {
    const value = filters[filter.key].trim()
    if (value !== '') search.set(filter.param, value)
  }
  const query = search.toString()
  return query === '' ? AUDIT_ROUTE[AUDIT_CAPABILITY.list] : `${AUDIT_ROUTE[AUDIT_CAPABILITY.list]}?${query}`
}

/** A stable string of the filters; part of the view key, so a filter change restarts the list. */
export function filterKey(filters: AuditFilters): string {
  return JSON.stringify([filters.correlationId, filters.entityType, filters.entityId])
}
