import type { ShellState } from '../shell/shellState'
import type { ViewOutcome } from '../shell/uiState'
import { API_BASE, readJson } from './discountLimitApi'
import { isBoundUuid } from '../product/wireGuards'

export const WAREHOUSE_TRANSFER_SCALE_CAPABILITY = 'settings.warehouse-transfer-scale.manage'
export const WAREHOUSE_TRANSFER_SCALE_ROUTE = '/ayarlar/depo-transfer-tartisi'
export const WAREHOUSE_TRANSFER_SCALE_PATH = '/settings/warehouse-transfer-scale'
export const WAREHOUSE_TRANSFER_SCALE_SOURCE = 'settings.warehouse-transfer-scale.v1'
export interface TransferWarehouse {
  readonly locationId: string; readonly locationCode: string; readonly locationName: string
  readonly siteId: string; readonly siteCode: string; readonly siteName: string
}
export interface TransferWarehouses {
  readonly companyId: string; readonly q: string | null; readonly limit: number
  readonly items: readonly TransferWarehouse[]; readonly searchWindowFull: boolean
}
export interface WarehouseScaleVersion {
  readonly recordId: string; readonly version: string; readonly requireExitWeighing: boolean; readonly locationVersion: number
  readonly recordedBy: string; readonly recordedAtUtc: string; readonly reason: string
}
export interface WarehouseScaleView {
  readonly companyId: string; readonly locationId: string
  readonly policy: { readonly status: 1 | 2 | 3; readonly companyId: string; readonly locationId: string
    readonly requireExitWeighing: boolean | null; readonly recordedVersion: number | null; readonly sourceVersion: string }
  readonly versions: readonly WarehouseScaleVersion[]
}
export interface RecordWarehouseScale { readonly expectedVersion: string; readonly requireExitWeighing: boolean; readonly reason: string }
const obj = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === 'object' && !Array.isArray(value)
const exact = (value: Record<string, unknown>, fields: readonly string[]) => Object.keys(value).length === fields.length && fields.every((field) => Object.hasOwn(value, field))
const text = (value: unknown, max: number): value is string => typeof value === 'string' && value.trim() !== '' && value.length <= max && !/[\u0000-\u001f\u007f-\u009f]/.test(value)
const id = (value: unknown, expected: string): value is string => isBoundUuid(value) && value.toLowerCase() === expected.toLowerCase()
const positive = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value > 0
const version = (value: unknown): value is string => typeof value === 'string' && /^[1-9]\d{0,18}$/.test(value) && BigInt(value) <= 9223372036854775807n
const utc = (value: unknown): value is string => typeof value === 'string' && /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|\+00:00)$/.test(value) && Number.isFinite(Date.parse(value))
export function decodeTransferWarehouses(raw: unknown, company: string, q: string | null): TransferWarehouses | null {
  if (!obj(raw) || !exact(raw, ['companyId', 'q', 'limit', 'items', 'searchWindowFull']) || !id(raw.companyId, company) || raw.q !== q
    || raw.limit !== 25 || !Array.isArray(raw.items) || raw.items.length > 25 || typeof raw.searchWindowFull !== 'boolean'
    || raw.searchWindowFull && raw.items.length !== 25) return null
  const items: TransferWarehouse[] = []
  for (const value of raw.items) {
    if (!obj(value) || !exact(value, ['locationId', 'locationCode', 'locationName', 'siteId', 'siteCode', 'siteName'])
      || !isBoundUuid(value.locationId) || !isBoundUuid(value.siteId) || !text(value.locationCode, 200) || !text(value.locationName, 2000)
      || !text(value.siteCode, 200) || !text(value.siteName, 2000)) return null
    items.push(value as unknown as TransferWarehouse)
  }
  if (new Set(items.map((value) => value.locationId.toLowerCase())).size !== items.length) return null
  return { companyId: raw.companyId, q, limit: 25, items, searchWindowFull: raw.searchWindowFull }
}
export function decodeWarehouseScale(raw: unknown, company: string, location: string): WarehouseScaleView | null {
  if (!obj(raw) || !exact(raw, ['companyId', 'locationId', 'policy', 'versions']) || !id(raw.companyId, company) || !id(raw.locationId, location)
    || !obj(raw.policy) || !exact(raw.policy, ['status', 'companyId', 'locationId', 'requireExitWeighing', 'recordedVersion', 'sourceVersion'])
    || !id(raw.policy.companyId, company) || !id(raw.policy.locationId, location) || !Array.isArray(raw.versions)) return null
  const policy = raw.policy
  const versions: WarehouseScaleVersion[] = []
  const recordIds = new Set<string>()
  for (const value of raw.versions) {
    if (!obj(value) || !exact(value, ['recordId', 'version', 'requireExitWeighing', 'locationVersion', 'recordedBy', 'recordedAtUtc', 'reason'])
      || !isBoundUuid(value.recordId) || recordIds.has(value.recordId.toLowerCase()) || !version(value.version)
      || typeof value.requireExitWeighing !== 'boolean' || !positive(value.locationVersion)
      || !text(value.recordedBy, 128) || !utc(value.recordedAtUtc) || !text(value.reason, 4000)
      || BigInt(value.version) !== BigInt(raw.versions.length - versions.length)) return null
    recordIds.add(value.recordId.toLowerCase()); versions.push(value as unknown as WarehouseScaleVersion)
  }
  if (policy.status === 1) {
    if (policy.requireExitWeighing !== false || policy.recordedVersion !== null || versions.length !== 0
      || policy.sourceVersion !== `${WAREHOUSE_TRANSFER_SCALE_SOURCE}/not-configured`) return null
  } else if (policy.status === 2) {
    if (typeof policy.requireExitWeighing !== 'boolean' || !positive(policy.recordedVersion) || versions.length === 0
      || versions[0].version !== String(policy.recordedVersion) || versions[0].requireExitWeighing !== policy.requireExitWeighing
      || policy.sourceVersion !== `${WAREHOUSE_TRANSFER_SCALE_SOURCE}/${policy.recordedVersion}`) return null
  } else if (policy.status === 3) {
    if (policy.requireExitWeighing !== null || policy.recordedVersion !== null || versions.length !== 0
      || policy.sourceVersion !== `${WAREHOUSE_TRANSFER_SCALE_SOURCE}/unavailable`) return null
  } else return null
  return { companyId: raw.companyId, locationId: raw.locationId, policy: policy as unknown as WarehouseScaleView['policy'], versions }
}
export function fetchTransferWarehouses(company: string, q: string | null, signal?: AbortSignal): Promise<ViewOutcome<TransferWarehouses>> {
  const values = new URLSearchParams({ limit: '25' }); if (q !== null) values.set('q', q)
  return readJson(`${WAREHOUSE_TRANSFER_SCALE_PATH}/warehouses?${values.toString()}`, company, (raw) => decodeTransferWarehouses(raw, company, q), signal)
}
export function fetchWarehouseScale(company: string, location: string, signal?: AbortSignal): Promise<ViewOutcome<WarehouseScaleView>> {
  return readJson(`${WAREHOUSE_TRANSFER_SCALE_PATH}/${encodeURIComponent(location)}`, company, (raw) => decodeWarehouseScale(raw, company, location), signal)
}
export type WarehouseScaleCommandOutcome = { readonly type: 'applied'; readonly replayed: boolean }
  | { readonly type: 'refused'; readonly status: number; readonly code: string | null; readonly terminalReplay: boolean }
  | { readonly type: 'unknown'; readonly reason: 'offline' | 'network-error' | 'service-unavailable' | 'malformed-response' }
