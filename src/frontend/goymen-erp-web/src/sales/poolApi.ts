/**
 * S-01 transport — sales requests (API-20/21) and sales quotes (API-22/23/24) over `/api/v1/sales/requests` and
 * `/api/v1/sales/quotes` (#824, #828). The transport invents no field and carries no business rule; what it adds is the
 * consumer check every Sales mutation makes:
 *  - a read is data only when every record belongs to the company it was read under, and a detail only when it is the
 *    record that was asked for;
 *  - a command answer is success only at the pinned status, with `isReplay` agreeing with `Idempotency-Replayed`, and
 *    describing THIS attempt (the record, the company and — for a create and the conversion — the sent content by value).
 *    Anything else is `malformed`: the write may exist, so the caller keeps the key and retries the same snapshot.
 *
 * `X-Company-Id` is the committed shell company — request context, never a body member.
 */

import { headers, readFailure, request, type Raw } from './api'
import { decodeDraftMutation, decodeProblem, salesDraftDetailPath, type DraftMutation } from './contract'
import type { OrderCommandOutcome } from './orderAttempt'
import {
  POOL_COMMAND_STATUS,
  POOL_CREATED_STATUS,
  SALES_QUOTE_PATH,
  SALES_REQUEST_PATH,
  decodeQuoteDetail,
  decodeQuoteMutation,
  decodeQuotePage,
  decodeRequestDetail,
  decodeRequestMutation,
  decodeRequestPage,
  salesQuoteCommandPath,
  salesQuoteDetailPath,
  salesRequestDecisionPath,
  salesRequestDetailPath,
  type CreateSalesQuoteBody,
  type CreateSalesRequestBody,
  type QuoteState,
  type RequestState,
  type SalesQuoteConversionBody,
  type SalesQuoteDetail,
  type SalesQuoteLineBody,
  type SalesQuoteLossBody,
  type SalesQuoteMutation,
  type SalesQuotePage,
  type SalesQuoteRevisionBody,
  type SalesRequestDecisionBody,
  type SalesRequestDecisionCommand,
  type SalesRequestDetail,
  type SalesRequestMutation,
  type SalesRequestPage,
} from './poolContract'
import type { ViewOutcome } from '../shell/uiState'

/** Source bound: 1..100, default 50. */
export const POOL_PAGE_SIZE = 50

/* ── Reads ────────────────────────────────────────────────────────────────── */

async function read<T>(
  path: string,
  companyId: string,
  signal: AbortSignal | undefined,
  decode: (body: unknown) => T | null,
  belongs: (data: T) => boolean,
): Promise<ViewOutcome<T>> {
  const raw = await request(path, { headers: headers(companyId) }, signal)
  if ('failure' in raw) return readFailure(raw.failure)
  if (raw.status !== 200) return { type: 'http', status: raw.status, body: raw.body }
  const data = decode(raw.body)
  // A record of another company is a tenant-binding breach, not a row to show.
  return data === null || !belongs(data) ? { type: 'malformed' } : { type: 'ok-single', data }
}

function listPath(base: string, cursor: string | null, state: string | null): string {
  const params = new URLSearchParams({ pageSize: String(POOL_PAGE_SIZE) })
  if (cursor !== null) params.set('cursor', cursor)
  // The exact state narrows the list on the server; the client never filters a partial page.
  if (state !== null) params.set('state', state)
  return `${base}?${params.toString()}`
}

export function fetchRequestPage(
  cursor: string | null,
  companyId: string,
  signal?: AbortSignal,
  state: RequestState | null = null,
): Promise<ViewOutcome<SalesRequestPage>> {
  return read(listPath(SALES_REQUEST_PATH, cursor, state), companyId, signal, decodeRequestPage,
    (page) => page.items.every((item) => item.companyId === companyId))
}

