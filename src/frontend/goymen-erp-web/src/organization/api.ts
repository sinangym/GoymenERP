/**
 * Organization read transport — sites, the location page, one location.
 *
 * Thin and hand-written, like `../shell/api.ts` and `../cari/api.ts`.
 * HANDOFF: when the generated client lands (Issue #122) these fetch calls move to
 * `src/api/generated/**`; the decoders in ./contract.ts survive that move.
 *
 * What this layer deliberately does NOT do, on the third Backend's source handover
 * (Issue #66 comment 5646385043):
 *
 *  - no `q` / search and no `siteId` filter parameter. The endpoint has neither, so
 *    sending one would be inventing a server capability; the screen must not offer a
 *    search box that silently only covers the page in hand;
 *  - no client-contract (`X-Erp-Contract`) header. That is a Counterparty
 *    compatibility mechanism; the shared test factory's Cari default is not this
 *    module's requirement;
 *  - no cursor. Paging is offset and the SERVER's echoed `page`/`pageSize` win.
 */

import {
  decodeLocation,
  decodeLocationPage,
  decodeSites,
  decodeWorkCenterDetail,
  decodeWorkCenters,
  type LocationPage,
  type LocationSummary,
  type SiteSummary,
  type WorkCenterDetail,
  type WorkCenterSummary,
} from './contract'
import type { ViewOutcome } from '../shell/uiState'

export const API_BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? '/api/v1'
const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY_ENABLED =
  import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'

export function devHeaders(): Record<string, string> {
  return DEV_IDENTITY_ENABLED ? { 'X-Development-User': DEV_USER } : {}
}

/** The server's own bounds (`OrganizationService`): page >= 1, pageSize in [1,100]. */
export const PAGE_SIZE_MAX = 100
export const DEFAULT_PAGE_SIZE = 25

interface RawResult {
  readonly status: number
  /** EXACT 200 only — `Response.ok` spans all of 2xx, which is not this contract. */
  readonly isOk200: boolean
  readonly body: unknown
}

async function read(
  path: string,
  companyId: string,
  signal?: AbortSignal,
): Promise<RawResult | 'aborted' | 'offline' | 'unreachable'> {
  let response: Response
  try {
    response = await fetch(`${API_BASE}${path}`, {
      headers: { ...devHeaders(), 'X-Company-Id': companyId },
      signal,
    })
  } catch {
    // Our own cancellation says nothing about the server.
    if (signal?.aborted) return 'aborted'
    return navigator.onLine ? 'unreachable' : 'offline'
  }
  let body: unknown = null
  try {
    body = await response.json()
  } catch {
    // Headers arrived but the body was cut off — if that was our cancellation it is
    // still an abort, not a bad body (the same distinction ../cari/api.ts draws).
    if (signal?.aborted) return 'aborted'
    // A 400 may legitimately carry an EMPTY body on this surface, so an unparseable
    // body is not by itself a malformed response; the status still classifies it.
    body = null
  }
  return { status: response.status, isOk200: response.status === 200, body }
}

function transportOutcome<T>(
  result: 'aborted' | 'offline' | 'unreachable',
): ViewOutcome<T> {
  if (result === 'aborted') return { type: 'fence-stale' }
  if (result === 'offline') return { type: 'offline' }
  return { type: 'network-error' }
}

/** `GET /api/v1/organization/sites` — the company's facilities, unpaged. */
export async function fetchSites(
  companyId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<readonly SiteSummary[]>> {
  const result = await read('/organization/sites', companyId, signal)
  if (typeof result === 'string') return transportOutcome(result)
  if (!result.isOk200) return { type: 'http', status: result.status, body: result.body }
  const sites = decodeSites(result.body)
  if (sites === null) return { type: 'malformed' }
  return { type: 'ok-single', data: sites }
}

/**
 * `GET /api/v1/organization/work-centers` — the caller's work centres, UNPAGED.
 *
 * Unpaged is the server's shape, not a simplification here: the endpoint takes no
 * `page`/`pageSize` and `GetWorkCentersAsync` returns the whole authorized set. So the
 * screen must not grow a pager that would only ever have one page, and must not offer a
 * search box that silently covers a partial set — the same rule `fetchSites` carries.
 *
 * `decodeWorkCenters` rejects the WHOLE array when one row is bad, for the reason it
 * states: on an unpaged endpoint the array IS the caller's entire authorized set, so a
 * list one row short cannot be told apart from a correct one.
 */
export async function fetchWorkCenters(
  companyId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<readonly WorkCenterSummary[]>> {
  const result = await read('/organization/work-centers', companyId, signal)
  if (typeof result === 'string') return transportOutcome(result)
  if (!result.isOk200) return { type: 'http', status: result.status, body: result.body }
  const workCenters = decodeWorkCenters(result.body)
  if (workCenters === null) return { type: 'malformed' }
  return { type: 'ok-single', data: workCenters }
}

/**
 * `GET /api/v1/organization/work-centers/{id}` — ORG-04, one work centre's read-only card.
 *
 * The same grant and scope as the list. Another company's, an out-of-scope and an unknown id are one
 * code-less 404, passed through as that status and never turned into an empty card. The answer must be the
 * requested record (`decodeWorkCenterDetail`), or it is malformed.
 */
export async function fetchWorkCenter(
  companyId: string,
  workCenterId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<WorkCenterDetail>> {
  const result = await read(
    `/organization/work-centers/${encodeURIComponent(workCenterId)}`,
    companyId,
    signal,
  )
  if (typeof result === 'string') return transportOutcome(result)
  if (!result.isOk200) return { type: 'http', status: result.status, body: result.body }
  const workCenter = decodeWorkCenterDetail(result.body, workCenterId)
  if (workCenter === null) return { type: 'malformed' }
  return { type: 'ok-single', data: workCenter }
}

export interface LocationPageQuery {
  readonly page: number
  readonly pageSize: number
}

export function buildLocationQuery(query: LocationPageQuery): string {
  const params = new URLSearchParams()
  params.set('page', String(query.page))
  params.set('pageSize', String(query.pageSize))
  return `?${params.toString()}`
}

/** `GET /api/v1/organization/locations?page&pageSize` — offset page of locations. */
export async function fetchLocationPage(
  companyId: string,
  query: LocationPageQuery,
  signal?: AbortSignal,
): Promise<ViewOutcome<LocationPage>> {
  const result = await read(
    `/organization/locations${buildLocationQuery(query)}`,
    companyId,
    signal,
  )
  if (typeof result === 'string') return transportOutcome(result)
  if (!result.isOk200) return { type: 'http', status: result.status, body: result.body }
  const page = decodeLocationPage(result.body)
  if (page === null) return { type: 'malformed' }
  return { type: 'ok-single', data: page }
}

/**
 * `GET /api/v1/organization/locations/{id}` — one location, WITH its current version.
 *
 * This is also the read that runs after a successful command: a 204 carries no DTO,
 * and a replayed 204 is not a claim about the record's state right now, so the fresh
 * value always comes from here and never from a locally predicted `version + 1`.
 */
export async function fetchLocation(
  companyId: string,
  locationId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<LocationSummary>> {
  const result = await read(
    `/organization/locations/${encodeURIComponent(locationId)}`,
    companyId,
    signal,
  )
  if (typeof result === 'string') return transportOutcome(result)
  if (!result.isOk200) return { type: 'http', status: result.status, body: result.body }
  const location = decodeLocation(result.body)
  if (location === null) return { type: 'malformed' }
  return { type: 'ok-single', data: location }
}
