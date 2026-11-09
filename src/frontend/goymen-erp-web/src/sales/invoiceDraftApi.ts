/**
 * API-32 / API-33 transport: create an invoice draft from shipment shares, and the draft's four state commands.
 *
 *   POST /api/v1/sales/invoices                      → 201 SalesInvoiceDetailView (Draft)
 *   POST /api/v1/sales/invoices/{id}/submission      → 200 SalesInvoiceDetailView (PendingApproval)
 *   POST /api/v1/sales/invoices/{id}/approval        → 200 SalesInvoiceDetailView (Approved)
 *   POST /api/v1/sales/invoices/{id}/rejection       → 200 SalesInvoiceDetailView (Rejected)
 *   POST /api/v1/sales/invoices/{id}/cancellation    → 200 SalesInvoiceDetailView (Cancelled)
 *
 * `X-Company-Id` is the company frozen with the attempt and `Idempotency-Key` the attempt's key. Success is the pinned status
 * AND a body that decodes, is the invoice that was asked for, is in the state the command leads to, and whose `isReplay` agrees
 * with the replay headers — anything else is `malformed`, so the caller keeps the key (the write may have happened).
 */
import { headers, request, type Raw } from './api'
import { CREATED_STATUS, STATE_COMMAND_STATUS, decodeProblem } from './contract'
import { decodeSalesInvoiceDetail, salesInvoicePath, type InvoiceState, type SalesInvoiceDetail } from './invoiceFinalizationContract'
import type { InvoiceCommandContext } from './invoiceApi'
import type { OrderCommandOutcome } from './orderAttempt'
import { isReferenceId } from './wirePrimitives'

export const SALES_INVOICES_PATH = '/sales/invoices'

/** API-32's bound on the shares of one body (`SalesInvoiceApiV1.MaximumShares`, the candidate read's own bound). */
export const MAX_INVOICE_DRAFT_SHARES = 500

/** API-32 body as the person chose it: the customer account and the whole shares (shipments), in the screen's order. */
export interface CreateSalesInvoicePayload {
  readonly companyAccountId: string
  readonly shipmentIds: readonly string[]
}

export const INVOICE_DRAFT_COMMAND = {
  submit: 'submit',
  approve: 'approve',
  reject: 'reject',
  cancel: 'cancel',
} as const
export type InvoiceDraftCommand = (typeof INVOICE_DRAFT_COMMAND)[keyof typeof INVOICE_DRAFT_COMMAND]

const COMMAND_SEGMENT: Record<InvoiceDraftCommand, string> = {
  submit: 'submission',
  approve: 'approval',
  reject: 'rejection',
  cancel: 'cancellation',
}

/** The state the server answers after each command; a 200 in any other state is not this command's answer. */
export const COMMAND_RESULT_STATE: Record<InvoiceDraftCommand, InvoiceState> = {
  submit: 'PendingApproval',
  approve: 'Approved',
  reject: 'Rejected',
  cancel: 'Cancelled',
}

/** Whether the command carries a reason member (the submission and the approval refuse one). */
export const commandTakesReason = (kind: InvoiceDraftCommand): boolean =>
  kind === INVOICE_DRAFT_COMMAND.reject || kind === INVOICE_DRAFT_COMMAND.cancel

export const invoiceDraftCommandPath = (kind: InvoiceDraftCommand, invoiceId: string) =>
  `${salesInvoicePath(invoiceId)}/${COMMAND_SEGMENT[kind]}`

export interface InvoiceDraftCommandPayload {
  readonly expectedVersion: string
  /** Reject / cancel only: the trimmed reason, or null for none. */
  readonly reason: string | null
}

/**
 * The exact wire body. A cancel sends no payment-allocation acknowledgement: Sales knows no allocation yet (SAT-D96 S2 is the
 * next slice), so the server's default (`false`) stands — the screen never confirms what it cannot show.
 */