export function fetchRequestDetail(requestId: string, companyId: string, signal?: AbortSignal): Promise<ViewOutcome<SalesRequestDetail>> {
  return read(salesRequestDetailPath(requestId), companyId, signal, (body) => decodeRequestDetail(body, requestId),
    (detail) => detail.request.companyId === companyId)
}

export function fetchQuotePage(
  cursor: string | null,
  companyId: string,
  signal?: AbortSignal,
  state: QuoteState | null = null,
): Promise<ViewOutcome<SalesQuotePage>> {
  return read(listPath(SALES_QUOTE_PATH, cursor, state), companyId, signal, decodeQuotePage,
    (page) => page.items.every((item) => item.companyId === companyId))
}

export function fetchQuoteDetail(quoteId: string, companyId: string, signal?: AbortSignal): Promise<ViewOutcome<SalesQuoteDetail>> {
  return read(salesQuoteDetailPath(quoteId), companyId, signal, (body) => decodeQuoteDetail(body, quoteId),
    (detail) => detail.quote.companyId === companyId)
}

/* ── Commands ─────────────────────────────────────────────────────────────── */

export interface PoolCommandContext {
  readonly idempotencyKey: string
  /** The company frozen with the attempt, not whatever the shell holds at retry time. */
  readonly companyId: string
}

async function post(path: string, payload: unknown, context: PoolCommandContext, signal: AbortSignal | undefined): Promise<Raw> {
  return request(
    path,
    {
      method: 'POST',
      headers: { ...headers(context.companyId), 'Content-Type': 'application/json', 'Idempotency-Key': context.idempotencyKey },
      body: JSON.stringify(payload),
    },
    signal,
  )
}

/**
 * The shared outcome rules: a transport failure is unknown; any 2xx other than `expected` is outside the contract (the
 * write may exist); any other status is a refusal with its problem; the pinned status is success only when `accept`
 * vouches for the body.
 */
function commandOutcome<T>(raw: Raw, expected: number, accept: (raw: Extract<Raw, { status: number }>) => T | null): OrderCommandOutcome<T> {
  if ('failure' in raw) {
    if (raw.failure === 'aborted') return { type: 'aborted' }
    if (raw.failure === 'offline') return { type: 'offline' }
    return { type: 'network-error' }
  }
  if (raw.status !== expected) {
    if (raw.status >= 200 && raw.status < 300) return { type: 'malformed' }
    return { type: 'http', status: raw.status, problem: decodeProblem(raw.body, raw.status) }
  }
  const data = accept(raw)
  return data === null ? { type: 'malformed' } : { type: 'succeeded', data }
}

const locationNames = (location: string | null, path: string) => location === null || location.endsWith(`/api/v1${path}`)

/** Whether a request answer describes the create that was sent: the company and, by value, every member the detail reflects. */
export function describesRequestAttempt(mutation: SalesRequestMutation, sent: CreateSalesRequestBody, companyId: string): boolean {
  const { detail } = mutation
  const summary = detail.request
  if (
    summary.companyId !== companyId ||
    summary.partyId !== sent.partyId ||
    summary.companyAccountId !== sent.companyAccountId ||
    summary.businessDate !== sent.businessDate ||
    detail.requestedDeliveryDate !== sent.requestedDeliveryDate ||
    detail.note !== sent.note ||
    detail.lines.length !== sent.lines.length
  ) {
    return false
  }
  return sent.lines.every((line) => {
    const answered = detail.lines.find((candidate) => candidate.lineId === line.lineId)
    return (
      answered !== undefined &&
      answered.productId === line.productId &&
      answered.productVersion === line.expectedProductVersion &&
      answered.orderUnitId === line.orderUnitId &&
      answered.quantity === line.quantity &&
      answered.requestedDeliveryDate === line.requestedDeliveryDate &&
      answered.note === line.note
    )
  })
}

