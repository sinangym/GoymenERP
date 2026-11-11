/**
 * UI-70b S-10 — the strict API-40 decoders (exact members, invariants fail closed) and their parity with the canonical OpenAPI
 * document the backend CI regenerates. SENTETIK wire bodies from ./customerReturnTestData.
 */
import { describe, expect, it } from 'vitest'
import {
  SALES_CUSTOMER_RETURNS_PATH,
  decodeCustomerReturnList,
  decodeCustomerReturnView,
  salesCustomerReturnApiPath,
  salesCustomerReturnsListPath,
} from './customerReturnContract'
import { customerReturnCommandPath } from './customerReturnApi'
import { decodeSalesInvoiceDetail } from './invoiceFinalizationContract'
import { INVOICE_ID, OTHER_INVOICE_ID } from './invoiceTestData'
import {
  APPROVER,
  PREPARER,
  RETURN_2,
  RETURN_ID,
  SHIPMENT_1,
  SHIPMENT_2,
  finalizedInvoiceWithLines,
  foreignReturnView,
  returnAmounts,
  returnLine,
  returnSummary,
  returnView,
} from './customerReturnTestData'

describe('UI-70b customer return view decoder', () => {
  it.each(['Draft', 'PendingApproval', 'Approved', 'Finalized'])('accepts a %s return with exactly its actor facts', (state) => {
    const view = decodeCustomerReturnView(returnView(state), RETURN_ID)
    expect(view?.state).toBe(state)
    expect(view?.lines[0].amounts.lineTotalAmount).toBe('240.00')
  })

  it('accepts a foreign-currency return whose TL side is complete, and a signed drift', () => {
    expect(decodeCustomerReturnView(foreignReturnView(), RETURN_ID)?.functionalTotals?.lineTotalAmount).toBe('8280.00')
    expect(decodeCustomerReturnView(returnView('Draft', { totals: returnAmounts({ driftAmount: '-0.01' }) }), RETURN_ID)).not.toBeNull()
  })

  it('accepts a shipment-linked return whose lines are all of that shipment', () => {
    expect(decodeCustomerReturnView(returnView('Draft', { shipmentId: SHIPMENT_1 }), RETURN_ID)?.shipmentId).toBe(SHIPMENT_1)
  })

  it.each([
    ['a stray member', returnView('Draft', { isReplay: false })],
    ['a missing member', (() => { const { updatedAtUtc: _drop, ...rest } = returnView('Draft'); return rest })()],
    ['another id', returnView('Draft', { id: RETURN_2 })],
    ['an unknown state', returnView('Draft', { state: 'Cancelled' })],
    ['a Draft carrying a submission', returnView('Draft', { submittedBy: PREPARER, submittedAtUtc: '2026-11-05T09:10:00Z' })],
    ['an approver who prepared it', returnView('Approved', { approvedBy: PREPARER })],
    ['an approver who submitted it', returnView('Approved', { submittedBy: APPROVER })],
    ['a Finalized return without its finalizer', returnView('Finalized', { finalizedBy: null })],
    ['a line of another shipment', returnView('Draft', { shipmentId: SHIPMENT_2 })],
    ['a rate without TL totals', returnView('Draft', { rate: '34.5000', functionalCurrencyId: SHIPMENT_1, functionalScale: 2 })],
    ['TL totals without a rate', returnView('Draft', { functionalTotals: returnAmounts() })],
    ['a TL return line with TL amounts', returnView('Draft', { lines: [returnLine({ functionalAmounts: returnAmounts() })] })],
    ['a foreign return line without TL amounts', foreignReturnView({ lines: [returnLine()] })],
    ['a repeated line number', returnView('Draft', { lines: [returnLine(), returnLine()] })],
    ['no lines', returnView('Draft', { lines: [] })],
    ['a negative amount', returnView('Draft', { totals: returnAmounts({ netAmount: '-1.00' }) })],
    ['a numeric amount', returnView('Draft', { totals: returnAmounts({ netAmount: 200 }) })],
  ])('refuses %s', (_label, wire) => {
    expect(decodeCustomerReturnView(wire, RETURN_ID)).toBeNull()
  })
})

describe('UI-70b customer return list decoder', () => {
  it('keeps the server order and refuses a repeated id', () => {
    const list = decodeCustomerReturnList({ items: [returnSummary({ id: RETURN_2 }), returnSummary()] }, null)
    expect(list?.map((row) => row.id)).toEqual([RETURN_2, RETURN_ID])
    expect(decodeCustomerReturnList({ items: [returnSummary(), returnSummary()] }, null)).toBeNull()
    expect(decodeCustomerReturnList({ items: [] }, null)).toEqual([])
  })

  it('refuses a row of another invoice under a filtered read, a stray member and a missing list', () => {
    expect(decodeCustomerReturnList({ items: [returnSummary()] }, INVOICE_ID)).toHaveLength(1)
    expect(decodeCustomerReturnList({ items: [returnSummary({ originalInvoiceId: OTHER_INVOICE_ID })] }, INVOICE_ID)).toBeNull()
    expect(decodeCustomerReturnList({ items: [returnSummary()], nextCursor: null }, null)).toBeNull()
    expect(decodeCustomerReturnList({ items: [returnSummary({ extra: 1 })] }, null)).toBeNull()
    expect(decodeCustomerReturnList({ items: [returnSummary({ lineCount: 0 })] }, null)).toBeNull()
    expect(decodeCustomerReturnList(null, null)).toBeNull()
  })

  it('narrows on the server with invoiceId', () => {
    expect(salesCustomerReturnsListPath(null)).toBe(SALES_CUSTOMER_RETURNS_PATH)
    expect(salesCustomerReturnsListPath(INVOICE_ID)).toBe(`${SALES_CUSTOMER_RETURNS_PATH}?invoiceId=${INVOICE_ID}`)
  })
})

