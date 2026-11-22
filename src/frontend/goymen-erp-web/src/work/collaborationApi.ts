/**
 * W1-C transport. READS go through the shared `readJson` (company in `X-Company-Id`, 200 + decode or the classified
 * outcome). COMMANDS carry one `Idempotency-Key` the caller owns: after an unknown outcome the SAME key is reused so a
 * retry can only replay. Success is exactly the contract status; a replay is accepted only when the
 * `Idempotency-Replayed` header is present together with an `Idempotency-Original-Correlation-Id`.
 */

import { readJson } from '../settings/discountLimitApi'
import type { ViewOutcome } from '../shell/uiState'
import {
  WORK_INBOX_PATH, WORK_LIMITS, WORK_MY_TASKS_PATH, WORK_PEOPLE_PATH, WORK_TASKS_PATH,
  decodeWorkInboxPage, decodeWorkInboxRead, decodeWorkNote, decodeWorkNotePage, decodeWorkPeoplePage, decodeWorkTask, decodeWorkTaskPage,
  problemCode, workInboxReadPath, workNotePassivationPath, workRecordNotesPath, workRecordTasksPath, workTaskCancellationPath, workTaskCompletionPath,
  type WorkInboxClass, type WorkInboxPage, type WorkInboxRead, type WorkNote, type WorkNotePage, type WorkPeoplePage, type WorkRecordType,
  type WorkTask, type WorkTaskPage,
} from './collaborationContract'

const API_BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? '/api/v1'
const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY_ENABLED = import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'

export const fetchRecordNotes = (companyId: string, type: WorkRecordType, recordId: string, includePassive: boolean, signal?: AbortSignal): Promise<ViewOutcome<WorkNotePage>> =>
  readJson(`${workRecordNotesPath(type, recordId)}?includePassive=${includePassive}`, companyId, (raw) => decodeWorkNotePage(raw, companyId, type, recordId), signal)

export const fetchMyTasks = (companyId: string, includeClosed: boolean, signal?: AbortSignal): Promise<ViewOutcome<WorkTaskPage>> =>
  readJson(`${WORK_MY_TASKS_PATH}?includeClosed=${includeClosed}`, companyId, (raw) => decodeWorkTaskPage(raw, companyId), signal)

export const fetchRecordTasks = (companyId: string, type: WorkRecordType, recordId: string, signal?: AbortSignal): Promise<ViewOutcome<WorkTaskPage>> =>
  readJson(workRecordTasksPath(type, recordId), companyId, (raw) => decodeWorkTaskPage(raw, companyId), signal)

export const fetchMyInbox = (companyId: string, notificationClass: WorkInboxClass | null, unreadOnly: boolean, signal?: AbortSignal): Promise<ViewOutcome<WorkInboxPage>> => {
  const search = new URLSearchParams({ unreadOnly: String(unreadOnly) })
  if (notificationClass !== null) search.set('notificationClass', notificationClass)
  return readJson(`${WORK_INBOX_PATH}?${search.toString()}`, companyId, (raw) => decodeWorkInboxPage(raw, companyId), signal)
}

export const searchWorkPeople = (companyId: string, query: string, signal?: AbortSignal): Promise<ViewOutcome<WorkPeoplePage>> => {
  const trimmed = query.trim().slice(0, WORK_LIMITS.peopleQuery)
  const search = trimmed.length === 0 ? '' : `?${new URLSearchParams({ query: trimmed }).toString()}`
  return readJson(`${WORK_PEOPLE_PATH}${search}`, companyId, (raw) => decodeWorkPeoplePage(raw, companyId), signal)
}

export type WorkCommandOutcome<T> =
  | { readonly type: 'applied'; readonly data: T; readonly replayed: boolean }
  | { readonly type: 'refused'; readonly status: number; readonly code: string | null; readonly replayed: boolean }
  /** May or may not have been applied: retry with the SAME key. */
  | { readonly type: 'unknown'; readonly reason: 'offline' | 'network-error' | 'server' | 'unexpected' }

