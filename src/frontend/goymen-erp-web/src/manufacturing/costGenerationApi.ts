import { MANUFACTURING_BASE, devHeaders } from './api'
import type { ViewOutcome } from '../shell/uiState'

export interface CostGenerationOutput {
  readonly outputId: string; readonly labelId: string; readonly netKg: string; readonly remainingKg: string | null; readonly consumedKg: string | null
  readonly remainingKgRatio?: CostQuantityRatio; readonly consumedKgRatio?: CostQuantityRatio
  readonly originalTlAmount: string | null; readonly remainingTlValue: string | null; readonly consumedTlDifference: string | null; readonly usdUnitCostPerKg: string | null
}
export interface CostQuantityRatio { readonly numerator: string; readonly denominator: string }
export interface CostGeneration {
  readonly companyId: string; readonly orderId: string; readonly generationId: string; readonly closedOrderVersion: number
  readonly revision: number; readonly inventoryVersion: string; readonly state: 'Pending' | 'Unreadable' | 'Recognized' | 'Reversed'
  readonly inputFingerprint: string; readonly calculatorVersion: string; readonly recordedAtUtc: string; readonly reason: string
  readonly correlationId: string; readonly costDifferenceEventId: string | null; readonly outputs: readonly CostGenerationOutput[]
}
export interface CostGenerationHistory { readonly orderId: string; readonly orderVersion: number; readonly generations: readonly CostGeneration[] }
export interface CostRecognitionAttempt { readonly idempotencyKey: string; readonly expectedVersion: number; readonly expectedGenerationRevision: number; readonly reason: string }
const guid = /^(?!00000000-0000-0000-0000-000000000000)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const number = /^(0|[1-9]\d*)(\.\d+)?$/
const object = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null && !Array.isArray(value)
const text = (value: unknown): value is string => typeof value === 'string' && value.trim().length > 0
const decimal = (value: unknown) => value === null || typeof value === 'string' && number.test(value)
const signedDecimal = (value: unknown) => value === null || typeof value === 'string' && /^-?(0|[1-9]\d*)(\.\d+)?$/.test(value)
const id = (value: unknown): value is string => typeof value === 'string' && guid.test(value)
const ratio = (raw: unknown): raw is CostQuantityRatio => {
  if (!object(raw) || Object.keys(raw).sort().join(',') !== 'denominator,numerator'
    || typeof raw.numerator !== 'string' || typeof raw.denominator !== 'string'
    || raw.numerator.length > 1024 || raw.denominator.length > 1024 || !/^(0|[1-9]\d*)$/.test(raw.numerator) || !/^[1-9]\d*$/.test(raw.denominator)) return false
  let a = BigInt(raw.numerator), b = BigInt(raw.denominator)
  while (b !== 0n) { const rest = a % b; a = b; b = rest }
  return a === 1n
}
const quantity = (scalar: unknown, exact: unknown): boolean => {
  if (exact === undefined) return typeof scalar === 'string' && number.test(scalar)
  if (!ratio(exact) || scalar !== null && (typeof scalar !== 'string' || !number.test(scalar))) return false
  if (scalar === null) return true
  const [whole, fraction = ''] = (scalar as string).split('.')
  // Duplicate wire evidence must agree exactly. No quantity, cost or rounding policy is computed here.
  return BigInt(whole + fraction) * BigInt(exact.denominator) === BigInt(exact.numerator) * 10n ** BigInt(fraction.length)
}
export const costQuantityText = (scalar: string | null, exact?: CostQuantityRatio) => scalar ?? (exact === undefined ? '—' : `${exact.numerator} / ${exact.denominator}`)
export function decodeCostGeneration(value: unknown, company: string, order: string): CostGeneration | null {
  if (!object(value) || value.companyId !== company || value.orderId !== order || !id(value.generationId)
    || !Number.isSafeInteger(value.closedOrderVersion) || (value.closedOrderVersion as number) < 2
    || !Number.isSafeInteger(value.revision) || (value.revision as number) < 1 || !text(value.inventoryVersion)
    || !['Pending', 'Unreadable', 'Recognized', 'Reversed'].includes(String(value.state))
    || typeof value.inputFingerprint !== 'string' || !/^[0-9a-f]{64}$/.test(value.inputFingerprint)
    || !text(value.calculatorVersion) || !text(value.reason) || !text(value.correlationId)
    || typeof value.recordedAtUtc !== 'string' || !Number.isFinite(Date.parse(value.recordedAtUtc))
    || !(value.costDifferenceEventId === null || id(value.costDifferenceEventId)) || !Array.isArray(value.outputs)) return null
  const ids = new Set<string>()
  for (const row of value.outputs) {
    if (!object(row) || !id(row.outputId) || !id(row.labelId) || ids.has(row.outputId)
      || typeof row.netKg !== 'string' || !number.test(row.netKg) || !quantity(row.remainingKg, row.remainingKgRatio)
      || !quantity(row.consumedKg, row.consumedKgRatio)
      || ![row.originalTlAmount, row.usdUnitCostPerKg].every(decimal) || !signedDecimal(row.remainingTlValue) || !signedDecimal(row.consumedTlDifference)
      || ['Pending', 'Unreadable'].includes(String(value.state)) && [row.originalTlAmount, row.remainingTlValue, row.consumedTlDifference, row.usdUnitCostPerKg].some(v => v !== null)) return null
    ids.add(String(row.outputId))
  }
  return value as unknown as CostGeneration
}
export function decodeCostAttempt(value: unknown): CostRecognitionAttempt | null {
  if (!object(value) || Object.keys(value).sort().join(',') !== 'expectedGenerationRevision,expectedVersion,idempotencyKey,reason'
    || !id(value.idempotencyKey) || !Number.isSafeInteger(value.expectedVersion) || (value.expectedVersion as number) < 2
    || !Number.isSafeInteger(value.expectedGenerationRevision) || (value.expectedGenerationRevision as number) < 0
    || !text(value.reason) || value.reason !== value.reason.trim() || value.reason.length > 256 || /[\u0000-\u001f\u007f-\u009f]/.test(value.reason)) return null
  return value as unknown as CostRecognitionAttempt
}
export function readCostAttempt(key: string): CostRecognitionAttempt | 'blocked' | null {
  try { const value = sessionStorage.getItem(key); return value === null ? null : decodeCostAttempt(JSON.parse(value)) ?? 'blocked' } catch { return 'blocked' }
}
export function persistCostAttempt(key: string, value: CostRecognitionAttempt): boolean {
  if (decodeCostAttempt(value) === null) return false
  try { const json = JSON.stringify(value); sessionStorage.setItem(key, json); return sessionStorage.getItem(key) === json } catch { return false }
}
export function clearCostAttempt(key: string): boolean {
  try { sessionStorage.removeItem(key); return sessionStorage.getItem(key) === null } catch { return false }
}
export function decodeCostHistory(value: unknown, company: string, order: string): CostGenerationHistory | null {
  if (!object(value) || value.orderId !== order || !Number.isSafeInteger(value.orderVersion) || (value.orderVersion as number) < 1
    || !Array.isArray(value.generations)) return null
  const keys = new Set<string>(); let close = 0; let revision = 0
  for (const row of value.generations) {
    const entry = decodeCostGeneration(row, company, order)
    if (entry === null || entry.closedOrderVersion < close || entry.closedOrderVersion === close && entry.revision <= revision
      || keys.has(`${entry.generationId}:${entry.revision}`)) return null
    keys.add(`${entry.generationId}:${entry.revision}`); close = entry.closedOrderVersion; revision = entry.revision
  }
  return value as unknown as CostGenerationHistory
}
export async function fetchCostGenerations(company: string, order: string, signal?: AbortSignal): Promise<ViewOutcome<CostGenerationHistory>> {
  let response: Response
  try { response = await fetch(`${MANUFACTURING_BASE}/orders/${encodeURIComponent(order)}/cost-generations`, { headers: { ...devHeaders(), 'X-Company-Id': company }, cache: 'no-store', signal }) }
  catch { return signal?.aborted ? { type: 'fence-stale' } : navigator.onLine ? { type: 'network-error' } : { type: 'offline' } }
  let body: unknown; try { body = await response.json() } catch { body = null }
  if (response.status !== 200) return { type: 'http', status: response.status, body }
  const decoded = decodeCostHistory(body, company, order)
  return decoded === null ? { type: 'malformed' } : { type: 'ok-single', data: decoded }
}
export async function recognizeCost(company: string, order: string, attempt: CostRecognitionAttempt): Promise<'applied' | 'unknown' | { status: number }> {
  if (decodeCostAttempt(attempt) === null) return 'unknown'
  let response: Response
  try { response = await fetch(`${MANUFACTURING_BASE}/orders/${encodeURIComponent(order)}/cost-recognition`, { method: 'POST',
    headers: { ...devHeaders(), 'X-Company-Id': company, 'Content-Type': 'application/json', 'Idempotency-Key': attempt.idempotencyKey },
    body: JSON.stringify({ expectedVersion: attempt.expectedVersion, expectedGenerationRevision: attempt.expectedGenerationRevision, reason: attempt.reason }) }) }
  catch { return 'unknown' }
  let body: unknown; try { body = await response.json() } catch { body = null }
  if (response.status === 200) {
    const replay = response.headers.get('Idempotency-Replayed')
    if (replay !== null && replay !== 'true' && replay !== 'false' || replay === 'true' && !response.headers.get('Idempotency-Original-Correlation-Id')?.trim()) return 'unknown'
    const row = decodeCostGeneration(body, company, order)
    return row !== null && row.closedOrderVersion === attempt.expectedVersion ? 'applied' : 'unknown'
  }
  return [400, 401, 403, 404, 409, 415, 422].includes(response.status) ? { status: response.status } : 'unknown'
}
