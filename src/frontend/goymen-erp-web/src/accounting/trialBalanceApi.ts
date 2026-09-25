import type { ViewOutcome } from '../shell/uiState'
import { ACCOUNTING_BASE, devHeaders } from './api'
import {
  decodeTrialBalanceReport,
  type TrialBalanceQuery,
  type TrialBalanceReport,
} from './trialBalanceContract'

export async function fetchTrialBalance(
  companyId: string,
  query: TrialBalanceQuery,
  signal?: AbortSignal,
): Promise<ViewOutcome<TrialBalanceReport>> {
  const search = new URLSearchParams({
    fromAccountingDate: query.fromAccountingDate,
    throughAccountingDate: query.throughAccountingDate,
  })
  if (query.currencyId !== null) search.set('currencyId', query.currencyId)

  let response: Response
  try {
    response = await fetch(`${ACCOUNTING_BASE}/trial-balance?${search.toString()}`, {
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
  const data = decodeTrialBalanceReport(body, query)
  return data === null ? { type: 'malformed' } : { type: 'ok-single', data }
}
