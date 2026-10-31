/**
 * SAT-D61…D70: the discount limit screen is bound to the REAL HTTP contract, not only to its fixtures. The routes it calls,
 * the bodies it sends and the fixtures its decoders accept are pinned to the canonical OpenAPI document the backend CI
 * regenerates (`contracts/openapi/goymen-erp-v1.json`). A backend rename, a new required member or a moved route turns
 * this red here instead of the screen silently decoding nothing. Not a live call.
 */

import { describe, expect, it } from 'vitest'
import {
  DISCOUNT_LIMIT_GROUPS_PATH,
  DISCOUNT_LIMIT_PEOPLE_PATH,
  LIMIT_PROBLEM,
  bindingPath,
  groupActivationPath,
  groupDeactivationPath,
  groupLimitCorrectionPath,
  groupLimitPath,
  groupLimitWithdrawalPath,
  groupNamePath,
} from './discountLimitContract'
import { binding, groupList } from './testHarness'

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
  return `/api/v1${builtPath.split('?')[0]}`.replace(`/groups/${ID}`, '/groups/{groupId}').replace(`/bindings/${ID}`, '/bindings/{userId}')
}

function operation(method: 'get' | 'post' | 'put', builtPath: string): Operation {
  const found = document.paths[template(builtPath)]?.[method]
  if (found === undefined) throw new Error(`OpenAPI has no ${method.toUpperCase()} ${template(builtPath)}`)
  return found
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
  expect((schema(schemaName).required ?? []).filter((key) => !keys.includes(key)), `${schemaName}: missing members`).toEqual([])
}

function requestSchemaOf(method: 'post' | 'put', builtPath: string): string {
  const reference = operation(method, builtPath).requestBody?.content?.['application/json']?.schema?.$ref ?? ''
  return reference.replace('#/components/schemas/', '')
}

describe('discount limit client ↔ canonical OpenAPI', () => {
  it('calls only routes the backend declares, with the method it uses', () => {
    operation('get', DISCOUNT_LIMIT_GROUPS_PATH)
    operation('post', DISCOUNT_LIMIT_GROUPS_PATH)
    operation('put', groupNamePath(ID))
    operation('post', groupDeactivationPath(ID))
    operation('post', groupActivationPath(ID))
    operation('put', groupLimitPath(ID))
    operation('post', groupLimitWithdrawalPath(ID))
    operation('post', groupLimitCorrectionPath(ID))
    operation('get', `${DISCOUNT_LIMIT_PEOPLE_PATH}?q=a&limit=25`)
    operation('get', bindingPath(ID))
    operation('put', bindingPath(ID))
  })

  it('sends exactly the declared request members', () => {
    expectFits({ code: 'A', name: 'A', reason: 'r' }, requestSchemaOf('post', DISCOUNT_LIMIT_GROUPS_PATH))
    expectFits({ expectedVersion: '1', name: 'A', reason: 'r' }, requestSchemaOf('put', groupNamePath(ID)))
    expectFits({ expectedVersion: '1', reason: 'r' }, requestSchemaOf('post', groupDeactivationPath(ID)))
    expectFits({ expectedVersion: '1', reason: 'r' }, requestSchemaOf('post', groupActivationPath(ID)))
    expectFits(
      { expectedVersion: '1', kind: 'Fixed', percent: '1', linkedGroupId: null, extraPoints: null, reason: 'r', effectiveFrom: null },
      requestSchemaOf('put', groupLimitPath(ID)),
    )
    expectFits({ expectedVersion: '1', limitVersion: '2', reason: 'r' }, requestSchemaOf('post', groupLimitWithdrawalPath(ID)))
    expectFits(
      {
        expectedVersion: '1',
        limitVersion: '2',
        kind: 'Fixed',
        percent: '1',
        linkedGroupId: null,
        extraPoints: null,
        reason: 'r',
        effectiveFrom: null,
      },
      requestSchemaOf('post', groupLimitCorrectionPath(ID)),
    )
    expectFits({ groupCodes: [], companies: [], reason: 'r' }, requestSchemaOf('put', bindingPath(ID)))
    expectFits({ companyId: ID, expectedVersion: '0' }, 'SalesDiscountLimitBindingCompanyVersionV1')
  })

  it('decodes fixtures shaped exactly like the declared answers', () => {
    const list = groupList()
    expectFits(list, 'SalesDiscountLimitGroupListV1')
    for (const group of list.groups) {
      expectFits(group, 'SalesDiscountLimitGroupViewV1')
      expectFits(group.effectiveLimit, 'SalesDiscountLimitEffectiveViewV1')
      if (group.limit !== null) expectFits(group.limit, 'SalesDiscountLimitDefinitionViewV1')
      for (const entry of group.scheduled) {
        expectFits(entry, 'SalesDiscountLimitScheduledViewV1')
        expectFits(entry.limit, 'SalesDiscountLimitDefinitionViewV1')
      }
    }

    const view = binding()
    expectFits(view, 'SalesDiscountLimitBindingViewV1')
    expectFits(view.companies[0], 'SalesDiscountLimitBindingCompanyViewV1')
    expectFits(view.companies[0].groups[0], 'SalesDiscountLimitBoundGroupV1')
    expectFits({ items: [], hasMore: false }, 'SalesDiscountLimitPersonListV1')
    expectFits({ id: ID, version: '1' }, 'SalesDiscountLimitGroupCreatedV1')
  })

  it('knows every problem code the routes declare', () => {
    const declared = new Set(
      Object.entries(document.paths)
        .filter(([route]) => route.startsWith('/api/v1/settings/sales-discount-limits'))
        .flatMap(([, methods]) => Object.values(methods))
        .flatMap((value) => Object.values(value.responses ?? {}))
        .flatMap((response) => response['x-erp-problem-codes'] ?? [])
        .filter((code) => code.startsWith('settings_')),
    )
    expect([...declared].sort()).toEqual(Object.values(LIMIT_PROBLEM).sort())
  })
})
