/**
 * Monthly scrap price transport (URT-S13, URT-D72): `GET /api/v1/manufacturing/scrap-prices?year=`,
 * `POST /api/v1/manufacturing/scrap-prices` (a month's first price) and
 * `POST /api/v1/manufacturing/scrap-prices/{year}/{month}/corrections` (the next version with a reason).
 *
 * The read answers a `ViewOutcome` like the other lists. The record command is the house command shape: one frozen
 * attempt and one idempotency key, never retried or re-keyed by this port; a missing or unreadable answer is `unknown`,
 * never a refusal, and the same attempt — same key, same body — may be sent again so the server decides whether it is
 * a replay. The attempt that is still unknown is held in memory per server session, actor and company, so a remount of
 * the page cannot mint a second key for it.
 */

import { MANUFACTURING_BASE, devHeaders } from './api'
import {
  SCRAP_PRICE_FIRST_VERSION,
  canonicalScrapPrice,
  decodeScrapPrice,
  decodeScrapPriceList,
  type ScrapPriceList,
  type ScrapPriceView,
} from './scrapPriceContract'
import type { ViewOutcome } from '../shell/uiState'

export async function fetchScrapPrices(
  companyId: string,
  year: number,
  signal?: AbortSignal,
): Promise<ViewOutcome<ScrapPriceList>> {
  let response: Response
  try {
    response = await fetch(`${MANUFACTURING_BASE}/scrap-prices?year=${encodeURIComponent(String(year))}`, {
      headers: { ...devHeaders(), 'X-Company-Id': companyId },
      cache: 'no-store',
      signal,
    })
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    return navigator.onLine ? { type: 'network-error' } : { type: 'offline' }
  }
  let body: unknown = null
  try {
    body = await response.json()
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    body = null
  }
  // EXACT 200: `Response.ok` spans all of 2xx, which is not this contract.
  if (response.status !== 200) return { type: 'http', status: response.status, body }
  const list = decodeScrapPriceList(body, year)
  return list === null ? { type: 'malformed' } : { type: 'ok-single', data: list }
}

/** One frozen entry: what is sent, exactly, every time this attempt is sent. */
export interface ScrapPriceAttempt {
  readonly idempotencyKey: string
  readonly year: number
  readonly month: number
  /** The text the user entered and the shape check accepted; sent unchanged. */
  readonly tlPerKg: string
}

export type ScrapPriceRecordOutcome =
  | {
      readonly type: 'applied'
      readonly price: ScrapPriceView
      readonly replayed: boolean
      readonly originalCorrelationId: string | null
    }
  | {
      readonly type: 'failed'
      readonly status: number
      readonly code: string | null
      readonly field: string | null
      readonly replayed: boolean
    }
  | {
      readonly type: 'unknown'
      readonly reason:
        | 'aborted'
        | 'offline'
        | 'network-error'
        | 'service-unavailable'
        | 'unexpected-status'
        | 'undecodable'
    }

const FAILURE_STATUSES: readonly number[] = [400, 401, 403, 404, 409, 415, 422]

function text(body: unknown, name: string): string | null {
  if (typeof body !== 'object' || body === null || Array.isArray(body)) return null
  const value = (body as Record<string, unknown>)[name]
  return typeof value === 'string' && value.length > 0 ? value : null
}

function header(headers: Headers, name: string): string | null {
  const value = headers.get(name)
  return value !== null && value.trim().length > 0 ? value.trim() : null
}

/** `Idempotency-Replayed: true` exactly when the original correlation id is named; anything else is unreadable. */
function replayMetadata(
  headers: Headers,
): { readonly replayed: boolean; readonly originalCorrelationId: string | null } | null {
  const replayed = header(headers, 'Idempotency-Replayed')?.toLowerCase() === 'true'
  const originalCorrelationId = header(headers, 'Idempotency-Original-Correlation-Id')
  return replayed === (originalCorrelationId !== null) ? { replayed, originalCorrelationId } : null
}

/**
 * Records one month's scrap price. Applied only on a 201 whose body is the same year, month and price that were sent
 * and whose replay headers agree with each other.
 */
