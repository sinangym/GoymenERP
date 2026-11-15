/**
 * B-25 transport for the Work queue (`GET /api/v1/work/queue`). Read-only: the queue decides nothing — each item is
 * decided on its owner's own screen, with that owner's authority, separation of duties and version rules.
 *
 * `X-Company-Id` is the committed shell company; an answer of another company is a tenant-binding breach, not data.
 */

import type { ViewOutcome } from '../shell/uiState'
import { WORK_QUEUE_PATH, belongsToCompany, decodeWorkQueue, type WorkQueue } from './workQueueContract'

const API_BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? '/api/v1'
const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY_ENABLED = import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'

function headers(companyId: string): Record<string, string> {
  return {
    ...(DEV_IDENTITY_ENABLED ? { 'X-Development-User': DEV_USER } : {}),
    'X-Company-Id': companyId,
  }
}

export async function fetchWorkQueue(companyId: string, signal?: AbortSignal): Promise<ViewOutcome<WorkQueue>> {
  let response: Response
  try {
    response = await fetch(`${API_BASE}${WORK_QUEUE_PATH}`, { headers: headers(companyId), signal })
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    if (!navigator.onLine) return { type: 'offline' }
    return { type: 'network-error' }
  }
  let body: unknown = null
  try {
    body = await response.json()
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
  }
  if (response.status !== 200) return { type: 'http', status: response.status, body }
  const data = decodeWorkQueue(body)
  return data === null || !belongsToCompany(data, companyId) ? { type: 'malformed' } : { type: 'ok-single', data }
}
