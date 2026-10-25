/**
 * TEK-08: the Sales screens are bound to the REAL HTTP contract, not only to the pinned examples.
 *
 * The component tests drive the pinned JSON examples in ./examples through the strict decoders. This file pins those
 * examples, the request bodies this module sends and every route it calls to the canonical OpenAPI document the backend
 * CI regenerates (`contracts/openapi/goymen-erp-v1.json`, drift-checked on every PR). A backend rename, a new required
 * member or a moved route turns this red here instead of the screen silently decoding nothing.
 *
 * WHAT THIS IS NOT: a live call. The real round trip is the `s04` browser suite. The Manufacturing lane's
 * `openapiContract.test.ts` is the pattern.
 */

import { describe, expect, it } from 'vitest'
import contactInformationResponse from './examples/contact-information-response.json'
import createRequest from './examples/create-request.json'
import createResponse from './examples/create-response.json'
import currenciesResponse from './examples/currencies-response.json'
import detailResponse from './examples/detail-response.json'
import listResponse from './examples/list-response.json'
import revisionsResponse from './examples/revisions-response.json'
import {
  SALES_DRAFT_PATH,
  salesDraftCancellationImpactPath,
  salesDraftCancellationPath,
  salesDraftCommandPath,
  salesDraftCommercialRevisionPath,
  salesDraftDeliveryProgressPath,
  salesDraftDeliverySchedulePath,
  salesDraftDeliverySchedulesPath,
  salesDraftDetailPath,
  salesDraftLoadingProgressPath,
  salesDraftPriceFloorExceptionApprovalPath,
  salesDraftQuantityRevisionPath,
  salesDraftRevisionsPath,
  type DraftStateCommand,
} from './contract'
import {
  SALES_REFERENCE_ACCOUNTS_PATH,
  SALES_REFERENCE_CONTACT_INFORMATION_PATH,
  SALES_REFERENCE_CURRENCIES_PATH,
  SALES_REFERENCE_PRODUCTS_PATH,
} from './api'
import { salesDraftLinePriceReferencesPath, salesDraftLinePricingPath } from './linePricing'
import { remainderChangesPath, remainderDecisionPath } from './remainderChange'

interface Schema {
  readonly properties?: Record<string, unknown>
  readonly required?: readonly string[]
}

interface Operation {
  readonly requestBody?: { readonly content?: { readonly 'application/json'?: { readonly schema?: { readonly $ref?: string } } } }
  readonly responses?: Record<string, { readonly content?: { readonly 'application/json'?: { readonly schema?: unknown } } }>
}

/**
 * Read with Node's own `fs` (the test runner's process), not through a module import: the canonical document lives at the
 * repository root, outside the web package Vite serves. Vitest runs from the web package.
 */
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

const API_PREFIX = '/api/v1'
const REF_PREFIX = '#/components/schemas/'

function schema(name: string): Schema {
  const found = document.components.schemas[name]
  if (found === undefined) throw new Error(`OpenAPI has no schema ${name}`)
  return found
}

/** A route the client builds, turned back into the template OpenAPI names (`{id}`, `{lineId}`, `{changeNo}`). */
function template(builtPath: string): string {
  return `${API_PREFIX}${builtPath}`
    .replace(/\/ID(?=\/|$)/g, '/{id}')
    .replace(/\/LINE(?=\/|$)/g, '/{lineId}')
    .replace(/\/7(?=\/|$)/g, '/{changeNo}')
}

function operation(method: 'get' | 'post', builtPath: string): Operation {
  const route = template(builtPath)
  const found = document.paths[route]?.[method]
  if (found === undefined) throw new Error(`OpenAPI has no ${method.toUpperCase()} ${route}`)
  return found
}

function requestSchema(builtPath: string): string {
  const reference = operation('post', builtPath).requestBody?.content?.['application/json']?.schema?.$ref
  if (reference === undefined || !reference.startsWith(REF_PREFIX)) throw new Error(`POST ${template(builtPath)} has no component body`)
  return reference.slice(REF_PREFIX.length)
}

/** Every member the example carries is declared, and every member the schema requires is in the example. */
function expectFits(example: object, schemaName: string): void {
  const declared = Object.keys(schema(schemaName).properties ?? {})
  const keys = Object.keys(example)
  expect(keys.filter((key) => !declared.includes(key)), `${schemaName}: undeclared members`).toEqual([])
  expect((schema(schemaName).required ?? []).filter((key) => !keys.includes(key)), `${schemaName}: missing required members`).toEqual([])
}

const COMMANDS: readonly DraftStateCommand[] = ['submission', 'approval', 'rejection', 'withdrawal', 'customer-acceptance']

