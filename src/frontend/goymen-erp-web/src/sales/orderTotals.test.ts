/**
 * API-03 / SAT-D11: the totals decoder reads `SalesOrderDraftTotalsResponse` exactly, only as the totals of the detail on
 * screen, and refuses every malformed or contradictory answer (null → the card shows no amount). The OpenAPI pin holds the
 * decoder's member lists to the canonical document the backend CI regenerates.
 */

import { describe, expect, it } from 'vitest'
import detailResponseRaw from './examples/detail-response.json?raw'
import totalsResponseRaw from './examples/totals-response.json?raw'
import { decodeDraftDetail, type DraftDetail } from './contract'
import { decodeOrderTotals, salesDraftTotalsPath } from './orderTotals'

type Body = Record<string, any>

const DRAFT = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
const LINE_1 = 'aaaaaaaa-1111-4111-8111-000000000001'
const LINE_2 = 'aaaaaaaa-1111-4111-8111-000000000002'
const UNIT = 'bbbbbbbb-1111-4111-8111-000000000002'
const CURRENCY = 'cccccccc-1111-4111-8111-000000000003'

/** The pinned detail example at version 4, in the totals example's currency, with the example's two lines (1.25 priced, 2 unpriced). */
function detailBody(): Body {
  const body = JSON.parse(detailResponseRaw) as Body
  body.draft = { ...body.draft, version: '4', currencyId: CURRENCY, lineCount: 2 }
  const base = { ...body.lines[0], orderUnitId: UNIT, requestedDeliverySlices: [], requestedDeliveryDate: null }
  body.lines = [
    { ...base, lineId: LINE_1, lineNo: 1, quantity: '1.25', unitPrice: '12.50', priceUnitId: UNIT, discountPercent: '10' },
    { ...base, lineId: LINE_2, lineNo: 2, quantity: '2' },
  ]
  return body
}
const detail = decodeDraftDetail(detailBody(), DRAFT) as DraftDetail

const totals = (): Body => JSON.parse(totalsResponseRaw) as Body

/** A complete, tax-computed answer (SENTETIK rates; no VAT master exists on main). */
function taxed(): Body {
  const body = totals()
  body.taxState = 'Computed'
  body.lines[1] = {
    ...body.lines[0], lineId: LINE_2, lineNo: 2, quantity: '2', netUnitPrice: '100', netAmount: '200.00',
  }
  for (const [index, line] of body.lines.entries()) {
    Object.assign(line, index === 0
      ? { taxState: 'Computed', vatPercent: '20', vatAmount: '2.87', withholdingShare: null, withheldVatAmount: '0.00', grossAmount: '17.21', payableAmount: '17.21' }
      : { taxState: 'Computed', vatPercent: '20', vatAmount: '40.00', withholdingShare: '0.5', withheldVatAmount: '20.00', grossAmount: '240.00', payableAmount: '220.00' })
  }
  body.totals = {
    state: 'Computed', unpricedLineCount: 0, priceUnitNotOrderUnitLineCount: 0, netAmount: '214.34', taxState: 'Computed',
    vatAmount: '42.87', withheldVatAmount: '20.00', grossAmount: '257.21', payableAmount: '237.21',
  }
  return body
}

