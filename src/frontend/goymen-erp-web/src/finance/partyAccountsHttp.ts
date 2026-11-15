/**
 * B-05 / M-03 transport — the party-account reads (`/api/v1/finance/party-accounts`).
 *
 * | Call | Route |
 * |---|---|
 * | `fetchPartyAccounts` | `GET /finance/party-accounts` |
 * | `fetchPartyAccount` | `GET /finance/party-accounts/{partyId}/{companyAccountId}` |
 *
 * 200 carries the resource. 403 / 503 / 400 are code-less platform problems and become `http` outcomes. A 422 with
 * one of the read's own codes is a stated refusal, handed to the decoder as its marker; any other 422 is not this
 * read's answer and is malformed. The company is only the verified shell company in `X-Company-Id`.
 */

import type { ViewOutcome } from '../shell/uiState'
import { isBoundUuid, type Decoded } from './contract'
import { API_BASE, devHeaders } from './http'
import { PARTY_BALANCE_TOO_MANY_CODE } from './httpPort'
import {
  decodePartyAccountAnswer,
  decodePartyAccountListAnswer,
  type PartyAccountAnswer,
  type PartyAccountListAnswer,
} from './partyAccountsContract'

export const PARTY_ACCOUNTS_PATH = '/finance/party-accounts'
export const PARTY_ACCOUNTS_TOO_MANY_CODE = 'finance_party_accounts_too_many_open_items'
export const PARTY_ACCOUNT_TOO_MANY_CODE = 'finance_party_account_too_many_open_items'

export const partyAccountsUrl = () => `${API_BASE}${PARTY_ACCOUNTS_PATH}`
export const partyAccountUrl = (partyId: string, companyAccountId: string) =>
  `${API_BASE}${PARTY_ACCOUNTS_PATH}/${encodeURIComponent(partyId)}/${encodeURIComponent(companyAccountId)}`

async function read<T>(url: string, companyId: string, refusals: Readonly<Record<string, unknown>>,
  decode: (body: unknown) => Decoded<T>, signal?: AbortSignal): Promise<ViewOutcome<T>> {
  try {
    const response = await fetch(url, {
      method: 'GET',
      headers: { ...devHeaders(), 'X-Company-Id': companyId, Accept: 'application/json' },
      cache: 'no-store',
      signal,
    })
    if (response.status !== 200 && response.status !== 422) return { type: 'http', status: response.status }
    let body: unknown
    try {
      body = await response.json()
    } catch {
      return signal?.aborted ? { type: 'fence-stale' } : { type: 'malformed' }
    }
    if (response.status === 422) {
      const code = typeof body === 'object' && body !== null ? (body as { readonly code?: unknown }).code : undefined
      if (typeof code !== 'string' || !Object.prototype.hasOwnProperty.call(refusals, code)) return { type: 'malformed' }
      body = refusals[code]
    }
    const decoded = decode(body)
    return decoded.ok ? { type: 'ok-single', data: decoded.value } : { type: 'malformed' }
  } catch {
    return signal?.aborted ? { type: 'fence-stale' } : navigator.onLine ? { type: 'network-error' } : { type: 'offline' }
  }
}

export function fetchPartyAccounts(companyId: string,
  signal?: AbortSignal): Promise<ViewOutcome<PartyAccountListAnswer>> {
  if (!isBoundUuid(companyId)) return Promise.resolve({ type: 'malformed' })
  return read(partyAccountsUrl(), companyId,
    { [PARTY_ACCOUNTS_TOO_MANY_CODE]: { partyAccountsRefusal: 'TooManyOpenItems' } },
    (body) => decodePartyAccountListAnswer(body, companyId), signal)
}

export function fetchPartyAccount(companyId: string, partyId: string, companyAccountId: string,
  signal?: AbortSignal): Promise<ViewOutcome<PartyAccountAnswer>> {
  if (!isBoundUuid(companyId) || !isBoundUuid(partyId) || !isBoundUuid(companyAccountId)) {
    return Promise.resolve({ type: 'malformed' })
  }
  return read(partyAccountUrl(partyId, companyAccountId), companyId,
    {
      [PARTY_ACCOUNT_TOO_MANY_CODE]: { partyAccountRefusal: 'TooManyOpenItems' },
      [PARTY_BALANCE_TOO_MANY_CODE]: { partyAccountRefusal: 'TooManyFacts' },
    },
    (body) => decodePartyAccountAnswer(body, companyId, partyId, companyAccountId), signal)
}
