import { describe, expect, it } from 'vitest'
import {
  POOL_FIELD,
  emptyPoolForm,
  lineFromQuote,
  lineFromRequest,
  parseOptionalDecimal,
  poolFieldForServerPath,
  poolLineFieldId,
  poolLineRowId,
  validateQuoteForm,
  validateQuoteRevision,
  validateRequestForm,
  withProduct,
  type PoolFormValues,
} from './poolForm'
import { decodeQuoteDetail, decodeRequestDetail } from './poolContract'
import { POOL_TEXT } from './poolText'
import { OPTIONS_A } from './testHarness'
import { ACCOUNT, CURRENCY, KG, PARTY, PRODUCT, QUOTE_ID, REQUEST_ID, quoteDetail, requestDetail } from './poolFixtures'

const LINE = '0f0f0f0f-0000-4000-8000-000000000001'

function filled(): PoolFormValues {
  const base = emptyPoolForm(LINE)
  return {
    ...base,
    partyId: PARTY,
    companyAccountId: ACCOUNT,
    businessDate: '2026-10-27',
    currencyId: CURRENCY,
    lines: [{ ...withProduct(base.lines[0], OPTIONS_A.products[0]), orderUnitId: KG, quantityText: '1,5' }],
  }
}

describe('request form', () => {
  it('builds the create body: text quantities, nulls for the unknown, no price', () => {
    const result = validateRequestForm(filled(), OPTIONS_A.accounts)
    expect(result).toEqual({
      ok: true,
      payload: {
        partyId: PARTY,
        companyAccountId: ACCOUNT,
        businessDate: '2026-10-27',
        lines: [{ lineId: LINE, productId: PRODUCT, expectedProductVersion: '7', orderUnitId: KG, quantity: '1.5', requestedDeliveryDate: null, note: null }],
        requestedDeliveryDate: null,
        note: null,
      },
    })
  })

  it('refuses an account the owner does not offer, a missing date and an ambiguous quantity', () => {
    const values = { ...filled(), companyAccountId: '55555555-5555-5555-5555-555555555599', businessDate: '' }
    const result = validateRequestForm({ ...values, lines: [{ ...values.lines[0], quantityText: '1.000' }] }, OPTIONS_A.accounts)
    expect(result.ok).toBe(false)
    if (result.ok) return
    expect(result.errors[POOL_FIELD.customer]).toBe(POOL_TEXT.validation.customerNotOffered)
    expect(result.errors[POOL_FIELD.businessDate]).toBe(POOL_TEXT.validation.businessDateRequired)
    expect(result.errors[poolLineFieldId(LINE, 'quantity')]).toBe(POOL_TEXT.validation.quantityAmbiguous)
  })
})

describe('quote form', () => {
  it('sends a price only with its unit, and a discount only beside a price', () => {
    const values = filled()
    const priced = { ...values, lines: [{ ...values.lines[0], unitPriceText: '42,50', priceUnitId: '' }] }
    const noUnit = validateQuoteForm(priced, OPTIONS_A.accounts, OPTIONS_A.currencies, [])
    expect(noUnit.ok === false && noUnit.errors[poolLineFieldId(LINE, 'priceUnitId')]).toBe(POOL_TEXT.validation.priceUnitRequired)
    const discountOnly = { ...values, lines: [{ ...values.lines[0], discountText: '5' }] }
    const refused = validateQuoteForm(discountOnly, OPTIONS_A.accounts, OPTIONS_A.currencies, [])
    expect(refused.ok === false && refused.errors[poolLineFieldId(LINE, 'discount')]).toBe(POOL_TEXT.validation.discountWithoutPrice)
    const ok = validateQuoteForm({ ...values, lines: [{ ...values.lines[0], unitPriceText: '42,50', priceUnitId: KG, discountText: '0' }] },
      OPTIONS_A.accounts, OPTIONS_A.currencies, [REQUEST_ID])
    expect(ok.ok && ok.payload.lines[0]).toMatchObject({ unitPrice: '42.5', priceUnitId: KG, discountPercent: '0' })
    expect(ok.ok && ok.payload.requestIds).toEqual([REQUEST_ID])
  })

  it('sends null requestIds when the quote covers none, and refuses a validity before the date', () => {
    const none = validateQuoteForm(filled(), OPTIONS_A.accounts, OPTIONS_A.currencies, [])
    expect(none.ok && none.payload.requestIds).toBeNull()
    expect(none.ok && none.payload.validUntil).toBeNull()
    const early = validateQuoteForm({ ...filled(), validUntil: '2026-10-26' }, OPTIONS_A.accounts, OPTIONS_A.currencies, [])
    expect(early.ok === false && early.errors[POOL_FIELD.validUntil]).toBe(POOL_TEXT.validation.validUntilBeforeDate)
  })

  it('requires an offered currency', () => {
    const result = validateQuoteForm({ ...filled(), currencyId: '' }, OPTIONS_A.accounts, OPTIONS_A.currencies, [])
    expect(result.ok === false && result.errors[POOL_FIELD.currency]).toBe(POOL_TEXT.validation.currencyRequired)
  })

  it('copies request lines as fixed rows and keeps quote lines under their own ids for a revision', () => {
    const request = decodeRequestDetail(requestDetail(), REQUEST_ID)!
    const copied = lineFromRequest(LINE, request.lines[0])
    expect(copied).toMatchObject({ fixedProduct: true, productVersion: '7', orderUnitId: KG, quantityText: '1500', requestedDeliveryDate: '2026-11-01' })
    const quote = decodeQuoteDetail(quoteDetail(), QUOTE_ID)!
    const values: PoolFormValues = { ...emptyPoolForm(LINE), businessDate: '2026-10-22', lines: quote.lines.map(lineFromQuote) }
    const revision = validateQuoteRevision(values, '3')
    expect(revision.ok && revision.payload).toMatchObject({
      expectedVersion: '3',
      lines: [{ lineId: quote.lines[0].lineId, unitPrice: '42.5', priceUnitId: KG, quantity: '1500' }],
    })
  })

  it('keeps zero as a value and empty as unknown', () => {
    expect(parseOptionalDecimal('')).toEqual({ ok: true, value: null })
    expect(parseOptionalDecimal('0,0')).toEqual({ ok: true, value: '0' })
    expect(parseOptionalDecimal('1.000')).toEqual({ ok: false, error: 'ambiguous-separator' })
  })
})

describe('server field paths', () => {
  it('resolves line indexes against the SENT lines, a whole-line rule onto the row, requests onto the requests', () => {
    const sent = [{ lineId: LINE }]
    expect(poolFieldForServerPath('lines[0].unitPrice', sent)).toBe(poolLineFieldId(LINE, 'unitPrice'))
    expect(poolFieldForServerPath('lines[0].discountPercent', sent)).toBe(poolLineFieldId(LINE, 'discount'))
    expect(poolFieldForServerPath('lines[0]', sent)).toBe(poolLineRowId(LINE))
    expect(poolFieldForServerPath('lines[3].quantity', sent)).toBeNull()
    expect(poolFieldForServerPath('requestIds[1]', sent)).toBe(POOL_FIELD.requests)
    expect(poolFieldForServerPath('quote', sent)).toBeNull()
  })
})
