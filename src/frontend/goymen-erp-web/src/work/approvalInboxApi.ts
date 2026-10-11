/**
 * D-05 transport for the Work approval reads (#639/#644/#645). Read-only: this module has no command — an approval is given on
 * the owning module's own screen, with that module's authority, separation of duties and version rules.
 *
 * `X-Company-Id` is the committed shell company; an answer of another company is a tenant-binding breach, not data.
 *
 * The two Inventory kinds are read under the Work approval contract 2.0 (#683): the request selects it with one header
 * and a successful answer must acknowledge exactly that value. Without the acknowledgment the answer is not the
 * selected contract and is refused whole — there is no second, legacy request and nothing is completed by other reads.
 * Finance and Sales send no selector and are read exactly as before.
 */

import type { ViewOutcome } from '../shell/uiState'
import {
  FINANCE_OFFICIAL_INVOICE_IMPORT_KIND,
  WORK_APPROVAL_CONTRACT_HEADER,
  WORK_APPROVALS_PATH,
  decodeApprovalDetail,
  decodeApprovalPage,
  workApprovalContractOf,
  workApprovalPath,
  type ApprovalDetail,
  type ApprovalItemOf,
  type ApprovalKind,
  type ApprovalPage,
} from './approvalInboxContract'

const API_BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? '/api/v1'
const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY_ENABLED = import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'

function headers(companyId: string, contract: string | null): Record<string, string> {
  return {
    ...(DEV_IDENTITY_ENABLED ? { 'X-Development-User': DEV_USER } : {}),
    'X-Company-Id': companyId,
    ...(contract === null ? {} : { [WORK_APPROVAL_CONTRACT_HEADER]: contract }),
  }
}

async function read<T>(
  path: string,
  companyId: string,
  /** The contract this read selects; null is the legacy read, which sends and expects no selector. */
  contract: string | null,
  signal: AbortSignal | undefined,
  decode: (body: unknown) => T | null,
  belongs: (data: T) => boolean,
): Promise<ViewOutcome<T>> {
  let response: Response
  try {
    response = await fetch(`${API_BASE}${path}`, { headers: headers(companyId, contract), signal })
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
  // A selected contract must be acknowledged as that single exact value (a repeated header reads as "2.0, 2.0").
  if (contract !== null && response.headers.get(WORK_APPROVAL_CONTRACT_HEADER) !== contract) return { type: 'malformed' }
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
  return read(`${WORK_APPROVALS_PATH}${query}`, companyId, workApprovalContractOf(kind), signal, (body) => decodeApprovalPage(body, kind),
    (page) => page.companyId.toLowerCase() === companyId.toLowerCase())
}

export function fetchApprovalDetail(
  kind: ApprovalKind,
  approvalId: string,
  companyId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<ApprovalDetail>> {
  return read(workApprovalPath(kind, approvalId), companyId, workApprovalContractOf(kind), signal,
    (body) => decodeApprovalDetail(body, kind, approvalId),
    (detail) => detail.item.companyId.toLowerCase() === companyId.toLowerCase())
}
