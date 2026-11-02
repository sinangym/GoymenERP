/**
 * MOD-03 → Sales: the tax read and options decoders read `SalesOrderDraftTaxResponse` / `SalesTaxOptionsResponse` exactly and only
 * for the detail on screen, and the builders of the order forms keep the tax choices' shape (only Settings values, a reason for
 * a line change, the document's number and date together). The OpenAPI pin holds the member lists to the canonical document.
 * Every code and rate here is SENTETIK.
 */

import { describe, expect, it } from 'vitest'
import detailResponseRaw from './examples/detail-response.json?raw'
import { buildCommercialRevisionRequest, commercialRowsFromDetail } from './commercialRevision'
import { decodeDraftDetail, type DraftDetail } from './contract'
import { buildHeaderRevisionRequest, decodeDraftHeader, headerFormValues, type DraftHeader } from './headerRevision'
import {
  buildBuyerTax,
  buildLineTaxChoice,
  decodeOrderTax,
  decodeTaxOptions,
  KEEP_LINE_TAX,
  readOptions,
  salesDraftTaxPath,
  salesTaxOptionsPath,
  taxCardOutcome,
  type BuyerSituationOption,
} from './orderTax'

type Body = Record<string, any>

const DRAFT = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
const LINE_1 = 'aaaaaaaa-1111-4111-8111-000000000001'
const LINE_2 = 'aaaaaaaa-1111-4111-8111-000000000002'
const UNIT = 'bbbbbbbb-1111-4111-8111-000000000002'

function detailBody(): Body {
  const body = JSON.parse(detailResponseRaw) as Body
  body.draft = { ...body.draft, version: '4', lineCount: 2 }
  const base = { ...body.lines[0], orderUnitId: UNIT, requestedDeliverySlices: [], requestedDeliveryDate: null }
  body.lines = [
    { ...base, lineId: LINE_1, lineNo: 1, quantity: '1.25', unitPrice: '12.50', priceUnitId: UNIT },
    { ...base, lineId: LINE_2, lineNo: 2, quantity: '2' },
  ]
  return body
}
const detail = decodeDraftDetail(detailBody(), DRAFT) as DraftDetail

const resolvedLine = (lineId: string, lineNo: number): Body => ({
  lineId, lineNo, hasSnapshot: true, origin: 'Default', resolvedOn: '2026-11-02', buyerSituation: 'Normal', itemType: 'Steel',
  state: 'Resolved', reason: null, vatState: 'Resolved', vatPercent: '20', exemptionCodeState: 'NotRequired', exemptionCode: null,
  exemptionCodeReason: null, withholdingState: 'Resolved', withholdingPercent: '50', withholdingCodeState: 'Unresolved',
  withholdingCode: null, withholdingCodeReason: 'withholding_code_missing', buyerAdjustment: null, buyerDocumentRequired: false,
  settingsSource: 'Record', settingsRecordId: 'dddddddd-1111-4111-8111-000000000009', settingsRecordVersion: 1,
  buyerSituationListVersion: 0, rateState: 'Computed', unavailableReason: null, invoiceBlockers: ['withholding_code_missing'],
  override: null,
})

const missingLine = (lineId: string, lineNo: number): Body => ({
  lineId, lineNo, hasSnapshot: false, origin: null, resolvedOn: null, buyerSituation: null, itemType: null, state: null, reason: null,
  vatState: null, vatPercent: null, exemptionCodeState: null, exemptionCode: null, exemptionCodeReason: null, withholdingState: null,
  withholdingPercent: null, withholdingCodeState: null, withholdingCode: null, withholdingCodeReason: null, buyerAdjustment: null,
  buyerDocumentRequired: false, settingsSource: null, settingsRecordId: null, settingsRecordVersion: null, buyerSituationListVersion: null,
  rateState: 'NoSource', unavailableReason: 'tax_snapshot_missing', invoiceBlockers: ['tax_snapshot_missing'], override: null,
})

