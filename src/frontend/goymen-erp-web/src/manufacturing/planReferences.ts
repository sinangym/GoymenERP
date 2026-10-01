/**
 * Inventory-owned parent-roll candidate contract consumed by Manufacturing plan creation.
 *
 * These figures describe physical state. They are never treated as availability, a reservation,
 * or permission to consume a roll; the plan command sends labelId + labelVersion and the server
 * revalidates that reference immediately before saving.
 */

import { API_BASE, devHeaders } from './api'
import type { ViewOutcome } from '../shell/uiState'

export const PARENT_ROLL_CANDIDATES_PATH = `${API_BASE}/inventory/references/parent-rolls`
export const PARENT_ROLL_CANDIDATE_LIMIT = 25
/** The owner's widest window (1..100), used to find ONE preselected label among the code matches. */
export const PARENT_ROLL_PRESELECT_LIMIT = 100

export interface ParentRollLot {
  readonly id: string
  readonly heatNumber: string | null
  readonly coilNumber: string | null
}

export interface ParentRollPosition {
  readonly ownerKind: string
  readonly ownerId: string
  readonly custodianKind: string
  readonly custodianId: string
  readonly locationId: string
  readonly productId: string
  readonly block: string
  readonly quantityKg: string
}

export interface ParentRollCandidate {
  readonly companyId: string
  readonly labelId: string
  readonly labelVersion: string
  readonly labelCode: string
  readonly productId: string
  /** Immutable Product-card version captured by Inventory when the label was created. */
  readonly productVersion: string
  readonly lot: ParentRollLot
  readonly heatNumber: string | null
  readonly coilNumber: string | null
  readonly documentStatus: string
  readonly requiredDocuments: readonly string[]
  readonly attachedDocuments: readonly string[]
  readonly physicalKg: string
  /** Free physical buckets only; not an available or usable quantity. */
  readonly unblockedPhysicalKg: string
  readonly blockedPhysicalKg: string
  readonly positions: readonly ParentRollPosition[]
  readonly draftSelectable: boolean
  readonly draftRefusalCode: string | null
  readonly productionUseRequiresRevalidation: true
}

export interface ParentRollCandidates {
  readonly items: readonly ParentRollCandidate[]
  readonly searchWindowFull: boolean
}

const GUID = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/
const VERSION = /^[1-9][0-9]*$/
const KG = /^(0|[1-9][0-9]*)(\.[0-9]*[1-9])?$/
const PRODUCT_VERSION_MAX_LENGTH = 128
const CONTROL = /[\u0000-\u001f\u007f-\u009f]/

type Json = Record<string, unknown>

function record(value: unknown): Json | null {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
    ? (value as Json)
    : null
}

function exact(value: Json, names: readonly string[]): boolean {
  const actual = Object.keys(value).sort()
  const expected = [...names].sort()
  return actual.length === expected.length && actual.every((name, index) => name === expected[index])
}

function guid(value: unknown): value is string {
  return typeof value === 'string' && GUID.test(value)
}

function text(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0
}

/** Product versions are opaque owner tokens, not Inventory's numeric label version. */
function productVersion(value: unknown): value is string {
  return typeof value === 'string'
    && value.length > 0
    && value.length <= PRODUCT_VERSION_MAX_LENGTH
    && value === value.trim()
    && !CONTROL.test(value)
}

function nullableText(value: unknown): value is string | null {
  return value === null || text(value)
}

function textList(value: unknown): value is string[] {
  return Array.isArray(value) && value.every(text)
}

function decodeLot(value: unknown): ParentRollLot | null {
  const source = record(value)
  if (
    source === null ||
    !exact(source, ['id', 'heatNumber', 'coilNumber']) ||
    !guid(source.id) ||
    !nullableText(source.heatNumber) ||
    !nullableText(source.coilNumber)
  ) {
    return null
  }
  return source as unknown as ParentRollLot
}

function decodePosition(value: unknown): ParentRollPosition | null {
  const source = record(value)
  if (
    source === null ||
    !exact(source, [
      'ownerKind',
      'ownerId',
      'custodianKind',
      'custodianId',
      'locationId',
      'productId',
      'block',
      'quantityKg',
    ]) ||
    !text(source.ownerKind) ||
    !guid(source.ownerId) ||
    !text(source.custodianKind) ||
    !guid(source.custodianId) ||
    !guid(source.locationId) ||
    !guid(source.productId) ||
    !text(source.block) ||
    typeof source.quantityKg !== 'string' ||
    !KG.test(source.quantityKg)
  ) {
    return null
  }
  return source as unknown as ParentRollPosition
}

