/**
 * Identity access-view read transport (AT-004). Thin and hand-written like `../organization/api.ts`; when the
 * generated client lands (Issue #122) the fetch calls move and the decoders in ./contract.ts survive.
 *
 * GETs only. There is no write function in this file and there must not be one: assigning or revoking a grant is
 * blocked (NEEDS-SINAN #66 5738113623, OPEN-013). No client-contract header (a Counterparty mechanism), no paging
 * (the lists are unpaged on the server) and no search parameter the server does not have.
 */

import {
  decodeRoleDetail,
  decodeRoleSummaries,
  decodeUserDetail,
  decodeUserSummaries,
  type AccessRoleDetail,
  type AccessRoleSummary,
  type AccessUserDetail,
  type AccessUserSummary,
} from './contract'
import type { ViewOutcome } from '../shell/uiState'

export const API_BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? '/api/v1'
const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY_ENABLED =
  import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'

function devHeaders(): Record<string, string> {
  return DEV_IDENTITY_ENABLED ? { 'X-Development-User': DEV_USER } : {}
}

interface RawResult {
  readonly status: number
  readonly isOk200: boolean
  readonly body: unknown
}

async function read(
  path: string,
  companyId: string,
  signal?: AbortSignal,
): Promise<RawResult | 'aborted' | 'offline' | 'unreachable'> {
  let response: Response
  try {
    response = await fetch(`${API_BASE}${path}`, {
      headers: { ...devHeaders(), 'X-Company-Id': companyId },
      signal,
    })
  } catch {
    if (signal?.aborted) return 'aborted'
    return navigator.onLine ? 'unreachable' : 'offline'
  }
  let body: unknown = null
  try {
    body = await response.json()
  } catch {
    if (signal?.aborted) return 'aborted'
    body = null
  }
  return { status: response.status, isOk200: response.status === 200, body }
}

function transportOutcome<T>(result: 'aborted' | 'offline' | 'unreachable'): ViewOutcome<T> {
  if (result === 'aborted') return { type: 'fence-stale' }
  if (result === 'offline') return { type: 'offline' }
  return { type: 'network-error' }
}

/** `GET /identity/access/users` — every user holding an assignment in the context company. */
export async function fetchAccessUsers(
  companyId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<AccessUserSummary>> {
  const result = await read('/identity/access/users', companyId, signal)
  if (typeof result === 'string') return transportOutcome(result)
  if (!result.isOk200) return { type: 'http', status: result.status, body: result.body }
  const users = decodeUserSummaries(result.body)
  if (users === null) return { type: 'malformed' }
  return { type: 'ok', data: users, filterActive: false }
}

/** `GET /identity/access/users/{id}` — one user's grants in the context company; 404 passes through as a status. */
export async function fetchAccessUser(
  companyId: string,
  userId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<AccessUserDetail>> {
  const result = await read(`/identity/access/users/${encodeURIComponent(userId)}`, companyId, signal)
  if (typeof result === 'string') return transportOutcome(result)
  if (!result.isOk200) return { type: 'http', status: result.status, body: result.body }
  const user = decodeUserDetail(result.body, userId)
  if (user === null) return { type: 'malformed' }
  return { type: 'ok-single', data: user }
}

/** `GET /identity/access/roles` — the role catalogue with holder counts in the context company. */
export async function fetchAccessRoles(
  companyId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<AccessRoleSummary>> {
  const result = await read('/identity/access/roles', companyId, signal)
  if (typeof result === 'string') return transportOutcome(result)
  if (!result.isOk200) return { type: 'http', status: result.status, body: result.body }
  const roles = decodeRoleSummaries(result.body)
  if (roles === null) return { type: 'malformed' }
  return { type: 'ok', data: roles, filterActive: false }
}

/** `GET /identity/access/roles/{id}` — one role's permissions and its holders in the context company. */
export async function fetchAccessRole(
  companyId: string,
  roleId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<AccessRoleDetail>> {
  const result = await read(`/identity/access/roles/${encodeURIComponent(roleId)}`, companyId, signal)
  if (typeof result === 'string') return transportOutcome(result)
  if (!result.isOk200) return { type: 'http', status: result.status, body: result.body }
  const role = decodeRoleDetail(result.body, roleId)
  if (role === null) return { type: 'malformed' }
  return { type: 'ok-single', data: role }
}
