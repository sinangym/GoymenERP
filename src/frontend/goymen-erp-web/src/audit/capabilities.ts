/**
 * Capability id, route and URL parameters of "Denetim kayıtları" — a STATEMENT OF INTENT, not a grant.
 *
 * The server side has the list read (`GET /api/v1/audit/events`, permission `identity.audit.read`, company-wide) but NO
 * shell capability declaration yet, and the details endpoint and its permission are only proposed (#66/6383408216): no `IShellCapabilityProvider` publishes `audit.events.list`. Until one does,
 * `hasCapability` answers false for every caller, the route renders the neutral not-found block and no nav entry is
 * drawn. That is the intended default-deny, not a bug: the screen opens the day the backend lane declares the
 * capability (`audit.events.list <- identity.audit.read`, company-wide), and every request is re-authorized
 * server-side regardless (ERP-000K §10.2).
 *
 * The route table lives in ./auditRoutes.tsx and is not yet spread into `shell/router.tsx`: mounting it, mirroring
 * the id into `shell/capabilities.ts` and drawing the nav entry are one shared-shell change (K3), made together
 * with the backend declaration.
 */

export const AUDIT_CAPABILITY = {
  /** Route `/ayarlar/denetim-kayitlari`. Read-only; there is no write capability and there must not be one. */
  eventsList: 'audit.events.list',
  /**
   * A CONTROL capability on the list, not a route: draws the per-row "Ayrıntılar" button. Proposed
   * `audit.events.details <- identity.audit.details.read`, held at first only by the system administrator and widenable
   * later (Sinan, #66/6383408216). Nothing in the list ever renders detail fields without it, and the details are read
   * from their own server-guarded endpoint, never from the list answer.
   */
  eventDetails: 'audit.events.details',
} as const

export type AuditCapabilityId = (typeof AUDIT_CAPABILITY)[keyof typeof AUDIT_CAPABILITY]

export const AUDIT_ROUTE: Record<typeof AUDIT_CAPABILITY.eventsList, string> = {
  [AUDIT_CAPABILITY.eventsList]: '/ayarlar/denetim-kayitlari',
}

export const AUDIT_NAV_LABEL: Record<typeof AUDIT_CAPABILITY.eventsList, string> = {
  [AUDIT_CAPABILITY.eventsList]: 'Denetim kayıtları',
}

/**
 * URL parameters: the filters and the page cursor live in the URL, so back, reload and a shared link show the same
 * page. Nothing is kept in browser storage.
 */
export const AUDIT_PARAM = {
  correlationId: 'korelasyon',
  entityType: 'varlikTipi',
  entityId: 'varlikId',
  cursor: 'imlec',
} as const

/**
 * The server's own canonical limits (`AuditEndpoints.IsTooLong`, OpenAPI `x-erp-canonical-max-length`). A longer value
 * is refused by the server with 400; checking it here only saves the round trip, it is not a policy of this screen.
 */
export const AUDIT_FILTER_MAX_LENGTH = {
  correlationId: 128,
  entityType: 160,
  entityId: 128,
  cursor: 128,
} as const

export interface AuditQuery {
  readonly correlationId: string | null
  readonly entityType: string | null
  readonly entityId: string | null
  readonly cursor: string | null
}

function readParam(params: URLSearchParams, name: string, maxLength: number): string | null {
  const value = params.get(name)?.trim() ?? ''
  if (value.length === 0 || value.length > maxLength) return null
  return value
}

/** The query the URL asks for. An over-long value is dropped rather than sent: it could only earn a 400. */
export function readAuditQuery(params: URLSearchParams): AuditQuery {
  return {
    correlationId: readParam(params, AUDIT_PARAM.correlationId, AUDIT_FILTER_MAX_LENGTH.correlationId),
    entityType: readParam(params, AUDIT_PARAM.entityType, AUDIT_FILTER_MAX_LENGTH.entityType),
    entityId: readParam(params, AUDIT_PARAM.entityId, AUDIT_FILTER_MAX_LENGTH.entityId),
    cursor: readParam(params, AUDIT_PARAM.cursor, AUDIT_FILTER_MAX_LENGTH.cursor),
  }
}

export function hasAuditFilter(query: AuditQuery): boolean {
  return query.correlationId !== null || query.entityType !== null || query.entityId !== null
}

/** The list path for a query; empty fields are left out so the plain path is the unfiltered first page. */
export function auditEventsPath(query: Partial<AuditQuery> = {}): string {
  const params = new URLSearchParams()
  if (query.correlationId) params.set(AUDIT_PARAM.correlationId, query.correlationId)
  if (query.entityType) params.set(AUDIT_PARAM.entityType, query.entityType)
  if (query.entityId) params.set(AUDIT_PARAM.entityId, query.entityId)
  if (query.cursor) params.set(AUDIT_PARAM.cursor, query.cursor)
  const search = params.toString()
  const path = AUDIT_ROUTE[AUDIT_CAPABILITY.eventsList]
  return search.length > 0 ? `${path}?${search}` : path
}
