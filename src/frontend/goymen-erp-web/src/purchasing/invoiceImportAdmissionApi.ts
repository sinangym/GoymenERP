import { API_BASE, purchasingHeaders } from './api'
import type { ViewOutcome } from '../shell/uiState'
import type { AdjustmentOutcome } from './receiptAdjustmentApi'
import type { ImportPreviewRequest } from './invoiceImportContract'
import type { InvoiceCommandResponse } from './incomingInvoiceContract'
import { decodeImportMappingReferences, decodeImportProducts, decodeImportRecord, decodeImportSupplier,
  type ImportRecordSnapshot } from './invoiceImportAdmissionContract'

const base = `${API_BASE}/purchasing/invoice-imports`
async function read<T>(company: string, id: string, action: string, payload: unknown, signal: AbortSignal, decode: (v: unknown) => T | null): Promise<ViewOutcome<T>> {
  try {
    const response = await fetch(`${base}/${encodeURIComponent(id)}/${action}`, { method: 'POST', cache: 'no-store', signal,
      headers: { ...purchasingHeaders(company), 'Content-Type': 'application/json' }, body: JSON.stringify(payload) })
    if (response.status !== 200) return response.status >= 200 && response.status < 300
      ? { type: 'malformed' } : { type: 'http', status: response.status }
    let body: unknown; try { body = await response.json() } catch { return { type: 'malformed' } }
    const value = decode(body); return value === null ? { type: 'malformed' } : { type: 'ok-single', data: value }
  } catch { return signal.aborted ? { type: 'fence-stale' } : { type: navigator.onLine ? 'network-error' : 'offline' } }
}
export const fetchImportMappingReferences = (company: string, id: string, source: ImportPreviewRequest, date: string, signal: AbortSignal) =>
  read(company, id, 'mapping-references', source, signal, v => decodeImportMappingReferences(v, company, id, source, date))
export const resolveImportSupplier = (company: string, id: string, source: ImportPreviewRequest, party: string, account: string, signal: AbortSignal) =>
  read(company, id, 'supplier-resolution', { source, supplierPartyId: party, companyAccountId: account }, signal, v => decodeImportSupplier(v, company, id, party, account))
export const fetchImportProducts = (company: string, id: string, source: ImportPreviewRequest, q: string, signal: AbortSignal) =>
  read(company, id, 'product-candidates', { source, q }, signal, v => decodeImportProducts(v, company, id))
export async function recordInvoiceImport(company: string, id: string, snapshot: ImportRecordSnapshot, key: string): Promise<AdjustmentOutcome<InvoiceCommandResponse>> {
  const correlation = `purchase-import-${key}`
  try {
    const response = await fetch(`${base}/${encodeURIComponent(id)}/records`, { method: 'POST', headers: { ...purchasingHeaders(company),
      'Content-Type': 'application/json', 'Idempotency-Key': key, 'X-Correlation-Id': correlation }, body: JSON.stringify(snapshot.request) })
    let body: unknown = null; try { body = await response.json() } catch { /* A malformed success remains unresolved. */ }
    const header = response.headers.get('Idempotency-Replayed'), replay = header === 'true'
    if (response.status >= 500) return { type: 'unknown', reason: 'service-unavailable' }
    if (response.status !== 200) {
      if (response.status >= 200 && response.status < 300) return { type: 'unknown', reason: 'malformed' }
      const code = body !== null && typeof body === 'object' && 'code' in body && typeof body.code === 'string' ? body.code : null
      if (response.status === 409 && (code === null || code.includes('concurrent') || code.includes('busy'))) return { type: 'unknown', reason: 'network-error' }
      return { type: 'failed', status: response.status, code, isReplay: replay }
    }
    if (header !== null && header !== 'true' && header !== 'false') return { type: 'unknown', reason: 'malformed' }
    const answer = decodeImportRecord(body, company, id, snapshot, replay)
    return answer === null || (replay ? response.headers.get('Idempotency-Original-Correlation-Id') !== answer.originalCorrelationId
      : answer.originalCorrelationId !== correlation) ? { type: 'unknown', reason: 'malformed' } : { type: 'applied', response: answer }
  } catch { return { type: 'unknown', reason: navigator.onLine ? 'network-error' : 'offline' } }
}
