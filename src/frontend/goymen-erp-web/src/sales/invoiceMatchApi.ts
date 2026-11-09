/**
 * API-35 (PR #981) transport: match an approved sales invoice to the official e-invoice.
 *
 *   POST /api/v1/sales/invoices/{id}/external-match   → 200 SalesInvoiceDetailView (still Approved, now with `match`)
 *
 * Body `{ expectedVersion, officialInvoiceNo, ettn, approveDifference, differenceReason }`; `X-Company-Id` is the company
 * frozen with the attempt and `Idempotency-Key` the attempt's key. A 200 is success only when the body decodes, is the invoice
 * that was sent, carries a match for the number and ETTN that were sent (and, on a first answer, is still Approved), and its
 * `isReplay` agrees with the replay headers — anything else is `malformed`, so the caller keeps the key (the write may have
 * happened). The difference and the tolerance in the match are the server's text; nothing here compares or recomputes them.
 */
import { headers, request } from './api'
import { STATE_COMMAND_STATUS, decodeProblem } from './contract'
import type { InvoiceCommandContext } from './invoiceApi'
import { decodeSalesInvoiceDetail, salesInvoicePath, type SalesInvoiceDetail } from './invoiceFinalizationContract'
import type { OrderCommandOutcome } from './orderAttempt'

export const salesInvoiceExternalMatchPath = (invoiceId: string) => `${salesInvoicePath(invoiceId)}/external-match`

/** `MatchSalesInvoiceRequest`, exactly as sent. */
export interface InvoiceMatchRequest {
  readonly expectedVersion: string
  readonly officialInvoiceNo: string
  readonly ettn: string
  readonly approveDifference: boolean
  readonly differenceReason: string | null
}

export async function sendInvoiceMatch(
  payload: InvoiceMatchRequest,
  context: InvoiceCommandContext,
  signal?: AbortSignal,
): Promise<OrderCommandOutcome<SalesInvoiceDetail>> {
  const raw = await request(
    salesInvoiceExternalMatchPath(context.invoiceId),
    {
      method: 'POST',
      headers: {
        ...headers(context.companyId),
        'Content-Type': 'application/json',
        'Idempotency-Key': context.idempotencyKey,
      },
      body: JSON.stringify({
        expectedVersion: payload.expectedVersion,
        officialInvoiceNo: payload.officialInvoiceNo,
        ettn: payload.ettn,
        approveDifference: payload.approveDifference,
        differenceReason: payload.differenceReason,
      }),
    },
    signal,
  )
  if ('failure' in raw) {
    if (raw.failure === 'aborted') return { type: 'aborted' }
    if (raw.failure === 'offline') return { type: 'offline' }
    return { type: 'network-error' }
  }
  if (raw.status !== STATE_COMMAND_STATUS) {
    if (raw.status >= 200 && raw.status < 300) return { type: 'malformed' }
    return { type: 'http', status: raw.status, problem: decodeProblem(raw.body, raw.status) }
  }
  const detail = decodeSalesInvoiceDetail(raw.body, context.invoiceId)
  if (
    detail === null ||
    // A first answer is the invoice just matched (still Approved). A replay answers the invoice as it is NOW, which may have
    // moved on (finalized) since; the recorded match it carries must still be the one that was sent.
    (!detail.isReplay && detail.state !== 'Approved') ||
    detail.match === null ||
    detail.match.officialInvoiceNo !== payload.officialInvoiceNo ||
    // The server writes the ETTN in its canonical "D" form; the comparison is case-insensitive like the GUID itself.
    detail.match.ettn.toLowerCase() !== payload.ettn.toLowerCase() ||
    detail.isReplay !== raw.replayed ||
    // The body names no original correlation: a replay must carry the header, a first answer must not.
    (detail.isReplay ? raw.replayCorrelationId === null : raw.replayCorrelationId !== null)
  ) {
    return { type: 'malformed' }
  }
  return { type: 'succeeded', data: detail }
}
