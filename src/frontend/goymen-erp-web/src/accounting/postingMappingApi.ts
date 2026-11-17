import type { ViewOutcome } from '../shell/uiState'
import { isDateOnly } from '../finance/dateOnly'
import { ACCOUNTING_BASE, devHeaders } from './api'

export interface PostingMappingRow {
  readonly purpose: string; readonly mappingKey: string; readonly accountId: string; readonly accountCode: string
  readonly itemType: string | null; readonly linePurpose: string | null; readonly isExport: boolean | null; readonly vatRateCode: string | null
}
export interface PostingMapping {
  readonly companyId: string; readonly mappingVersion: number; readonly effectiveFrom: string
  readonly status: 'Prepared' | 'Active' | 'Superseded'; readonly version: number
  readonly accountPlanSourceVersion: string; readonly accountPlanSourceSha256: string
  readonly preparedByUserId: string; readonly preparedAtUtc: string
  readonly activatedByUserId: string | null; readonly activatedAtUtc: string | null; readonly rows: readonly PostingMappingRow[]
}
export interface PostingMappingWorkspace {
  readonly companyId: string; readonly latestMappingVersion: number; readonly accountPlanState: 'Available' | 'Unavailable'
  readonly accountPlanSourceVersion: string | null; readonly accountPlanSourceSha256: string | null
  readonly accounts: readonly { readonly accountId: string; readonly code: string; readonly name: string }[]
  readonly cogsDefaults: readonly { readonly mappingKey: 'trade-goods' | 'manufactured'; readonly accountCode: string; readonly accountId: string | null }[]
  readonly active: PostingMapping | null; readonly prepared: readonly PostingMapping[]
}
export type PostingMappingAttempt = { readonly kind: 'prepare'; readonly key: string
  readonly request: { readonly expectedLatestMappingVersion: number; readonly effectiveFrom: string; readonly rows: readonly Omit<PostingMappingRow, 'accountCode'>[] } }
  | { readonly kind: 'activate'; readonly key: string; readonly mappingVersion: number
    readonly request: { readonly expectedVersion: number; readonly expectedActiveMappingVersion: number } }
export type PostingMappingOutcome = { readonly type: 'applied'; readonly value: PostingMapping }
  | { readonly type: 'refused'; readonly status: number } | { readonly type: 'unknown' }

const rec = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v)
const guid = (v: unknown): v is string => typeof v === 'string' && /^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(v) && v !== '00000000-0000-0000-0000-000000000000'
const same = (a: string, b: string) => a.toLowerCase() === b.toLowerCase()
const text = (v: unknown): v is string => typeof v === 'string' && v.trim() !== ''
const nullableText = (v: unknown) => v === null || text(v)
const hash = (v: unknown) => typeof v === 'string' && /^[0-9a-f]{64}$/i.test(v)
const number = (v: unknown, min = 1): v is number => Number.isSafeInteger(v) && (v as number) >= min
const instant = (v: unknown) => typeof v === 'string' && Number.isFinite(Date.parse(v))
const keys: Record<string, readonly string[]> = { 'sales-invoice': ['receivable', 'revenue', 'vat-output', 'withholding-receivable'],
  'purchase-invoice': ['payable', 'inventory', 'vat-input', 'withholding-payable', 'expense'], 'cost-of-goods-sold': ['trade-goods', 'manufactured'] }