describe('decodeOrderTotals', () => {
  it('builds the route under the draft detail', () => {
    expect(salesDraftTotalsPath('a/b')).toBe('/sales/drafts/a%2Fb/totals')
  })

  it('reads the server’s amounts verbatim: a computed line, an unpriced line, an incomplete document, the tax without a source', () => {
    expect(detail).not.toBeNull()
    const decoded = decodeOrderTotals(totals(), detail)
    expect(decoded).toMatchObject({
      draftVersion: '4', revision: 'Rev.0', currencyId: CURRENCY, amountScale: 2, taxState: 'NoSource',
      totals: { state: 'Incomplete', unpricedLineCount: 1, priceUnitNotOrderUnitLineCount: 0, netAmount: null, taxState: 'NotComputed', tax: null },
    })
    expect(decoded?.lines.map((line) => [line.lineNo, line.state, line.netUnitPrice, line.netAmount, line.taxState, line.tax])).toEqual([
      [1, 'Computed', '11.475', '14.34', 'NoSource', null],
      [2, 'Unpriced', null, null, 'NotComputed', null],
    ])
  })

  it('reads a tax-computed answer with VAT, withholding, gross and payable per line and on the document', () => {
    const decoded = decodeOrderTotals(taxed(), detail)
    expect(decoded?.totals).toEqual({
      state: 'Computed', unpricedLineCount: 0, priceUnitNotOrderUnitLineCount: 0, netAmount: '214.34', taxState: 'Computed',
      tax: { vatAmount: '42.87', withheldVatAmount: '20.00', grossAmount: '257.21', payableAmount: '237.21' },
    })
    expect(decoded?.lines[1]).toMatchObject({ vatPercent: '20', withholdingShare: '0.5', tax: { withheldVatAmount: '20.00', payableAmount: '220.00' } })
  })

  it('matches the lines by identity in any case and order and shows them in the detail’s order', () => {
    const body = totals()
    body.lines.reverse()
    body.lines[1].lineId = LINE_1.toUpperCase()
    body.currencyId = CURRENCY.toUpperCase()
    const decoded = decodeOrderTotals(body, detail)
    expect(decoded?.lines.map((line) => line.lineId)).toEqual([LINE_1, LINE_2])
    expect(decoded?.currencyId).toBe(CURRENCY)
  })

  const refusals: readonly [string, (body: Body) => void][] = [
    ['another version', (b) => { b.draftVersion = '5' }],
    ['another revision', (b) => { b.revision = 'Rev.1' }],
    ['another currency', (b) => { b.currencyId = 'dddddddd-1111-4111-8111-000000000004' }],
    ['an extra member', (b) => { b.extra = 1 }],
    ['a missing nullable member', (b) => { delete b.lines[1].vatAmount }],
    ['a number for an amount', (b) => { b.lines[0].netAmount = 14.34 }],
    ['an amount not padded to the scale', (b) => { b.lines[0].netAmount = '14.3' }],
    ['a negative amount', (b) => { b.lines[0].netAmount = '-14.34' }],
    ['an unknown line state', (b) => { b.lines[0].state = 'Estimated' }],
    ['an unknown tax state', (b) => { b.taxState = 'Zero' }],
    ['a NotComputed top-level tax', (b) => { b.taxState = 'NotComputed' }],
    ['a missing line', (b) => { b.lines.pop() }],
    ['a repeated line', (b) => { b.lines[1] = { ...b.lines[0] } }],
    ['a renumbered line', (b) => { b.lines[0].lineNo = 3 }],
    ['a line in another unit', (b) => { b.lines[0].orderUnitId = 'eeeeeeee-1111-4111-8111-000000000005' }],
    ['a line of another quantity', (b) => { b.lines[0].quantity = '1.5' }],
    ['a computed line without an amount', (b) => { b.lines[0].netAmount = null }],
    ['an unpriced line with an amount', (b) => { b.lines[1].netAmount = '0.00' }],
    ['an unpriced line whose tax is not NotComputed', (b) => { b.lines[1].taxState = 'NoSource' }],
    ['a zero VAT standing in for no source', (b) => { b.lines[0].vatAmount = '0.00' }],
    ['a computed line tax without a VAT master', (b) => {
      Object.assign(b.lines[0], { taxState: 'Computed', vatPercent: '20', vatAmount: '2.87', withheldVatAmount: '0.00', grossAmount: '17.21', payableAmount: '17.21' })
    }],
    ['an incomplete document with a partial sum', (b) => { b.totals.netAmount = '14.34' }],
    ['an incomplete document called computed', (b) => { b.totals.state = 'Computed' }],
    ['a wrong unpriced count', (b) => { b.totals.unpricedLineCount = 0 }],
    ['an amount scale out of range', (b) => { b.amountScale = 29 }],
  ]
  it.each(refusals)('refuses %s', (_, mutate) => {
    const body = totals()
    mutate(body)
    expect(decodeOrderTotals(body, detail)).toBeNull()
  })

  const taxedRefusals: readonly [string, (body: Body) => void][] = [
    ['a document VAT that is null while every line is taxed', (b) => { b.totals.vatAmount = null }],
    ['a document tax NoSource while every line is taxed', (b) => {
      Object.assign(b.totals, { taxState: 'NoSource', vatAmount: null, withheldVatAmount: null, grossAmount: null, payableAmount: null })
    }],
    ['a taxed line without its VAT rate', (b) => { b.lines[0].vatPercent = null }],
    ['a taxed line with a null payable', (b) => { b.lines[1].payableAmount = null }],
  ]
  it.each(taxedRefusals)('refuses %s', (_, mutate) => {
    const body = taxed()
    expect(decodeOrderTotals(body, detail)).not.toBeNull()
    mutate(body)
    expect(decodeOrderTotals(body, detail)).toBeNull()
  })
})

