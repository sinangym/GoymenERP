/**
 * UI-63 S-09 transport — API-32 `GET /api/v1/sales/invoices?pageSize&cursor`. A read only; the transport invents no field and
 * carries no business rule. A body is data only when the strict decoder vouches for it; anything else is `malformed`.
 * The summary carries no company member: `X-Company-Id` (the committed shell company) is the server's fence.
 */

import { headers, readFailure, request } from './api'
import { decodeSalesInvoicePage, salesInvoicesListPath, type SalesInvoicePage } from './invoiceListContract'
import type { ViewOutcome } from '../shell/uiState'

export async function fetchSalesInvoicePage(
  cursor: string | null,
  companyId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<SalesInvoicePage>> {
  const raw = await request(salesInvoicesListPath(cursor), { headers: headers(companyId) }, signal)
  if ('failure' in raw) return readFailure(raw.failure)
  if (raw.status !== 200) return { type: 'http', status: raw.status, body: raw.body }
  const page = decodeSalesInvoicePage(raw.body)
  return page === null ? { type: 'malformed' } : { type: 'ok-single', data: page }
}
