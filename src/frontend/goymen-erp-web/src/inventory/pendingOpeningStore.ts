import { isOpeningCountRequest, type OpeningCountRequest } from './contract'

export interface PendingOpeningCount {
  readonly actorId: string
  readonly companyId: string
  readonly idempotencyKey: string
  readonly payload: OpeningCountRequest
}

export interface LastOpeningCount {
  readonly actorId: string
  readonly companyId: string
  readonly countId: string
}

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

export function pendingOpeningCountKey(sessionEpoch: number, actorId: string | null, companyId: string | null): string | null {
  return actorId === null || companyId === null ? null : `goymen.inventory.opening-create.v1:${sessionEpoch}:${actorId}:${companyId}`
}

export function lastOpeningCountKey(sessionEpoch: number, actorId: string | null, companyId: string | null): string | null {
  return actorId === null || companyId === null ? null : `goymen.inventory.opening-last.v1:${sessionEpoch}:${actorId}:${companyId}`
}

function pending(value: unknown): PendingOpeningCount | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return null
  const candidate = value as Partial<PendingOpeningCount>
  return typeof candidate.actorId === 'string' && candidate.actorId.length > 0 &&
    typeof candidate.companyId === 'string' && GUID.test(candidate.companyId) &&
    typeof candidate.idempotencyKey === 'string' && GUID.test(candidate.idempotencyKey) &&
    isOpeningCountRequest(candidate.payload)
    ? candidate as PendingOpeningCount
    : null
}

function last(value: unknown): LastOpeningCount | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return null
  const candidate = value as Partial<LastOpeningCount>
  return typeof candidate.actorId === 'string' && candidate.actorId.length > 0 &&
    typeof candidate.companyId === 'string' && GUID.test(candidate.companyId) &&
    typeof candidate.countId === 'string' && GUID.test(candidate.countId)
    ? candidate as LastOpeningCount
    : null
}

function read<T>(key: string | null, decode: (value: unknown) => T | null): T | null {
  if (key === null) return null
  try {
    const raw = sessionStorage.getItem(key)
    return raw === null ? null : decode(JSON.parse(raw))
  } catch { return null }
}

function write(key: string | null, value: unknown): void {
  if (key === null) return
  try { sessionStorage.setItem(key, JSON.stringify(value)) } catch { /* component state remains fail-closed */ }
}

export const recallPendingOpeningCount = (key: string | null) => read(key, pending)
export const reservePendingOpeningCount = (key: string | null, value: PendingOpeningCount) => write(key, value)
export function releasePendingOpeningCount(key: string | null, idempotencyKey: string): void {
  if (key === null || recallPendingOpeningCount(key)?.idempotencyKey !== idempotencyKey) return
  try { sessionStorage.removeItem(key) } catch { /* no-op */ }
}
export const recallLastOpeningCount = (key: string | null) => read(key, last)
export const rememberLastOpeningCount = (key: string | null, value: LastOpeningCount) => write(key, value)
