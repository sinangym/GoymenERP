/**
 * UI-65 wire decoder against API-37: the pinned example fits the canonical OpenAPI schemas the backend CI regenerates
 * (`contracts/openapi/goymen-erp-v1.json`), the client calls only the declared route with no query member, and the decoder refuses
 * every missing, extra or mistyped member, a missing or repeated check, an entry whose identities disagree with its kind and an
 * entry under an unavailable check. Data SENTETIK.
 */

import { describe, expect, it } from 'vitest'
import controlCenterResponse from './examples/control-center-response.json'
import {
  CONTROL_CENTER_CHECK_STATES,
  CONTROL_CENTER_KINDS,
  CONTROL_CENTER_SEVERITIES,
  SALES_CONTROL_CENTER_PATH,
  decodeControlCenter,
} from './controlCenterContract'

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
  expect(declared.filter((key) => !keys.includes(key)), `${schemaName}: declared members the example lacks`).toEqual([])
}

const clone = <T>(value: T): T => JSON.parse(JSON.stringify(value)) as T
type Wire = Record<string, unknown>
const body = () => clone(controlCenterResponse) as unknown as { checks: Wire[]; items: Wire[] } & Wire

describe('API-37 client ↔ canonical OpenAPI', () => {
  it('calls only the declared GET route, which reads no query member', () => {
    const operation = document.paths[`/api/v1${SALES_CONTROL_CENTER_PATH}`]?.get
    expect(operation).toBeDefined()
    expect(operation?.responses['200']?.content?.['application/json']?.schema?.$ref).toBe('#/components/schemas/SalesControlCenterResponse')
    expect(operation?.parameters?.filter((parameter) => parameter.in === 'query')).toEqual([])
  })

  it('pins the example to every schema it carries and the enums to the wire values', () => {
    expectFits(controlCenterResponse, 'SalesControlCenterResponse')
    for (const check of controlCenterResponse.checks) expectFits(check, 'SalesControlCenterCheckView')
    for (const item of controlCenterResponse.items) expectFits(item, 'SalesControlCenterItemView')
    expect(schema('SalesControlCenterCheckKindView').enum).toEqual([...CONTROL_CENTER_KINDS])
    expect(schema('SalesControlCenterSeverityView').enum).toEqual([...CONTROL_CENTER_SEVERITIES])
    expect(schema('SalesControlCenterCheckStateView').enum).toEqual([...CONTROL_CENTER_CHECK_STATES])
  })
})