const taxBody = (): Body => ({
  draftVersion: detail.draft.version,
  revision: detail.draft.revision,
  businessDate: '2026-11-02',
  buyerTax: { situation: 'Normal' },
  lines: [resolvedLine(LINE_1, 1), missingLine(LINE_2, 2)],
})

const SITUATIONS: readonly BuyerSituationOption[] = [
  { code: 'ExportRegistered', name: 'İhraç kayıtlı satış', withholdingApplies: false, vatMode: 'ZeroWithExemptionCode', documentRequired: true },
  { code: 'Normal', name: 'Normal alıcı', withholdingApplies: true, vatMode: 'Normal', documentRequired: false },
]

const optionsBody = (): Body => ({
  on: '2026-11-02', buyerSituationsState: 'Resolved', buyerSituationsReason: null, buyerSituationListVersion: 0,
  buyerSituations: SITUATIONS.map((s) => ({ ...s })), exemptionCodesState: 'Unresolved', exemptionCodesReason: 'code_list_missing',
  exemptionCodes: [], itemTypes: ['Steel', 'Scrap', 'Service'],
  vatRatesState: 'Resolved', vatRatesReason: null, vatRates: [{ code: '10', name: 'SENTETIK %10', percent: '10', taxCode: null }],
  withholdingRatiosState: 'Unresolved', withholdingRatiosReason: 'rate_list_missing', withholdingRatios: [],
})

describe('decodeOrderTax', () => {
  it('builds the routes under the draft and the Sales base', () => {
    expect(salesDraftTaxPath('a/b')).toBe('/sales/drafts/a%2Fb/tax')
    expect(salesTaxOptionsPath('2026-11-02')).toBe('/sales/tax-options?on=2026-11-02')
  })

  it('reads the buyer tax and every line verbatim, a line without a snapshot named as such', () => {
    const decoded = decodeOrderTax(taxBody(), detail)
    expect(decoded?.buyerTax).toEqual({ situation: 'Normal', exemptionCode: null, documentNo: null, documentDate: null })
    expect(decoded?.lines.map((line) => [line.lineNo, line.origin, line.vatPercent, line.rateState, line.unavailableReason])).toEqual([
      [1, 'Default', '20', 'Computed', null],
      [2, null, null, 'NoSource', 'tax_snapshot_missing'],
    ])
  })

  it('reads a reasoned line change with who and why, and an order written before the tax wiring (no buyer tax)', () => {
    const body = taxBody()
    body.buyerTax = null
    body.lines[0] = { ...body.lines[0], origin: 'Override', override: { itemType: null, buyerSituation: 'Normal', exemptionCode: null,
      reason: 'SENTETIK gerekçe', changedBy: 'synthetic-actor', changedAtUtc: '2026-11-02T09:00:00Z', vatRateCode: '10',
      withholdingRatioCode: null } }
    const decoded = decodeOrderTax(body, detail)
    expect(decoded?.buyerTax).toBeNull()
    expect(decoded?.lines[0].override?.reason).toBe('SENTETIK gerekçe')
    expect(decoded?.lines[0].override?.vatRateCode).toBe('10')
  })

  const refusals: readonly [string, (body: Body) => void][] = [
    ['another version', (b) => { b.draftVersion = '5' }],
    ['another revision', (b) => { b.revision = 'Rev.9' }],
    ['an extra member', (b) => { b.extra = 1 }],
    ['a missing line', (b) => { b.lines.pop() }],
    ['a renumbered line', (b) => { b.lines[0].lineNo = 3 }],
    ['a number for a rate', (b) => { b.lines[0].vatPercent = 20 }],
    ['a computed rate with a reason', (b) => { b.lines[0].unavailableReason = 'tax_unresolved' }],
    ['a NoSource rate without a reason', (b) => { b.lines[1].unavailableReason = null }],
    ['an override origin without its facts', (b) => { b.lines[0].origin = 'Override' }],
    ['a missing snapshot that still carries a part', (b) => { b.lines[1].state = 'Resolved' }],
    ['a buyer document without its date', (b) => { b.buyerTax = { situation: 'ExportRegistered', documentNo: 'X-1' } }],
    ['an unknown buyer tax member', (b) => { b.buyerTax = { situation: 'Normal', rate: '20' } }],
  ]
  it.each(refusals)('refuses %s', (_, mutate) => {
    const body = taxBody()
    mutate(body)
    expect(decodeOrderTax(body, detail)).toBeNull()
  })

  it('decides what the card shows: no lines → neutral; a withheld price → forbidden without a read; else the read', () => {
    const ok = { type: 'ok-single' as const, data: decodeOrderTax(taxBody(), detail)! }
    expect(taxCardOutcome({ ...detail, lines: [] }, ok)).toEqual({ type: 'no-lines' })
    expect(taxCardOutcome({ ...detail, lines: detail.lines.map((line) => ({ ...line, priceDisclosure: 'Withheld' as const })) }, ok))
      .toEqual({ type: 'http', status: 403 })
    expect(taxCardOutcome(detail, ok)).toBe(ok)
  })
})

