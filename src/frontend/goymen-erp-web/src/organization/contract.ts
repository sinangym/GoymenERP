/**
 * Organization wire contract — runtime decoders for the Ayarlar/Organizasyon slice.
 *
 * SOURCE, not proposal. Every shape here is read off the MERGED backend at
 * `main@3e7dd98` and the canonical `contracts/openapi/goymen-erp-v1.json`:
 *
 *   GET /api/v1/organization/sites                       -> SiteSummary[]
 *   GET /api/v1/organization/locations                   -> PageResultOfLocationSummary
 *   GET /api/v1/organization/locations/{id}              -> LocationSummary | 404
 *   PUT /api/v1/organization/locations/{id}/active-state -> 204 | 400 | 404 | 409
 *
 * (`src/backend/GoymenERP.Api/Endpoints/OrganizationEndpoints.cs`,
 *  `.../Organization.Application/Contracts/OrganizationModels.cs`.)
 *
 * THREE WAYS THIS CONTRACT DIFFERS FROM CARI'S, all verified at the source. The Cari
 * protocol is deliberately NOT carried over:
 *
 *  1. PAGING IS OFFSET, NOT CURSOR. `page` / `pageSize` / `totalCount`; there is no
 *     cursor and no `includeCount` switch — the count is always present. The server
 *     clamps `pageSize` to [1,100] and `page` to >= 1 (`OrganizationService`), so the
 *     echoed values can legitimately differ from what we asked for; the decoder reads
 *     the SERVER's values and the screen paginates from those, never from its own.
 *
 *  2. THE PROBLEM BODY CARRIES NO STABLE `code`. 404 and 409 are declared as
 *     `ErpCodeLessProblemDetailsContract`, whose schema literally forbids the field
 *     (`"not": {"required": ["code"]}`). So there is no `problemCodeOf` dictionary on
 *     this surface: classification is HTTP status plus the `conflict` extension, and
 *     nothing parses a localized title or detail string.
 *
 *  3. THERE IS NO CLIENT-CONTRACT ACK. The Cari write gate (`X-Erp-Contract` request
 *     header + acknowledgment, `isCommandContractReady`) is a Counterparty
 *     compatibility mechanism. These endpoints neither require nor emit it, so
 *     imitating it here would be inventing a protocol the server does not speak.
 */

/** Canonical UUID form the API emits. Anything else is not an id we will send back. */
const UUID = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/

function isUuid(value: unknown): value is string {
  return typeof value === 'string' && UUID.test(value)
}

function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

/**
 * `Site.Type` reaches the wire as `SiteType.ToString()` — today `Factory`,
 * `Warehouse` or `ExternalWarehouse`. It is typed as an open `string` in the OpenAPI
 * schema, so the decoder accepts any non-empty token and the label layer decides what
 * to show. A value we do not recognise must not fail the page: a site type added
 * server-side is not a malformed response.
 */
export const SITE_TYPE = {
  factory: 'Factory',
  warehouse: 'Warehouse',
  externalWarehouse: 'ExternalWarehouse',
} as const

export interface SiteSummary {
  readonly id: string
  readonly code: string
  readonly name: string
  /** Raw server token. Never parsed for business meaning, only labelled. */
  readonly type: string
  readonly isActive: boolean
}

export interface LocationSummary {
  readonly id: string
  readonly siteId: string
  readonly code: string
  readonly name: string
  readonly isExternal: boolean
  readonly isActive: boolean
  /**
   * Optimistic-concurrency token. `TenantEntity.Version` starts at 1 and is
   * incremented once per accepted change, and the request schema constrains
   * `expectedVersion` to `^[1-9][0-9]*$` with minimum 1 — so a version below 1 is not
   * a value this contract can produce.
   *
   * It is declared `integer/int64` on the RESPONSE, i.e. a JSON number, while the
   * Cari surface publishes the same concept as a decimal string precisely because an
   * int64 past 2^53 cannot survive an IEEE-754 double. A counter incremented once per
   * edit will not reach 2^53, but "will not" is not "cannot", and a silently rounded
   * version is the one value that must never be sent back as `expectedVersion` — that
   * is how a stale write gets accepted as a current one. So a version that is not an
   * exactly-representable integer >= 1 FAILS CLOSED as malformed: the screen refuses
   * rather than writing with a number it cannot vouch for.
   */
  readonly version: number
}

