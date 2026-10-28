/**
 * UI-60 / UI-61 wire decoders against API-30 / API-31: the pinned examples fit the canonical OpenAPI schemas the backend CI
 * regenerates (`contracts/openapi/goymen-erp-v1.json`), the client calls only declared routes, and the decoders refuse every
 * missing, extra or mistyped member, a state without its facts, another company's record and a repeated shipment.
 */

import { describe, expect, it } from 'vitest'
import dispatchesResponse from './examples/dispatches-response.json'
import candidatesResponse from './examples/invoice-candidates-response.json'
import { dispatchListPath, invoiceCandidatesPath } from './dispatchApi'
import { SALES_DISPATCHES_PATH, SALES_INVOICE_CANDIDATES_PATH, decodeDispatchPage, decodeInvoiceCandidates } from './dispatchContract'

const COMPANY = '99999999-9999-9999-9999-999999999999'

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
const firstItem = (body: Wire) => (body.items as Wire[])[0]
const firstGroup = (body: Wire) => (body.groups as Wire[])[0]

describe('API-30 / API-31 client ↔ canonical OpenAPI', () => {
  it('calls only the two declared GET routes with the query members the backend reads', () => {
    for (const [route, response, queries] of [
      [SALES_DISPATCHES_PATH, 'SalesDispatchListResponse', ['pageSize', 'cursor', 'state']],
      [SALES_INVOICE_CANDIDATES_PATH, 'SalesInvoiceCandidatesResponse', ['companyAccountId']],
    ] as const) {
      const operation = document.paths[`/api/v1${route}`]?.get
      expect(operation, route).toBeDefined()
      expect(operation?.responses['200']?.content?.['application/json']?.schema?.$ref).toBe(`#/components/schemas/${response}`)
      expect(operation?.parameters?.filter((parameter) => parameter.in === 'query').map((parameter) => parameter.name)).toEqual(queries)
    }
    expect(dispatchListPath('abc', 'Approved')).toBe('/sales/dispatches?pageSize=50&cursor=abc&state=Approved')
    expect(dispatchListPath(null, null)).toBe('/sales/dispatches?pageSize=50')
    expect(invoiceCandidatesPath(null)).toBe('/sales/invoice-candidates')
    expect(invoiceCandidatesPath('44444444-4444-4444-8444-444444444444')).toBe(
      '/sales/invoice-candidates?companyAccountId=44444444-4444-4444-8444-444444444444',
    )
  })

  it('pins the examples to every schema they carry and the enums to the wire values', () => {
    expectFits(dispatchesResponse, 'SalesDispatchListResponse')
    for (const item of dispatchesResponse.items) {
      expectFits(item, 'SalesDispatchSummaryView')
      expectFits(item.customer, 'SalesDispatchCustomerView')
      for (const line of item.lines) expectFits(line, 'SalesDispatchLineView')
    }
    expectFits(candidatesResponse, 'SalesInvoiceCandidatesResponse')
    for (const group of candidatesResponse.groups) {
      expectFits(group, 'SalesInvoiceCandidateGroupView')
      expectFits(group.key, 'SalesInvoiceGroupKeyView')
      expectFits(group.customer, 'SalesDispatchCustomerView')
      for (const total of group.quantityTotals) expectFits(total, 'SalesInvoiceQuantityTotalView')
      for (const share of group.shares) expectFits(share, 'SalesInvoiceCandidateShareView')
    }
    expect(schema('SalesDispatchNoteStateView').enum).toEqual(['Draft', 'Approved', 'Cancelled'])
    expect(schema('SalesInvoicedStateView').enum).toEqual(['NoSource'])
    expect(schema('SalesInvoiceGroupKeyPart').enum).toEqual(['Company', 'Customer', 'Currency', 'Tax', 'ExchangeRate', 'PaymentTerms', 'EDocument'])
    // No money member on either read.
    for (const name of ['SalesDispatchLineView', 'SalesInvoiceCandidateShareView', 'SalesInvoiceCandidateGroupView']) {
      expect(Object.keys(schema(name).properties ?? {}).filter((key) => /price|amount|total(?!s)|tax|vat/i.test(key))).toEqual([])
    }
  })
})

