/**
 * UI-80 wire decoders against API-41 / API-42: the pinned examples fit the canonical OpenAPI schemas the backend CI regenerates
 * (`contracts/openapi/goymen-erp-v1.json`), the client calls only declared routes with declared query members, and the decoders
 * refuse every missing, extra or mistyped member, a state without its facts, a withheld amount that arrives anyway and a "NoSource"
 * variance that carries a list.
 */

import { describe, expect, it } from 'vitest'
import openOrdersResponse from './examples/open-orders-report-response.json'
import lostResponse from './examples/lost-opportunities-report-response.json'
import priceVarianceResponse from './examples/price-variance-report-response.json'
import { NO_REPORT_FILTER, parseReportFilterKey, reportFilterKey, reportPath } from './reportApi'
import {
  SALES_REPORT_LOST_OPPORTUNITIES_PATH,
  SALES_REPORT_OPEN_ORDERS_PATH,
  SALES_REPORT_PRICE_VARIANCE_PATH,
  decodeLostOpportunityReport,
  decodeOpenOrderReport,
  decodePriceVarianceReport,
} from './reportContract'

interface Schema {
  readonly properties?: Record<string, unknown>
  readonly required?: readonly string[]
  readonly enum?: readonly string[]
}

const nodeProcess = (globalThis as unknown as {
  readonly process: { readonly cwd: () => string; readonly getBuiltinModule: (name: string) => unknown }
}).process
const fs = nodeProcess.getBuiltinModule('node:fs') as { readFileSync(path: string, encoding: 'utf8'): string }
const path = nodeProcess.getBuiltinModule('node:path') as { resolve(...segments: string[]): string }
const document = JSON.parse(
  fs.readFileSync(path.resolve(nodeProcess.cwd(), '../../../contracts/openapi/goymen-erp-v1.json'), 'utf8'),
) as {
  readonly paths: Record<string, Record<string, { readonly parameters?: readonly { readonly name: string; readonly in: string }[]; readonly responses: Record<string, { readonly content?: Record<string, { readonly schema?: { readonly $ref?: string } }> }> }>>
  readonly components: { readonly schemas: Record<string, Schema> }
}

function schema(name: string): Schema {
  const found = document.components.schemas[name]
  if (found === undefined) throw new Error(`OpenAPI has no schema ${name}`)
  return found
}

function expectFits(example: object, schemaName: string): void {
  const declared = Object.keys(schema(schemaName).properties ?? {})
  const keys = Object.keys(example)
  expect(keys.filter((key) => !declared.includes(key)), `${schemaName}: undeclared members`).toEqual([])
  expect((schema(schemaName).required ?? []).filter((key) => !keys.includes(key)), `${schemaName}: missing required members`).toEqual([])
}

const clone = <T>(value: T): T => JSON.parse(JSON.stringify(value)) as T
type Wire = Record<string, unknown>

describe('API-41 / API-42 client ↔ canonical OpenAPI', () => {
  it('calls only the three declared GET routes with the query members the backend reads', () => {
    const filter = ['from', 'to', 'companyAccountId']
    for (const [route, response, queries] of [
      [SALES_REPORT_OPEN_ORDERS_PATH, 'SalesOpenOrderReportResponse', [...filter, 'pageSize', 'cursor']],
      [SALES_REPORT_LOST_OPPORTUNITIES_PATH, 'SalesLostOpportunityReportResponse', [...filter, 'reasonId', 'pageSize', 'cursor']],
      [SALES_REPORT_PRICE_VARIANCE_PATH, 'SalesPriceVarianceReportResponse', [...filter, 'pageSize', 'cursor']],
    ] as const) {
      const operation = document.paths[`/api/v1${route}`]?.get
      expect(operation, route).toBeDefined()
      expect(operation?.responses['200']?.content?.['application/json']?.schema?.$ref).toBe(`#/components/schemas/${response}`)
      expect(operation?.parameters?.filter((parameter) => parameter.in === 'query').map((parameter) => parameter.name)).toEqual(queries)
    }
    expect(reportPath(SALES_REPORT_OPEN_ORDERS_PATH, NO_REPORT_FILTER, null)).toBe('/sales/reports/open-orders?pageSize=50')
    const filtered = { from: '2026-10-01', to: '2026-10-31', companyAccountId: '44444444-4444-4444-8444-444444444444' }
    expect(reportPath(SALES_REPORT_OPEN_ORDERS_PATH, filtered, 'abc')).toBe(
      '/sales/reports/open-orders?from=2026-10-01&to=2026-10-31&companyAccountId=44444444-4444-4444-8444-444444444444&pageSize=50&cursor=abc',
    )
    expect(parseReportFilterKey(reportFilterKey(filtered))).toEqual(filtered)
    expect(parseReportFilterKey(reportFilterKey(NO_REPORT_FILTER))).toEqual(NO_REPORT_FILTER)
    // 422 is the open-order report's own "too wide" answer.
    expect(Object.keys(document.paths[`/api/v1${SALES_REPORT_OPEN_ORDERS_PATH}`].get.responses)).toContain('422')
  })

  it('pins the examples to every schema they carry and the enums to the wire values', () => {
    expectFits(openOrdersResponse, 'SalesOpenOrderReportResponse')
    for (const group of openOrdersResponse.groups) {
      expectFits(group, 'SalesOpenOrderGroupView')
      expectFits(group.customer, 'SalesReportCustomerView')
      for (const line of group.lines) expectFits(line, 'SalesOpenOrderLineView')
      for (const line of group.unmeasured) expectFits(line, 'SalesOpenOrderUnmeasuredLineView')
    }
    expectFits(lostResponse, 'SalesLostOpportunityReportResponse')
    for (const item of lostResponse.items) {
      expectFits(item, 'SalesLostOpportunityView')
      if (item.reason !== null) expectFits(item.reason, 'SalesLostReasonView')
      expectFits(item.amount, 'SalesLostAmountView')
    }
    expectFits(priceVarianceResponse, 'SalesPriceVarianceReportResponse')
    expect(schema('SalesOpenOrderMeasureView').enum).toEqual(['ApprovedMinusShipped'])
    expect(schema('SalesOpenOrderUnmeasuredReasonView').enum).toEqual(['PendingReapproval', 'TolerancePolicyUnavailable'])
    expect(schema('SalesPriceDisclosure').enum).toEqual(['Visible', 'Withheld'])
    expect(schema('SalesPriceVarianceSourceStateView').enum).toEqual(['NoSource', 'Available'])
    expect(schema('SalesPriceVarianceDirectionView').enum).toEqual(['Below', 'Above'])
  })
})

