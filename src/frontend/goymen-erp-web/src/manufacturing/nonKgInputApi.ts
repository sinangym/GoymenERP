import { MANUFACTURING_BASE, devHeaders } from './api'
import type { ViewOutcome } from '../shell/uiState'
export interface NonKgFact { readonly factId: string; readonly revision: number; readonly kind: 1 | 2; readonly item: string; readonly unit: 1 | 2 | 3; readonly quantity: string; readonly occurredAtUtc: string; readonly replacesFactId: string | null; readonly reason: string }
export interface NonKgHistory { readonly companyId: string; readonly orderId: string; readonly orderVersion: number; readonly revision: number; readonly facts: readonly NonKgFact[] }
export interface NonKgBody { readonly factId: string; readonly expectedVersion: number; readonly expectedRevision: number; readonly kind: 'Paint' | 'Other'; readonly item: string; readonly unit: 'Litre' | 'Metre' | 'Piece'; readonly quantity: string; readonly occurredAtUtc: string; readonly replacesFactId: string | null; readonly reason: string }
export interface NonKgAttempt { readonly companyId: string; readonly orderId: string; readonly key: string; readonly body: NonKgBody }
const object = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v)
const guid = (v: unknown): v is string => typeof v === 'string' && /^(?!00000000-0000-0000-0000-000000000000)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(v)
const text = (v: unknown, max: number): v is string => typeof v === 'string' && v.trim() === v && v.length > 0 && v.length <= max
export const nonKgQuantity = (v: unknown): v is string => typeof v === 'string' && v.length <= 60 && /^(0|[1-9]\d*)(\.\d*[1-9])?$/.test(v)
const utc = (v: unknown): v is string => typeof v === 'string' && /(?:Z|\+00:00)$/.test(v) && Number.isFinite(Date.parse(v))
// Key sets mirror ManufacturingNonKgInputHistoryV1 / ManufacturingNonKgInputConsumptionV1 one-to-one.
const HISTORY_KEYS = 'companyId,facts,orderId,orderVersion,revision'
const FACT_KEYS = 'factId,item,kind,occurredAtUtc,quantity,reason,replacesFactId,revision,unit'
const keys = (v: Record<string, unknown>) => Object.keys(v).sort().join(',')
export function decodeNonKgHistory(v: unknown, company: string, order: string): NonKgHistory | null {
  if (!object(v) || keys(v) !== HISTORY_KEYS || v.companyId !== company || v.orderId !== order || !Number.isSafeInteger(v.orderVersion) || (v.orderVersion as number) < 1
    || !Number.isSafeInteger(v.revision) || (v.revision as number) < 0 || !Array.isArray(v.facts) || v.facts.length !== v.revision) return null
  const seen = new Map<string, NonKgFact>(); const replaced = new Set<string>()
  for (const [index, raw] of v.facts.entries()) {
    if (!object(raw) || keys(raw) !== FACT_KEYS || !guid(raw.factId) || seen.has(raw.factId) || raw.revision !== index + 1 || ![1, 2].includes(Number(raw.kind))
      || ![1, 2, 3].includes(Number(raw.unit)) || typeof raw.kind !== 'number' || typeof raw.unit !== 'number' || raw.kind === 1 && raw.unit !== 1
      || !text(raw.item, 200) || !nonKgQuantity(raw.quantity) || !utc(raw.occurredAtUtc) || !text(raw.reason, 1000)) return null
    if (raw.replacesFactId !== null) {
      if (!guid(raw.replacesFactId) || replaced.has(raw.replacesFactId)) return null
      const original = seen.get(raw.replacesFactId)
      if (!original || original.kind !== raw.kind || original.unit !== raw.unit) return null
      replaced.add(raw.replacesFactId)
    } else if (raw.quantity === '0') return null
    seen.set(raw.factId, raw as unknown as NonKgFact)
  }
  return v as unknown as NonKgHistory
}
export function decodeNonKgAttempt(v: unknown, company: string, order: string): NonKgAttempt | null {
  if (!object(v) || Object.keys(v).sort().join(',') !== 'body,companyId,key,orderId' || v.companyId !== company || v.orderId !== order || !guid(v.key) || !object(v.body)) return null
  const b = v.body
  if (Object.keys(b).sort().join(',') !== 'expectedRevision,expectedVersion,factId,item,kind,occurredAtUtc,quantity,reason,replacesFactId,unit'
    || !guid(b.factId) || !Number.isSafeInteger(b.expectedVersion) || (b.expectedVersion as number) < 1 || !Number.isSafeInteger(b.expectedRevision)
    || (b.expectedRevision as number) < 0 || !['Paint', 'Other'].includes(String(b.kind)) || !['Litre', 'Metre', 'Piece'].includes(String(b.unit))
    || b.kind === 'Paint' && b.unit !== 'Litre' || !text(b.item, 200) || !nonKgQuantity(b.quantity) || !utc(b.occurredAtUtc)
    || !(b.replacesFactId === null || guid(b.replacesFactId)) || b.replacesFactId === null && b.quantity === '0' || !text(b.reason, 1000)) return null
  return v as unknown as NonKgAttempt
}
export function readNonKgAttempt(key: string, company: string, order: string): NonKgAttempt | 'blocked' | null {
  try { const raw = sessionStorage.getItem(key); return raw === null ? null : decodeNonKgAttempt(JSON.parse(raw), company, order) ?? 'blocked' } catch { return 'blocked' }
}
export function persistNonKgAttempt(key: string, attempt: NonKgAttempt): boolean {
  try { const raw = JSON.stringify(attempt); sessionStorage.setItem(key, raw); return sessionStorage.getItem(key) === raw } catch { return false }
}
export function clearNonKgAttempt(key: string): boolean { try { sessionStorage.removeItem(key); return sessionStorage.getItem(key) === null } catch { return false } }
export async function fetchNonKgInputs(company: string, order: string, signal: AbortSignal): Promise<ViewOutcome<NonKgHistory>> {
  let response: Response
  try { response = await fetch(`${MANUFACTURING_BASE}/orders/${encodeURIComponent(order)}/non-kg-inputs`, { headers: { ...devHeaders(), 'X-Company-Id': company }, cache: 'no-store', signal }) }
  catch { return signal.aborted ? { type: 'fence-stale' } : navigator.onLine ? { type: 'network-error' } : { type: 'offline' } }
  let body: unknown; try { body = await response.json() } catch { body = null }
  if (response.status !== 200) return { type: 'http', status: response.status, body }
  const data = decodeNonKgHistory(body, company, order); return data === null ? { type: 'malformed' } : { type: 'ok-single', data }
}
export async function recordNonKgInput(attempt: NonKgAttempt): Promise<'applied' | 'unknown' | { status: number }> {
  let response: Response
  try { response = await fetch(`${MANUFACTURING_BASE}/orders/${encodeURIComponent(attempt.orderId)}/non-kg-inputs`, { method: 'POST', headers: {
    ...devHeaders(), 'X-Company-Id': attempt.companyId, 'Content-Type': 'application/json', 'Idempotency-Key': attempt.key }, body: JSON.stringify(attempt.body) }) } catch { return 'unknown' }
  if (response.status === 200) { try { return decodeNonKgHistory(await response.json(), attempt.companyId, attempt.orderId) ? 'applied' : 'unknown' } catch { return 'unknown' } }
  return [400, 401, 403, 404, 409, 415, 422].includes(response.status) ? { status: response.status } : 'unknown'
}
