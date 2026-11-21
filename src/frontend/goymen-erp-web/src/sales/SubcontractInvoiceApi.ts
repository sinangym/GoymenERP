/**
 * F2 transport: the subcontract service ("fason hizmet") invoice draft from one delivery weighing.
 *
 *   POST /api/v1/sales/subcontract-service-invoices        → 201 SalesSubcontractServiceInvoiceDetailView (Draft)
 *   GET  /api/v1/sales/subcontract-service-invoices/{id}   → SalesSubcontractServiceInvoiceDetailView
 *
 * The body names the order, the loading instruction and the weighing only — the net kilograms, the price, the tax and every
 * amount are the server's. `X-Company-Id` is the company frozen with the attempt and `Idempotency-Key` the attempt's key.
 * Success is the pinned status AND a body that decodes, is a Draft of the order, instruction and weighing that were asked for,
 * and whose `isReplay` agrees with the replay headers — anything else is `malformed`, so the caller keeps the key.
 */
import { headers, readFailure, request, type Raw } from './api'
import { CREATED_STATUS, decodeProblem } from './contract'
import type { OrderCommandOutcome } from './orderAttempt'
import {
  SUBCONTRACT_SERVICE_INVOICES_PATH,
  decodeSubcontractInvoiceDetail,
  subcontractServiceInvoicePath,
  type SubcontractInvoiceDetail,
  type SubcontractInvoiceList,
  decodeSubcontractInvoiceList,
  subcontractInvoicesOfOrderPath,
} from './SubcontractInvoiceContract'
import type { ViewOutcome } from '../shell/uiState'

export async function fetchSubcontractInvoiceList(salesOrderId: string, companyId: string, cursor: string | null,
  signal?: AbortSignal): Promise<ViewOutcome<SubcontractInvoiceList>> {
  const query = new URLSearchParams({ pageSize: '50', ...(cursor === null ? {} : { cursor }) })
  const raw = await request(`${subcontractInvoicesOfOrderPath(salesOrderId)}?${query}`, { headers: headers(companyId) }, signal)
  if ('failure' in raw) return readFailure(raw.failure)
  if (raw.status !== 200) return { type: 'http', status: raw.status, body: raw.body }
  const data = decodeSubcontractInvoiceList(raw.body, salesOrderId)
  return data === null ? { type: 'malformed' } : { type: 'ok-single', data }
}

/** The create body as the person chose it: the subcontract order and the delivery weighing of its loading instruction. */
export interface CreateSubcontractInvoicePayload {
  readonly salesOrderId: string
  readonly loadingInstructionId: string
  readonly weighingId: string
}

interface KeyedContext {
  readonly idempotencyKey: string
  /** The company frozen with the attempt, not whatever the shell holds at retry time. */
  readonly companyId: string
}

function failureOutcome<R>(failure: Extract<Raw, { readonly failure: unknown }>['failure']): OrderCommandOutcome<R> {
  if (failure === 'aborted') return { type: 'aborted' }
  if (failure === 'offline') return { type: 'offline' }
  return { type: 'network-error' }
}

const sameId = (left: string, right: string) => left.toLowerCase() === right.toLowerCase()

/** The body names no original correlation: a replay must carry the header, a first answer must not. */
const replayAgrees = (detail: SubcontractInvoiceDetail, replayed: boolean, replayCorrelationId: string | null) =>
  detail.isReplay === replayed && (detail.isReplay ? replayCorrelationId !== null : replayCorrelationId === null)

/** The exact wire body, in the server's member order. */
export const subcontractInvoiceBody = (payload: CreateSubcontractInvoicePayload) => ({
  salesOrderId: payload.salesOrderId,
  loadingInstructionId: payload.loadingInstructionId,
  weighingId: payload.weighingId,
})

export async function createSubcontractInvoice(
  payload: CreateSubcontractInvoicePayload,
  context: KeyedContext,
  signal?: AbortSignal,
): Promise<OrderCommandOutcome<SubcontractInvoiceDetail>> {
  const raw = await request(
    SUBCONTRACT_SERVICE_INVOICES_PATH,
    {
      method: 'POST',
      headers: {
        ...headers(context.companyId),
        'Content-Type': 'application/json',
        'Idempotency-Key': context.idempotencyKey,
      },
      body: JSON.stringify(subcontractInvoiceBody(payload)),
    },
    signal,
  )
  if ('failure' in raw) return failureOutcome(raw.failure)
  if (raw.status !== CREATED_STATUS) {
    if (raw.status >= 200 && raw.status < 300) return { type: 'malformed' }
    return { type: 'http', status: raw.status, problem: decodeProblem(raw.body, raw.status) }
  }
  const detail = decodeSubcontractInvoiceDetail(raw.body, null)
  if (
    detail === null ||
    (detail.state !== 'Draft' && !detail.isReplay) ||
    !sameId(detail.salesOrderId, payload.salesOrderId) ||
    !sameId(detail.loadingInstructionId, payload.loadingInstructionId) ||
    !sameId(detail.weighingId, payload.weighingId) ||
    !replayAgrees(detail, raw.replayed, raw.replayCorrelationId)
  ) {
    return { type: 'malformed' }
  }
  return { type: 'succeeded', data: detail }
}

export async function fetchSubcontractInvoice(
  invoiceId: string,
  companyId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<SubcontractInvoiceDetail>> {
  const raw = await request(subcontractServiceInvoicePath(invoiceId), { headers: headers(companyId) }, signal)
  if ('failure' in raw) return readFailure(raw.failure)
  if (raw.status !== 200) return { type: 'http', status: raw.status, body: raw.body }
  const detail = decodeSubcontractInvoiceDetail(raw.body, invoiceId)
  return detail === null ? { type: 'malformed' } : { type: 'ok-single', data: detail }
}

export type SubcontractAction = 'submit' | 'approve' | 'reject' | 'cancel' | 'match' | 'finalize' | 'resend-finance'
export interface SubcontractActionPayload {
  readonly invoiceId: string
  readonly action: SubcontractAction
  readonly expectedVersion: string
  readonly reason?: string
  readonly officialInvoiceNo?: string
  readonly ettn?: string
  readonly approveDifference?: boolean
  readonly differenceReason?: string
}

export async function commandSubcontractInvoice(payload: SubcontractActionPayload, context: KeyedContext): Promise<OrderCommandOutcome<SubcontractInvoiceDetail>> {
  const { invoiceId, action, ...body } = payload
  const raw = await request(`${subcontractServiceInvoicePath(invoiceId)}/${action}`, {
    method: 'POST', headers: { ...headers(context.companyId), 'Content-Type': 'application/json', 'Idempotency-Key': context.idempotencyKey },
    body: JSON.stringify(body),
  }, undefined)
  if ('failure' in raw) return failureOutcome(raw.failure)
  if (raw.status !== 200) return raw.status >= 200 && raw.status < 300 ? { type: 'malformed' }
    : { type: 'http', status: raw.status, problem: decodeProblem(raw.body, raw.status) }
  const detail = decodeSubcontractInvoiceDetail(raw.body, invoiceId)
  if (detail === null || !replayAgrees(detail, raw.replayed, raw.replayCorrelationId)) return { type: 'malformed' }
  return { type: 'succeeded', data: detail }
}