describe('open order report decoder', () => {
  it('reads the example exactly, quantity text as text, one group per unit', () => {
    const report = decodeOpenOrderReport(openOrdersResponse)
    expect(report?.items.map((group) => [group.unitCode, group.remainingQuantity, group.complete])).toEqual([
      ['kg', '250.5', false],
      ['adet', '0', true],
    ])
    expect(report?.tooWide).toBe(false)
  })

  it('refuses a missing, extra or mistyped member, a contradicting complete flag, a wrong order count and a repeated group', () => {
    const cases: ((body: Wire) => void)[] = [
      (body) => delete body.measure,
      (body) => (body.extra = 1),
      (body) => ((body.groups as Wire[])[0].remainingQuantity = 2250.5),
      (body) => ((body.groups as Wire[])[0].remainingQuantity = '-1'),
      (body) => ((body.groups as Wire[])[0].complete = true),
      (body) => ((body.groups as Wire[])[1].orderCount = 2),
      (body) => ((body.groups as Wire[])[1] = (body.groups as Wire[])[0]),
      (body) => (((body.groups as Wire[])[0].lines as Wire[])[0].basisRevision = 'rev0'),
      (body) => (((body.groups as Wire[])[0].unmeasured as Wire[])[0].reason = 'Other'),
      (body) => ((body.groups as Wire[])[0].price = '1'),
      // SAT-D130: completed within the tolerance means nothing open.
      (body) => (((body.groups as Wire[])[0].lines as Wire[])[1].remainingQuantity = '10'),
      (body) => delete (((body.groups as Wire[])[0].lines as Wire[])[0]).completedWithinTolerance,
    ]
    for (const change of cases) {
      const body = clone(openOrdersResponse) as unknown as Wire
      change(body)
      expect(decodeOpenOrderReport(body)).toBeNull()
    }
  })
})

describe('lost opportunity report decoder', () => {
  it('reads the example, a legacy loss without a list reason, and every amount in its own currency', () => {
    const report = decodeLostOpportunityReport(lostResponse)
    expect(report?.items.map((item) => [item.reason?.code ?? null, item.amount?.netAmount ?? null])).toEqual([
      ['SENTETIK-FIYAT', '14840.25'],
      [null, null],
    ])
  })

  it('refuses a withheld amount that arrives, a missing visible amount, another currency and a net that is not at its scale', () => {
    const cases: ((body: Wire) => void)[] = [
      (body) => (body.amountDisclosure = 'Withheld'),
      (body) => ((body.items as Wire[])[0].amount = null),
      (body) => (((body.items as Wire[])[0].amount as Wire).currencyId = '33333333-3333-4333-8333-333333333334'),
      (body) => (((body.items as Wire[])[0].amount as Wire).netAmount = '14840.2'),
      (body) => (((body.items as Wire[])[1].amount as Wire).netAmount = '1.00'),
      (body) => (((body.items as Wire[])[1].explanation = null)),
      (body) => ((body.items as Wire[])[0].revision = 'Rev.01'),
    ]
    for (const change of cases) {
      const body = clone(lostResponse) as unknown as Wire
      change(body)
      expect(decodeLostOpportunityReport(body)).toBeNull()
    }
    const withheld = clone(lostResponse) as unknown as Wire
    withheld.amountDisclosure = 'Withheld'
    for (const item of withheld.items as Wire[]) item.amount = null
    expect(decodeLostOpportunityReport(withheld)?.items.every((item) => item.amount === null)).toBe(true)
  })
})

describe('price variance report decoder', () => {
  it('reads NoSource only with null items and cursor — never an empty list', () => {
    expect(decodePriceVarianceReport(priceVarianceResponse)).toEqual({ state: 'NoSource' })
    expect(decodePriceVarianceReport({ state: 'NoSource', items: [], nextCursor: null })).toBeNull()
    expect(decodePriceVarianceReport({ state: 'NoSource', items: null, nextCursor: 'x' })).toBeNull()
    expect(decodePriceVarianceReport({ state: 'Available', items: null, nextCursor: null })).toBeNull()
    expect(decodePriceVarianceReport({ state: 'Available', items: [], nextCursor: null })).toEqual({ state: 'Available', items: [], nextCursor: null })
    expect(decodePriceVarianceReport({ state: 'Unknown', items: null, nextCursor: null })).toBeNull()
  })
})