export async function recordScrapPrice(
  companyId: string,
  attempt: ScrapPriceAttempt,
  signal?: AbortSignal,
): Promise<ScrapPriceRecordOutcome> {
  let response: Response
  try {
    response = await fetch(`${MANUFACTURING_BASE}/scrap-prices`, {
      method: 'POST',
      headers: {
        ...devHeaders(),
        'Content-Type': 'application/json',
        'X-Company-Id': companyId,
        'Idempotency-Key': attempt.idempotencyKey,
      },
      body: JSON.stringify({ year: attempt.year, month: attempt.month, tlPerKg: attempt.tlPerKg }),
      signal,
    })
  } catch {
    if (signal?.aborted) return { type: 'unknown', reason: 'aborted' }
    return { type: 'unknown', reason: navigator.onLine ? 'network-error' : 'offline' }
  }

  let body: unknown = null
  try {
    body = await response.json()
  } catch {
    body = null
  }

  if (response.status >= 500) return { type: 'unknown', reason: 'service-unavailable' }
  const metadata = replayMetadata(response.headers)
  if (response.status === 201) {
    const price = decodeScrapPrice(body)
    if (
      metadata === null ||
      price === null ||
      price.year !== attempt.year ||
      price.month !== attempt.month ||
      price.tlPerKg !== canonicalScrapPrice(attempt.tlPerKg) ||
      price.version !== SCRAP_PRICE_FIRST_VERSION
    ) {
      return { type: 'unknown', reason: 'undecodable' }
    }
    return { type: 'applied', price, ...metadata }
  }
  return refusal(response, body, metadata)
}

/** A non-success answer of either command: a refusal the server decided, or unknown when it cannot be read. */
function refusal(
  response: Response,
  body: unknown,
  metadata: { readonly replayed: boolean; readonly originalCorrelationId: string | null } | null,
): ScrapPriceRecordOutcome {
  if (response.status >= 200 && response.status < 300) return { type: 'unknown', reason: 'unexpected-status' }
  if (metadata === null) return { type: 'unknown', reason: 'undecodable' }
  const code = text(body, 'code')
  if ((response.status === 409 || response.status === 422) && code === null) {
    return { type: 'unknown', reason: 'undecodable' }
  }
  if (!FAILURE_STATUSES.includes(response.status)) return { type: 'unknown', reason: 'unexpected-status' }
  return { type: 'failed', status: response.status, code, field: text(body, 'field'), replayed: metadata.replayed }
}

/** One frozen correction (URT-D72): what is sent, exactly, every time this attempt is sent. */
export interface ScrapPriceCorrectionAttempt {
  readonly idempotencyKey: string
  readonly year: number
  readonly month: number
  /** The month's effective version the user corrected; a stale one is the server's 409. */
  readonly expectedVersion: number
  /** The text the user entered and the shape check accepted; sent unchanged. */
  readonly tlPerKg: string
  /** The reason as entered; the server trims it. */
  readonly reason: string
}

/**
 * Corrects one month's effective scrap price with the next version (URT-D72). Applied only on a 200 whose body is the
 * same month, the price that was sent, the version after the expected one and the trimmed reason, with agreeing replay
 * headers. A month without a price is a 404 without a code; a stale version is 409 `manufacturing_version_conflict`.
 */
