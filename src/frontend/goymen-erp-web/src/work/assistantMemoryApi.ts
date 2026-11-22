/**
 * WP-C6 transport for the owner's own assistant memory. READS go through the shared `readJson` (company in
 * `X-Company-Id`, 200 + decode or the classified outcome). COMMANDS carry one `Idempotency-Key` the caller owns
 * (`pendingKey`/`settleKey` from the W1-C transport): after an unknown outcome the SAME key is reused so a retry can only
 * replay. A replay is accepted only with both `Idempotency-Replayed` and `Idempotency-Original-Correlation-Id`.
 */

import { readJson } from '../settings/discountLimitApi'
import type { ViewOutcome } from '../shell/uiState'
import type { WorkCommandOutcome } from './collaborationApi'
import { problemCode } from './collaborationContract'
import {
  ASSISTANT_MEMORY_ENTRIES_PATH, ASSISTANT_MEMORY_STATUS_PATH, ASSISTANT_MEMORY_VIEWS_PATH, assistantMemoryDeletionPath, assistantMemoryRevisionPath,
  decodeAssistantMemoryDeletion, decodeAssistantMemoryEntry, decodeAssistantMemoryEntryPage, decodeAssistantMemoryStatus, decodeAssistantMemoryViewPage,
  type AssistantMemoryDeletion, type AssistantMemoryEntry, type AssistantMemoryEntryPage, type AssistantMemoryKind, type AssistantMemoryStatus,
  type AssistantMemoryViewPage,
} from './assistantMemoryContract'

const API_BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? '/api/v1'
const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY_ENABLED = import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'

export const fetchAssistantMemoryStatus = (companyId: string, userId: string, signal?: AbortSignal): Promise<ViewOutcome<AssistantMemoryStatus>> =>
  readJson(ASSISTANT_MEMORY_STATUS_PATH, companyId, (raw) => decodeAssistantMemoryStatus(raw, companyId, userId), signal)

export const fetchMyAssistantMemory = (companyId: string, userId: string, includeInactive: boolean, signal?: AbortSignal): Promise<ViewOutcome<AssistantMemoryEntryPage>> =>
  readJson(`${ASSISTANT_MEMORY_ENTRIES_PATH}?includeInactive=${includeInactive}`, companyId, (raw) => decodeAssistantMemoryEntryPage(raw, companyId, userId), signal)

export const fetchViewsOfMyAssistantMemory = (companyId: string, userId: string, signal?: AbortSignal): Promise<ViewOutcome<AssistantMemoryViewPage>> =>
  readJson(ASSISTANT_MEMORY_VIEWS_PATH, companyId, (raw) => decodeAssistantMemoryViewPage(raw, companyId, userId), signal)

async function post<T>(path: string, companyId: string, idempotencyKey: string, body: object, success: number,
  decode: (raw: unknown) => T | null): Promise<WorkCommandOutcome<T>> {
  let response: Response
  try {
    response = await fetch(`${API_BASE}${path}`, {
      method: 'POST',
      headers: {
        ...(DEV_IDENTITY_ENABLED ? { 'X-Development-User': DEV_USER } : {}),
        'X-Company-Id': companyId, 'Content-Type': 'application/json', 'Idempotency-Key': idempotencyKey,
      },
      body: JSON.stringify(body),
    })
  } catch {
    return { type: 'unknown', reason: navigator.onLine ? 'network-error' : 'offline' }
  }
  const replayHeader = (response.headers.get('Idempotency-Replayed') ?? '').trim().toLowerCase() === 'true'
  const original = response.headers.get('Idempotency-Original-Correlation-Id')
  if (replayHeader && (original === null || original.trim().length === 0)) return { type: 'unknown', reason: 'unexpected' }
  let raw: unknown = null
  try { raw = await response.json() } catch { raw = null }
  if (response.status === success) {
    const data = decode(raw)
    return data === null ? { type: 'unknown', reason: 'unexpected' } : { type: 'applied', data, replayed: replayHeader }
  }
  if (response.status >= 500) return { type: 'unknown', reason: 'server' }
  if (response.status < 400) return { type: 'unknown', reason: 'unexpected' }
  return { type: 'refused', status: response.status, code: problemCode(raw), replayed: replayHeader }
}

export const createAssistantMemoryEntry = (companyId: string, userId: string, key: string, kind: AssistantMemoryKind, body: string): Promise<WorkCommandOutcome<AssistantMemoryEntry>> =>
  post(ASSISTANT_MEMORY_ENTRIES_PATH, companyId, key, { kind, body }, 201, (raw) => {
    const entry = decodeAssistantMemoryEntry(raw, companyId, userId)
    return entry !== null && entry.state === 'active' ? entry : null
  })

export const reviseAssistantMemoryEntry = (companyId: string, userId: string, key: string, entryId: string, expectedVersion: string, kind: AssistantMemoryKind,
  body: string): Promise<WorkCommandOutcome<AssistantMemoryEntry>> =>
  post(assistantMemoryRevisionPath(entryId), companyId, key, { expectedVersion, kind, body }, 200, (raw) => {
    const entry = decodeAssistantMemoryEntry(raw, companyId, userId, entryId)
    return entry !== null && entry.state === 'active' ? entry : null
  })

export const deleteAssistantMemoryEntry = (companyId: string, key: string, entryId: string, expectedVersion: string): Promise<WorkCommandOutcome<AssistantMemoryDeletion>> =>
  post(assistantMemoryDeletionPath(entryId), companyId, key, { expectedVersion }, 200, (raw) => decodeAssistantMemoryDeletion(raw, entryId))