async function post<T>(path: string, companyId: string, idempotencyKey: string | null, body: object | null, success: number,
  decode: (raw: unknown) => T | null): Promise<WorkCommandOutcome<T>> {
  let response: Response
  try {
    response = await fetch(`${API_BASE}${path}`, {
      method: 'POST',
      headers: {
        ...(DEV_IDENTITY_ENABLED ? { 'X-Development-User': DEV_USER } : {}),
        'X-Company-Id': companyId,
        ...(body === null ? {} : { 'Content-Type': 'application/json' }),
        ...(idempotencyKey === null ? {} : { 'Idempotency-Key': idempotencyKey }),
      },
      body: body === null ? undefined : JSON.stringify(body),
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

export interface AddNoteBody { readonly body: string; readonly mentionedUserIds: readonly string[]; readonly correctsNoteId: string | null }
export const addRecordNote = (companyId: string, key: string, type: WorkRecordType, recordId: string, body: AddNoteBody): Promise<WorkCommandOutcome<WorkNote>> =>
  post(workRecordNotesPath(type, recordId), companyId, key, body, 201, (raw) => decodeWorkNote(raw, companyId, recordId))

export const passivateNote = (companyId: string, key: string, noteId: string, reason: string): Promise<WorkCommandOutcome<WorkNote>> =>
  post(workNotePassivationPath(noteId), companyId, key, { reason }, 200, (raw) => {
    const note = decodeWorkNote(raw, companyId)
    return note !== null && note.id === noteId && note.passivatedAtUtc !== null ? note : null
  })

export interface CreateTaskBody { readonly title: string; readonly details: string | null; readonly assigneeUserId: string; readonly dueAtUtc: string; readonly recordType: WorkRecordType | null; readonly recordId: string | null }
export const createTask = (companyId: string, key: string, body: CreateTaskBody): Promise<WorkCommandOutcome<WorkTask>> =>
  post(WORK_TASKS_PATH, companyId, key, body, 201, (raw) => {
    const task = decodeWorkTask(raw, companyId)
    return task !== null && task.status === 'open' && task.assigneeUserId === body.assigneeUserId ? task : null
  })

export const completeTask = (companyId: string, key: string, taskId: string, expectedVersion: string): Promise<WorkCommandOutcome<WorkTask>> =>
  post(workTaskCompletionPath(taskId), companyId, key, { expectedVersion }, 200, (raw) => {
    const task = decodeWorkTask(raw, companyId, taskId)
    return task !== null && task.status === 'completed' ? task : null
  })

export const cancelTask = (companyId: string, key: string, taskId: string, expectedVersion: string, reason: string): Promise<WorkCommandOutcome<WorkTask>> =>
  post(workTaskCancellationPath(taskId), companyId, key, { expectedVersion, reason }, 200, (raw) => {
    const task = decodeWorkTask(raw, companyId, taskId)
    return task !== null && task.status === 'cancelled' ? task : null
  })

/** Marking read is naturally idempotent on the server (the first read time is kept), so it carries no key. */
export const markInboxRead = (companyId: string, id: string): Promise<WorkCommandOutcome<WorkInboxRead>> =>
  post(workInboxReadPath(id), companyId, null, null, 200, (raw) => decodeWorkInboxRead(raw, id))

/**
 * Pending idempotency keys survive a reload in sessionStorage, scoped to session epoch, actor, company and target, so an
 * unknown outcome is retried with the same key. Storage failures fall back to a fresh key (never a crash).
 */
export function pendingKey(scope: string): string {
  try {
    const existing = sessionStorage.getItem(scope)
    if (existing !== null && existing.length > 0 && existing.length <= 128) return existing
    const key = crypto.randomUUID()
    sessionStorage.setItem(scope, key)
    return key
  } catch {
    return crypto.randomUUID()
  }
}

export function settleKey(scope: string): void {
  try { sessionStorage.removeItem(scope) } catch { /* storage unavailable: nothing to settle */ }
}

export const commandScope = (action: string, sessionEpoch: number, actorId: string, companyId: string, target: string) =>
  `goymen.work.${action}.v1:${sessionEpoch}:${actorId}:${companyId}:${target}`