const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY = import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'
export async function recordWarehouseScale(snapshot: WarehouseScaleSnapshot): Promise<WarehouseScaleCommandOutcome> {
  let response: Response
  try {
    response = await fetch(`${API_BASE}${WAREHOUSE_TRANSFER_SCALE_PATH}/${encodeURIComponent(snapshot.locationId)}/versions`, {
      method: 'POST', headers: { ...(DEV_IDENTITY ? { 'X-Development-User': DEV_USER } : {}), 'X-Company-Id': snapshot.companyId,
        'Content-Type': 'application/json', 'Idempotency-Key': snapshot.key }, body: JSON.stringify(snapshot.payload),
    })
  } catch { return { type: 'unknown', reason: navigator.onLine ? 'network-error' : 'offline' } }
  const replay = response.headers.get('Idempotency-Replayed'); const correlation = response.headers.get('Idempotency-Original-Correlation-Id')
  if (response.status === 204) {
    return (replay === null && correlation === null || replay === 'true' && text(correlation, 128))
      ? { type: 'applied', replayed: replay === 'true' } : { type: 'unknown', reason: 'malformed-response' }
  }
  if (response.status < 400 || response.status >= 500) return { type: 'unknown', reason: response.status >= 500 ? 'service-unavailable' : 'malformed-response' }
  let raw: unknown = null; try { raw = await response.json() } catch { /* Status remains a definite refusal. */ }
  const code = obj(raw) && typeof raw.code === 'string' ? raw.code : null
  return { type: 'refused', status: response.status, code,
    terminalReplay: response.status === 409 && replay === 'true' && text(correlation, 128)
      && code === 'settings_warehouse_transfer_scale_version_conflict' }
}
export interface WarehouseScaleSnapshot {
  readonly binding: string; readonly companyId: string; readonly locationId: string
  readonly payload: RecordWarehouseScale; readonly fingerprint: string; readonly key: string
}
export type WarehouseScaleAttempt = { readonly phase: 'idle' }
  | { readonly phase: 'sending'; readonly snapshot: WarehouseScaleSnapshot; readonly unresolved: boolean }
  | { readonly phase: 'unknown'; readonly snapshot: WarehouseScaleSnapshot; readonly reason: Extract<WarehouseScaleCommandOutcome, { type: 'unknown' }>['reason'] }
  | { readonly phase: 'failed'; readonly snapshot: WarehouseScaleSnapshot; readonly status: number; readonly unresolved: boolean }
  | { readonly phase: 'completed'; readonly snapshot: WarehouseScaleSnapshot; readonly replayed: boolean }
