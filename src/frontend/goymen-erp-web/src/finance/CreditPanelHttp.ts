/**
 * F018 plan §L3 transport — the Cari card's Finance panel (`/api/v1/finance/party-credit-panel`).
 *
 * | Call | Route |
 * |---|---|
 * | `fetchCreditPanel` | `GET /finance/party-credit-panel/parties/{partyId}/accounts/{companyAccountId}` |
 *
 * The read answers 200 with the panel; 403 (neither the credit limit nor the collateral read) / 404 (an account the
 * caller cannot see) / 503 (an authority that cannot be measured) are code-less platform problems and become `http`
 * outcomes. The company is only the verified shell company in `X-Company-Id`. Nothing is written.
 */

import type { ViewOutcome } from '../shell/uiState'
import { isBoundUuid } from './contract'
import { decodeCreditPanel, type CreditPanelView } from './CreditPanelContract'
import { API_BASE, devHeaders } from './http'

export const PARTY_CREDIT_PANEL_PATH = '/finance/party-credit-panel'

export const creditPanelUrl = (partyId: string, companyAccountId: string) =>
  `${API_BASE}${PARTY_CREDIT_PANEL_PATH}/parties/${encodeURIComponent(partyId)}/accounts/${encodeURIComponent(companyAccountId)}`

export async function fetchCreditPanel(companyId: string, partyId: string, companyAccountId: string,
  signal?: AbortSignal): Promise<ViewOutcome<CreditPanelView>> {
  if (!isBoundUuid(companyId) || !isBoundUuid(partyId) || !isBoundUuid(companyAccountId)) return { type: 'malformed' }
  try {
    const response = await fetch(creditPanelUrl(partyId, companyAccountId), {
      method: 'GET',
      headers: { ...devHeaders(), 'X-Company-Id': companyId, Accept: 'application/json' },
      cache: 'no-store',
      signal,
    })
    if (response.status !== 200) return { type: 'http', status: response.status }
    let body: unknown
    try {
      body = await response.json()
    } catch {
      return signal?.aborted ? { type: 'fence-stale' } : { type: 'malformed' }
    }
    const decoded = decodeCreditPanel(body, companyId, partyId, companyAccountId)
    return decoded.ok ? { type: 'ok-single', data: decoded.value } : { type: 'malformed' }
  } catch {
    return signal?.aborted ? { type: 'fence-stale' } : navigator.onLine ? { type: 'network-error' } : { type: 'offline' }
  }
}