export interface LocationPage {
  readonly items: readonly LocationSummary[]
  /** The page the SERVER served, after its own clamping — not the one we asked for. */
  readonly page: number
  readonly pageSize: number
  readonly totalCount: number
}

/** The 409 body's `conflict` extension (`ErpVersionConflictContract`). */
export interface VersionConflict {
  readonly expectedVersion: number
  readonly currentVersion: number
}

function isSafeVersion(value: unknown): value is number {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= 1
}

/**
 * `expectedVersion` goes back out as a decimal string — allowed by the request schema
 * (`type: [integer, string]`, `pattern: ^[1-9][0-9]*$`) and immune to any numeric
 * formatting surprise on the way out.
 */
export function versionToken(version: number): string {
  return String(version)
}

export function decodeSite(raw: unknown): SiteSummary | null {
  if (!isRecord(raw)) return null
  const { id, code, name, type, isActive } = raw
  if (!isUuid(id)) return null
  if (!isNonEmptyString(code) || !isNonEmptyString(name)) return null
  if (!isNonEmptyString(type)) return null
  if (typeof isActive !== 'boolean') return null
  return { id, code, name, type, isActive }
}

export function decodeSites(raw: unknown): readonly SiteSummary[] | null {
  if (!Array.isArray(raw)) return null
  const sites: SiteSummary[] = []
  for (const entry of raw) {
    const site = decodeSite(entry)
    // One bad row invalidates the WHOLE list. Dropping it would silently narrow the
    // facility inventory the user is reading, and a shorter list is indistinguishable
    // from a correct one.
    if (site === null) return null
    sites.push(site)
  }
  return sites
}

export function decodeLocation(raw: unknown): LocationSummary | null {
  if (!isRecord(raw)) return null
  const { id, siteId, code, name, isExternal, isActive, version } = raw
  if (!isUuid(id) || !isUuid(siteId)) return null
  if (!isNonEmptyString(code) || !isNonEmptyString(name)) return null
  if (typeof isExternal !== 'boolean' || typeof isActive !== 'boolean') return null
  if (!isSafeVersion(version)) return null
  return { id, siteId, code, name, isExternal, isActive, version }
}

function isPageCounter(value: unknown): value is number {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0
}

export function decodeLocationPage(raw: unknown): LocationPage | null {
  if (!isRecord(raw)) return null
  const { items, page, pageSize, totalCount } = raw
  if (!Array.isArray(items)) return null
  // `page` and `pageSize` are >= 1 after the server's clamp; `totalCount` may be 0.
  if (!isPageCounter(page) || page < 1) return null
  if (!isPageCounter(pageSize) || pageSize < 1) return null
  if (!isPageCounter(totalCount)) return null
  const decoded: LocationSummary[] = []
  for (const entry of items) {
    const location = decodeLocation(entry)
    if (location === null) return null
    decoded.push(location)
  }
  /**
   * A page cannot hold more rows than its own page size. This is the one cross-field
   * check worth making: it is the shape a mis-paged or spliced response takes, and
   * believing it would make the pager compute a wrong last page and hide rows.
   */
  if (decoded.length > pageSize) return null
  return { items: decoded, page, pageSize, totalCount }
}

/**
 * Reads `conflict` out of a 409 body. Returns null when the body is absent or does
 * not carry a well-formed conflict — the screen then still knows it hit a 409 from
 * the STATUS, and simply cannot show the version diff. A conflict is never inferred
 * from the localized title or detail text.
 */
export function decodeVersionConflict(raw: unknown): VersionConflict | null {
  if (!isRecord(raw)) return null
  const conflict = raw.conflict
  if (!isRecord(conflict)) return null
  const { expectedVersion, currentVersion } = conflict
  if (!isSafeVersion(expectedVersion) || !isSafeVersion(currentVersion)) return null
  return { expectedVersion, currentVersion }
}

/** Total pages for an offset pager. Zero rows is ONE (empty) page, not zero. */
export function pageCount(page: LocationPage): number {
  return Math.max(1, Math.ceil(page.totalCount / page.pageSize))
}
