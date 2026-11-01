/**
 * SAT-D61…D70 transport — thin and hand-written like `../identity/api.ts`, over the decoders in ./discountLimitContract.ts.
 *
 *  - READS send the selected company only (`X-Company-Id`); the server fences every answer to it.
 *  - COMMANDS send one `Idempotency-Key` the CALLER owns: the screen keeps the same key for a retry after an unknown
 *    outcome (no response, a 5xx) so a retry can only replay, never apply twice, and mints a new key for a new attempt.
 *    Success is EXACTLY the contract's status (201 with the created id, or 204); any other 2xx is an unknown outcome,
 *    never read as success. A refusal is read by its status and stable `code`, never by localized text.
 */

import {
  DISCOUNT_LIMIT_GROUPS_PATH,
  DISCOUNT_LIMIT_PEOPLE_PATH,
  LIMIT_INPUT,
  bindingPath,
  decodeBinding,
  decodeCreated,
  decodeGroupList,
  decodePeople,
  groupActivationPath,
  groupDeactivationPath,
  groupLimitCorrectionPath,
  groupLimitPath,
  groupLimitWithdrawalPath,
  groupNamePath,
  problemCodeOf,
  problemSubjectOf,
  type Binding,
  type CorrectLimitBody,
  type CreateGroupBody,
  type DeactivateGroupBody,
  type LimitGroupCreated,
  type LimitGroupList,
  type PersonList,
  type RenameGroupBody,
  type SetBindingBody,
  type SetLimitBody,
  type WithdrawLimitBody,
} from './discountLimitContract'
import type { ViewOutcome } from '../shell/uiState'

export const API_BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? '/api/v1'
const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY_ENABLED =
  import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'

function devHeaders(): Record<string, string> {
  return DEV_IDENTITY_ENABLED ? { 'X-Development-User': DEV_USER } : {}
}

export async function readJson<T>(
  path: string,
  companyId: string,
  decode: (raw: unknown) => T | null,
  signal?: AbortSignal,
): Promise<ViewOutcome<T>> {
  let response: Response
  try {
    response = await fetch(`${API_BASE}${path}`, { headers: { ...devHeaders(), 'X-Company-Id': companyId }, signal })
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    return navigator.onLine ? { type: 'network-error' } : { type: 'offline' }
  }
  let body: unknown = null
  try {
    body = await response.json()
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    body = null
  }
  if (response.status !== 200) return { type: 'http', status: response.status, body }
  const data = decode(body)
  return data === null ? { type: 'malformed' } : { type: 'ok-single', data }
}

export function fetchLimitGroups(companyId: string, signal?: AbortSignal): Promise<ViewOutcome<LimitGroupList>> {
  return readJson(DISCOUNT_LIMIT_GROUPS_PATH, companyId, (raw) => decodeGroupList(raw, companyId), signal)
}

/** The company's active people whose name contains the (trimmed) fragment; at most the technical page size. */
export function fetchPeople(companyId: string, query: string, signal?: AbortSignal): Promise<ViewOutcome<PersonList>> {
  const trimmed = query.trim().slice(0, LIMIT_INPUT.peopleQueryMaximumLength)
  const search = new URLSearchParams({ limit: String(LIMIT_INPUT.peopleSearchLimit) })
  if (trimmed.length > 0) search.set('q', trimmed)
  return readJson(`${DISCOUNT_LIMIT_PEOPLE_PATH}?${search.toString()}`, companyId, decodePeople, signal)
}

export function fetchBinding(companyId: string, userId: string, signal?: AbortSignal): Promise<ViewOutcome<Binding>> {
  return readJson(bindingPath(userId), companyId, (raw) => decodeBinding(raw, userId), signal)
}

export interface CommandContext {
  readonly companyId: string
  /** One key per attempt, owned by the caller (kept for a retry after an unknown outcome). */
  readonly idempotencyKey: string
}

