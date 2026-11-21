import { afterEach, describe, expect, it, vi } from 'vitest'
import { decodeSubcontractInvoiceDetail } from './SubcontractInvoiceContract'
import { commandSubcontractInvoice } from './SubcontractInvoiceApi'
import { amounts, matchOf, finalizationOf, plannedInstalments } from './invoiceTestData'
import { subcontractInvoiceDetail, SUBCONTRACT_INVOICE_ID } from './subcontractInvoiceTestData'

const companyId = '11111111-1111-4111-8111-111111111111'
const lifecycle = () => ({ decision: null, instalments: plannedInstalments(), exchangeRate: null, functionalTotals: null,
  match: matchOf(), finalization: null })
afterEach(() => vi.unstubAllGlobals())

describe('SC34 source date, frozen amounts and lifecycle contract', () => {
  it('accepts a matched foreign invoice only when its rate day is the actual official date', () => {
    const matched = matchOf()
    const facts = { ...lifecycle(), match: matched, exchangeRate: { rate: '40', rateDay: matched.invoiceDate,
      bulletinDate: '2026-10-30', functionalCurrencyId: companyId, functionalScale: 2 }, functionalTotals: amounts('47216.00') }
    const body = subcontractInvoiceDetail({ state: 'Approved', version: '4', lifecycle: facts })
    const decoded = decodeSubcontractInvoiceDetail(body, SUBCONTRACT_INVOICE_ID)
    expect(decoded?.lifecycle?.functionalTotals?.payableAmount).toBe('47216.00')
    expect(decodeSubcontractInvoiceDetail({ ...body, lifecycle: { ...facts,
      exchangeRate: { ...facts.exchangeRate, rateDay: '2026-11-14' } } }, SUBCONTRACT_INVOICE_ID)).toBeNull()
  })

  it('refuses missing proof, partial lifecycle and a finalized body without the actual ledger', () => {
    for (const facts of [{ ...lifecycle(), extra: true }, { ...lifecycle(), match: matchOf({ sourceProof: null }) }, null]) {
      expect(decodeSubcontractInvoiceDetail(subcontractInvoiceDetail({ state: 'Finalized', lifecycle: facts }), SUBCONTRACT_INVOICE_ID)).toBeNull()
    }
    const body = subcontractInvoiceDetail({ state: 'Finalized', version: '5', lifecycle: { ...lifecycle(), finalization: finalizationOf() } })
    expect(decodeSubcontractInvoiceDetail(body, SUBCONTRACT_INVOICE_ID)?.lifecycle?.finalization?.financeStatus).toBe('waiting')
  })

  it('sends only the version and official identity under the frozen company and key; never a client rate or amount', async () => {
    const fetcher = vi.fn(async () => new Response(JSON.stringify(subcontractInvoiceDetail({ state: 'Approved', version: '4', lifecycle: lifecycle() })),
      { status: 200, headers: { 'Content-Type': 'application/json' } }))
    vi.stubGlobal('fetch', fetcher)
    const payload = { invoiceId: SUBCONTRACT_INVOICE_ID, action: 'match' as const, expectedVersion: '3',
      officialInvoiceNo: 'SENTETIK-1', ettn: 'e41d6f79-b532-47f8-9821-6be258535fad', approveDifference: false }
    expect((await commandSubcontractInvoice(payload, { companyId, idempotencyKey: 'SENTETIK-key' })).type).toBe('succeeded')
    const [url, init] = fetcher.mock.calls[0] as unknown as [string, RequestInit]
    expect(url).toContain(`/subcontract-service-invoices/${SUBCONTRACT_INVOICE_ID}/match`)
    expect(init.headers).toMatchObject({ 'X-Company-Id': companyId, 'Idempotency-Key': 'SENTETIK-key' })
    expect(JSON.parse(init.body as string)).toEqual({ expectedVersion: '3', officialInvoiceNo: 'SENTETIK-1', ettn: payload.ettn, approveDifference: false })
  })

  it('does not count another invoice or an unexpected successful status as command success', async () => {
    const payload = { invoiceId: SUBCONTRACT_INVOICE_ID, action: 'submit' as const, expectedVersion: '1' }
    for (const [status, id] of [[201, SUBCONTRACT_INVOICE_ID], [200, companyId]] as const) {
      vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify(subcontractInvoiceDetail({ id })),
        { status, headers: { 'Content-Type': 'application/json' } })))
      expect((await commandSubcontractInvoice(payload, { companyId, idempotencyKey: 'SENTETIK-key' })).type).toBe('malformed')
    }
  })
})