describe('Sales client ↔ canonical OpenAPI', () => {
  it('calls only routes the backend declares, with the method it uses', () => {
    const reads = [
      SALES_DRAFT_PATH,
      salesDraftDetailPath('ID'),
      salesDraftRevisionsPath('ID'),
      salesDraftDeliverySchedulesPath('ID'),
      salesDraftDeliveryProgressPath('ID'),
      salesDraftLoadingProgressPath('ID'),
      salesDraftCancellationImpactPath('ID'),
      salesDraftCancellationPath('ID'),
      salesDraftLinePricingPath('ID'),
      salesDraftLinePriceReferencesPath('ID', 'LINE'),
      remainderChangesPath('ID'),
      SALES_REFERENCE_ACCOUNTS_PATH,
      SALES_REFERENCE_CURRENCIES_PATH,
      SALES_REFERENCE_CONTACT_INFORMATION_PATH,
      SALES_REFERENCE_PRODUCTS_PATH,
    ]
    const writes = [
      SALES_DRAFT_PATH,
      salesDraftCommercialRevisionPath('ID'),
      salesDraftPriceFloorExceptionApprovalPath('ID'),
      salesDraftQuantityRevisionPath('ID'),
      salesDraftDeliverySchedulePath('ID'),
      salesDraftCancellationPath('ID'),
      remainderChangesPath('ID'),
      remainderDecisionPath('ID', 7, 'approval'),
      remainderDecisionPath('ID', 7, 'rejection'),
      ...COMMANDS.map((command) => salesDraftCommandPath('ID', command)),
    ]
    for (const route of reads) expect(() => operation('get', route), route).not.toThrow()
    for (const route of writes) expect(() => operation('post', route), route).not.toThrow()
  })

  it('sends each command body as the component schema the backend reads', () => {
    expect(requestSchema(SALES_DRAFT_PATH)).toBe('CreateSalesOrderDraftRequest')
    expectFits(createRequest, 'CreateSalesOrderDraftRequest')
    expect(requestSchema(salesDraftCancellationPath('ID'))).toBe('SalesDraftCancelRequest')
    expectFits({ expectedVersion: '4', reason: 'r', impactToken: 'a'.repeat(64) }, 'SalesDraftCancelRequest')
    expect(requestSchema(salesDraftQuantityRevisionPath('ID'))).toBe('SalesDraftQuantityRevisionRequest')
    expect(requestSchema(salesDraftDeliverySchedulePath('ID'))).toBe('SalesDraftDeliveryScheduleRequest')
    expect(requestSchema(salesDraftCommercialRevisionPath('ID'))).toBe('SalesDraftCommercialRevisionRequest')
    expect(requestSchema(salesDraftPriceFloorExceptionApprovalPath('ID'))).toBe('SalesDraftPriceFloorExceptionApprovalRequest')
    expectFits({ expectedVersion: '3', lines: [] }, 'SalesDraftPriceFloorExceptionApprovalRequest')
    expect(requestSchema(remainderChangesPath('ID'))).toBe('SalesDraftRemainderChangeRequest')
    expect(requestSchema(remainderDecisionPath('ID', 7, 'approval'))).toBe('SalesDraftRemainderDecisionRequest')
    for (const command of COMMANDS) {
      expect(requestSchema(salesDraftCommandPath('ID', command)), command).toBe('SalesDraftStateCommandRequest')
    }
    expectFits({ expectedVersion: '4' }, 'SalesDraftStateCommandRequest')
    expectFits({ expectedVersion: '4', reason: 'r' }, 'SalesDraftStateCommandRequest')
  })

  it('pins every example the component tests decode to the schema the server answers with', () => {
    expectFits(detailResponse, 'SalesOrderDraftDetailView')
    expectFits(detailResponse.draft, 'SalesOrderDraftSummaryView')
    expectFits(detailResponse.approval, 'SalesDraftApprovalV1')
    expectFits(detailResponse.customerAcceptance, 'SalesDraftCustomerAcceptanceV1')
    for (const line of detailResponse.lines) expectFits(line, 'SalesOrderDraftLineDetail')
    expectFits(createResponse, 'SalesOrderDraftResponse')
    expectFits(listResponse, 'SalesOrderDraftListResponse')
    for (const item of listResponse.items) expectFits(item, 'SalesOrderDraftSummaryView')
    expectFits(revisionsResponse, 'SalesOrderDraftRevisionsResponse')
    expectFits(currenciesResponse, 'SalesCurrencyOptionsV1')
    expectFits(contactInformationResponse, 'SalesContactInformationOptionsV1')
  })

  it('answers the detail read as the plain view or the opted-in party-display view, and nothing else', () => {
    const answer = operation('get', salesDraftDetailPath('ID')).responses?.['200']?.content?.['application/json']?.schema as {
      readonly anyOf?: readonly { readonly $ref: string }[]
    }
    expect((answer.anyOf ?? []).map((entry) => entry.$ref.slice(REF_PREFIX.length)).sort())
      .toEqual(['SalesOrderDraftDetailView', 'SalesOrderDraftReadDetailViewV1'])
    expect(Object.keys(schema('SalesOrderDraftReadDetailViewV1').properties ?? {})).toContain('partyDisplay')
  })
})
