/**
 * API-09 / API-36 (PR #986) transport: the order's invoices, one invoice's detail and the two finalization commands.
 *
 *   GET  /api/v1/sales/drafts/{id}/invoices               → SalesDraftInvoicesResponse (under the draft read)
 *   GET  /api/v1/sales/invoices/{id}                      → SalesInvoiceDetailView (invoice read + price read)
 *   POST /api/v1/sales/invoices/{id}/finalization         → 200 SalesInvoiceDetailView (Finalized)
 *   POST /api/v1/sales/invoices/{id}/finance-admission    → 200 SalesInvoiceDetailView (Finalized)
 *
 * The command body is `{ expectedVersion }` only; `X-Company-Id` is the company frozen with the attempt and `Idempotency-Key`
 * the attempt's key. A 200 is success only when the body decodes, is the invoice that was sent, is Finalized with its
 * finalization ledger, and its `isReplay` agrees with the replay headers — anything else is `malformed`, so the caller keeps
 * the key (the write may have happened).
 */
import { headers, readFailure, request } from './api'
import { STATE_COMMAND_STATUS, decodeProblem } from './contract'
import {
  decodeDraftInvoices,
  decodeSalesInvoiceDetail,
  invoiceFinalizationCommandPath,
  salesDraftInvoicesPath,
  salesInvoicePath,
  type DraftInvoices,
  type InvoiceFinalizationKind,
  type InvoiceFinalizationRequest,
  type SalesInvoiceDetail,
} from './invoiceFinalizationContract'
import type { OrderCommandOutcome } from './orderAttempt'
import type { ViewOutcome } from '../shell/uiState'

export async function fetchDraftInvoices(
  draftId: string,
  companyId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<DraftInvoices>> {
  const raw = await request(salesDraftInvoicesPath(draftId), { headers: headers(companyId) }, signal)
  if ('failure' in raw) return readFailure(raw.failure)
  if (raw.status !== 200) return { type: 'http', status: raw.status, body: raw.body }
  // The wire carries no company member: the read stays tied to its request and the context namespace.
  const invoices = decodeDraftInvoices(raw.body)
  return invoices === null ? { type: 'malformed' } : { type: 'ok-single', data: invoices }
}

export async function fetchSalesInvoice(
  invoiceId: string,
  companyId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<SalesInvoiceDetail>> {
  const raw = await request(salesInvoicePath(invoiceId), { headers: headers(companyId) }, signal)
  if ('failure' in raw) return readFailure(raw.failure)
  if (raw.status !== 200) return { type: 'http', status: raw.status, body: raw.body }
  const detail = decodeSalesInvoiceDetail(raw.body, invoiceId)
  return detail === null ? { type: 'malformed' } : { type: 'ok-single', data: detail }
}

export interface InvoiceCommandContext {
  readonly idempotencyKey: string
  /** The company frozen with the attempt, not whatever the shell holds at retry time. */
  readonly companyId: string
  readonly invoiceId: string
}

export async function sendInvoiceFinalization(
  kind: InvoiceFinalizationKind,
  payload: InvoiceFinalizationRequest,
  context: InvoiceCommandContext,
  signal?: AbortSignal,
): Promise<OrderCommandOutcome<SalesInvoiceDetail>> {
  const raw = await request(
    invoiceFinalizationCommandPath(kind, context.invoiceId),
    {
      method: 'POST',
      headers: {
        ...headers(context.companyId),
        'Content-Type': 'application/json',
        'Idempotency-Key': context.idempotencyKey,
      },
      body: JSON.stringify({ expectedVersion: payload.expectedVersion }),
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
    detail.state !== 'Finalized' ||
    detail.finalization === null ||
    detail.isReplay !== raw.replayed ||
    // The body names no original correlation: a replay must carry the header, a first answer must not.
    (detail.isReplay ? raw.replayCorrelationId === null : raw.replayCorrelationId !== null)
  ) {
    return { type: 'malformed' }
  }
  return { type: 'succeeded', data: detail }
}
