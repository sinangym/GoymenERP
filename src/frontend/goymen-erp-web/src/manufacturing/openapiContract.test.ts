/**
 * The Manufacturing port is bound to the REAL HTTP contract, not to a fixture's shape.
 *
 * Business #66 5734735965: the screen must read and write the real `/api/v1/manufacturing`
 * endpoints. The component tests drive synthetic fixtures; this file pins those fixtures and
 * this module's decoder and request bodies to the canonical OpenAPI document the backend CI
 * regenerates (`contracts/openapi/goymen-erp-v1.json`, drift-checked on every PR). A backend
 * rename, a new required field or a changed path turns this red here instead of leaving the
 * screen silently decoding nothing.
 *
 * WHAT THIS IS NOT: a live call. The real round trip is the `mfg` browser suite.
 */

import { describe, expect, it } from 'vitest'
import {
  closeBody,
  decodeMassBalance,
  decodeOrder,
  decodeOrderList,
  decodePlan,
  FACT_KINDS,
  OUTPUT_CLASSES,
} from './contract'
import { MANUFACTURING_BASE } from './api'
import { massBalance, order, orderList, orderSummary, plan, plannedInput, roll, ROLL_1 } from './testHarness'

interface Schema {
  readonly properties?: Record<string, unknown>
  readonly required?: readonly string[]
}

/**
 * Read with Node's own `fs` (the test runner's process), not through a module import: the
 * canonical document lives at the repository root, outside the web package that Vite serves and
 * the web image copies, so neither an import nor the app's type set may reach it. Node 24 in CI.
 */
const nodeProcess = (globalThis as unknown as {
  readonly process: {
    readonly cwd: () => string
    readonly getBuiltinModule: (name: string) => unknown
  }
}).process
const fs = nodeProcess.getBuiltinModule('node:fs') as { readFileSync(path: string, encoding: 'utf8'): string }
const path = nodeProcess.getBuiltinModule('node:path') as { resolve(...segments: string[]): string }
// Vitest runs from the web package (`npm test` in src/frontend/goymen-erp-web).
const openapiText = fs.readFileSync(
  path.resolve(nodeProcess.cwd(), '../../../contracts/openapi/goymen-erp-v1.json'),
  'utf8',
)

const document = JSON.parse(openapiText) as {
  readonly paths: Record<string, Record<string, unknown>>
  readonly components: { readonly schemas: Record<string, Schema> }
}

function schema(name: string): Schema {
  const found = document.components.schemas[name]
  if (found === undefined) throw new Error(`OpenAPI has no schema ${name}`)
  return found
}

function names(name: string): string[] {
  return Object.keys(schema(name).properties ?? {}).sort()
}

function requestSchemaName(pathName: string): string {
  const operation = document.paths[pathName]?.post as {
    readonly requestBody?: {
      readonly content?: {
        readonly 'application/json'?: { readonly schema?: { readonly $ref?: string } }
      }
    }
  } | undefined
  const reference = operation?.requestBody?.content?.['application/json']?.schema?.$ref
  const prefix = '#/components/schemas/'
  if (reference === undefined || !reference.startsWith(prefix)) {
    throw new Error(`OpenAPI POST ${pathName} has no component request schema`)
  }
  return reference.slice(prefix.length)
}

