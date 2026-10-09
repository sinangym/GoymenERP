/**
 * D-05 transport for the Work approval reads (#639/#644/#645). Read-only: this module has no command — an approval is given on
 * the owning module's own screen, with that module's authority, separation of duties and version rules.
 *
 * `X-Company-Id` is the committed shell company; an answer of another company is a tenant-binding breach, not data.
 */

import type { ViewOutcome } from '../shell/uiState'
import {
  FINANCE_OFFICIAL_INVOICE_IMPORT_KIND,
  WORK_APPROVALS_PATH,
  decodeApprovalDetail,
  decodeApprovalPage,
  workApprovalPath,
  type ApprovalDetail,
  type ApprovalItemOf,
  type ApprovalKind,
  type ApprovalPage,
} from './approvalInboxContract'

const API_BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? '/api/v1'
const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY_ENABLED = import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'

function headers(companyId: string): Record<string, string> {
  return {
    ...(DEV_IDENTITY_ENABLED ? { 'X-Development-User': DEV_USER } : {}),
    'X-Company-Id': companyId,
  }
}

async function read<T>(
  path: string,
  companyId: string,
  signal: AbortSignal | undefined,
  decode: (body: unknown) => T | null,
  belongs: (data: T) => boolean,
): Promise<ViewOutcome<T>> {
  let response: Response
  try {
    response = await fetch(`${API_BASE}${path}`, { headers: headers(companyId), signal })
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
  const data = decode(body)
  return data === null || !belongs(data) ? { type: 'malformed' } : { type: 'ok-single', data }
}

/**
 * One page of ONE kind. The Finance pilot keeps the request it was accepted with (no `kind`: the server's Finance
 * default); every other kind is asked for explicitly. The answer must be of that kind and of this company.
 */
export function fetchApprovalPage<K extends ApprovalKind>(
  kind: K,
  cursor: string | null,
  companyId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<ApprovalPage<ApprovalItemOf<K>>>> {
  const params = new URLSearchParams()
  if (kind !== FINANCE_OFFICIAL_INVOICE_IMPORT_KIND) params.set('kind', kind)
  if (cursor !== null) params.set('cursor', cursor)
  const query = params.size === 0 ? '' : `?${params.toString()}`
  return read(`${WORK_APPROVALS_PATH}${query}`, companyId, signal, (body) => decodeApprovalPage(body, kind),
    (page) => page.companyId.toLowerCase() === companyId.toLowerCase())
}

export function fetchApprovalDetail(
  kind: ApprovalKind,
  approvalId: string,
  companyId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<ApprovalDetail>> {
  return read(workApprovalPath(kind, approvalId), companyId, signal, (body) => decodeApprovalDetail(body, kind, approvalId),
    (detail) => detail.item.companyId.toLowerCase() === companyId.toLowerCase())
}
