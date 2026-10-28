/**
 * S-07 / S-08 transport — API-30 `GET /api/v1/sales/dispatches` and API-31 `GET /api/v1/sales/invoice-candidates`. Reads only;
 * the transport invents no field and carries no business rule. A body is data only when the strict decoder vouches for it and
 * every record belongs to the company it was read under (`X-Company-Id` is the committed shell company); anything else is
 * `malformed` and the screen shows no number rather than a wrong one.
 */

import { headers, readFailure, request } from './api'
import {
  DISPATCH_PAGE_SIZE,
  SALES_DISPATCHES_PATH,
  SALES_INVOICE_CANDIDATES_PATH,
  decodeDispatchPage,
  decodeInvoiceCandidates,
  type DispatchPage,
  type DispatchState,
  type InvoiceCandidates,
} from './dispatchContract'
import type { ViewOutcome } from '../shell/uiState'

export function dispatchListPath(cursor: string | null, state: DispatchState | null): string {
  const params = new URLSearchParams({ pageSize: String(DISPATCH_PAGE_SIZE) })
  if (cursor !== null) params.set('cursor', cursor)
  // The exact state narrows the list on the server; the client never filters a partial page.
  if (state !== null) params.set('state', state)
  return `${SALES_DISPATCHES_PATH}?${params.toString()}`
}

export function invoiceCandidatesPath(companyAccountId: string | null): string {
  return companyAccountId === null
    ? SALES_INVOICE_CANDIDATES_PATH
    : `${SALES_INVOICE_CANDIDATES_PATH}?${new URLSearchParams({ companyAccountId }).toString()}`
}

export async function fetchDispatchPage(
  cursor: string | null,
  companyId: string,
  signal?: AbortSignal,
  state: DispatchState | null = null,
): Promise<ViewOutcome<DispatchPage>> {
  const raw = await request(dispatchListPath(cursor, state), { headers: headers(companyId) }, signal)
  if ('failure' in raw) return readFailure(raw.failure)
  if (raw.status !== 200) return { type: 'http', status: raw.status, body: raw.body }
  const page = decodeDispatchPage(raw.body)
  // A note of another company, or a state the filter did not ask for, is not a row to show.
  if (page === null || page.items.some((item) => item.companyId !== companyId || (state !== null && item.state !== state))) {
    return { type: 'malformed' }
  }
  return { type: 'ok-single', data: page }
}

export async function fetchInvoiceCandidates(
  companyId: string,
  signal?: AbortSignal,
  companyAccountId: string | null = null,
): Promise<ViewOutcome<InvoiceCandidates>> {
  const raw = await request(invoiceCandidatesPath(companyAccountId), { headers: headers(companyId) }, signal)
  if ('failure' in raw) return readFailure(raw.failure)
  if (raw.status !== 200) return { type: 'http', status: raw.status, body: raw.body }
  const candidates = decodeInvoiceCandidates(raw.body, companyId)
  if (candidates === null
    || (companyAccountId !== null && candidates.groups.some((group) => group.key.companyAccountId !== companyAccountId))) {
    return { type: 'malformed' }
  }
  return { type: 'ok-single', data: candidates }
}