describe('decodeControlCenter', () => {
  it('accepts the example and keeps the server order and numbers as given', () => {
    const decoded = decodeControlCenter(body())
    expect(decoded?.uninvoicedDispatchGraceDays).toBe(7)
    expect(decoded?.checks).toHaveLength(6)
    expect(decoded?.items.map((item) => item.reference)).toEqual([
      'IRS-SENTETIK-000011', 'FTR-SENTETIK-000004', 'FTR-SENTETIK-000006', 'FTR-SENTETIK-000005', 'FTR-SENTETIK-000007',
    ])
    expect(decoded?.items[0].ageDays).toBe(11)
  })

  it('refuses a missing, extra or mistyped member at every level', () => {
    const missing = body()
    delete missing.uninvoicedDispatchGraceDays
    expect(decodeControlCenter(missing)).toBeNull()
    const extra = body()
    extra.total = '0'
    expect(decodeControlCenter(extra)).toBeNull()
    const stringInt = body()
    stringInt.uninvoicedDispatchGraceDays = '7'
    expect(decodeControlCenter(stringInt)).toBeNull()
    const zeroGrace = body()
    zeroGrace.uninvoicedDispatchGraceDays = 0
    expect(decodeControlCenter(zeroGrace)).toBeNull()
    const badDate = body()
    badDate.businessDate = '2026-02-30'
    expect(decodeControlCenter(badDate)).toBeNull()

    const checkExtra = body()
    checkExtra.checks[0].note = 'x'
    expect(decodeControlCenter(checkExtra)).toBeNull()
    const checkState = body()
    checkState.checks[0].state = 'Unknown'
    expect(decodeControlCenter(checkState)).toBeNull()

    for (const [member, value] of [
      ['kind', 'Other'], ['severity', 'High'], ['partyId', '00000000-0000-0000-0000-000000000000'], ['companyAccountId', 'x'],
      ['reference', ' '], ['referenceDate', '2026-13-01'], ['ageDays', -1], ['ageDays', 1.5], ['count', 0], ['count', '1'],
    ] as const) {
      const wrong = body()
      wrong.items[1][member] = value
      expect(decodeControlCenter(wrong), `${member}=${String(value)}`).toBeNull()
    }
    const itemMissing = body()
    delete itemMissing.items[1].salesOrderIds
    expect(decodeControlCenter(itemMissing)).toBeNull()
    // SAT-D246: the retired single member is not accepted, neither in place of the list nor beside it.
    const singleOrder = body()
    singleOrder.items[1].salesOrderId = (singleOrder.items[1].salesOrderIds as string[])[0]
    delete singleOrder.items[1].salesOrderIds
    expect(decodeControlCenter(singleOrder)).toBeNull()
    const bothOrderMembers = body()
    bothOrderMembers.items[1].salesOrderId = null
    expect(decodeControlCenter(bothOrderMembers)).toBeNull()
    const itemAmount = body()
    itemAmount.items[1].amount = '100.00'
    expect(decodeControlCenter(itemAmount)).toBeNull()
  })

  it('keeps every order of a multi-order invoice entry in the server order, as a copy', () => {
    const wire = body()
    const decoded = decodeControlCenter(wire)
    expect(decoded?.items[3].salesOrderIds).toEqual(['c3c3c3c3-0000-4000-8000-000000000005', 'c3c3c3c3-0000-4000-8000-000000000008'])
    expect(decoded?.items[0].salesOrderIds).toEqual(['c3c3c3c3-0000-4000-8000-000000000007'])
    expect(decoded?.items[3].salesOrderIds).not.toBe(wire.items[3].salesOrderIds)
  })

  it('refuses an order list that is not a non-empty list of distinct identities', () => {
    for (const value of [
      null,
      'c3c3c3c3-0000-4000-8000-000000000004',
      [],
      ['x'],
      ['00000000-0000-0000-0000-000000000000'],
      [null],
      ['c3c3c3c3-0000-4000-8000-000000000004', 'c3c3c3c3-0000-4000-8000-000000000004'],
      ['c3c3c3c3-0000-4000-8000-00000000000a', 'C3C3C3C3-0000-4000-8000-00000000000A'],
    ]) {
      const wrong = body()
      wrong.items[1].salesOrderIds = value
      expect(decodeControlCenter(wrong), JSON.stringify(value)).toBeNull()
    }
  })

  it('refuses a missing or repeated check: a check never goes silent', () => {
    const missing = body()
    missing.checks.pop()
    expect(decodeControlCenter(missing)).toBeNull()
    const repeated = body()
    repeated.checks[5] = { ...repeated.checks[0] }
    expect(decodeControlCenter(repeated)).toBeNull()
  })

  it('refuses an entry whose identities disagree with its kind', () => {
    const shipmentWithInvoice = body()
    shipmentWithInvoice.items[0].invoiceId = 'f1f1f1f1-0000-4000-8000-000000000099'
    expect(decodeControlCenter(shipmentWithInvoice)).toBeNull()
    const shipmentWithoutOrder = body()
    shipmentWithoutOrder.items[0].salesOrderIds = []
    expect(decodeControlCenter(shipmentWithoutOrder)).toBeNull()
    const shipmentTwoOrders = body()
    shipmentTwoOrders.items[0].salesOrderIds = ['c3c3c3c3-0000-4000-8000-000000000007', 'c3c3c3c3-0000-4000-8000-000000000017']
    expect(decodeControlCenter(shipmentTwoOrders)).toBeNull()
    const shipmentLines = body()
    shipmentLines.items[0].count = 2
    expect(decodeControlCenter(shipmentLines)).toBeNull()
    const invoiceWithoutId = body()
    invoiceWithoutId.items[1].invoiceId = null
    expect(decodeControlCenter(invoiceWithoutId)).toBeNull()
    const invoiceWithShipment = body()
    invoiceWithShipment.items[1].shipmentId = 'a8a8a8a8-0000-4000-8000-000000000099'
    expect(decodeControlCenter(invoiceWithShipment)).toBeNull()
  })

  it('refuses an entry under an unavailable check, and an entry dated after the business day', () => {
    const unavailable = body()
    unavailable.checks[0] = { kind: 'UninvoicedDispatchOverdue', state: 'Unavailable', truncated: false }
    expect(decodeControlCenter(unavailable)).toBeNull()
    unavailable.items.shift()
    expect(decodeControlCenter(unavailable)?.checks[0].state).toBe('Unavailable')
    const future = body()
    future.items[1].referenceDate = '2026-11-10'
    expect(decodeControlCenter(future)).toBeNull()
  })
})