describe('dispatch list decoder', () => {
  it('reads the example page exactly, quantity text as text', () => {
    const page = decodeDispatchPage(dispatchesResponse)
    expect(page?.items.map((item) => item.state)).toEqual(['Approved', 'Draft'])
    expect(page?.items[0].lines[0].quantity).toBe('1250.5')
    expect(page?.nextCursor).toBe('c2FsZXMtZGlzcGF0Y2gtY3Vyc29y')
  })

  it('refuses a missing, extra or mistyped member and a repeated note', () => {
    const missing = clone(dispatchesResponse) as Wire
    delete firstItem(missing).invoicing
    expect(decodeDispatchPage(missing)).toBeNull()
    const extra = clone(dispatchesResponse) as Wire
    firstItem(extra).unitPrice = '10'
    expect(decodeDispatchPage(extra)).toBeNull()
    const number = clone(dispatchesResponse) as Wire
    ;((firstItem(number).lines as Wire[])[0]).quantity = 1250.5
    expect(decodeDispatchPage(number)).toBeNull()
    const repeated = clone(dispatchesResponse) as Wire
    ;(repeated.items as Wire[])[1] = clone(firstItem(repeated))
    expect(decodeDispatchPage(repeated)).toBeNull()
    expect(decodeDispatchPage({ items: [] })).toBeNull()
  })

  it('never shows a state without its facts', () => {
    const approvedWithoutNumber = clone(dispatchesResponse) as Wire
    firstItem(approvedWithoutNumber).systemNumber = null
    expect(decodeDispatchPage(approvedWithoutNumber)).toBeNull()
    const draftWithNumber = clone(dispatchesResponse) as Wire
    ;(draftWithNumber.items as Wire[])[1].systemNumber = 'IRS-000009'
    expect(decodeDispatchPage(draftWithNumber)).toBeNull()
    const cancelledWithoutTime = clone(dispatchesResponse) as Wire
    firstItem(cancelledWithoutTime).state = 'Cancelled'
    expect(decodeDispatchPage(cancelledWithoutTime)).toBeNull()
    const unknownState = clone(dispatchesResponse) as Wire
    firstItem(unknownState).state = 'approved'
    expect(decodeDispatchPage(unknownState)).toBeNull()
  })
})

describe('invoice candidates decoder', () => {
  it('reads the example groups under their company', () => {
    const candidates = decodeInvoiceCandidates(candidatesResponse, COMPANY)
    expect(candidates?.groups).toHaveLength(1)
    expect(candidates?.groups[0].unresolvedKeyParts).toEqual(['PaymentTerms', 'Tax', 'ExchangeRate', 'EDocument'])
    expect(candidates?.groups[0].quantityTotals).toEqual([
      { unitId: '66666666-6666-4666-8666-666666666666', unitCode: 'kg', quantity: '4.5' },
    ])
    // Another company's group is a tenant-binding breach.
    expect(decodeInvoiceCandidates(candidatesResponse, '11111111-1111-4111-8111-111111111111')).toBeNull()
  })

  it('refuses a complete flag that disagrees with the unresolved parts, an unknown part and a repeated shipment', () => {
    const disagreeing = clone(candidatesResponse) as Wire
    firstGroup(disagreeing).complete = true
    expect(decodeInvoiceCandidates(disagreeing, COMPANY)).toBeNull()
    const unknownPart = clone(candidatesResponse) as Wire
    firstGroup(unknownPart).unresolvedKeyParts = ['Vat']
    expect(decodeInvoiceCandidates(unknownPart, COMPANY)).toBeNull()
    const repeated = clone(candidatesResponse) as Wire
    ;((firstGroup(repeated).shares as Wire[])[1]).shipmentId = (firstGroup(repeated).shares as Wire[])[0].shipmentId
    expect(decodeInvoiceCandidates(repeated, COMPANY)).toBeNull()
  })

  it('refuses a share in a unit the group has no total for, a customer not of the key and a money member', () => {
    const otherUnit = clone(candidatesResponse) as Wire
    ;((firstGroup(otherUnit).shares as Wire[])[0]).unitId = '68686868-6868-4686-8686-686868686868'
    expect(decodeInvoiceCandidates(otherUnit, COMPANY)).toBeNull()
    const otherCustomer = clone(candidatesResponse) as Wire
    ;(firstGroup(otherCustomer).customer as Wire).companyAccountId = '45454545-4545-4454-8454-454545454545'
    expect(decodeInvoiceCandidates(otherCustomer, COMPANY)).toBeNull()
    const money = clone(candidatesResponse) as Wire
    firstGroup(money).amount = '100.00'
    expect(decodeInvoiceCandidates(money, COMPANY)).toBeNull()
  })
})
