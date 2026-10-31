/**
 * MOD-03: the tax master screen is bound to the REAL HTTP contract — the routes it calls, the bodies it sends and the
 * problem codes it knows are pinned to the canonical OpenAPI document (`contracts/openapi/goymen-erp-v1.json`). Not a live call.
 */

import { describe, expect, it } from 'vitest'
import {
  ITEM_TYPES,
  TAX_MASTER_PATH,
  TAX_PROBLEM,
  codeListPath,
  codeListWithdrawalPath,
  itemTypeDefaultPath,
  itemTypeDefaultWithdrawalPath,
  WITHHOLDING_THRESHOLD_PATH,
  thresholdWithdrawalPath,
} from './taxMasterContract'

interface Schema {
  readonly properties?: Record<string, unknown>
  readonly required?: readonly string[]
}

interface Operation {
  readonly requestBody?: { readonly content?: { readonly 'application/json'?: { readonly schema?: { readonly $ref?: string } } } }
  readonly responses?: Record<string, { readonly 'x-erp-problem-codes'?: readonly string[] }>
}

const nodeProcess = (globalThis as unknown as {
  readonly process: { readonly cwd: () => string; readonly getBuiltinModule: (name: string) => unknown }
}).process
const fs = nodeProcess.getBuiltinModule('node:fs') as { readFileSync(path: string, encoding: 'utf8'): string }
const path = nodeProcess.getBuiltinModule('node:path') as { resolve(...segments: string[]): string }
const document = JSON.parse(
  fs.readFileSync(path.resolve(nodeProcess.cwd(), '../../../contracts/openapi/goymen-erp-v1.json'), 'utf8'),
) as {
  readonly paths: Record<string, Record<string, Operation>>
  readonly components: { readonly schemas: Record<string, Schema> }
}

const ID = '11111111-1111-4111-8111-111111111111'

function template(builtPath: string): string {
  return `/api/v1${builtPath}`
    .replace(`/item-type-defaults/records/${ID}`, '/item-type-defaults/records/{recordId}')
    .replace(/\/item-type-defaults\/(Steel|Scrap|Service)$/, '/item-type-defaults/{itemType}')
    .replace(`/withholding-threshold/records/${ID}`, '/withholding-threshold/records/{recordId}')
    .replace(/\/code-lists\/(Exemption|Withholding)\/versions/, '/code-lists/{listKind}/versions')
    .replace(/\/versions\/7\/withdrawal$/, '/versions/{listVersion}/withdrawal')
}

function operation(method: 'get' | 'post', builtPath: string): Operation {
  const found = document.paths[template(builtPath)]?.[method]
  if (found === undefined) throw new Error(`OpenAPI has no ${method.toUpperCase()} ${template(builtPath)}`)
  return found
}

function expectFits(example: object, method: 'post', builtPath: string): void {
  const reference = operation(method, builtPath).requestBody?.content?.['application/json']?.schema?.$ref ?? ''
  const schema = document.components.schemas[reference.replace('#/components/schemas/', '')]
  expect(schema, `no request schema for ${builtPath}`).toBeDefined()
  const declared = Object.keys(schema.properties ?? {})
  const keys = Object.keys(example)
  expect(keys.filter((key) => !declared.includes(key))).toEqual([])
  expect((schema.required ?? []).filter((key) => !keys.includes(key))).toEqual([])
}

describe('tax master client ↔ canonical OpenAPI', () => {
  it('calls only routes the backend declares, with the method it uses', () => {
    operation('get', TAX_MASTER_PATH)
    for (const itemType of ITEM_TYPES) operation('post', itemTypeDefaultPath(itemType))
    operation('post', itemTypeDefaultWithdrawalPath(ID))
    operation('post', codeListPath('Exemption'))
    operation('post', codeListWithdrawalPath('Withholding', '7'))
    operation('post', WITHHOLDING_THRESHOLD_PATH)
    operation('post', thresholdWithdrawalPath(ID))
  })

  it('sends exactly the declared request members', () => {
    expectFits(
      {
        expectedVersion: '0',
        vatApplies: true,
        vatPercent: '20',
        exemptionCode: null,
        withholdingApplies: true,
        withholdingPercent: '50',
        withholdingCode: 'T1',
        reason: 'r',
        effectiveFrom: '2026-11-01',
      },
      'post',
      itemTypeDefaultPath('Steel'),
    )
    expectFits({ expectedVersion: '1', reason: 'r' }, 'post', itemTypeDefaultWithdrawalPath(ID))
    expectFits({ expectedVersion: '0', entries: [], reason: 'r' }, 'post', codeListPath('Withholding'))
    expectFits({ expectedVersion: '1', reason: 'r' }, 'post', codeListWithdrawalPath('Withholding', '7'))
    expectFits({ expectedVersion: '0', amount: '1', currencyCode: 'TRY', reason: 'r' }, 'post', WITHHOLDING_THRESHOLD_PATH)
    expectFits({ expectedVersion: '1', reason: 'r' }, 'post', thresholdWithdrawalPath(ID))
  })

  it('knows every problem code the routes declare', () => {
    const declared = new Set(
      Object.entries(document.paths)
        .filter(([route]) => route.startsWith('/api/v1/settings/tax-master'))
        .flatMap(([, methods]) => Object.values(methods))
        .flatMap((value) => Object.values(value.responses ?? {}))
        .flatMap((response) => response['x-erp-problem-codes'] ?? [])
        .filter((code) => code.startsWith('settings_')),
    )
    expect([...declared].sort()).toEqual(Object.values(TAX_PROBLEM).sort())
  })
})