describe('decodeTaxOptions', () => {
  it('reads the Settings lists of the date, an empty list with its reason', () => {
    const decoded = decodeTaxOptions(optionsBody(), '2026-11-02')
    expect(decoded?.buyerSituations.map((s) => s.code)).toEqual(['ExportRegistered', 'Normal'])
    expect([decoded?.exemptionCodesState, decoded?.exemptionCodesReason, decoded?.exemptionCodes]).toEqual(['Unresolved', 'code_list_missing', []])
    expect(readOptions({ type: 'ok-single', data: decoded! }).notice).toBeNull()
    expect(readOptions(undefined).options).toBeNull()
    // SAT-D181: the rate lists — a resolved one with its exact percents, an empty one with its Settings reason.
    expect(decoded?.vatRates).toEqual([{ code: '10', name: 'SENTETIK %10', percent: '10', taxCode: null }])
    expect([decoded?.withholdingRatiosState, decoded?.withholdingRatiosReason, decoded?.withholdingRatios]).toEqual(['Unresolved', 'rate_list_missing', []])
    expect(readOptions({ type: 'http', status: 503 }).options).toBeNull()
  })

  it.each([
    ['another date', (b: Body) => { b.on = '2026-11-03' }],
    ['a resolved list with a reason', (b: Body) => { b.buyerSituationsReason = 'x' }],
    ['an unresolved list with entries', (b: Body) => { b.exemptionCodes = [{ code: '701', name: 'x' }] }],
    ['an extra entry member', (b: Body) => { b.buyerSituations[0].rate = '0' }],
    ['a number for a listed percent', (b: Body) => { b.vatRates[0].percent = 10 }],
    ['an unresolved rate list with entries', (b: Body) => { b.withholdingRatios = [{ code: '5/10', name: 'x', percent: '50', taxCode: null }] }],
  ])('refuses %s', (_, mutate) => {
    const body = optionsBody()
    mutate(body)
    expect(decodeTaxOptions(body, '2026-11-02')).toBeNull()
  })
})

