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
import { decodePlanList } from './planListContract'
import { decodePlanRevisions } from './planRevisionContract'
import type { RevisePlanRequest } from './planCommandApi'
import {
  massBalance,
  order,
  orderList,
  orderSummary,
  plan,
  plannedInput,
  planList,
  planRevision,
  planRevisions,
  planSummary,
  revisionInput,
  roll,
  ROLL_1,
  technicalSnapshot,
} from './testHarness'

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

/**
 * M01.2 cut-to-length: an additive property the fixtures and the edit request deliberately omit. It must be declared,
 * nullable to the given component schema and outside the required set, so a body or view without it stays valid.
 */
const CUT_TO_LENGTH = 'cutToLengthSpec'

function withoutCutToLength(name: string): string[] {
  return names(name).filter((property) => property !== CUT_TO_LENGTH)
}

function expectOptionalCutToLength(name: string, component: string): void {
  expect(schema(name).properties?.[CUT_TO_LENGTH], name).toEqual({
    oneOf: [{ type: 'null' }, { $ref: `#/components/schemas/${component}` }],
  })
  expect(schema(name).required ?? [], name).not.toContain(CUT_TO_LENGTH)
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
    const rollView = { ...roll(ROLL_1), commercialContext: null }
    expect(Object.keys(order()).sort()).toEqual(names('OrderViewV1'))
    expect(Object.keys(rollView).sort()).toEqual(names('RollBalanceViewV1'))
    expect(Object.keys(order().remainingTarget).sort()).toEqual(names('RemainingTargetViewV1'))
    expect(['basis', 'kg']).toEqual(names('TargetViewV1'))
    for (const name of ['OrderViewV1', 'RemainingTargetViewV1', 'TargetViewV1']) {
      expect([...(schema(name).required ?? [])].sort()).toEqual(names(name))
    }
    expect([...(schema('RollBalanceViewV1').required ?? [])].sort()).toEqual(
      names('RollBalanceViewV1').filter((name) => name !== 'commercialContext'),
    )
    // The fixture the component tests use decodes, so the decoder reads these very names.
    expect(decodeOrder(order({ target: { basis: 'GoodOutputMass', kg: '900' } }))).not.toBeNull()
  })

  it('reads exactly the current PlanViewV1 properties and keeps additive legacy projections optional', () => {
    const inputView = { ...plannedInput(ROLL_1), commercialContext: null }
    expect(Object.keys(plan()).sort()).toEqual(withoutCutToLength('PlanViewV1'))
    expect(Object.keys(inputView).sort()).toEqual(names('PlannedInputViewV1'))
    expect([...(schema('PlanViewV1').required ?? [])].sort()).toEqual(
      names('PlanViewV1').filter(
        (name) => name !== 'calculation' && name !== 'slittingSpec' && name !== CUT_TO_LENGTH,
      ),
    )
    expectOptionalCutToLength('PlanViewV1', 'CutToLengthSpecViewV1')
    expect([...(schema('PlannedInputViewV1').required ?? [])].sort()).toEqual(
      names('PlannedInputViewV1').filter(
        (name) => name !== 'technicalSnapshot' && name !== 'commercialContext',
      ),
    )
    expect(plan()).toMatchObject({ calculation: null, slittingSpec: null })
    expect(inputView).toMatchObject({ technicalSnapshot: null, commercialContext: null })
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

  it('declares the additive ordered slitting request and stored view without adding it to revisions', () => {
    expect(names('SlittingSpecRequestV1')).toEqual(['lines', 'windingTargetKg'])
    expect(names('SlittingSpecViewV1')).toEqual(['lines', 'windingTargetKg'])
    expect(names('SlitLineRequestV1')).toEqual(['bandWidthMm', 'count'])
    expect(names('SlitLineViewV1')).toEqual(['bandWidthMm', 'count'])
    expect(schema('SavePlanRequestV1').properties).toHaveProperty('slittingSpec')
    expect(schema('PlanPreviewRequestV1').properties).toHaveProperty('slittingSpec')
    expect(schema('PlanViewV1').properties).toHaveProperty('slittingSpec')
    expect(schema('RevisePlanRequestV1').properties).not.toHaveProperty('slittingSpec')
    expect(schema('PlanRevisionViewV1').properties).not.toHaveProperty('slittingSpec')
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

  it('reads exactly the properties PlanListViewV1 and PlanSummaryViewV1 declare, all required, on the list route', () => {
    const page = planList([planSummary()], 'CURSOR-1')
    expect(Object.keys(page).sort()).toEqual(names('PlanListViewV1'))
    expect(Object.keys(page.plans[0] as object).sort()).toEqual(names('PlanSummaryViewV1'))
    for (const name of ['PlanListViewV1', 'PlanSummaryViewV1']) {
      expect([...(schema(name).required ?? [])].sort()).toEqual(names(name))
    }
    // Both statuses decode from the fixture the screen tests use, so the decoder reads these very names.
    expect(decodePlanList(page)).not.toBeNull()
    expect(
      decodePlanList(planList([planSummary({ status: 'Approved', version: 2, approvedAtUtc: '2026-09-18T10:00:00.000Z' })])),
    ).not.toBeNull()
    const plans = `${MANUFACTURING_BASE.replace(/^.*\/api\/v1/, '/api/v1')}/plans`
    const operation = document.paths[plans]?.get as
      | {
          readonly operationId?: string
          readonly responses?: Record<string, { readonly content?: Record<string, { readonly schema?: { readonly $ref?: string } }> }>
        }
      | undefined
    expect(operation?.operationId).toBe('ListManufacturingPlansV1')
    expect(operation?.responses?.['200']?.content?.['application/json']?.schema?.$ref).toBe(
      '#/components/schemas/PlanListViewV1',
    )
    // The save stays a POST on the same path: the list is the path's GET, not a replacement.
    expect(document.paths[plans]).toHaveProperty('post')
  })

  // MFG-PLAN-EDIT: the edit command and the history read. These two operations are added by the module; the canonical
  // document is regenerated by the central writer, so these cases are red until that round lands (by design: the
  // screen must not call a route the canonical contract does not declare).
  it('sends exactly the properties RevisePlanRequestV1 declares, on the revision route', () => {
    const request: RevisePlanRequest = {
      expectedVersion: 1,
      workCenterId: 'd0000000-0000-4000-8000-000000000003',
      inputs: [{
        parentRollId: ROLL_1,
        plannedKg: '800',
        expectedLabelVersion: '7',
        commercialContext: null,
      }],
      target: { basis: 'GoodOutputMass', kg: '700' },
      reason: 'Gerekçe',
    }
    const plans = `${MANUFACTURING_BASE.replace(/^.*\/api\/v1/, '/api/v1')}/plans`
    expect(requestSchemaName(`${plans}/{planId}/revision`)).toBe('RevisePlanRequestV1')
    expect(Object.keys(request).sort()).toEqual(withoutCutToLength('RevisePlanRequestV1'))
    expect([...(schema('RevisePlanRequestV1').required ?? [])].sort()).toEqual(withoutCutToLength('RevisePlanRequestV1'))
    expectOptionalCutToLength('RevisePlanRequestV1', 'CutToLengthSpecRequestV1')
    expect(Object.keys(request.inputs[0]).sort()).toEqual(names('PlannedInputRequestV1'))
    expect(Object.keys(request.target ?? {}).sort()).toEqual(names('TargetRequestV1'))
  })

  it('reads exactly the properties PlanRevisionListViewV1 and its parts declare, all required, on the history route', () => {
    const history = planRevisions([
      planRevision(),
      planRevision({
        revision: 1,
        planVersion: 2,
        reason: 'Gerekçe',
        revisedBy: 'actor',
        inputs: [revisionInput(ROLL_1, '800', '7', null, technicalSnapshot())],
      }),
    ])
    expect(Object.keys(history).sort()).toEqual(names('PlanRevisionListViewV1'))
    expect(Object.keys(history.revisions[0] as object).sort()).toEqual(withoutCutToLength('PlanRevisionViewV1'))
    expect(Object.keys(revisionInput(ROLL_1)).sort()).toEqual(names('PlanRevisionInputViewV1'))
    expect([...(schema('PlanRevisionListViewV1').required ?? [])].sort()).toEqual(names('PlanRevisionListViewV1'))
    expect([...(schema('PlanRevisionViewV1').required ?? [])].sort()).toEqual(withoutCutToLength('PlanRevisionViewV1'))
    expectOptionalCutToLength('PlanRevisionViewV1', 'CutToLengthSpecViewV1')
    expect([...(schema('PlanRevisionInputViewV1').required ?? [])].sort()).toEqual(
      ['parentRollId', 'plannedKg', 'expectedLabelVersion'].sort(),
    )
    expect(schema('PlanRevisionInputViewV1').properties).toHaveProperty('technicalSnapshot')
    expect(decodePlanRevisions(history)).not.toBeNull()
    const plans = `${MANUFACTURING_BASE.replace(/^.*\/api\/v1/, '/api/v1')}/plans`
    const operation = document.paths[`${plans}/{planId}/revisions`]?.get as { readonly operationId?: string } | undefined
    expect(operation?.operationId).toBe('ListManufacturingPlanRevisionsV1')
    expect((document.paths[`${plans}/{planId}/revision`]?.post as { readonly operationId?: string } | undefined)
      ?.operationId).toBe('ReviseManufacturingPlanV1')
  })

  it('knows the five output classes by the server names the domain uses', () => {
    // The dictionary is typed `additionalProperties: string`; the names are the domain's enum.
    expect(schema('RollBalanceViewV1').properties?.outputsKg).toMatchObject({ additionalProperties: { type: 'string' } })
    expect([...OUTPUT_CLASSES]).toEqual(['GoodProduct', 'SecondQuality', 'UsableRemnant', 'Scrap', 'ProcessLoss'])
  })
})
