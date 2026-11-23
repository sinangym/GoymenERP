import { describe, expect, it } from 'vitest'
import { CREATE_ALLOCATION_422_CODES, CREATE_ALLOCATION_INSUFFICIENT_FREE_PIECES } from '../sales/allocationContract'
import {
  ALLOCATION_FIELDS, ALLOCATION_OPTIONAL_FIELDS, allocationChangeRefusalText,
} from './allocationChangeContract'
import { ALLOCATION_PIECE_REFUSAL_TEXT } from './allocationPieceText'

// KLN-D119 (AK-4b): the piece members of the allocation, suggestion and label-balance contracts, pinned against the
// canonical OpenAPI document. Read with Node's own fs (the file lives outside the web package), as
// src/manufacturing/openapiContract.test.ts does. Vitest runs from the web package.
const nodeProcess = (globalThis as unknown as {
  readonly process: { readonly cwd: () => string; readonly getBuiltinModule: (name: string) => unknown }
}).process
const nodeFs = nodeProcess.getBuiltinModule('node:fs') as { readFileSync(path: string, encoding: 'utf8'): string }
const nodePath = nodeProcess.getBuiltinModule('node:path') as { resolve(...segments: string[]): string }

interface Schema {
  readonly required?: readonly string[]
  readonly properties?: Record<string, { readonly type?: string | readonly string[] }>
}
interface Operation { readonly responses?: Record<string, Record<string, unknown>> }
const document = JSON.parse(nodeFs.readFileSync(
  nodePath.resolve(nodeProcess.cwd(), '../../../contracts/openapi/goymen-erp-v1.json'), 'utf8')) as {
  readonly paths: Record<string, Record<string, Operation>>
  readonly components: { readonly schemas: Record<string, Schema> }
}

function schema(name: string): Schema {
  const found = document.components.schemas[name]
  if (found === undefined) throw new Error(`OpenAPI has no schema ${name}.`)
  return found
}
const names = (name: string) => Object.keys(schema(name).properties ?? {}).sort()
const required = (name: string) => [...(schema(name).required ?? [])].sort()
const optional = (name: string) => names(name).filter((member) => !required(name).includes(member))

function codes(path: string, status: string): readonly string[] {
  const declared = document.paths[path]?.post?.responses?.[status]?.['x-erp-problem-codes']
  if (!Array.isArray(declared)) throw new Error(`OpenAPI declares no ${status} problem codes for ${path}.`)
  return declared as string[]
}

const CREATE = '/api/v1/inventory/physical-allocations'
const CHANGE = '/api/v1/inventory/physical-allocations/{allocationId}/change'
const PIECE_422 = [
  'inventory_allocation_piece_count_invalid', 'inventory_allocation_piece_count_not_tracked',
  'inventory_allocation_piece_count_required',
]
const PIECE_409 = 'inventory_allocation_insufficient_free_pieces'

describe('KLN-D119 piece members against the published OpenAPI contract', () => {
  it('knows every member of the allocation view, the piece figures and the substitute flag as optional ones', () => {
    expect(required('InventoryPhysicalAllocationViewV1')).toEqual([...ALLOCATION_FIELDS].sort())
    expect(optional('InventoryPhysicalAllocationViewV1')).toEqual([...ALLOCATION_OPTIONAL_FIELDS].sort())
  })

  it('sends pieceCount as an optional integer on create and on change', () => {
    for (const name of ['CreateInventoryPhysicalAllocationRequestV1', 'ChangeInventoryPhysicalAllocationRequestV1']) {
      expect(optional(name), name).toContain('pieceCount')
      expect(schema(name).properties?.pieceCount?.type, name).toContain('integer')
    }
  })

  it('reads the optional piece members of the suggestion, the label balances and a position', () => {
    expect(optional('InventoryAllocationSuggestionV1')).toEqual(['unreservedPieces'])
    expect(optional('InventoryLabelBalancesV1')).toEqual(['blockedPieces', 'freePieces', 'physicalPieces'])
    expect(optional('InventoryPositionV1')).toEqual(['pieceCount'])
    expect(required('InventoryLabelBalancesV1')).toEqual(['blockedKg', 'freeKg', 'labelId', 'physicalKg', 'positions'])
    expect(required('InventoryPositionV1')).toEqual([
      'block', 'custodianId', 'custodianKind', 'locationId', 'ownerId', 'ownerKind', 'productId', 'quantityKg',
    ])
  })

  it('has a Turkish text for each piece refusal the create and change commands declare', () => {
    for (const path of [CREATE, CHANGE]) {
      expect(codes(path, '422').filter((code) => code.includes('piece')).sort(), path).toEqual(PIECE_422)
      expect(codes(path, '409').filter((code) => code.includes('pieces')), path).toEqual([PIECE_409])
    }
    expect(Object.keys(ALLOCATION_PIECE_REFUSAL_TEXT).sort()).toEqual([PIECE_409, ...PIECE_422].sort())
    for (const code of PIECE_422) expect(allocationChangeRefusalText(422, code)).toBe(ALLOCATION_PIECE_REFUSAL_TEXT[code])
    expect(allocationChangeRefusalText(409, PIECE_409)).toBe(ALLOCATION_PIECE_REFUSAL_TEXT[PIECE_409])
    expect(CREATE_ALLOCATION_INSUFFICIENT_FREE_PIECES).toBe(PIECE_409)
    expect(Object.values(CREATE_ALLOCATION_422_CODES).filter((code) => code.includes('piece')).sort()).toEqual(PIECE_422)
  })
})