describe('tax builders', () => {
  it('builds the buyer tax: the code only for a KDV %0 situation, the document only where required and complete', () => {
    const exportRegistered = SITUATIONS[0]
    expect(buildBuyerTax({ situation: 'Normal', exemptionCode: '701', documentNo: 'X', documentDate: '2026-11-02' }, SITUATIONS[1]))
      .toEqual({ ok: true, buyerTax: { situation: 'Normal' } })
    expect(buildBuyerTax({ situation: 'ExportRegistered', exemptionCode: ' 701 ', documentNo: 'IK-1', documentDate: '2026-11-02' }, exportRegistered))
      .toEqual({ ok: true, buyerTax: { situation: 'ExportRegistered', exemptionCode: '701', documentNo: 'IK-1', documentDate: '2026-11-02' } })
    expect(buildBuyerTax({ situation: 'ExportRegistered', exemptionCode: '', documentNo: 'IK-1', documentDate: '' }, exportRegistered))
      .toEqual({ ok: false, error: 'document-incomplete' })
    expect(buildBuyerTax({ situation: ' ', exemptionCode: '', documentNo: '', documentDate: '' }, undefined))
      .toEqual({ ok: false, error: 'situation-required' })
  })

  it('builds a line tax choice: keep is omitted, default carries nothing, a change needs a situation and a reason', () => {
    expect(buildLineTaxChoice(KEEP_LINE_TAX)).toEqual({ ok: true, tax: null })
    expect(buildLineTaxChoice({ ...KEEP_LINE_TAX, mode: 'default' })).toEqual({ ok: true, tax: { mode: 'Default' } })
    expect(buildLineTaxChoice({ ...KEEP_LINE_TAX, mode: 'override', buyerSituation: 'Normal' }))
      .toEqual({ ok: false, error: 'tax-reason-required' })
    expect(buildLineTaxChoice({ ...KEEP_LINE_TAX, mode: 'override', reason: 'r' })).toEqual({ ok: false, error: 'tax-situation-required' })
    expect(buildLineTaxChoice({ mode: 'override', buyerSituation: 'Normal', itemType: 'Scrap', exemptionCode: '', reason: ' r ' }))
      .toEqual({ ok: true, tax: { mode: 'Override', buyerSituation: 'Normal', reason: 'r', itemType: 'Scrap' } })
    // SAT-D181: a listed VAT rate / withholding ratio travels by its code only (the percent is the server's list).
    expect(buildLineTaxChoice({ ...KEEP_LINE_TAX, mode: 'override', buyerSituation: 'Normal', reason: 'r', vatRateCode: '10', withholdingRatioCode: '2/10' }))
      .toEqual({ ok: true, tax: { mode: 'Override', buyerSituation: 'Normal', reason: 'r', vatRateCode: '10', withholdingRatioCode: '2/10' } })
  })

  it('puts the line tax choice on the commercial revision only when the person chose one', () => {
    const rows = commercialRowsFromDetail(detail)
    const kept = buildCommercialRevisionRequest('4', rows)
    expect(kept.ok && kept.payload.lines.every((line) => !('tax' in line))).toBe(true)
    const changed = buildCommercialRevisionRequest('4', rows.map((row, index) => index === 0
      ? { ...row, tax: { mode: 'override' as const, buyerSituation: 'Normal', itemType: '', exemptionCode: '', reason: 'SENTETIK' } }
      : row))
    expect(changed.ok && changed.payload.lines[0].tax).toEqual({ mode: 'Override', buyerSituation: 'Normal', reason: 'SENTETIK' })
    expect(buildCommercialRevisionRequest('4', rows.map((row) => ({ ...row, tax: { ...KEEP_LINE_TAX, mode: 'override' as const } }))))
      .toEqual({ ok: false, lineNo: 1, reason: 'tax-situation-required' })
  })

  it('sends a header revision buyer tax only when it changes the order’s', () => {
    const ID = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`
    const header = decodeDraftHeader({
      draftVersion: '4', revision: 'Rev.1', partyId: ID(1), companyAccountId: ID(2), currencyId: ID(3), deliveryAddress: null,
      contact: null, salesOwnerUserId: null, salesOwnerDisplayName: null, dueTermsDisclosure: 'Visible', dueTerms: null,
      customerOrCurrencyRevisionPending: false,
    }) as DraftHeader
    const values = { ...headerFormValues(header, () => 'k'), reason: 'SENTETIK' }
    const current = { situation: 'Normal', exemptionCode: null, documentNo: null, documentDate: null }
    const same = { situation: 'Normal', exemptionCode: '', documentNo: '', documentDate: '' }
    expect(buildHeaderRevisionRequest(header, values, { input: same, current, situation: SITUATIONS[1] }))
      .toEqual({ ok: false, error: { kind: 'no-change' } })
    // An order written before the tax wiring starts at "Normal": keeping it is no change either.
    expect(buildHeaderRevisionRequest(header, values, { input: same, current: null, situation: SITUATIONS[1] }))
      .toEqual({ ok: false, error: { kind: 'no-change' } })
    const changed = buildHeaderRevisionRequest(header, values, {
      input: { situation: 'ExportRegistered', exemptionCode: '', documentNo: 'IK-1', documentDate: '2026-11-02' }, current, situation: SITUATIONS[0],
    })
    expect(changed.ok && changed.payload.buyerTax).toEqual({ situation: 'ExportRegistered', documentNo: 'IK-1', documentDate: '2026-11-02' })
    expect(buildHeaderRevisionRequest(header, values, {
      input: { situation: 'ExportRegistered', exemptionCode: '', documentNo: 'IK-1', documentDate: '' }, current, situation: SITUATIONS[0],
    })).toEqual({ ok: false, error: { kind: 'buyer-tax', error: 'document-incomplete' } })
  })
})

const nodeProcess = (globalThis as unknown as {
  readonly process: { cwd(): string; getBuiltinModule(name: string): unknown }
}).process
const nodeFs = nodeProcess.getBuiltinModule('node:fs') as { readFileSync(path: string, encoding: 'utf8'): string }
const nodePath = nodeProcess.getBuiltinModule('node:path') as { resolve(...segments: string[]): string }

describe('MOD-03 → Sales OpenAPI pin', () => {
  const document = JSON.parse(nodeFs.readFileSync(
    nodePath.resolve(nodeProcess.cwd(), '../../../contracts/openapi/goymen-erp-v1.json'), 'utf8')) as {
    readonly paths: Record<string, Record<string, { readonly operationId?: string; readonly responses?: Record<string, any> }>>
    readonly components: { readonly schemas: Record<string, { readonly required?: readonly string[]; readonly properties?: Record<string, unknown> }> }
  }
  const schema = (name: string) => document.components.schemas[name]

  it('declares the two reads the screens call', () => {
    expect(document.paths['/api/v1/sales/drafts/{id}/tax']?.get?.operationId).toBe('GetSalesOrderDraftTaxV1')
    expect(document.paths['/api/v1/sales/tax-options']?.get?.operationId).toBe('GetSalesTaxOptionsV1')
  })

  it.each([
    ['SalesOrderDraftTaxResponse', ['businessDate', 'buyerTax', 'draftVersion', 'lines', 'revision'], []],
    ['SalesDraftBuyerTaxV1', ['situation'], ['documentDate', 'documentNo', 'exemptionCode']],
    ['SalesDraftLineTaxOverrideViewV1', ['buyerSituation', 'changedAtUtc', 'changedBy', 'exemptionCode', 'itemType', 'reason', 'vatRateCode',
      'withholdingRatioCode'], []],
    ['SalesTaxRateOptionV1', ['code', 'name', 'percent', 'taxCode'], []],
    ['SalesTaxBuyerSituationOptionV1', ['code', 'documentRequired', 'name', 'vatMode', 'withholdingApplies'], []],
    ['SalesTaxCodeOptionV1', ['code', 'name'], []],
    ['SalesTaxOptionsResponse', ['buyerSituationListVersion', 'buyerSituations', 'buyerSituationsReason', 'buyerSituationsState', 'exemptionCodes',
      'exemptionCodesReason', 'exemptionCodesState', 'itemTypes', 'on', 'vatRates', 'vatRatesReason', 'vatRatesState', 'withholdingRatios',
      'withholdingRatiosReason', 'withholdingRatiosState'], []],
    ['SalesDraftLineTaxChoiceV1', ['mode'], ['buyerSituation', 'exemptionCode', 'itemType', 'reason', 'vatRateCode', 'withholdingRatioCode']],
  ])('%s: the decoder and builders use exactly the members the contract declares', (name, required, optional) => {
    expect([...(schema(name).required ?? [])].sort()).toEqual([...required].sort())
    expect(Object.keys(schema(name).properties ?? {}).sort()).toEqual([...required, ...optional].sort())
  })

  it('SalesDraftLineTaxV1: every member the decoder reads is required', () => {
    const line = schema('SalesDraftLineTaxV1')
    expect([...(line.required ?? [])].sort()).toEqual(Object.keys(line.properties ?? {}).sort())
    expect(Object.keys(line.properties ?? {})).toHaveLength(29)
  })
})
