import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import { decodeInvoiceKilogramPrices, decodeKilogramPrice, decodeOrderKilogramPrices } from './kilogramPriceContract'
import { InvoiceKilogramPricesView, OrderKilogramPricesView, kilogramPriceText } from './SalesKilogramPrices'
import { KILOGRAM_PRICE_TEXT, problemCodeText } from './salesText'
import { SALES_PROBLEM_CODE } from './contract'
import type { InvoiceLine } from './invoiceFinalizationContract'

// SENTETIK: golden K02 (40 adet, 10 000,00 TL, teorik 493,800 kg, kantar 500,160 kg).
const LINE = '11111111-1111-4111-8111-111111111111'
const CURRENCY = '22222222-2222-4222-8222-222222222222'
const shown = (value: string) => ({ state: 'Shown', value })

const computedOrder = () => ({
  state: 'Computed',
  currencyId: CURRENCY,
  lines: [{
    lineId: LINE, lineNo: 1, theoreticalKg: '1234.5', theoreticalPrice: shown('20.25'), invoicedQuantity: '40',
    invoicedAmount: '10000.00', actualKg: '500.16', actualPrice: shown('19.99'), kgDifference: '6.36', deviationPercent: '1.29',
  }],
  documentTheoreticalPrice: shown('20.25'),
  documentActualPrice: shown('19.99'),
})

afterEach(() => cleanup())

describe('SAT-D436 kg price contract', () => {
  it('decodes a pending read with nothing else and refuses figures beside it', () => {
    expect(decodeOrderKilogramPrices({ state: 'FormulaGoldenPending', currencyId: CURRENCY })?.state).toBe('FormulaGoldenPending')
    expect(decodeOrderKilogramPrices({ state: 'FormulaGoldenPending', currencyId: CURRENCY, lines: [] })).toBeNull()
    expect(decodeInvoiceKilogramPrices({ state: 'FormulaGoldenPending' })?.lines).toBeNull()
  })

  it('decodes the computed order read and refuses extra, missing or mistyped members', () => {
    expect(decodeOrderKilogramPrices(computedOrder())?.lines?.[0]?.actualPrice).toEqual(shown('19.99'))
    expect(decodeOrderKilogramPrices({ ...computedOrder(), extra: 1 })).toBeNull()
    const { documentActualPrice: _omitted, ...missing } = computedOrder()
    expect(decodeOrderKilogramPrices(missing)).toBeNull()
    expect(decodeOrderKilogramPrices({ ...computedOrder(), state: 'Other' })).toBeNull()
    expect(decodeOrderKilogramPrices({ ...computedOrder(), lines: [{ ...computedOrder().lines[0], invoicedQuantity: 40 }] })).toBeNull()
  })

  it('takes a shown price only with a 0,01 value and an unavailable one only without', () => {
    expect(decodeKilogramPrice(shown('20.25'))).toEqual(shown('20.25'))
    expect(decodeKilogramPrice(shown('20.251'))).toBeNull()
    expect(decodeKilogramPrice({ state: 'Shown' })).toBeNull()
    expect(decodeKilogramPrice({ state: 'ZeroKg', value: '1.00' })).toBeNull()
    expect(decodeKilogramPrice({ state: 'WeighedKgUnavailable' })).toEqual({ state: 'WeighedKgUnavailable', value: null })
  })
})

describe('SAT-D436 kg price views', () => {
  it('shows the closed indicator text while the golden is pending', () => {
    render(<OrderKilogramPricesView prices={decodeOrderKilogramPrices({ state: 'FormulaGoldenPending', currencyId: CURRENCY })!} />)
    expect(screen.getByTestId('sales-kilogram-prices-pending').textContent).toBe(KILOGRAM_PRICE_TEXT.pending)
  })

  it('shows the server figures as given and the reasons as text', () => {
    const prices = decodeOrderKilogramPrices({
      ...computedOrder(),
      documentActualPrice: { state: 'DocumentNotAllKilogramLines' },
    })!
    render(<OrderKilogramPricesView prices={prices} />)
    expect(screen.getByTestId('sales-kilogram-line-1-theoretical').textContent).toBe(KILOGRAM_PRICE_TEXT.perKg('20.25'))
    expect(screen.getByTestId('sales-kilogram-line-1-actual').textContent).toBe(KILOGRAM_PRICE_TEXT.perKg('19.99'))
    expect(screen.getByTestId('sales-kilogram-document-actual').textContent)
      .toBe(KILOGRAM_PRICE_TEXT.state.DocumentNotAllKilogramLines)
  })

  it('shows a piece invoice line kg information and its pending prices', () => {
    const line = {
      lineId: LINE, lineNo: 1, shipmentId: LINE, dispatchNoteNumber: 'IRS-1', productId: LINE, unitId: LINE, quantity: '40',
      netUnitPrice: '250', vatPercent: '20', amounts: { netAmount: '10000.00', vatAmount: '2000.00', withheldVatAmount: '0.00',
        grossAmount: '12000.00', payableAmount: '12000.00' }, theoreticalKg: '493.8', actualKg: '500.16',
    } as InvoiceLine
    render(<InvoiceKilogramPricesView prices={decodeInvoiceKilogramPrices({ state: 'FormulaGoldenPending' })!} lines={[line]} />)
    expect(screen.getByTestId('sales-invoice-kilogram-info-1').textContent).toContain('493.8')
    expect(screen.getByTestId('sales-invoice-kilogram-info-1').textContent).toContain('500.16')
    expect(screen.getByTestId('sales-invoice-kilogram-prices-pending').textContent).toBe(KILOGRAM_PRICE_TEXT.pending)
  })

  it('has a Turkish text for every price state and the approval refusal', () => {
    expect(kilogramPriceText({ state: 'ZeroKg', value: null })).toMatch(/\S/)
    expect(problemCodeText(SALES_PROBLEM_CODE.kilogramsPerUnitUnavailable)).toMatch(/kg\/adet/)
  })
})

describe('SAT-D434 / SAT-D436 invoice detail members (additive)', async () => {
  const { invoiceDetail, INVOICE_ID } = await import('./invoiceTestData')
  const { invoiceLine } = await import('./customerReturnTestData')
  const { decodeSalesInvoiceDetail } = await import('./invoiceFinalizationContract')
  const withPiece = (line: Record<string, unknown>, extra: Record<string, unknown> = {}) =>
    ({ ...invoiceDetail({ lines: [invoiceLine({ quantity: '40', ...line })] }), ...extra })

  it('decodes a piece line kg information and the pending kg price block', () => {
    const decoded = decodeSalesInvoiceDetail(withPiece({ theoreticalKg: '493.8', actualKg: '500.16' },
      { kilogramPrices: { state: 'FormulaGoldenPending' } }), INVOICE_ID)
    expect([decoded?.lines[0]?.theoreticalKg, decoded?.lines[0]?.actualKg]).toEqual(['493.8', '500.16'])
    expect(decoded?.kilogramPrices?.state).toBe('FormulaGoldenPending')
  })

  it('refuses half the kg information, a mistyped kg and a malformed price block', () => {
    expect(decodeSalesInvoiceDetail(withPiece({ theoreticalKg: '493.8' }), INVOICE_ID)).toBeNull()
    expect(decodeSalesInvoiceDetail(withPiece({ theoreticalKg: 493.8, actualKg: '500.16' }), INVOICE_ID)).toBeNull()
    expect(decodeSalesInvoiceDetail(withPiece({}, { kilogramPrices: { state: 'Other' } }), INVOICE_ID)).toBeNull()
  })
})