export async function createRequest(
  payload: CreateSalesRequestBody,
  context: PoolCommandContext,
  signal?: AbortSignal,
): Promise<OrderCommandOutcome<SalesRequestMutation>> {
  const raw = await post(SALES_REQUEST_PATH, payload, context, signal)
  return commandOutcome(raw, POOL_CREATED_STATUS, (answer) => {
    const mutation = decodeRequestMutation(answer.body)
    if (
      mutation === null ||
      mutation.isReplay !== answer.replayed ||
      !describesRequestAttempt(mutation, payload, context.companyId) ||
      !locationNames(answer.location, salesRequestDetailPath(mutation.detail.request.id))
    ) {
      return null
    }
    return mutation
  })
}

/** API-21: approve or reject. The answer must describe THIS request under THIS company. */
export async function sendRequestDecision(
  command: SalesRequestDecisionCommand,
  payload: SalesRequestDecisionBody,
  context: PoolCommandContext & { readonly requestId: string },
  signal?: AbortSignal,
): Promise<OrderCommandOutcome<SalesRequestMutation>> {
  const raw = await post(salesRequestDecisionPath(context.requestId, command), payload, context, signal)
  return commandOutcome(raw, POOL_COMMAND_STATUS, (answer) => {
    const mutation = decodeRequestMutation(answer.body)
    if (
      mutation === null ||
      mutation.isReplay !== answer.replayed ||
      mutation.detail.request.id !== context.requestId ||
      mutation.detail.request.companyId !== context.companyId
    ) {
      return null
    }
    return mutation
  })
}

/** A quoted line answered as sent; the commercial texts are compared only when the caller may see them. */
function sameQuoteLines(detail: SalesQuoteDetail, sent: readonly SalesQuoteLineBody[]): boolean {
  if (detail.lines.length !== sent.length) return false
  return sent.every((line) => {
    const answered = detail.lines.find((candidate) => candidate.lineId === line.lineId)
    return (
      answered !== undefined &&
      answered.productId === line.productId &&
      answered.productVersion === line.expectedProductVersion &&
      answered.orderUnitId === line.orderUnitId &&
      answered.quantity === line.quantity &&
      answered.requestedDeliveryDate === line.requestedDeliveryDate &&
      answered.note === line.note &&
      (answered.priceDisclosure === 'Withheld' ||
        (answered.unitPrice === line.unitPrice &&
          answered.priceUnitId === line.priceUnitId &&
          answered.discountPercent === line.discountPercent))
    )
  })
}

export function describesQuoteAttempt(mutation: SalesQuoteMutation, sent: CreateSalesQuoteBody, companyId: string): boolean {
  const { detail } = mutation
  const quote = detail.quote
  const sentRequests = new Set(sent.requestIds ?? [])
  return (
    quote.companyId === companyId &&
    quote.partyId === sent.partyId &&
    quote.companyAccountId === sent.companyAccountId &&
    quote.currencyId === sent.currencyId &&
    quote.businessDate === sent.businessDate &&
    quote.validUntil === sent.validUntil &&
    detail.requestedDeliveryDate === sent.requestedDeliveryDate &&
    detail.note === sent.note &&
    detail.requestIds.length === sentRequests.size &&
    detail.requestIds.every((id) => sentRequests.has(id)) &&
    sameQuoteLines(detail, sent.lines)
  )
}

export async function createQuote(
  payload: CreateSalesQuoteBody,
  context: PoolCommandContext,
  signal?: AbortSignal,
): Promise<OrderCommandOutcome<SalesQuoteMutation>> {
  const raw = await post(SALES_QUOTE_PATH, payload, context, signal)
  return commandOutcome(raw, POOL_CREATED_STATUS, (answer) => {
    const mutation = decodeQuoteMutation(answer.body)
    if (
      mutation === null ||
      mutation.isReplay !== answer.replayed ||
      !describesQuoteAttempt(mutation, payload, context.companyId) ||
      !locationNames(answer.location, salesQuoteDetailPath(mutation.detail.quote.id))
    ) {
      return null
    }
    return mutation
  })
}

