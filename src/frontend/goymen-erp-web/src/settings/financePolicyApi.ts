import { API_BASE, readJson, send, noBody, type CommandContext } from './discountLimitApi'
import type { ViewOutcome } from '../shell/uiState'
import { FINANCE_POLICY_PATH, decodeFinancePolicies, decodeFinancePolicyPreview, type FinancePolicyFamily } from './financePolicyContract'
export const fetchFinancePolicies = (company: string, signal?: AbortSignal) => readJson(FINANCE_POLICY_PATH, company, raw => decodeFinancePolicies(raw, company), signal)
export const commandFinancePolicy = (context: CommandContext, suffix: string, body: object) => send('POST', `${FINANCE_POLICY_PATH}/${suffix}`, context, body, 204, noBody)
// Preview is read-only but has a typed POST body; it does not get an idempotency identity or write an attempt.
export async function previewFinancePolicy(company: string, family: FinancePolicyFamily, body: unknown, signal?: AbortSignal): Promise<ViewOutcome<import('./financePolicyContract').FinancePolicyPreview>> {
  let response: Response
  const dev = import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'
  try { response = await fetch(`${API_BASE}${FINANCE_POLICY_PATH}/${family}/preview`, { method: 'POST', credentials: 'same-origin', signal,
    headers: { 'Content-Type': 'application/json', 'X-Company-Id': company, ...(dev ? { 'X-Development-User': (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user' } : {}) }, body: JSON.stringify(body) }) }
  catch { return signal?.aborted ? { type: 'fence-stale' } : navigator.onLine ? { type: 'network-error' } : { type: 'offline' } }
  if (response.status !== 200) return { type: 'http', status: response.status }
  try { const data = decodeFinancePolicyPreview(await response.json(), family); return data === null ? { type: 'malformed' } : { type: 'ok-single', data } } catch { return { type: 'malformed' } }
}
