import { API_BASE, purchasingHeaders } from './api'
import { post } from './receiptAdjustmentApi'
import { decodeIncomingInvoice, decodeInvoiceCommand, decodeInvoicePage, decodeInvoiceReceipts, decodeInvoiceAdditionalCosts,
  type IncomingInvoice, type IncomingInvoiceLine, type InvoiceCommand } from './incomingInvoiceContract'
import type { ViewOutcome } from '../shell/uiState'
const base = `${API_BASE}/purchasing/invoices`
async function read<T>(company: string, path: string, signal: AbortSignal, decode: (body: unknown) => T | null): Promise<ViewOutcome<T>> {
  try {
    const response = await fetch(path, { headers: purchasingHeaders(company), signal, cache: 'no-store' })
    if (response.status !== 200) return response.status >= 200 && response.status < 300
      ? { type: 'malformed' } : { type: 'http', status: response.status }
    let body: unknown
    try { body = await response.json() } catch { return { type: 'malformed' } }
    const data = decode(body)
    return data === null ? { type: 'malformed' } : { type: 'ok-single', data }
  } catch { return signal.aborted ? { type: 'fence-stale' } : { type: navigator.onLine ? 'network-error' : 'offline' } }
}
export const fetchIncomingInvoices = (company: string, officialNumber: string, pageIndex: number, signal: AbortSignal) =>
  read(company, `${base}?${new URLSearchParams({ officialNumber, pageIndex: String(pageIndex) })}`, signal,
    (body) => decodeInvoicePage(body, company, pageIndex))
export const fetchIncomingInvoice = (company: string, id: string, signal: AbortSignal) =>
  read(company, `${base}/${encodeURIComponent(id)}`, signal, (body) => decodeIncomingInvoice(body, company, id))
export const fetchInvoiceReceipts = (company: string, invoice: IncomingInvoice, line: IncomingInvoiceLine, pageIndex: number, signal: AbortSignal) =>
  read(company, `${base}/${encodeURIComponent(invoice.id)}/lines/${encodeURIComponent(line.id)}/receipt-candidates?pageIndex=${pageIndex}`,
    signal, (body) => decodeInvoiceReceipts(body, invoice, line, pageIndex))
export const fetchInvoiceAdditionalCosts = (company: string, invoice: IncomingInvoice, line: IncomingInvoiceLine, pageIndex: number, signal: AbortSignal) =>
  read(company, `${base}/${encodeURIComponent(invoice.id)}/lines/${encodeURIComponent(line.id)}/additional-cost-candidates?pageIndex=${pageIndex}`,
    signal, body => decodeInvoiceAdditionalCosts(body, invoice, line, pageIndex))
export const submitInvoiceCommand = (company: string, id: string, command: InvoiceCommand, key: string) =>
  post(company, `${base}/${encodeURIComponent(id)}/${command.action}`, command.request, key, 200,
    (body, replay) => decodeInvoiceCommand(body, company, id, replay))
