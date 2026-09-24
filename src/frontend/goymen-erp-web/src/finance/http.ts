/**
 * Finance HTTP transport — only for what the Backend has PUBLISHED.
 *
 * The draft-context read (#261) lives here. FIN-T04b publishes the draft save, list, detail and
 * bank-account list; their adapter is `./httpPort.ts`. Record and Reverse have no route (T05).
 *
 * Hand-written like `../organization/api.ts`; its tiny dev-identity helper is repeated here on
 * purpose rather than imported across a single-writer lane.
 */

import type { DraftContextRequest } from './draftContext'
import type { ManualBankPaymentDraftContextRequest } from './manualBankPaymentDraftContext'

export const API_BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? '/api/v1'
const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY_ENABLED = import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'

export function devHeaders(): Record<string, string> {
  return DEV_IDENTITY_ENABLED ? { 'X-Development-User': DEV_USER } : {}
}

/**
 * - `ok`: an EXACT 200 arrived; `body` is its parsed JSON (undefined when unparseable, which the
 *   decoder then refuses);
 * - `status`: any other HTTP status arrived — the body is not needed to classify it;
 * - `no-reply`: nothing trustworthy arrived (network, offline, our own abort).
 */
export type HttpReadReply =
  | { readonly kind: 'ok'; readonly body: unknown }
  | { readonly kind: 'status'; readonly status: number }
  | { readonly kind: 'no-reply' }

export const DRAFT_CONTEXT_PATH = '/finance/manual-receipts/draft-context'
export const MANUAL_BANK_PAYMENT_DRAFT_CONTEXT_PATH = '/finance/manual-bank-payments/draft-context'

export function draftContextUrl(request: DraftContextRequest): string {
  const params = new URLSearchParams()
  params.set('treasuryAccountId', request.treasuryAccountId)
  params.set('partyId', request.partyId)
  params.set('companyAccountId', request.companyAccountId)
  params.set('occurredOn', request.occurredOn)
  return `${API_BASE}${DRAFT_CONTEXT_PATH}?${params.toString()}`
}

export async function fetchDraftContext(
  companyId: string,
  request: DraftContextRequest,
  signal: AbortSignal,
): Promise<HttpReadReply> {
  let response: Response
  try {
    response = await fetch(draftContextUrl(request), {
      headers: { ...devHeaders(), 'X-Company-Id': companyId, Accept: 'application/json' },
      signal,
    })
  } catch {
    return { kind: 'no-reply' }
  }
  if (response.status !== 200) return { kind: 'status', status: response.status }
  try {
    return { kind: 'ok', body: await response.json() }
  } catch {
    return signal.aborted ? { kind: 'no-reply' } : { kind: 'ok', body: undefined }
  }
}

export function manualBankPaymentDraftContextUrl(request: ManualBankPaymentDraftContextRequest): string {
  const params = new URLSearchParams()
  params.set('treasuryAccountId', request.treasuryAccountId)
  params.set('partyId', request.partyId)
  params.set('companyAccountId', request.companyAccountId)
  params.set('occurredOn', request.occurredOn)
  return `${API_BASE}${MANUAL_BANK_PAYMENT_DRAFT_CONTEXT_PATH}?${params.toString()}`
}

export async function fetchManualBankPaymentDraftContext(companyId: string,
  request: ManualBankPaymentDraftContextRequest, signal: AbortSignal): Promise<HttpReadReply> {
  let response: Response
  try {
    response = await fetch(manualBankPaymentDraftContextUrl(request), {
      headers: { ...devHeaders(), 'X-Company-Id': companyId, Accept: 'application/json' }, signal,
    })
  } catch {
    return { kind: 'no-reply' }
  }
  if (response.status !== 200) return { kind: 'status', status: response.status }
  try {
    return { kind: 'ok', body: await response.json() }
  } catch {
    return signal.aborted ? { kind: 'no-reply' } : { kind: 'ok', body: undefined }
  }
}