export type CommandOutcome<T> =
  | { readonly type: 'applied'; readonly data: T; readonly replayed: boolean }
  | {
      readonly type: 'refused'
      readonly status: number
      readonly code: string | null
      readonly companyId: string | null
      readonly groupCode: string | null
      readonly replayed: boolean
    }
  /** The command may or may not have been applied: retry with the SAME key. */
  | { readonly type: 'unknown'; readonly reason: 'offline' | 'network-error' | 'server' | 'unexpected' }

export async function send<T>(
  method: 'POST' | 'PUT',
  path: string,
  context: CommandContext,
  body: object,
  success: number,
  decode: (raw: unknown) => T | null,
  signal?: AbortSignal,
): Promise<CommandOutcome<T>> {
  let response: Response
  try {
    response = await fetch(`${API_BASE}${path}`, {
      method,
      headers: {
        ...devHeaders(),
        'Content-Type': 'application/json',
        'X-Company-Id': context.companyId,
        'Idempotency-Key': context.idempotencyKey,
      },
      body: JSON.stringify(body),
      signal,
    })
  } catch {
    return { type: 'unknown', reason: navigator.onLine ? 'network-error' : 'offline' }
  }
  const replayed = (response.headers.get('Idempotency-Replayed') ?? '').trim().toLowerCase() === 'true'
  let raw: unknown = null
  try {
    raw = await response.json()
  } catch {
    raw = null
  }
  if (response.status === success) {
    const data = decode(raw)
    return data === null ? { type: 'unknown', reason: 'unexpected' } : { type: 'applied', data, replayed }
  }
  if (response.status >= 500) return { type: 'unknown', reason: 'server' }
  if (response.status < 400) return { type: 'unknown', reason: 'unexpected' }
  return { type: 'refused', status: response.status, code: problemCodeOf(raw), ...problemSubjectOf(raw), replayed }
}

/** A 204 carries no body: reaching the exact status IS the answer. */
export const noBody = (): true => true

export function createLimitGroup(context: CommandContext, body: CreateGroupBody, signal?: AbortSignal) {
  return send<LimitGroupCreated>('POST', DISCOUNT_LIMIT_GROUPS_PATH, context, body, 201, decodeCreated, signal)
}

export function renameLimitGroup(context: CommandContext, groupId: string, body: RenameGroupBody, signal?: AbortSignal) {
  return send<true>('PUT', groupNamePath(groupId), context, body, 204, noBody, signal)
}

export function deactivateLimitGroup(
  context: CommandContext,
  groupId: string,
  body: DeactivateGroupBody,
  signal?: AbortSignal,
) {
  return send<true>('POST', groupDeactivationPath(groupId), context, body, 204, noBody, signal)
}

export function reactivateLimitGroup(
  context: CommandContext,
  groupId: string,
  body: DeactivateGroupBody,
  signal?: AbortSignal,
) {
  return send<true>('POST', groupActivationPath(groupId), context, body, 204, noBody, signal)
}

export function setGroupLimit(context: CommandContext, groupId: string, body: SetLimitBody, signal?: AbortSignal) {
  return send<true>('PUT', groupLimitPath(groupId), context, body, 204, noBody, signal)
}

export function withdrawGroupLimit(
  context: CommandContext,
  groupId: string,
  body: WithdrawLimitBody,
  signal?: AbortSignal,
) {
  return send<true>('POST', groupLimitWithdrawalPath(groupId), context, body, 204, noBody, signal)
}

export function correctGroupLimit(context: CommandContext, groupId: string, body: CorrectLimitBody, signal?: AbortSignal) {
  return send<true>('POST', groupLimitCorrectionPath(groupId), context, body, 204, noBody, signal)
}

export function setBinding(context: CommandContext, userId: string, body: SetBindingBody, signal?: AbortSignal) {
  return send<true>('PUT', bindingPath(userId), context, body, 204, noBody, signal)
}
