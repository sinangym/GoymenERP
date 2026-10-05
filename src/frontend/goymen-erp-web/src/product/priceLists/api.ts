/**
 * U-05 transport. `X-Company-Id` on every request (the list is the committed company's); one caller-supplied
 * `Idempotency-Key` per submitted snapshot on every command, never minted or rotated here. Success is EXACTLY the
 * contract's status (201 create, 200 otherwise) with a body the decoder accepts; anything else is a definite refusal
 * (4xx with its problem code) or an UNKNOWN outcome (no response, 5xx, unreadable success) — the command may have
 * applied, and the caller keeps the key.
 */

import type { ViewOutcome } from '../../shell/uiState'
import {
  decodePriceListPage,
  decodePriceListResult,
  decodePriceListView,
  decodeProblem,
  decodeProductOptions,
  type CreatePriceListRequest,
  type PriceListPage,
  type PriceListProblem,
  type PriceListProductOptions,
  type PriceListResult,
  type PriceListView,
  type PublishPriceListVersionRequest,
  type RevisePriceListDraftRequest,
  type StartPriceListVersionRequest,
} from './contract'

export const API_BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? '/api/v1'
const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY_ENABLED = import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'

function devHeaders(): Record<string, string> {
  return DEV_IDENTITY_ENABLED ? { 'X-Development-User': DEV_USER } : {}
}

async function read<T>(path: string, companyId: string, decode: (body: unknown) => T | null, signal?: AbortSignal): Promise<ViewOutcome<T>> {
  let response: Response
  try {
    response = await fetch(`${API_BASE}${path}`, { headers: { ...devHeaders(), 'X-Company-Id': companyId }, signal })
  } catch {
    if (typeof navigator !== 'undefined' && !navigator.onLine) return { type: 'offline' }
    return { type: 'network-error' }
  }

  if (response.status !== 200) {
    let body: unknown = undefined
    try {
      body = await response.json()
    } catch {
      body = undefined
    }
    return { type: 'http', status: response.status, body }
  }

  try {
    const decoded = decode(await response.json())
    return decoded === null ? { type: 'malformed' } : { type: 'ok-single', data: decoded }
  } catch {
    return { type: 'malformed' }
  }
}

export function fetchPriceLists(companyId: string, cursor: string | null, signal?: AbortSignal): Promise<ViewOutcome<PriceListPage>> {
  const query = cursor === null ? '' : `?cursor=${encodeURIComponent(cursor)}`
  return read(`/price-lists${query}`, companyId, decodePriceListPage, signal)
}

export function fetchPriceList(priceListId: string, companyId: string, signal?: AbortSignal): Promise<ViewOutcome<PriceListView>> {
  return read(`/price-lists/${encodeURIComponent(priceListId)}`, companyId, (body) => {
    const decoded = decodePriceListView(body)
    return decoded !== null && sameIdentity(decoded.id, priceListId) ? decoded : null
  }, signal)
}

export function fetchProductOptions(query: string, companyId: string, signal?: AbortSignal): Promise<ViewOutcome<PriceListProductOptions>> {
  const text = query.trim()
  return read(`/price-lists/product-options${text === '' ? '' : `?q=${encodeURIComponent(text)}`}`, companyId, decodeProductOptions, signal)
}

export type PriceListCommandOutcome =
  | { readonly type: 'succeeded'; readonly data: PriceListResult }
  | { readonly type: 'http'; readonly status: number; readonly problem: PriceListProblem }
  | { readonly type: 'unknown'; readonly reason: 'offline' | 'network-error' | 'service-unavailable' | 'malformed-response' }

export type PriceListCommand =
  | { readonly kind: 'create'; readonly payload: CreatePriceListRequest }
  | { readonly kind: 'revise-draft'; readonly priceListId: string; readonly payload: RevisePriceListDraftRequest }
  | { readonly kind: 'start-version'; readonly priceListId: string; readonly payload: StartPriceListVersionRequest }
  | { readonly kind: 'publish'; readonly priceListId: string; readonly payload: PublishPriceListVersionRequest }

export function commandPath(command: PriceListCommand): string {
  switch (command.kind) {
    case 'create':
      return '/price-lists'
    case 'revise-draft':
      return `/price-lists/${encodeURIComponent(command.priceListId)}/draft-revision`
    case 'start-version':
      return `/price-lists/${encodeURIComponent(command.priceListId)}/versions`
    case 'publish':
      return `/price-lists/${encodeURIComponent(command.priceListId)}/publication`
  }
}

/** Sends one command with the caller's key. Never retries on its own and never rotates the key. */
export async function sendPriceListCommand(command: PriceListCommand, companyId: string, idempotencyKey: string): Promise<PriceListCommandOutcome> {
  const success = command.kind === 'create' ? 201 : 200
  let response: Response
  try {
    response = await fetch(`${API_BASE}${commandPath(command)}`, {
      method: 'POST',
      headers: { ...devHeaders(), 'Content-Type': 'application/json', 'X-Company-Id': companyId, 'Idempotency-Key': idempotencyKey },
      body: JSON.stringify(command.payload),
    })
  } catch {
    if (typeof navigator !== 'undefined' && !navigator.onLine) return { type: 'unknown', reason: 'offline' }
    return { type: 'unknown', reason: 'network-error' }
  }

  // A replay of the create answers 200 with the stored list: the same success, never a second list.
  if (response.status === success || (command.kind === 'create' && response.status === 200)) {
    try {
      const data = decodePriceListResult(await response.json())
      const expectedId = command.kind === 'create' ? command.payload.priceListId : command.priceListId
      const replayHeader = response.headers.get('Idempotency-Replayed')
      const originalHeader = response.headers.get('Idempotency-Original-Correlation-Id')
      const hasReplayHeader = replayHeader !== null
      const headerSaysReplay = replayHeader === 'true'
      const replayConsistent = data !== null
        && hasReplayHeader === data.isReplay
        && headerSaysReplay === data.isReplay
        && (data.isReplay ? originalHeader === data.originalCorrelationId : originalHeader === null)
        && (command.kind !== 'create' || (response.status === 200) === data.isReplay)
      return data === null || !sameIdentity(data.priceList.id, expectedId) || !replayConsistent
        ? { type: 'unknown', reason: 'malformed-response' }
        : { type: 'succeeded', data }
    } catch {
      return { type: 'unknown', reason: 'malformed-response' }
    }
  }

  if (response.status >= 500 || (response.status >= 200 && response.status < 300)) {
    return { type: 'unknown', reason: response.status >= 500 ? 'service-unavailable' : 'malformed-response' }
  }

  let body: unknown = null
  try {
    body = await response.json()
  } catch {
    body = null
  }

  return { type: 'http', status: response.status, problem: decodeProblem(body) }
}

function sameIdentity(left: string, right: string): boolean {
  return left.toLowerCase() === right.toLowerCase()
}
