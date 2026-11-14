/**
 * F-025 / B-11: the exchange rate screen is bound to the REAL HTTP contract — the routes it calls, the bodies it sends,
 * the members it decodes and the problem codes it knows are pinned to the canonical OpenAPI document
 * (`contracts/openapi/goymen-erp-v1.json`). Not a live call.
 */

import { describe, expect, it } from 'vitest'
import {
  EXCHANGE_RATE_IMPORTS_PATH,
  EXCHANGE_RATE_PROBLEM,
  exchangeRateCorrectionPath,
  exchangeRateHalfDayMarkPath,
  exchangeRateListPath,
  exchangeRateResolutionPath,
  exchangeRateReverificationPath,
} from './exchangeRateContract'

interface Schema {
  readonly properties?: Record<string, unknown>
  readonly required?: readonly string[]
}

interface Operation {
  readonly requestBody?: { readonly content?: { readonly 'application/json'?: { readonly schema?: { readonly $ref?: string } } } }
  readonly responses?: Record<
    string,
    {
      readonly 'x-erp-problem-codes'?: readonly string[]
      readonly content?: Record<string, { readonly schema?: { readonly $ref?: string } }>
    }
  >
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

const DAY = '2026-11-13'

function template(builtPath: string): string {
  return `/api/v1${builtPath.split('?')[0]}`.replace(/\/days\/\d{4}-\d{2}-\d{2}\//, '/days/{date}/')
}

function operation(method: 'get' | 'post', builtPath: string): Operation {
  const found = document.paths[template(builtPath)]?.[method]
  if (found === undefined) throw new Error(`OpenAPI has no ${method.toUpperCase()} ${template(builtPath)}`)
  return found
}

function expectFits(example: object, builtPath: string): void {
  const reference = operation('post', builtPath).requestBody?.content?.['application/json']?.schema?.$ref ?? ''
  const schema = document.components.schemas[reference.replace('#/components/schemas/', '')]
  expect(schema, `no request schema for ${builtPath}`).toBeDefined()
  const declared = Object.keys(schema.properties ?? {})
  const keys = Object.keys(example)
  expect(keys.filter((key) => !declared.includes(key))).toEqual([])
  expect((schema.required ?? []).filter((key) => !keys.includes(key))).toEqual([])
}

function okReference(method: 'get' | 'post', builtPath: string): string | undefined {
  return operation(method, builtPath).responses?.['200']?.content?.['application/json']?.schema?.$ref
}

const members = (name: string) => Object.keys(document.components.schemas[name]?.properties ?? {}).sort()

describe('exchange rate client ↔ canonical OpenAPI', () => {
  it('calls only routes the backend declares, with the method and success status it uses', () => {
    expect(okReference('get', exchangeRateListPath(DAY, DAY))).toBe('#/components/schemas/ExchangeRateDayListV1')
    expect(okReference('get', exchangeRateResolutionPath(DAY, 'USD'))).toBe('#/components/schemas/ExchangeRateResolutionV1')
    for (const command of [exchangeRateCorrectionPath(DAY), exchangeRateHalfDayMarkPath(DAY), exchangeRateReverificationPath(DAY)]) {
      expect(Object.keys(operation('post', command).responses ?? {})).toContain('204')
    }
    expect(okReference('post', EXCHANGE_RATE_IMPORTS_PATH)).toBe('#/components/schemas/ExchangeRateImportSummaryV1')
  })

  it('sends exactly the declared request members', () => {
    expectFits({ expectedVersion: '2', currencyCode: 'USD', sellingRate: '34.5', reason: 'r' }, exchangeRateCorrectionPath(DAY))
    expectFits({ expectedVersion: '0', state: 'HalfDay', reason: 'r' }, exchangeRateHalfDayMarkPath(DAY))
    expectFits({ expectedVersion: '2', reason: 'r' }, exchangeRateReverificationPath(DAY))
    expectFits({ from: DAY, to: DAY }, EXCHANGE_RATE_IMPORTS_PATH)
  })

  it('decodes exactly the members the reads and the import answer (response schemas)', () => {
    // Every member below is read by the decoders in ./exchangeRateContract.ts; a new or renamed member must be decoded first.
    expect(members('ExchangeRateDayListV1')).toEqual([
      'bulletinSourceEnabled', 'companyId', 'days', 'from', 'halfDaySourceEnabled', 'to',
    ])
    expect(members('ExchangeRateDayViewV1')).toEqual([
      'bulletinState', 'date', 'halfDayProvenance', 'halfDayState', 'rates', 'records', 'version',
    ])
    expect(members('ExchangeRateValueViewV1')).toEqual(['currencyCode', 'provenance', 'recordId', 'recordVersion', 'sellingRate'])
    expect(members('ExchangeRateLineV1')).toEqual(['currencyCode', 'sellingRate'])
    expect(members('ExchangeRateRecordViewV1')).toEqual([
      'kind', 'provenance', 'rates', 'reason', 'recordId', 'recordVersion', 'recordedAtUtc', 'recordedBy', 'sourceReference', 'state',
    ])
    expect(members('ExchangeRateImportSummaryV1')).toEqual(['days'])
    expect(members('ExchangeRateImportDayV1')).toEqual(['bulletinOutcome', 'date', 'halfDayOutcome'])
    expect(members('ExchangeRateResolutionV1')).toEqual([
      'bulletinDate', 'code', 'currencyCode', 'halfDayRuleApplied', 'previousFullBusinessDay', 'provenance', 'rateDay',
      'recordId', 'recordVersion', 'sellingRate', 'source', 'sourceCurrencyId', 'state', 'stoppedAt', 'targetCurrencyId',
    ])
  })

  it('knows every exchange rate problem code the routes declare, and only those', () => {
    const declared = new Set(
      Object.entries(document.paths)
        .filter(([route]) => route.startsWith('/api/v1/settings/exchange-rates'))
        .flatMap(([, methods]) => Object.values(methods))
        .flatMap((value) => Object.values(value.responses ?? {}))
        .flatMap((response) => response['x-erp-problem-codes'] ?? [])
        .filter((code) => code.startsWith('exchange_rate_') || code.startsWith('settings_')),
    )
    // `exchange_rate_source_disabled` is a code-less-in-OpenAPI 503 the backend may name; the client treats any 5xx as an
    // unknown outcome, so it is known but not declared.
    const known = Object.values(EXCHANGE_RATE_PROBLEM).filter((code) => code !== EXCHANGE_RATE_PROBLEM.sourceDisabled)
    expect([...declared].sort()).toEqual([...known].sort())
  })
})