type QuoteContext = PoolCommandContext & { readonly quoteId: string }

async function sendQuoteMutation(
  path: string,
  payload: SalesQuoteRevisionBody | SalesQuoteLossBody,
  context: QuoteContext,
  signal: AbortSignal | undefined,
): Promise<OrderCommandOutcome<SalesQuoteMutation>> {
  const raw = await post(path, payload, context, signal)
  return commandOutcome(raw, POOL_COMMAND_STATUS, (answer) => {
    const mutation = decodeQuoteMutation(answer.body)
    if (
      mutation === null ||
      mutation.isReplay !== answer.replayed ||
      mutation.detail.quote.id !== context.quoteId ||
      mutation.detail.quote.companyId !== context.companyId
    ) {
      return null
    }
    return mutation
  })
}

export const reviseQuote = (payload: SalesQuoteRevisionBody, context: QuoteContext, signal?: AbortSignal) =>
  sendQuoteMutation(salesQuoteCommandPath(context.quoteId, 'revision'), payload, context, signal)

export const recordQuoteLoss = (payload: SalesQuoteLossBody, context: QuoteContext, signal?: AbortSignal) =>
  sendQuoteMutation(salesQuoteCommandPath(context.quoteId, 'loss'), payload, context, signal)

/**
 * No 162: whether the created order is THIS quote's — the same company, customer and currency, the business date that
 * was sent, and every quoted line verbatim under its own line id (the conversion keeps them). A price the caller may not
 * see on either side is not compared.
 *
 * A REPLAY (the same key again, after an unknown outcome) answers the order as it stands NOW: it may have been revised
 * since the conversion (quantity, price), so its lines are not compared — the header facts the order never changes
 * (company, customer, currency, the sent business date) still bind it, and the server's replay is keyed on this attempt.
 */
export function describesConversion(mutation: DraftMutation, quote: SalesQuoteDetail, sent: SalesQuoteConversionBody, companyId: string): boolean {
  const { detail } = mutation
  const draft = detail.draft
  if (
    draft.companyId !== companyId ||
    draft.partyId !== quote.quote.partyId ||
    draft.companyAccountId !== quote.quote.companyAccountId ||
    draft.currencyId !== quote.quote.currencyId ||
    draft.businessDate !== sent.businessDate
  ) {
    return false
  }
  if (mutation.isReplay) return true
  if (detail.lines.length !== quote.lines.length) return false
  return quote.lines.every((line) => {
    const answered = detail.lines.find((candidate) => candidate.lineId === line.lineId)
    const pricesVisible = answered?.priceDisclosure === 'Visible' && line.priceDisclosure === 'Visible'
    return (
      answered !== undefined &&
      answered.productId === line.productId &&
      answered.orderUnitId === line.orderUnitId &&
      answered.quantity === line.quantity &&
      (!pricesVisible ||
        (answered.unitPrice === line.unitPrice &&
          answered.priceUnitId === line.priceUnitId &&
          answered.discountPercent === line.discountPercent))
    )
  })
}

/** API-23 "Siparişe dönüştür": 201 with the ORDER's detail and Location; the same key again answers the same order. */
export async function convertQuote(
  payload: SalesQuoteConversionBody,
  quote: SalesQuoteDetail,
  context: PoolCommandContext,
  signal?: AbortSignal,
): Promise<OrderCommandOutcome<DraftMutation>> {
  const raw = await post(salesQuoteCommandPath(quote.quote.id, 'conversion'), payload, context, signal)
  return commandOutcome(raw, POOL_CREATED_STATUS, (answer) => {
    const mutation = decodeDraftMutation(answer.body)
    if (
      mutation === null ||
      mutation.isReplay !== answer.replayed ||
      !describesConversion(mutation, quote, payload, context.companyId) ||
      !locationNames(answer.location, salesDraftDetailPath(mutation.detail.draft.id))
    ) {
      return null
    }
    return mutation
  })
}
