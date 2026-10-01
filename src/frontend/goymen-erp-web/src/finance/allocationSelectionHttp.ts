import type { AttemptBinding } from './receiptCommand'
import {
  decodeSourceDetailFor,
  decodeSourcePage,
  isBoundUuid,
  type SourceDetail,
  type SourcePage,
} from './contract'
import { API_BASE, devHeaders } from './http'
import {
  decodeAppliedAllocationOpenItemBatch,
  decodeApprovedAllocationOpenItemSourcePage,
  type AppliedAllocationOpenItemBatch,
  type ApprovedAllocationOpenItemSourceBatch,
  type ApprovedAllocationOpenItemSourcePage,
} from './allocationSelectionContract'

export type AllocationSelectionHttpResult<T> =
  | { readonly kind: 'found'; readonly resource: T }
  | { readonly kind: 'authorization'; readonly status: 401 | 403 }
  | { readonly kind: 'not-found' | 'unavailable' | 'malformed' | 'no-reply' }

export function approvedAllocationSourcesUrl(cursor: string | null): string {
  const query = cursor === null ? '' : `?${new URLSearchParams({ cursor }).toString()}`
  return `${API_BASE}/finance/official-invoice-imports/approved-sources${query}`
}

async function read<T>(
  path: string,
  binding: AttemptBinding,
  decode: (value: unknown) => { readonly ok: false } | { readonly ok: true; readonly value: T },
  signal?: AbortSignal,
): Promise<AllocationSelectionHttpResult<T>> {
  if (!isBoundUuid(binding.companyId)) return { kind: 'malformed' }
  let response: Response
  try {
    response = await fetch(path, {
      method: 'GET',
      headers: { ...devHeaders(), 'X-Company-Id': binding.companyId, Accept: 'application/json' },
      signal,
    })
  } catch {
    return { kind: 'no-reply' }
  }
  if (response.status === 401 || response.status === 403) return { kind: 'authorization', status: response.status }
  if (response.status === 404) return { kind: 'not-found' }
  if (response.status === 503) return { kind: 'unavailable' }
  if (response.status !== 200) return { kind: 'malformed' }
  try {
    const decoded = decode(await response.json())
    return decoded.ok ? { kind: 'found', resource: decoded.value } : { kind: 'malformed' }
  } catch (error) {
    return signal?.aborted || error instanceof TypeError ? { kind: 'no-reply' } : { kind: 'malformed' }
  }
}

export function readApprovedAllocationSources(
  binding: AttemptBinding,
  cursor: string | null,
  signal?: AbortSignal,
): Promise<AllocationSelectionHttpResult<ApprovedAllocationOpenItemSourcePage>> {
  if (cursor !== null && (cursor.length === 0 || cursor.length > 8192)) return Promise.resolve({ kind: 'malformed' })
  return read(
    approvedAllocationSourcesUrl(cursor),
    binding,
    (value) => decodeApprovedAllocationOpenItemSourcePage(value, binding.companyId),
    signal,
  )
}

export function readAllocationReceiptSources(
  binding: AttemptBinding,
  cursor: string | null,
  signal?: AbortSignal,
): Promise<AllocationSelectionHttpResult<SourcePage>> {
  if (cursor !== null && (cursor.length === 0 || cursor.length > 8192)) return Promise.resolve({ kind: 'malformed' })
  const query = cursor === null ? '' : `?${new URLSearchParams({ cursor }).toString()}`
  return read(
    `${API_BASE}/finance/manual-receipts${query}`,
    binding,
    (value) => decodeSourcePage(value, binding.companyId),
    signal,
  )
}

export function readAllocationReceiptDetail(
  binding: AttemptBinding,
  sourceId: string,
  signal?: AbortSignal,
): Promise<AllocationSelectionHttpResult<SourceDetail>> {
  if (!isBoundUuid(sourceId)) return Promise.resolve({ kind: 'malformed' })
  return read(
    `${API_BASE}/finance/manual-receipts/${encodeURIComponent(sourceId)}`,
    binding,
    (value) => decodeSourceDetailFor(sourceId)(value, binding.companyId),
    signal,
  )
}

export function readAppliedAllocationOpenItems(
  binding: AttemptBinding,
  batch: ApprovedAllocationOpenItemSourceBatch,
  signal?: AbortSignal,
): Promise<AllocationSelectionHttpResult<AppliedAllocationOpenItemBatch>> {
  if (!isBoundUuid(batch.batchId) || batch.companyId !== binding.companyId) return Promise.resolve({ kind: 'malformed' })
  return read(
    `${API_BASE}/finance/official-invoice-imports/${encodeURIComponent(batch.batchId)}`,
    binding,
    (value) => decodeAppliedAllocationOpenItemBatch(value, binding.companyId, batch),
    signal,
  )
}