const base = `${ACCOUNTING_BASE}/posting-account-mappings`
export function decodePostingMapping(body: unknown, company: string): PostingMapping | null {
  if (!rec(body) || !guid(body.companyId) || !same(body.companyId, company) || !number(body.mappingVersion) || !number(body.version)
    || !text(body.effectiveFrom) || !isDateOnly(body.effectiveFrom) || !['Prepared', 'Active', 'Superseded'].includes(body.status as string)
    || !text(body.accountPlanSourceVersion) || !hash(body.accountPlanSourceSha256) || !guid(body.preparedByUserId) || !instant(body.preparedAtUtc)
    || !Array.isArray(body.rows) || body.rows.length === 0 || body.rows.length > 1000) return null
  if (body.status === 'Prepared' ? body.activatedByUserId !== null || body.activatedAtUtc !== null
    : !guid(body.activatedByUserId) || same(body.activatedByUserId, body.preparedByUserId) || !instant(body.activatedAtUtc)) return null
  const seen = new Set<string>()
  for (const row of body.rows) {
    if (!rec(row) || !text(row.purpose) || !text(row.mappingKey) || !keys[row.purpose]?.includes(row.mappingKey) || !guid(row.accountId)
      || !text(row.accountCode) || !nullableText(row.itemType) || !nullableText(row.linePurpose) || !nullableText(row.vatRateCode)
      || row.isExport !== null && typeof row.isExport !== 'boolean') return null
    if (row.purpose === 'cost-of-goods-sold' && [row.itemType, row.linePurpose, row.vatRateCode, row.isExport].some(v => v !== null)) return null
    const key = JSON.stringify([row.purpose, row.mappingKey, row.itemType, row.linePurpose, row.isExport, row.vatRateCode])
    if (seen.has(key)) return null
    seen.add(key)
  }
  return body as unknown as PostingMapping
}
export function decodePostingMappingWorkspace(body: unknown, company: string): PostingMappingWorkspace | null {
  if (!rec(body) || !guid(body.companyId) || !same(body.companyId, company) || !number(body.latestMappingVersion, 0)
    || !['Available', 'Unavailable'].includes(body.accountPlanState as string) || !Array.isArray(body.accounts)
    || !Array.isArray(body.cogsDefaults) || body.cogsDefaults.length !== 2 || !Array.isArray(body.prepared)) return null
  if (body.accountPlanState === 'Available' ? !text(body.accountPlanSourceVersion) || !hash(body.accountPlanSourceSha256) || body.accounts.length === 0
    : body.accountPlanSourceVersion !== null || body.accountPlanSourceSha256 !== null || body.accounts.length !== 0) return null
  if (body.accounts.some(x => !rec(x) || !guid(x.accountId) || !text(x.code) || !text(x.name))
    || new Set(body.accounts.map(x => (x.accountId as string).toLowerCase())).size !== body.accounts.length
    || new Set(body.accounts.map(x => x.code)).size !== body.accounts.length) return null
  if (new Set(body.cogsDefaults.map(x => rec(x) && x.mappingKey)).size !== 2) return null
  for (const item of body.cogsDefaults) {
    if (!rec(item) || !['trade-goods', 'manufactured'].includes(item.mappingKey as string)
      || item.accountCode !== (item.mappingKey === 'trade-goods' ? '621' : '620')
      || item.accountId !== null && (!guid(item.accountId) || !body.accounts.some(x => same(x.accountId, item.accountId as string) && x.code === item.accountCode))) return null
  }
  const active = body.active === null ? null : decodePostingMapping(body.active, company)
  if (body.active !== null && (active === null || active.status !== 'Active')) return null
  const prepared = body.prepared.map(x => decodePostingMapping(x, company))
  if (prepared.some(x => x === null || x.status !== 'Prepared' || x.mappingVersion > (body.latestMappingVersion as number))
    || new Set(prepared.map(x => x?.mappingVersion)).size !== prepared.length
    || active !== null && (active.mappingVersion > body.latestMappingVersion || prepared.some(x => x?.mappingVersion === active.mappingVersion))) return null
  return { ...body, active, prepared } as unknown as PostingMappingWorkspace
}
export async function fetchPostingMappingWorkspace(company: string, signal?: AbortSignal): Promise<ViewOutcome<PostingMappingWorkspace>> {
  try {
    const response = await fetch(`${base}/workspace`, { headers: { ...devHeaders(), 'X-Company-Id': company }, signal })
    const body: unknown = await response.json().catch(() => null)
    if (response.status !== 200) return { type: 'http', status: response.status, body }
    const value = decodePostingMappingWorkspace(body, company)
    return value === null ? { type: 'malformed' } : { type: 'ok-single', data: value }
  } catch { return signal?.aborted ? { type: 'fence-stale' } : navigator.onLine ? { type: 'network-error' } : { type: 'offline' } }
}
export async function sendPostingMapping(company: string, attempt: PostingMappingAttempt, actor: string): Promise<PostingMappingOutcome> {
  try {
    const response = await fetch(attempt.kind === 'prepare' ? base : `${base}/${attempt.mappingVersion}/activation`, {
      method: 'POST', headers: { ...devHeaders(), 'X-Company-Id': company, 'Content-Type': 'application/json', 'Idempotency-Key': attempt.key },
      body: JSON.stringify(attempt.request) })
    const body: unknown = await response.json().catch(() => null)
    if (response.status === (attempt.kind === 'prepare' ? 201 : 200)) {
      const value = decodePostingMapping(body, company)
      if (value === null || !guid(actor) || (attempt.kind === 'prepare' ? !same(value.preparedByUserId, actor)
        : value.activatedByUserId === null || !same(value.activatedByUserId, actor))) return { type: 'unknown' }
      if (attempt.kind === 'prepare') {
        const actual = value.rows.map(({ accountCode: _code, ...row }) => row)
        if (value.status !== 'Prepared' || value.mappingVersion !== attempt.request.expectedLatestMappingVersion + 1 || value.version !== 1
          || value.effectiveFrom !== attempt.request.effectiveFrom || actual.length !== attempt.request.rows.length
          || actual.some((row, index) => Object.keys(row).some(key => row[key as keyof typeof row] !== attempt.request.rows[index][key as keyof typeof row]))) return { type: 'unknown' }
      } else if (value.status !== 'Active' || value.mappingVersion !== attempt.mappingVersion || value.version !== attempt.request.expectedVersion + 1) return { type: 'unknown' }
      return { type: 'applied', value }
    }
    const code = rec(body) && typeof body.code === 'string' ? body.code : ''
    return [400, 401, 403, 404, 415].includes(response.status)
      || [409, 422].includes(response.status) && code.startsWith('accounting_')
      ? { type: 'refused', status: response.status } : { type: 'unknown' }
  } catch { return { type: 'unknown' } }
}