export async function correctScrapPrice(
  companyId: string,
  attempt: ScrapPriceCorrectionAttempt,
  signal?: AbortSignal,
): Promise<ScrapPriceRecordOutcome> {
  let response: Response
  try {
    response = await fetch(
      `${MANUFACTURING_BASE}/scrap-prices/${encodeURIComponent(String(attempt.year))}/${encodeURIComponent(String(attempt.month))}/corrections`,
      {
        method: 'POST',
        headers: {
          ...devHeaders(),
          'Content-Type': 'application/json',
          'X-Company-Id': companyId,
          'Idempotency-Key': attempt.idempotencyKey,
        },
        body: JSON.stringify({
          expectedVersion: attempt.expectedVersion,
          tlPerKg: attempt.tlPerKg,
          reason: attempt.reason,
        }),
        signal,
      },
    )
  } catch {
    if (signal?.aborted) return { type: 'unknown', reason: 'aborted' }
    return { type: 'unknown', reason: navigator.onLine ? 'network-error' : 'offline' }
  }

  let body: unknown = null
  try {
    body = await response.json()
  } catch {
    body = null
  }

  if (response.status >= 500) return { type: 'unknown', reason: 'service-unavailable' }
  const metadata = replayMetadata(response.headers)
  if (response.status === 200) {
    const price = decodeScrapPrice(body)
    if (
      metadata === null ||
      price === null ||
      price.year !== attempt.year ||
      price.month !== attempt.month ||
      price.tlPerKg !== canonicalScrapPrice(attempt.tlPerKg) ||
      price.version !== attempt.expectedVersion + 1 ||
      price.reason !== attempt.reason.trim()
    ) {
      return { type: 'unknown', reason: 'undecodable' }
    }
    return { type: 'applied', price, ...metadata }
  }
  return refusal(response, body, metadata)
}

/** Unknown attempts of this document, by server session, actor and company; never stored beyond the document. */
const pendingScrapPrices = new Map<string, ScrapPriceAttempt>()
/** Unknown corrections, in a slot of their own: a held first entry never blocks a correction, nor the reverse. */
const pendingScrapPriceCorrections = new Map<string, ScrapPriceCorrectionAttempt>()

/**
 * The slot of a context, or null when there is no actor or company. The server session binding is part of the slot:
 * an attempt held for an earlier session in this document is not this session's.
 */
export function pendingScrapPriceKey(
  sessionBinding: string | null,
  actorId: string | null,
  companyId: string | null,
): string | null {
  return actorId === null || companyId === null ? null : JSON.stringify([sessionBinding, actorId, companyId])
}

export function readPendingScrapPrice(key: string | null): ScrapPriceAttempt | null {
  return key === null ? null : (pendingScrapPrices.get(key) ?? null)
}

/**
 * Holds the attempt before it is sent. Returns false — and nothing may be sent — when there is no slot, or another
 * unresolved attempt holds it (only that exact attempt may be sent again).
 */
export function reservePendingScrapPrice(key: string | null, attempt: ScrapPriceAttempt): boolean {
  if (key === null) return false
  const held = pendingScrapPrices.get(key)
  if (held !== undefined && JSON.stringify(held) !== JSON.stringify(attempt)) return false
  pendingScrapPrices.set(key, Object.freeze({ ...attempt }))
  return true
}

/** Releases only the attempt that settled; a different attempt in the same slot is never removed. */
export function releasePendingScrapPrice(key: string | null, idempotencyKey: string): void {
  if (key === null) return
  if (pendingScrapPrices.get(key)?.idempotencyKey === idempotencyKey) pendingScrapPrices.delete(key)
}

export function readPendingScrapPriceCorrection(key: string | null): ScrapPriceCorrectionAttempt | null {
  return key === null ? null : (pendingScrapPriceCorrections.get(key) ?? null)
}

/** As `reservePendingScrapPrice`, for a correction. */
export function reservePendingScrapPriceCorrection(key: string | null, attempt: ScrapPriceCorrectionAttempt): boolean {
  if (key === null) return false
  const held = pendingScrapPriceCorrections.get(key)
  if (held !== undefined && JSON.stringify(held) !== JSON.stringify(attempt)) return false
  pendingScrapPriceCorrections.set(key, Object.freeze({ ...attempt }))
  return true
}

/** As `releasePendingScrapPrice`, for a correction. */
export function releasePendingScrapPriceCorrection(key: string | null, idempotencyKey: string): void {
  if (key === null) return
  if (pendingScrapPriceCorrections.get(key)?.idempotencyKey === idempotencyKey) pendingScrapPriceCorrections.delete(key)
}

/** Test isolation only. */
export function clearPendingScrapPrices(): void {
  pendingScrapPrices.clear()
  pendingScrapPriceCorrections.clear()
}