const nodeProcess = (globalThis as unknown as {
  readonly process: { cwd(): string; getBuiltinModule(name: string): unknown }
}).process
const nodeFs = nodeProcess.getBuiltinModule('node:fs') as { readFileSync(path: string, encoding: 'utf8'): string }
const nodePath = nodeProcess.getBuiltinModule('node:path') as { resolve(...segments: string[]): string }

interface Schema { readonly required?: readonly string[]; readonly properties?: Record<string, Record<string, any>> }

describe('API-03 OpenAPI pin', () => {
  const document = JSON.parse(nodeFs.readFileSync(
    nodePath.resolve(nodeProcess.cwd(), '../../../contracts/openapi/goymen-erp-v1.json'), 'utf8')) as {
    readonly paths: Record<string, Record<string, { readonly operationId?: string; readonly responses?: Record<string, any> }>>
    readonly components: { readonly schemas: Record<string, Schema> }
  }
  const schema = (name: string) => document.components.schemas[name]

  it('declares the read this card calls, with its 404 and 503', () => {
    const get = document.paths['/api/v1/sales/drafts/{id}/totals']?.get
    expect(get?.operationId).toBe('GetSalesOrderDraftTotalsV1')
    expect(get?.responses?.['200']?.content?.['application/json']?.schema?.$ref).toBe('#/components/schemas/SalesOrderDraftTotalsResponse')
    expect(Object.keys(get?.responses ?? {})).toEqual(expect.arrayContaining(['200', '404', '503']))
  })

  it.each([
    ['SalesOrderDraftTotalsResponse', ['amountScale', 'currencyId', 'draftVersion', 'lines', 'revision', 'taxState', 'totals']],
    ['SalesDraftDocumentTotalsV1', ['grossAmount', 'netAmount', 'payableAmount', 'priceUnitNotOrderUnitLineCount', 'state', 'taxState',
      'unpricedLineCount', 'vatAmount', 'withheldVatAmount']],
    ['SalesDraftLineTotalV1', ['grossAmount', 'lineId', 'lineNo', 'netAmount', 'netUnitPrice', 'orderUnitId', 'payableAmount', 'quantity',
      'state', 'taxState', 'vatAmount', 'vatPercent', 'withheldVatAmount', 'withholdingShare']],
  ])('%s: the decoder reads exactly the members the contract declares, all required', (name, keys) => {
    expect([...(schema(name).required ?? [])].sort()).toEqual([...keys].sort())
    expect(Object.keys(schema(name).properties ?? {}).sort()).toEqual([...keys].sort())
  })
})
