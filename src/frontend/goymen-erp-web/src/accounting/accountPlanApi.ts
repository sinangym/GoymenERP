import type { ViewOutcome } from '../shell/uiState'
import { ACCOUNTING_BASE, devHeaders } from './api'
import { decodeAccountPlanView, type AccountPlanView } from './accountPlanContract'

/**
 * M-05: `GET /accounting/account-plan` with only the verified shell company. A success is EXACTLY 200 with a
 * body the strict decoder accepts; every other status keeps its problem body for the auth boundary.
 */
export async function fetchAccountPlan(companyId: string, signal?: AbortSignal): Promise<ViewOutcome<AccountPlanView>> {
  let response: Response
  try {
    response = await fetch(`${ACCOUNTING_BASE}/account-plan`, {
      headers: { ...devHeaders(), 'X-Company-Id': companyId },
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
  }
  if (response.status !== 200) return { type: 'http', status: response.status, body }
  const data = decodeAccountPlanView(body)
  return data === null ? { type: 'malformed' } : { type: 'ok-single', data }
}