export function invoiceDraftCommandBody(kind: InvoiceDraftCommand, payload: InvoiceDraftCommandPayload): Record<string, unknown> {
  return commandTakesReason(kind)
    ? { expectedVersion: payload.expectedVersion, reason: payload.reason }
    : { expectedVersion: payload.expectedVersion }
}

function failureOutcome<R>(failure: Extract<Raw, { readonly failure: unknown }>['failure']): OrderCommandOutcome<R> {
  if (failure === 'aborted') return { type: 'aborted' }
  if (failure === 'offline') return { type: 'offline' }
  return { type: 'network-error' }
}

/** The body names no original correlation: a replay must carry the header, a first answer must not. */
const replayAgrees = (detail: SalesInvoiceDetail, replayed: boolean, replayCorrelationId: string | null) =>
  detail.isReplay === replayed && (detail.isReplay ? replayCorrelationId !== null : replayCorrelationId === null)

interface KeyedContext {
  readonly idempotencyKey: string
  /** The company frozen with the attempt, not whatever the shell holds at retry time. */
  readonly companyId: string
}

const postJson = (path: string, body: unknown, context: KeyedContext, signal: AbortSignal | undefined) =>
  request(
    path,
    {
      method: 'POST',
      headers: {
        ...headers(context.companyId),
        'Content-Type': 'application/json',
        'Idempotency-Key': context.idempotencyKey,
      },
      body: JSON.stringify(body),
    },
    signal,
  )

export async function createSalesInvoice(
  payload: CreateSalesInvoicePayload,
  context: KeyedContext,
  signal?: AbortSignal,
): Promise<OrderCommandOutcome<SalesInvoiceDetail>> {
  const raw = await postJson(
    SALES_INVOICES_PATH,
    { companyAccountId: payload.companyAccountId, shares: payload.shipmentIds.map((shipmentId) => ({ shipmentId })) },
    context,
    signal,
  )
  if ('failure' in raw) return failureOutcome(raw.failure)
  if (raw.status !== CREATED_STATUS) {
    if (raw.status >= 200 && raw.status < 300) return { type: 'malformed' }
    return { type: 'http', status: raw.status, problem: decodeProblem(raw.body, raw.status) }
  }
  // The new invoice's id is the server's; the body must still be one whole, valid detail of that id.
  const id = typeof raw.body === 'object' && raw.body !== null ? (raw.body as { readonly id?: unknown }).id : undefined
  const detail = isReferenceId(id) ? decodeSalesInvoiceDetail(raw.body, id) : null
  if (
    detail === null ||
    detail.state !== 'Draft' ||
    detail.companyAccountId.toLowerCase() !== payload.companyAccountId.toLowerCase() ||
    !replayAgrees(detail, raw.replayed, raw.replayCorrelationId)
  ) {
    return { type: 'malformed' }
  }
  return { type: 'succeeded', data: detail }
}

export async function sendInvoiceDraftCommand(
  kind: InvoiceDraftCommand,
  payload: InvoiceDraftCommandPayload,
  context: InvoiceCommandContext,
  signal?: AbortSignal,
): Promise<OrderCommandOutcome<SalesInvoiceDetail>> {
  const raw = await postJson(invoiceDraftCommandPath(kind, context.invoiceId), invoiceDraftCommandBody(kind, payload), context, signal)
  if ('failure' in raw) return failureOutcome(raw.failure)
  if (raw.status !== STATE_COMMAND_STATUS) {
    if (raw.status >= 200 && raw.status < 300) return { type: 'malformed' }
    return { type: 'http', status: raw.status, problem: decodeProblem(raw.body, raw.status) }
  }
  const detail = decodeSalesInvoiceDetail(raw.body, context.invoiceId)
  if (detail === null || detail.state !== COMMAND_RESULT_STATE[kind] || !replayAgrees(detail, raw.replayed, raw.replayCorrelationId)) {
    return { type: 'malformed' }
  }
  return { type: 'succeeded', data: detail }
}
