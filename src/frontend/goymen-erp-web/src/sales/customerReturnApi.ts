/**
 * UI-70b S-10 transport — API-40 customer return invoices (SAT-D288…SAT-D294).
 *
 *   GET  /api/v1/sales/customer-returns[?invoiceId]       → the list (one page; the server sends no cursor)
 *   GET  /api/v1/sales/customer-returns/{id}              → the detail
 *   POST /api/v1/sales/customer-returns                   → 201 the recorded Draft
 *   POST /api/v1/sales/customer-returns/{id}/{command}    → 200 the moved return
 *
 * `X-Company-Id` is the company frozen with the attempt and `Idempotency-Key` the attempt's key. The view carries no
 * `isReplay`: a replay is said ONLY by the `Idempotency-Replayed` header, and a replay must name its original correlation
 * while a first answer must not — anything else, a body the strict decoder refuses, or a body in the wrong state is
 * `malformed`, so the caller keeps the key (the write may have happened).
 */
import { headers, readFailure, request } from './api'
import { CREATED_STATUS, STATE_COMMAND_STATUS, decodeProblem } from './contract'
import {
  SALES_CUSTOMER_RETURNS_PATH,
  decodeCustomerReturnList,
  decodeCustomerReturnView,
  salesCustomerReturnApiPath,
  salesCustomerReturnsListPath,
  type CustomerReturnState,
  type CustomerReturnSummary,
  type CustomerReturnView,
} from './customerReturnContract'
import type { OrderCommandOutcome } from './orderAttempt'
import type { ViewOutcome } from '../shell/uiState'

/** The list as the pool paging reads it: the server answers the whole list at once, so the cursor is always the end. */
export interface CustomerReturnPage {
  readonly items: readonly CustomerReturnSummary[]
  readonly nextCursor: null
}

export async function fetchCustomerReturnPage(
  _cursor: string | null,
  companyId: string,
  signal: AbortSignal | undefined,
  invoiceId: string | null,
): Promise<ViewOutcome<CustomerReturnPage>> {
  const raw = await request(salesCustomerReturnsListPath(invoiceId), { headers: headers(companyId) }, signal)
  if ('failure' in raw) return readFailure(raw.failure)
  if (raw.status !== 200) return { type: 'http', status: raw.status, body: raw.body }
  const items = decodeCustomerReturnList(raw.body, invoiceId)
  return items === null ? { type: 'malformed' } : { type: 'ok-single', data: { items, nextCursor: null } }
}

