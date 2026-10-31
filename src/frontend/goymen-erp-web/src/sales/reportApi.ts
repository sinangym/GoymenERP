/**
 * UI-80 transport — API-41 `GET /api/v1/sales/reports/open-orders` and API-42 `GET /api/v1/sales/reports/lost-opportunities` /
 * `…/price-variance`. Reads only; the transport invents no field and carries no business rule. A body is data only when the strict
 * decoder vouches for it and every record matches the filter it was read under; anything else is `malformed` and the screen shows
 * no number rather than a wrong one. Every read is bound to the committed shell company (`X-Company-Id`).
 */

import { headers, readFailure, request } from './api'
import {
  REPORT_PAGE_SIZE,
  REPORT_SCOPE_TOO_LARGE,
  SALES_REPORT_LOST_OPPORTUNITIES_PATH,
  SALES_REPORT_OPEN_ORDERS_PATH,
  SALES_REPORT_PRICE_VARIANCE_PATH,
  decodeLostOpportunityReport,
  decodeOpenOrderReport,
  decodePriceVarianceReport,
  type LostOpportunityReport,
  type OpenOrderReport,
  type PriceVarianceReport,
} from './reportContract'
import type { ViewOutcome } from '../shell/uiState'

/** The server-side filter of a report: an inclusive date range and one customer account, each optional. */
export interface ReportFilter {
  readonly from: string | null
  readonly to: string | null
  readonly companyAccountId: string | null
}

export const NO_REPORT_FILTER: ReportFilter = { from: null, to: null, companyAccountId: null }

/** The filter as one stable string (the paging hook's filter identity: changing it restarts the list). */
export function reportFilterKey(filter: ReportFilter): string {
  return [filter.from ?? '-', filter.to ?? '-', filter.companyAccountId ?? '-'].join('|')
}

export function parseReportFilterKey(key: string | null): ReportFilter {
  if (key === null) return NO_REPORT_FILTER
  const [from, to, companyAccountId] = key.split('|').map((part) => (part === '-' ? null : part))
  return { from: from ?? null, to: to ?? null, companyAccountId: companyAccountId ?? null }
}

export function reportPath(base: string, filter: ReportFilter, cursor: string | null, paged = true): string {
  const params = new URLSearchParams()
  if (filter.from !== null) params.set('from', filter.from)
  if (filter.to !== null) params.set('to', filter.to)
  if (filter.companyAccountId !== null) params.set('companyAccountId', filter.companyAccountId)
  if (paged) params.set('pageSize', String(REPORT_PAGE_SIZE))
  if (cursor !== null) params.set('cursor', cursor)
  const query = params.toString()
  return query === '' ? base : `${base}?${query}`
}

async function read<T>(path: string, companyId: string, signal: AbortSignal | undefined, decode: (body: unknown) => T | null,
  belongs: (data: T) => boolean): Promise<ViewOutcome<T>> {
  const raw = await request(path, { headers: headers(companyId) }, signal)
  if ('failure' in raw) return readFailure(raw.failure)
  if (raw.status !== 200) return { type: 'http', status: raw.status, body: raw.body }
  const data = decode(raw.body)
  if (data === null || !belongs(data)) return { type: 'malformed' }
  return { type: 'ok-single', data }
}

export async function fetchOpenOrderReport(
  cursor: string | null,
  companyId: string,
  signal: AbortSignal | undefined,
  filterKey: string | null,
): Promise<ViewOutcome<OpenOrderReport>> {
  const filter = parseReportFilterKey(filterKey)
  const outcome = await read(reportPath(SALES_REPORT_OPEN_ORDERS_PATH, filter, cursor), companyId, signal, decodeOpenOrderReport,
    // A group of another customer than the one asked for is not a row to show.
    (data) => filter.companyAccountId === null || data.items.every((group) => group.customer.companyAccountId === filter.companyAccountId))
  // The first page refused as too wide is its own state (narrow the filter); a later page keeps the generic retry.
  if (cursor === null && outcome.type === 'http' && outcome.status === 422 && problemCode(outcome.body) === REPORT_SCOPE_TOO_LARGE) {
    return { type: 'ok-single', data: { measure: 'ApprovedMinusShipped', items: [], nextCursor: null, tooWide: true } }
  }
  return outcome
}

function problemCode(body: unknown): string | null {
  return typeof body === 'object' && body !== null && typeof (body as { code?: unknown }).code === 'string'
    ? (body as { code: string }).code
    : null
}

export function fetchLostOpportunityReport(
  cursor: string | null,
  companyId: string,
  signal: AbortSignal | undefined,
  filterKey: string | null,
): Promise<ViewOutcome<LostOpportunityReport>> {
  const filter = parseReportFilterKey(filterKey)
  return read(reportPath(SALES_REPORT_LOST_OPPORTUNITIES_PATH, filter, cursor), companyId, signal, decodeLostOpportunityReport,
    (data) =>
      data.items.every((item) =>
        (filter.companyAccountId === null || item.customer.companyAccountId === filter.companyAccountId)
        && (filter.from === null || item.lostOn >= filter.from)
        && (filter.to === null || item.lostOn <= filter.to)))
}

export function fetchPriceVarianceReport(
  companyId: string,
  signal: AbortSignal | undefined,
  filter: ReportFilter,
): Promise<ViewOutcome<PriceVarianceReport>> {
  return read(reportPath(SALES_REPORT_PRICE_VARIANCE_PATH, filter, null), companyId, signal, decodePriceVarianceReport, () => true)
}
