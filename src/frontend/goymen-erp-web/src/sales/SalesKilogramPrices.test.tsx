import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import { decodeInvoiceKilogramPrices, decodeKilogramPrice, decodeOrderKilogramPrices } from './kilogramPriceContract'
import { InvoiceKilogramPricesView, OrderKilogramPricesView } from './SalesKilogramPrices'
import { kilogramPriceText } from './kilogramPriceText'
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
    render(<OrderKilogramPricesView currency="TRY" prices={decodeOrderKilogramPrices({ state: 'FormulaGoldenPending', currencyId: CURRENCY })!} />)
    expect(screen.getByTestId('sales-kilogram-prices-pending').textContent).toBe(KILOGRAM_PRICE_TEXT.pending)
  })

  it('shows the server figures as given and the reasons as text', () => {
    const prices = decodeOrderKilogramPrices({
      ...computedOrder(),
      documentActualPrice: { state: 'DocumentNotAllKilogramLines' },
    })!
    render(<OrderKilogramPricesView currency="TRY" prices={prices} />)
    expect(screen.getByTestId('sales-kilogram-line-1-theoretical').textContent).toBe(KILOGRAM_PRICE_TEXT.perKg('20.25', 'TRY'))
    expect(screen.getByTestId('sales-kilogram-line-1-actual').textContent).toBe(KILOGRAM_PRICE_TEXT.perKg('19.99', 'TRY'))
    expect(screen.getByTestId('sales-kilogram-document-actual').textContent)
      .toBe(KILOGRAM_PRICE_TEXT.state.DocumentNotAllKilogramLines)
  })

  it('shows a piece invoice line kg information and its pending prices', () => {
    const line = {
      lineId: LINE, lineNo: 1, shipmentId: LINE, dispatchNoteNumber: 'IRS-1', productId: LINE, unitId: LINE, quantity: '40',
      netUnitPrice: '250', vatPercent: '20', amounts: { netAmount: '10000.00', vatAmount: '2000.00', withheldVatAmount: '0.00',
        grossAmount: '12000.00', payableAmount: '12000.00' }, theoreticalKg: '493.8', actualKg: '500.16',
    } as InvoiceLine
    render(<InvoiceKilogramPricesView currency="TRY" prices={decodeInvoiceKilogramPrices({ state: 'FormulaGoldenPending' })!} lines={[line]} />)
    expect(screen.getByTestId('sales-invoice-kilogram-info-1').textContent).toContain('493.8')
    expect(screen.getByTestId('sales-invoice-kilogram-info-1').textContent).toContain('500.16')
    expect(screen.getByTestId('sales-invoice-kilogram-prices-pending').textContent).toBe(KILOGRAM_PRICE_TEXT.pending)
  })

  it('has a Turkish text for every price state and the approval refusal', () => {
    expect(kilogramPriceText({ state: 'ZeroKg', value: null }, 'TRY')).toMatch(/\S/)
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

describe('SAT-D436 / AK-4b: the opened (Computed) kg price panels', () => {
  const invoiceLineOf = (over: Record<string, unknown> = {}) => ({
    lineId: LINE, lineNo: 1, shipmentId: LINE, dispatchNoteNumber: 'IRS-1', productId: LINE, unitId: LINE, quantity: '40',
    netUnitPrice: '250', vatPercent: '20', amounts: { netAmount: '10000.00', vatAmount: '2000.00', withheldVatAmount: '0.00',
      grossAmount: '12000.00', payableAmount: '12000.00' }, theoreticalKg: '493.8', actualKg: '500.16', ...over,
  }) as InvoiceLine

  it('shows every server figure of a computed order line and the document figures, with no pending text', () => {
    render(<OrderKilogramPricesView currency="TRY" prices={decodeOrderKilogramPrices(computedOrder())!} />)
    const line = screen.getByTestId('sales-kilogram-line-1')
    const fact = (label: string) => {
      const term = Array.from(line.querySelectorAll('dt')).find((dt) => dt.textContent === label)
      return term?.nextElementSibling?.textContent
    }
    expect(fact(KILOGRAM_PRICE_TEXT.theoreticalKg)).toBe('1234.5')
    expect(fact(KILOGRAM_PRICE_TEXT.theoreticalPrice)).toBe(KILOGRAM_PRICE_TEXT.perKg('20.25', 'TRY'))
    expect(fact(KILOGRAM_PRICE_TEXT.invoicedQuantity)).toBe('40')
    expect(fact(KILOGRAM_PRICE_TEXT.invoicedAmount)).toBe('10000.00')
    expect(fact(KILOGRAM_PRICE_TEXT.actualKg)).toBe('500.16')
    expect(fact(KILOGRAM_PRICE_TEXT.actualPrice)).toBe(KILOGRAM_PRICE_TEXT.perKg('19.99', 'TRY'))
    expect(fact(KILOGRAM_PRICE_TEXT.kgDifference)).toBe('6.36')
    expect(fact(KILOGRAM_PRICE_TEXT.deviationPercent)).toBe(KILOGRAM_PRICE_TEXT.percent('1.29'))
    expect(screen.getByTestId('sales-kilogram-document-theoretical').textContent).toBe(KILOGRAM_PRICE_TEXT.perKg('20.25', 'TRY'))
    expect(screen.getByTestId('sales-kilogram-document-actual').textContent).toBe(KILOGRAM_PRICE_TEXT.perKg('19.99', 'TRY'))
    expect(screen.queryByTestId('sales-kilogram-prices-pending')).toBeNull()
    // The document currency note stays: the figures are in the document currency, no rate is applied (SAT-D439).
    expect(screen.getByText(KILOGRAM_PRICE_TEXT.note)).toBeTruthy()
  })

  it('shows a dash for the figures the server did not send, never a zero', () => {
    const base = computedOrder()
    const prices = decodeOrderKilogramPrices({
      ...base,
      lines: [{ ...base.lines[0], theoreticalKg: null, theoreticalPrice: { state: 'TheoreticalKgUnavailable' },
        invoicedQuantity: '0', invoicedAmount: null, actualKg: null, actualPrice: { state: 'WeighedKgUnavailable' },
        kgDifference: null, deviationPercent: null }],
    })!
    render(<OrderKilogramPricesView currency="TRY" prices={prices} />)
    const values = Array.from(screen.getByTestId('sales-kilogram-line-1').querySelectorAll('dd')).map((dd) => dd.textContent)
    expect(values).toEqual([
      KILOGRAM_PRICE_TEXT.none, KILOGRAM_PRICE_TEXT.state.TheoreticalKgUnavailable, '0', KILOGRAM_PRICE_TEXT.none,
      KILOGRAM_PRICE_TEXT.none, KILOGRAM_PRICE_TEXT.state.WeighedKgUnavailable, KILOGRAM_PRICE_TEXT.none,
      KILOGRAM_PRICE_TEXT.none,
    ])
  })

  it.each([
    'TheoreticalKgUnavailable', 'WeighedKgUnavailable', 'ZeroKg', 'AmountUnavailable', 'DocumentNotAllKilogramLines',
    'OutOfRange',
  ] as const)('tells why a price is not shown for %s, on the line and on the document', (state) => {
    const reason = KILOGRAM_PRICE_TEXT.state[state]
    expect(reason).toMatch(/\S/)
    const base = computedOrder()
    const prices = decodeOrderKilogramPrices({
      ...base, lines: [{ ...base.lines[0], theoreticalPrice: { state }, actualPrice: { state } }],
      documentTheoreticalPrice: { state }, documentActualPrice: { state },
    })!
    render(<OrderKilogramPricesView currency="TRY" prices={prices} />)
    expect(screen.getByTestId('sales-kilogram-line-1-theoretical').textContent).toBe(reason)
    expect(screen.getByTestId('sales-kilogram-line-1-actual').textContent).toBe(reason)
    expect(screen.getByTestId('sales-kilogram-document-theoretical').textContent).toBe(reason)
    expect(screen.getByTestId('sales-kilogram-document-actual').textContent).toBe(reason)
  })

  it('has one distinct reason text per unavailable state', () => {
    const reasons = (['TheoreticalKgUnavailable', 'WeighedKgUnavailable', 'ZeroKg', 'AmountUnavailable',
      'DocumentNotAllKilogramLines', 'OutOfRange'] as const).map((state) => KILOGRAM_PRICE_TEXT.state[state])
    expect(new Set(reasons).size).toBe(reasons.length)
  })

  it('shows a computed invoice block: each line’s two kg prices and the document figures', () => {
    const prices = decodeInvoiceKilogramPrices({
      state: 'Computed',
      lines: [{ lineId: LINE, theoreticalPrice: shown('20.25'), actualPrice: shown('19.99') }],
      documentTheoreticalPrice: shown('20.25'),
      documentActualPrice: { state: 'DocumentNotAllKilogramLines' },
    })!
    render(<InvoiceKilogramPricesView currency="TRY" prices={prices} lines={[invoiceLineOf()]} />)
    const line = screen.getByTestId('sales-invoice-kilogram-line-1').textContent
    expect(line).toContain(`${KILOGRAM_PRICE_TEXT.theoreticalPrice}: ${KILOGRAM_PRICE_TEXT.perKg('20.25', 'TRY')}`)
    expect(line).toContain(`${KILOGRAM_PRICE_TEXT.actualPrice}: ${KILOGRAM_PRICE_TEXT.perKg('19.99', 'TRY')}`)
    expect(screen.getByTestId('sales-invoice-kilogram-document-theoretical').textContent).toBe(KILOGRAM_PRICE_TEXT.perKg('20.25', 'TRY'))
    expect(screen.getByTestId('sales-invoice-kilogram-document-actual').textContent)
      .toBe(KILOGRAM_PRICE_TEXT.state.DocumentNotAllKilogramLines)
    expect(screen.getByTestId('sales-invoice-kilogram-info-1').textContent).toContain('493.8')
    expect(screen.queryByTestId('sales-invoice-kilogram-prices-pending')).toBeNull()
  })

  it('withholds the invoice block when a kg price line is not one of the invoice lines (never "0. satır")', () => {
    const prices = decodeInvoiceKilogramPrices({
      state: 'Computed',
      lines: [{ lineId: '99999999-9999-4999-8999-999999999999', theoreticalPrice: shown('20.25'), actualPrice: shown('19.99') }],
      documentTheoreticalPrice: shown('20.25'),
      documentActualPrice: shown('19.99'),
    })!
    render(<InvoiceKilogramPricesView currency="TRY" prices={prices} lines={[invoiceLineOf()]} />)
    expect(screen.getByTestId('sales-invoice-kilogram-prices-unmatched').textContent).toBe(KILOGRAM_PRICE_TEXT.unmatchedLine)
    expect(screen.queryByText(KILOGRAM_PRICE_TEXT.line(0))).toBeNull()
    expect(screen.queryByTestId('sales-invoice-kilogram-line-0')).toBeNull()
  })

  it('decodes a computed invoice block strictly', () => {
    const block = {
      state: 'Computed', lines: [{ lineId: LINE, theoreticalPrice: shown('20.25'), actualPrice: { state: 'ZeroKg' } }],
      documentTheoreticalPrice: shown('20.25'), documentActualPrice: { state: 'WeighedKgUnavailable' },
    }
    expect(decodeInvoiceKilogramPrices(block)?.lines?.[0]?.actualPrice).toEqual({ state: 'ZeroKg', value: null })
    expect(decodeInvoiceKilogramPrices({ ...block, lines: null })).toBeNull()
    expect(decodeInvoiceKilogramPrices({ ...block, documentActualPrice: undefined })).toBeNull()
    expect(decodeInvoiceKilogramPrices({ ...block, lines: [{ ...block.lines[0], extra: 1 }] })).toBeNull()
    expect(decodeInvoiceKilogramPrices({ ...block, lines: [{ ...block.lines[0], theoreticalPrice: { state: 'Shown', value: 20.25 } }] }))
      .toBeNull()
    expect(decodeInvoiceKilogramPrices({ ...block, currencyId: CURRENCY })).toBeNull()
  })
})