describe('UI-70b invoice line members a return names', () => {
  it('reads the shipment, dispatch note, product and unit of each finalized invoice line', () => {
    const detail = decodeSalesInvoiceDetail(finalizedInvoiceWithLines(), INVOICE_ID)
    expect(detail?.lines.map((line) => [line.shipmentId, line.dispatchNoteNumber])).toEqual([
      [SHIPMENT_1, 'IRS-2026-0001'],
      [SHIPMENT_2, 'IRS-2026-0002'],
    ])
  })
})

/* ── OpenAPI parity (contracts/openapi/goymen-erp-v1.json, drift-checked on every PR) ───────────────────────────── */

interface Schema {
  readonly properties?: Record<string, unknown>
  readonly required?: readonly string[]
}
interface Operation {
  readonly requestBody?: { readonly content?: Record<string, { readonly schema?: { readonly $ref?: string } }> }
  readonly responses?: Record<string, unknown>
}
const nodeProcess = (globalThis as unknown as {
  readonly process: { readonly cwd: () => string; readonly getBuiltinModule: (name: string) => unknown }
}).process
const fs = nodeProcess.getBuiltinModule('node:fs') as { readFileSync(path: string, encoding: 'utf8'): string }
const path = nodeProcess.getBuiltinModule('node:path') as { resolve(...segments: string[]): string }
const document = JSON.parse(
  fs.readFileSync(path.resolve(nodeProcess.cwd(), '../../../contracts/openapi/goymen-erp-v1.json'), 'utf8'),
) as { readonly paths: Record<string, Record<string, Operation>>; readonly components: { readonly schemas: Record<string, Schema> } }

function members(name: string): string[] {
  const found = document.components.schemas[name]
  if (found === undefined) throw new Error(`OpenAPI has no schema ${name}`)
  return Object.keys(found.properties ?? {}).sort()
}
const keys = (wire: object) => Object.keys(wire).sort()

describe('UI-70b OpenAPI parity', () => {
  it('the fixtures the decoders accept carry exactly the members each API-40 schema declares', () => {
    const view = returnView('Finalized')
    expect(keys(view)).toEqual(members('SalesCustomerReturnView'))
    expect(keys(returnLine())).toEqual(members('SalesCustomerReturnLineView'))
    expect(keys(returnAmounts())).toEqual(members('SalesCustomerReturnAmountsView'))
    expect(keys(returnSummary())).toEqual(members('SalesCustomerReturnSummaryView'))
    expect(members('SalesCustomerReturnListResponse')).toEqual(['items'])
  })

  it('every route the client calls exists with the request body the client sends', () => {
    const at = (built: string) => `/api/v1${built}`.replace(RETURN_ID, '{id}')
    expect(document.paths[at(SALES_CUSTOMER_RETURNS_PATH)]?.get).toBeDefined()
    expect(document.paths[at(salesCustomerReturnApiPath(RETURN_ID))]?.get).toBeDefined()
    const create = document.paths[at(SALES_CUSTOMER_RETURNS_PATH)]?.post
    expect(create?.requestBody?.content?.['application/json']?.schema?.$ref).toBe('#/components/schemas/CreateSalesCustomerReturnRequest')
    expect(members('CreateSalesCustomerReturnRequest')).toEqual(
      ['customerDocumentDate', 'customerDocumentNo', 'lines', 'originalInvoiceId', 'shipmentId'],
    )
    expect(members('CreateSalesCustomerReturnLineRequest')).toEqual(['originalInvoiceLineId', 'quantity'])
    for (const command of ['submission', 'approval', 'finalization'] as const) {
      const operation = document.paths[at(customerReturnCommandPath(RETURN_ID, command))]?.post
      expect(operation, command).toBeDefined()
      const reference = operation?.requestBody?.content?.['application/json']?.schema?.$ref ?? ''
      // The client sends the version it read; the shared command body declares an optional reason the return never sends.
      const name = reference.replace('#/components/schemas/', '')
      expect(members(name)).toContain('expectedVersion')
      expect(document.components.schemas[name].required ?? []).toEqual(['expectedVersion'])
    }
    expect(Object.keys(document.paths[at(customerReturnCommandPath(RETURN_ID, 'finalization'))]?.post?.responses ?? {})).toContain('503')
  })
})