const idle: WarehouseScaleAttempt = Object.freeze({ phase: 'idle' })
const attempts = new Map<string, WarehouseScaleAttempt>(); const listeners = new Map<string, Set<() => void>>()
export function warehouseScaleBinding(shell: ShellState): string | null {
  const context = shell.activeContext
  return context?.scopeMode === 'single' && context.companyId !== null && !shell.sessionInvalid
    ? JSON.stringify([API_BASE, shell.sessionEpoch, context.userId, context.companyId.toLowerCase(), 'warehouse-transfer-scale']) : null
}
export const warehouseScaleAttempt = (binding: string | null): WarehouseScaleAttempt => binding === null ? idle : attempts.get(binding) ?? idle
export function subscribeWarehouseScaleAttempt(binding: string | null, callback: () => void): () => void {
  if (binding === null) return () => {}
  const group = listeners.get(binding) ?? new Set<() => void>(); group.add(callback); listeners.set(binding, group)
  return () => { group.delete(callback); if (group.size === 0) listeners.delete(binding) }
}
function publish(binding: string, value: WarehouseScaleAttempt) { attempts.set(binding, value); listeners.get(binding)?.forEach((callback) => callback()) }
export const warehouseScaleFrozen = (value: WarehouseScaleAttempt) => value.phase === 'sending' || value.phase === 'unknown' || value.phase === 'failed' && value.unresolved
export function beginWarehouseScaleAttempt(binding: string | null, companyId: string, locationId: string, payload: RecordWarehouseScale,
  key: () => string = () => `warehouse-scale-${crypto.randomUUID()}`): WarehouseScaleSnapshot | null {
  if (binding === null) return null
  const current = warehouseScaleAttempt(binding); if (current.phase === 'sending') return null
  const fingerprint = JSON.stringify([companyId.toLowerCase(), locationId.toLowerCase(), payload])
  if (warehouseScaleFrozen(current) && current.phase !== 'idle') {
    if (current.snapshot.fingerprint !== fingerprint) return null
    publish(binding, { phase: 'sending', snapshot: current.snapshot, unresolved: true }); return current.snapshot
  }
  const snapshot = Object.freeze({ binding, companyId, locationId, payload: Object.freeze({ ...payload }), fingerprint, key: key() })
  publish(binding, { phase: 'sending', snapshot, unresolved: false }); return snapshot
}
export function finishWarehouseScaleAttempt(snapshot: WarehouseScaleSnapshot, outcome: WarehouseScaleCommandOutcome) {
  const current = warehouseScaleAttempt(snapshot.binding)
  if (current.phase === 'idle' || current.phase === 'completed' || current.snapshot.key !== snapshot.key || current.phase === 'failed' && !current.unresolved) return
  const unresolved = current.phase === 'unknown' || current.unresolved
  if (outcome.type === 'applied') publish(snapshot.binding, { phase: 'completed', snapshot, replayed: outcome.replayed })
  else if (outcome.type === 'unknown') publish(snapshot.binding, { phase: 'unknown', snapshot, reason: outcome.reason })
  else publish(snapshot.binding, { phase: 'failed', snapshot, status: outcome.status, unresolved: unresolved && !outcome.terminalReplay })
}
export function leaveWarehouseScaleAttempt(binding: string | null) {
  const current = warehouseScaleAttempt(binding)
  if (binding !== null && current.phase === 'sending') publish(binding, { phase: 'unknown', snapshot: current.snapshot, reason: 'network-error' })
}
export function resetWarehouseScaleAttemptsForTests() { attempts.clear(); listeners.clear() }
