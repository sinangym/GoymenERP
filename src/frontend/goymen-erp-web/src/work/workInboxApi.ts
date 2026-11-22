/**
 * W1-A transport for the Work inbox. The read (`GET /api/v1/work/inbox?view=awaiting-my-decision`) decides nothing; each
 * item is decided on its owner's screen. The measurement event (`POST /api/v1/work/inbox/events`) is fire-and-forget:
 * it never blocks navigation and its failure is never shown — it is Work's own count, not a user action.
 *
 * `X-Company-Id` is the committed shell company; an answer of another company is a tenant-binding breach, not data.
 */

import type { ViewOutcome } from '../shell/uiState'
import {
  WORK_INBOX_EVENTS_PATH,
  WORK_INBOX_PATH,
  WORK_INBOX_VIEW_AWAITING_MY_DECISION,
  decodeWorkInbox,
  decodeWorkInboxEventRecorded,
  type WorkInbox,
  type WorkInboxEventType,
  type WorkInboxItem,
} from './workInboxContract'

const API_BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? '/api/v1'
const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY_ENABLED = import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'

function headers(companyId: string): Record<string, string> {
  return {
    ...(DEV_IDENTITY_ENABLED ? { 'X-Development-User': DEV_USER } : {}),
    'X-Company-Id': companyId,
  }
}

export async function fetchWorkInbox(companyId: string, signal?: AbortSignal): Promise<ViewOutcome<WorkInbox>> {
  const query = new URLSearchParams({ view: WORK_INBOX_VIEW_AWAITING_MY_DECISION }).toString()
  let response: Response
  try {
    response = await fetch(`${API_BASE}${WORK_INBOX_PATH}?${query}`, { headers: headers(companyId), signal })
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
  const data = decodeWorkInbox(body, companyId)
  return data === null ? { type: 'malformed' } : { type: 'ok-single', data }
}

/**
 * Records one measurement event and resolves to whether it was accepted. Never rejects: every failure (no network,
 * 4xx, 5xx, an off-contract answer) resolves to false and is ignored by the screen.
 */
export async function recordWorkInboxEvent(
  companyId: string,
  item: Pick<WorkInboxItem, 'kind' | 'itemId' | 'waitingSinceUtc'>,
  eventType: WorkInboxEventType,
): Promise<boolean> {
  try {
    const clientEventId = crypto.randomUUID()
    const response = await fetch(`${API_BASE}${WORK_INBOX_EVENTS_PATH}`, {
      method: 'POST',
      headers: { ...headers(companyId), 'Content-Type': 'application/json' },
      body: JSON.stringify({ clientEventId, kind: item.kind, itemId: item.itemId, waitingSinceUtc: item.waitingSinceUtc, eventType }),
      // The owner link navigates away at once; the count still leaves.
      keepalive: true,
    })
    if (response.status !== 200) return false
    return decodeWorkInboxEventRecorded(await response.json(), clientEventId) !== null
  } catch {
    return false
  }
}