export async function fetchCustomerReturn(
  returnId: string,
  companyId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<CustomerReturnView>> {
  const raw = await request(salesCustomerReturnApiPath(returnId), { headers: headers(companyId) }, signal)
  if ('failure' in raw) return readFailure(raw.failure)
  if (raw.status !== 200) return { type: 'http', status: raw.status, body: raw.body }
  const view = decodeCustomerReturnView(raw.body, returnId)
  return view === null ? { type: 'malformed' } : { type: 'ok-single', data: view }
}

export interface CreateCustomerReturnLineBody {
  readonly originalInvoiceLineId: string
  readonly quantity: string
}

/** `CreateSalesCustomerReturnRequest`, every member present (`shipmentId` null when the return names no shipment). */
export interface CreateCustomerReturnBody {
  readonly originalInvoiceId: string
  readonly customerDocumentNo: string
  readonly customerDocumentDate: string
  readonly shipmentId: string | null
  readonly lines: readonly CreateCustomerReturnLineBody[]
}

/** `SalesDraftStateCommandRequest`: the version the screen read. */
export interface CustomerReturnCommandBody {
  readonly expectedVersion: string
}

export type CustomerReturnCommand = 'submission' | 'approval' | 'finalization'

/** The state each command lands in; a 200 in any other state is not the pinned success. */
export const CUSTOMER_RETURN_COMMAND_TARGET: Readonly<Record<CustomerReturnCommand, CustomerReturnState>> = {
  submission: 'PendingApproval',
  approval: 'Approved',
  finalization: 'Finalized',
}

export const customerReturnCommandPath = (returnId: string, command: CustomerReturnCommand) =>
  `${salesCustomerReturnApiPath(returnId)}/${command}`

/** A recorded answer and whether it was the replay of an earlier send (the header, not the body). */
export interface CustomerReturnMutation {
  readonly view: CustomerReturnView
  readonly isReplay: boolean
}

export interface CustomerReturnCommandContext {
  readonly idempotencyKey: string
  /** The company frozen with the attempt, not whatever the shell holds at retry time. */
  readonly companyId: string
}

type Answer = Awaited<ReturnType<typeof request>>

function interpret(
  raw: Answer,
  expectedStatus: number,
  accept: (body: unknown) => CustomerReturnView | null,
): OrderCommandOutcome<CustomerReturnMutation> {
  if ('failure' in raw) {
    if (raw.failure === 'aborted') return { type: 'aborted' }
    if (raw.failure === 'offline') return { type: 'offline' }
    return { type: 'network-error' }
  }
  if (raw.status !== expectedStatus) {
    if (raw.status >= 200 && raw.status < 300) return { type: 'malformed' }
    return { type: 'http', status: raw.status, problem: decodeProblem(raw.body, raw.status) }
  }
  const view = accept(raw.body)
  // A replay names its original correlation; a first answer does not.
  if (view === null || raw.replayed !== (raw.replayCorrelationId !== null)) return { type: 'malformed' }
  return { type: 'succeeded', data: { view, isReplay: raw.replayed } }
}

function post(path: string, body: unknown, context: CustomerReturnCommandContext, signal?: AbortSignal) {
  return request(
    path,
    {
      method: 'POST',
      headers: { ...headers(context.companyId), 'Content-Type': 'application/json', 'Idempotency-Key': context.idempotencyKey },
      body: JSON.stringify(body),
    },
    signal,
  )
}

export async function createCustomerReturn(
  payload: CreateCustomerReturnBody,
  context: CustomerReturnCommandContext,
  signal?: AbortSignal,
): Promise<OrderCommandOutcome<CustomerReturnMutation>> {
  const body: CreateCustomerReturnBody = {
    originalInvoiceId: payload.originalInvoiceId,
    customerDocumentNo: payload.customerDocumentNo,
    customerDocumentDate: payload.customerDocumentDate,
    shipmentId: payload.shipmentId,
    lines: payload.lines.map((line) => ({ originalInvoiceLineId: line.originalInvoiceLineId, quantity: line.quantity })),
  }
  const raw = await post(SALES_CUSTOMER_RETURNS_PATH, body, context, signal)
  return interpret(raw, CREATED_STATUS, (value) => {
    if (typeof value !== 'object' || value === null || typeof (value as { id?: unknown }).id !== 'string') return null
    const view = decodeCustomerReturnView(value, (value as { id: string }).id)
    return view !== null
      && view.state === 'Draft'
      && view.originalInvoiceId.toLowerCase() === payload.originalInvoiceId.toLowerCase()
      && view.lines.length === payload.lines.length
      ? view
      : null
  })
}

export async function sendCustomerReturnCommand(
  command: CustomerReturnCommand,
  returnId: string,
  payload: CustomerReturnCommandBody,
  context: CustomerReturnCommandContext,
  signal?: AbortSignal,
): Promise<OrderCommandOutcome<CustomerReturnMutation>> {
  const raw = await post(customerReturnCommandPath(returnId, command), { expectedVersion: payload.expectedVersion }, context, signal)
  return interpret(raw, STATE_COMMAND_STATUS, (value) => {
    const view = decodeCustomerReturnView(value, returnId)
    return view !== null && view.state === CUSTOMER_RETURN_COMMAND_TARGET[command] ? view : null
  })
}
