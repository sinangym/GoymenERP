import { API_BASE, logisticsHeaders } from './api'
import { decodeProblem } from './contract'
import { decodeDispatchKilograms, decodeKgResponse, type DispatchKilograms, type KgCommand, type KgResponse } from './dispatchKilogramsContract'
import type { AttemptSnapshot, CommandOutcome } from './instructionAttempt'
import type { ViewOutcome } from '../shell/uiState'

export const kgReadPath = (note: string) => `/logistics/dispatch-notes/${encodeURIComponent(note)}/weighing-difference`
export const kgRecordPath = '/logistics/weighing-differences'
export const kgApprovePath = (record: string) => `${kgRecordPath}/${encodeURIComponent(record)}/approve`
const json = async (response: Response): Promise<unknown> => { try { return await response.json() } catch { return null } }
// Text normalization only: no allocation, price or tolerance calculation in the browser.
const sameKg = (a: string, b: string) => a.replace(/\.0+$/, '').replace(/(\.[0-9]*?)0+$/, '$1') === b.replace(/\.0+$/, '').replace(/(\.[0-9]*?)0+$/, '$1')
export async function readDispatchKilograms(company: string, note: string, signal?: AbortSignal): Promise<ViewOutcome<DispatchKilograms>> {
  try {
    const response = await fetch(`${API_BASE}${kgReadPath(note)}`, { headers: logisticsHeaders(company), cache: 'no-store', signal })
    if (signal?.aborted) return { type: 'fence-stale' }
    if (response.status !== 200) return { type: 'http', status: response.status }
    const value = decodeDispatchKilograms(await json(response), company, note)
    return value ? { type: 'ok-single', data: value } : { type: 'malformed' }
  } catch { return { type: signal?.aborted ? 'fence-stale' : navigator.onLine ? 'network-error' : 'offline' } }
}
export async function sendDispatchKilograms(snapshot: AttemptSnapshot<KgCommand>, note: string, signal?: AbortSignal): Promise<CommandOutcome<KgResponse>> {
  try {
    const response = await fetch(`${API_BASE}${snapshot.path}`, { method: 'POST', headers: { ...logisticsHeaders(snapshot.companyId),
      'Content-Type': 'application/json', 'Idempotency-Key': snapshot.key }, body: JSON.stringify(snapshot.payload), signal })
    if (signal?.aborted) return { type: 'aborted' }
    const body = await json(response)
    if (response.status !== 200) return response.status >= 200 && response.status < 300 ? { type: 'malformed' }
      : { type: 'http', status: response.status, problem: decodeProblem(body, response.status) }
    const result = decodeKgResponse(body, snapshot.companyId, note)
    if (!result || result.isReplay !== (response.headers.get('Idempotency-Replayed') === 'true')
      || result.isReplay && response.headers.get('Idempotency-Original-Correlation-Id') !== result.originalCorrelationId) return { type: 'malformed' }
    if ('recordId' in snapshot.payload) {
      if (result.record.recordId !== snapshot.payload.recordId || result.record.kilogramAllocation === null) return { type: 'malformed' }
      const expected = snapshot.payload.finalizedDocument, actual = result.record.kilogramAllocation.finalizedDocument
      if (expected === null ? actual !== null : actual === null || expected.documentId !== actual.documentId || expected.ettn !== actual.ettn || expected.kind !== actual.kind) return { type: 'malformed' }
      if (expected && actual && (expected.expectedDocumentVersion !== actual.documentVersion || expected.expectedDocumentSha256.toLowerCase() !== actual.sha256.toLowerCase())) return { type: 'malformed' }
      if (expected !== null && (expected.lines.length !== result.record.kilogramAllocation.lines.length || expected.lines.some(line => {
        const returned = result.record.kilogramAllocation!.lines.find(x => x.shipmentId === line.shipmentId)
        return !returned || returned.lineNo !== line.lineNo || returned.productId !== line.productId || !sameKg(returned.kilograms, line.kilograms)
      }))) return { type: 'malformed' }
      if (expected === null && (snapshot.payload.products.length !== result.record.products.length || snapshot.payload.products.some(product => {
        const returned = result.record.products.find(x => x.productId === product.productId)
        return !returned || !sameKg(product.scaleKg, returned.scaleKg)
      }))) return { type: 'malformed' }
    } else if (snapshot.path !== kgApprovePath(result.record.recordId) || result.record.status !== 'Approved' || result.record.version !== snapshot.payload.expectedVersion + 1) return { type: 'malformed' }
    return { type: 'succeeded', data: result }
  } catch { return { type: signal?.aborted ? 'aborted' : navigator.onLine ? 'network-error' : 'offline' } }
}
