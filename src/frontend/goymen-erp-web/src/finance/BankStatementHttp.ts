import type { ViewOutcome } from '../shell/uiState'
import { API_BASE, devHeaders } from './http'
import { decodeCommandResult, decodeDetail, decodeImportList, decodeMatchResult, type MatchCommandResult,
  type StatementCommandResult, type StatementDetail, type StatementFormat, type StatementImport, type CandidateKind } from './BankStatementContract'

const imports = `${API_BASE}/finance/bank-statement-imports`
const matches = `${API_BASE}/finance/bank-statement-matches`
const headers = (companyId: string) => ({ ...devHeaders(), 'X-Company-Id': companyId, Accept: 'application/json' })

async function read<T>(url: string, companyId: string, decode: (body: unknown) => T | null, signal?: AbortSignal): Promise<ViewOutcome<T>> {
  try {
    const response = await fetch(url, { headers: headers(companyId), cache: 'no-store', signal })
    if (response.status !== 200) return { type: 'http', status: response.status }
    let body: unknown
    try { body = await response.json() } catch { return { type: 'malformed' } }
    if (signal?.aborted) return { type: 'fence-stale' }
    const data = decode(body)
    return data === null ? { type: 'malformed' } : { type: 'ok-single', data }
  } catch { return signal?.aborted ? { type: 'fence-stale' } : navigator.onLine ? { type: 'network-error' } : { type: 'offline' } }
}

export const fetchStatementImports = (companyId: string, signal?: AbortSignal): Promise<ViewOutcome<readonly StatementImport[]>> =>
  read(imports, companyId, (body) => decodeImportList(body, companyId), signal)

export const fetchStatementDetail = (companyId: string, importId: string, signal?: AbortSignal): Promise<ViewOutcome<StatementDetail>> =>
  read(`${imports}/${encodeURIComponent(importId)}`, companyId, (body) => decodeDetail(body, companyId, importId), signal)

export type Reply<T> = { readonly kind: 'answered'; readonly result: T } | { readonly kind: 'refused'; readonly status: number } | { readonly kind: 'unknown' }

async function post<T extends { readonly outcome: string; readonly replayed: boolean }>(url: string, companyId: string, key: string,
  body: unknown, decode: (value: unknown) => T | null): Promise<Reply<T>> {
  try {
    const response = await fetch(url, { method: 'POST', cache: 'no-store', body: JSON.stringify(body),
      headers: { ...headers(companyId), 'Content-Type': 'application/json', 'Idempotency-Key': key } })
    if ([400, 401, 403, 404, 415].includes(response.status)) return { kind: 'refused', status: response.status }
    if (![200, 409, 422].includes(response.status)) return { kind: 'unknown' }
    const result = decode(await response.json())
    if (result === null || result.replayed !== (response.headers.get('Idempotency-Replayed') === 'true')
      || (response.status === 200) !== (result.outcome === 'Succeeded')
      || (response.status === 422) !== (result.outcome === 'ValidationFailed')) return { kind: 'unknown' }
    return { kind: 'answered', result }
  } catch { return { kind: 'unknown' } }
}

export interface ReserveRequest { readonly importId: string; readonly bankAccountId: string; readonly format: StatementFormat; readonly bankKey: string | null }
export interface ParseRequest { readonly expectedVersion: string; readonly documentId: string; readonly documentVersion: number }
export interface ConfirmRequest { readonly matchId: string; readonly lineId: string; readonly kind: CandidateKind; readonly recordId: string }
export interface ReverseRequest { readonly expectedVersion: string; readonly reversalId: string; readonly reason: string }

export const reserveStatement = (companyId: string, key: string, request: ReserveRequest): Promise<Reply<StatementCommandResult>> =>
  post(imports, companyId, key, request, (value) => decodeCommandResult(value, companyId))
export const parseStatement = (companyId: string, importId: string, key: string, request: ParseRequest): Promise<Reply<StatementCommandResult>> =>
  post(`${imports}/${encodeURIComponent(importId)}/parse`, companyId, key, request, (value) => decodeCommandResult(value, companyId))
export const confirmStatementMatch = (companyId: string, importId: string, key: string, request: ConfirmRequest): Promise<Reply<MatchCommandResult>> =>
  post(`${imports}/${encodeURIComponent(importId)}/matches`, companyId, key, request, (value) => decodeMatchResult(value, companyId))
export const reverseStatementMatch = (companyId: string, matchId: string, key: string, request: ReverseRequest): Promise<Reply<MatchCommandResult>> =>
  post(`${matches}/${encodeURIComponent(matchId)}/reverse`, companyId, key, request, (value) => decodeMatchResult(value, companyId))