describe('Manufacturing port ↔ canonical OpenAPI', () => {
  it('reads exactly the properties OrderViewV1 and its parts declare, all required', () => {
    expect(Object.keys(order()).sort()).toEqual(names('OrderViewV1'))
    expect(Object.keys(roll(ROLL_1)).sort()).toEqual(names('RollBalanceViewV1'))
    expect(Object.keys(order().remainingTarget).sort()).toEqual(names('RemainingTargetViewV1'))
    expect(['basis', 'kg']).toEqual(names('TargetViewV1'))
    for (const name of ['OrderViewV1', 'RollBalanceViewV1', 'RemainingTargetViewV1', 'TargetViewV1']) {
      expect([...(schema(name).required ?? [])].sort()).toEqual(names(name))
    }
    // The fixture the component tests use decodes, so the decoder reads these very names.
    expect(decodeOrder(order({ target: { basis: 'GoodOutputMass', kg: '900' } }))).not.toBeNull()
  })

  it('reads exactly the properties PlanViewV1 and its parts declare, all required', () => {
    expect(Object.keys(plan()).sort()).toEqual(names('PlanViewV1'))
    expect(Object.keys(plannedInput(ROLL_1)).sort()).toEqual(names('PlannedInputViewV1'))
    for (const name of ['PlanViewV1', 'PlannedInputViewV1']) {
      expect([...(schema(name).required ?? [])].sort()).toEqual(names(name))
    }
    // The fixture the screen tests use decodes, so the decoder reads these very names — and the
    // approved shape decodes too, which the Draft fixture alone would not show.
    expect(decodePlan(plan({ target: { basis: 'GoodOutputMass', kg: '900' } }))).not.toBeNull()
    expect(
      decodePlan(plan({
        status: 'Approved',
        version: 2,
        approvedByUserId: '99999999-9999-9999-9999-999999999999',
        approvedAtUtc: '2026-09-18T09:00:00.000Z',
      })),
    ).not.toBeNull()
  })

  it('declares the exact strict inventory request schema for each realization route', () => {
    const root = MANUFACTURING_BASE.replace(/^.*\/api\/v1/, '/api/v1')
    const base = `${root}/orders/{orderId}`
    const requests = [
      {
        kind: 'input-issues',
        schemaName: 'IssueInventoryInputRequestV1',
        properties: ['expectedVersion', 'issueId', 'kg', 'movementId', 'occurredAtUtc', 'parentRollId'],
      },
      {
        kind: 'input-returns',
        schemaName: 'ReturnInventoryInputRequestV1',
        properties: [
          'expectedParentLabelVersion',
          'expectedVersion',
          'kg',
          'movementId',
          'occurredAtUtc',
          'parentRollId',
          'returnId',
        ],
      },
      {
        kind: 'outputs',
        schemaName: 'DeclareInventoryOutputRequestV1',
        properties: [
          'coilNumber',
          'custodianId',
          'custodianKind',
          'expectedParentLabelVersion',
          'expectedProductVersion',
          'expectedVersion',
          'heatNumber',
          'initialBlock',
          'kg',
          'labelCode',
          'labelId',
          'locationId',
          'lotId',
          'movementId',
          'occurredAtUtc',
          'outputClass',
          'outputId',
          'ownerId',
          'ownerKind',
          'parentRollId',
          'productId',
          'requiredDocuments',
        ],
      },
    ] as const

    for (const request of requests) {
      expect(requestSchemaName(`${base}/${request.kind}`), request.kind).toBe(request.schemaName)
      expect(names(request.schemaName), request.schemaName).toEqual([...request.properties].sort())
      expect([...(schema(request.schemaName).required ?? [])].sort(), request.schemaName).toEqual(
        [...request.properties].sort(),
      )
    }
  })

  it('calls routes the API declares: the order read, the plan read and one POST per fact kind', () => {
    const root = MANUFACTURING_BASE.replace(/^.*\/api\/v1/, '/api/v1')
    expect(document.paths[`${root}/plans/{planId}`]).toHaveProperty('get')
    const base = `${root}/orders/{orderId}`
    expect(document.paths[base]).toHaveProperty('get')
    for (const kind of FACT_KINDS) {
      expect(document.paths[`${base}/${kind}`], kind).toHaveProperty('post')
    }
  })

  it('reads exactly the properties MassBalanceViewV1 (GDC-BAL/1) and its parts declare, all required', () => {
    const view = massBalance()
    const line = view.rolls[0]
    expect(Object.keys(view).sort()).toEqual(names('MassBalanceViewV1'))
    expect(Object.keys(line).sort()).toEqual(names('RollMassBalanceViewV1'))
    expect(Object.keys(line.difference).sort()).toEqual(names('MassDifferenceViewV1'))
    expect(Object.keys(line.scrapPercent).sort()).toEqual(names('EngineAnswerViewV1'))
    for (const name of ['MassBalanceViewV1', 'RollMassBalanceViewV1', 'MassDifferenceViewV1', 'EngineAnswerViewV1']) {
      expect([...(schema(name).required ?? [])].sort()).toEqual(names(name))
    }
    expect(decodeMassBalance(view)).not.toBeNull()
    const base = `${MANUFACTURING_BASE.replace(/^.*\/api\/v1/, '/api/v1')}/orders/{orderId}`
    expect(document.paths[`${base}/mass-balance`]).toHaveProperty('get')
  })

  it('reads exactly the properties OrderListViewV1 and OrderSummaryViewV1 declare, all required', () => {
    const page = orderList([orderSummary()], 'CURSOR-1')
    expect(Object.keys(page).sort()).toEqual(names('OrderListViewV1'))
    expect(Object.keys(page.orders[0] as object).sort()).toEqual(names('OrderSummaryViewV1'))
    for (const name of ['OrderListViewV1', 'OrderSummaryViewV1']) {
      expect([...(schema(name).required ?? [])].sort()).toEqual(names(name))
    }
    expect(decodeOrderList(page)).not.toBeNull()
  })

  it('sends exactly the property the close request declares, and calls the list and close routes', () => {
    expect(Object.keys(closeBody({ idempotencyKey: 'k', expectedVersion: 1 })).sort()).toEqual(
      names('CloseOrderRequestV1'),
    )
    const orders = `${MANUFACTURING_BASE.replace(/^.*\/api\/v1/, '/api/v1')}/orders`
    expect(document.paths[orders]).toHaveProperty('get')
    expect(document.paths[`${orders}/{orderId}/close`]).toHaveProperty('post')
  })

  it('knows the five output classes by the server names the domain uses', () => {
    // The dictionary is typed `additionalProperties: string`; the names are the domain's enum.
    expect(schema('RollBalanceViewV1').properties?.outputsKg).toMatchObject({ additionalProperties: { type: 'string' } })
    expect([...OUTPUT_CLASSES]).toEqual(['GoodProduct', 'SecondQuality', 'UsableRemnant', 'Scrap', 'ProcessLoss'])
  })
})
