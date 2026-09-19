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
import { decodeOrder, factBody, FACT_KINDS, OUTPUT_CLASSES, type FactSnapshot } from './contract'
import { MANUFACTURING_BASE } from './api'
import { order, roll, ROLL_1 } from './testHarness'

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

  it('sends exactly the properties the fact requests declare', () => {
    const snapshot: FactSnapshot = {
      kind: 'input-issues',
      factId: 'f0000000-0000-4000-8000-000000000001',
      idempotencyKey: 'k',
      expectedVersion: 1,
      parentRollId: ROLL_1,
      kg: '1',
      occurredAtUtc: '2026-09-18T09:00:00.000Z',
      outputClass: null,
    }
    expect(Object.keys(factBody(snapshot)).sort()).toEqual(names('RecordFactRequestV1'))
    expect(Object.keys(factBody({ ...snapshot, kind: 'outputs', outputClass: 'Scrap' })).sort()).toEqual(
      names('DeclareOutputRequestV1'),
    )
  })

  it('calls routes the API declares: the order read and one POST per fact kind', () => {
    const base = `${MANUFACTURING_BASE.replace(/^.*\/api\/v1/, '/api/v1')}/orders/{orderId}`
    expect(document.paths[base]).toHaveProperty('get')
    for (const kind of FACT_KINDS) {
      expect(document.paths[`${base}/${kind}`], kind).toHaveProperty('post')
    }
  })

  it('knows the five output classes by the server names the domain uses', () => {
    // The dictionary is typed `additionalProperties: string`; the names are the domain's enum.
    expect(schema('RollBalanceViewV1').properties?.outputsKg).toMatchObject({ additionalProperties: { type: 'string' } })
    expect([...OUTPUT_CLASSES]).toEqual(['GoodProduct', 'SecondQuality', 'UsableRemnant', 'Scrap', 'ProcessLoss'])
  })
})