function decodeCandidate(value: unknown): ParentRollCandidate | null {
  const source = record(value)
  if (
    source === null ||
    !exact(source, [
      'companyId',
      'labelId',
      'labelVersion',
      'labelCode',
      'productId',
      'productVersion',
      'lot',
      'heatNumber',
      'coilNumber',
      'documentStatus',
      'requiredDocuments',
      'attachedDocuments',
      'physicalKg',
      'unblockedPhysicalKg',
      'blockedPhysicalKg',
      'positions',
      'draftSelectable',
      'draftRefusalCode',
      'productionUseRequiresRevalidation',
    ]) ||
    !guid(source.companyId) ||
    !guid(source.labelId) ||
    typeof source.labelVersion !== 'string' ||
    !VERSION.test(source.labelVersion) ||
    !text(source.labelCode) ||
    !guid(source.productId) ||
    !productVersion(source.productVersion) ||
    !nullableText(source.heatNumber) ||
    !nullableText(source.coilNumber) ||
    !text(source.documentStatus) ||
    !textList(source.requiredDocuments) ||
    !textList(source.attachedDocuments) ||
    typeof source.physicalKg !== 'string' ||
    !KG.test(source.physicalKg) ||
    typeof source.unblockedPhysicalKg !== 'string' ||
    !KG.test(source.unblockedPhysicalKg) ||
    typeof source.blockedPhysicalKg !== 'string' ||
    !KG.test(source.blockedPhysicalKg) ||
    !Array.isArray(source.positions) ||
    typeof source.draftSelectable !== 'boolean' ||
    !nullableText(source.draftRefusalCode) ||
    source.productionUseRequiresRevalidation !== true
  ) {
    return null
  }
  const lot = decodeLot(source.lot)
  const positions = source.positions.map(decodePosition)
  if (
    lot === null ||
    positions.some((position) => position === null) ||
    positions.some((position) => position!.productId !== source.productId) ||
    (source.draftSelectable ? source.draftRefusalCode !== null : source.draftRefusalCode === null)
  ) {
    return null
  }
  return { ...source, lot, positions: positions as ParentRollPosition[] } as unknown as ParentRollCandidate
}

export function decodeParentRollCandidates(value: unknown): ParentRollCandidates | null {
  const source = record(value)
  if (
    source === null ||
    !exact(source, ['items', 'searchWindowFull']) ||
    !Array.isArray(source.items) ||
    typeof source.searchWindowFull !== 'boolean'
  ) {
    return null
  }
  const items = source.items.map(decodeCandidate)
  if (items.some((item) => item === null)) return null
  const candidates = items as ParentRollCandidate[]
  if (new Set(candidates.map((item) => item.labelId)).size !== candidates.length) return null
  return { items: candidates, searchWindowFull: source.searchWindowFull }
}

async function bodyOf(response: Response): Promise<unknown> {
  try {
    return await response.json()
  } catch {
    return null
  }
}

/** Reads the Inventory owner's bounded candidate window; the command still revalidates selection. */
export async function fetchParentRollCandidates(
  companyId: string,
  q = '',
  signal?: AbortSignal,
  limit: number = PARENT_ROLL_CANDIDATE_LIMIT,
): Promise<ViewOutcome<ParentRollCandidates>> {
  const query = new URLSearchParams({ limit: String(limit) })
  const search = q.trim()
  if (search !== '') query.set('q', search)
  try {
    const response = await fetch(`${PARENT_ROLL_CANDIDATES_PATH}?${query.toString()}`, {
      headers: { ...devHeaders(), 'X-Company-Id': companyId },
      cache: 'no-store',
      signal,
    })
    const body = await bodyOf(response)
    if (response.status !== 200) return { type: 'http', status: response.status, body }
    const candidates = decodeParentRollCandidates(body)
    return candidates === null || candidates.items.some((item) => item.companyId !== companyId)
      ? { type: 'malformed' }
      : { type: 'ok-single', data: candidates }
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    return navigator.onLine ? { type: 'network-error' } : { type: 'offline' }
  }
}
