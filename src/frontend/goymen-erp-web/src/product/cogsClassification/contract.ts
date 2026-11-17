import { isBoundUuid, isInstant } from '../wireGuards'

export const COGS_SOURCE = 'product.cogs-classification.v1'
export const COGS_REASON_MAXIMUM = 1000
export type CogsKind = 1 | 2
export interface CogsView {
  readonly companyId: string
  readonly productId: string
  readonly productVersion: string
  readonly classificationVersion: string
  readonly classification: CogsKind | null
  readonly changedInCompanyId: string | null
  readonly updatedAtUtc: string | null
  readonly sourceVersion: string
}
export interface SetCogsRequest {
  readonly expectedProductVersion: string
  readonly expectedClassificationVersion: string
  readonly classification: CogsKind
  readonly reason: string
}
export interface CogsMutation {
  readonly classification: CogsView
  readonly isReplay: boolean
  readonly originalCorrelationId: string
}
const object = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null && !Array.isArray(value)
const exact = (value: Record<string, unknown>, keys: readonly string[]) => Object.keys(value).length === keys.length && keys.every((key) => Object.hasOwn(value, key))
export const sameCogsId = (left: string, right: string) => left.toLowerCase() === right.toLowerCase()
export const cogsVersion = (value: unknown): value is string => typeof value === 'string' && /^(0|[1-9]\d{0,18})$/.test(value) && BigInt(value) <= 9_223_372_036_854_775_807n
export const cogsKind = (value: unknown): value is CogsKind => value === 1 || value === 2

/** A class never implies a Finance account. No source/version/value defaults are manufactured. */
export function decodeCogsView(value: unknown, companyId: string, productId: string): CogsView | null {
  if (!object(value) || !exact(value, ['companyId', 'productId', 'productVersion', 'classificationVersion', 'classification', 'changedInCompanyId', 'updatedAtUtc', 'sourceVersion'])
    || !isBoundUuid(value.companyId) || !sameCogsId(value.companyId, companyId) || !isBoundUuid(value.productId) || !sameCogsId(value.productId, productId)
    || !cogsVersion(value.productVersion) || value.productVersion === '0' || !cogsVersion(value.classificationVersion)
    || value.sourceVersion !== COGS_SOURCE) return null
  if (value.classificationVersion === '0') {
    if (value.classification !== null || value.changedInCompanyId !== null || value.updatedAtUtc !== null) return null
  } else if (!cogsKind(value.classification) || !isBoundUuid(value.changedInCompanyId) || !isInstant(value.updatedAtUtc)
    || !/(Z|\+00:00)$/.test(value.updatedAtUtc)) return null
  // The card is global: its last change may legitimately have originated in another company.
  return value as unknown as CogsView
}
export function decodeCogsMutation(value: unknown, companyId: string, productId: string, request: SetCogsRequest): CogsMutation | null {
  if (!object(value) || !exact(value, ['classification', 'isReplay', 'originalCorrelationId']) || typeof value.isReplay !== 'boolean'
    || typeof value.originalCorrelationId !== 'string' || value.originalCorrelationId.trim() === '' || value.originalCorrelationId.length > 128) return null
  const view = decodeCogsView(value.classification, companyId, productId)
  if (view === null || view.classification !== request.classification || view.productVersion !== request.expectedProductVersion
    || view.classificationVersion === '0' || !cogsVersion(request.expectedClassificationVersion)) return null
  const next = (BigInt(request.expectedClassificationVersion) + 1n).toString()
  if (view.classificationVersion !== request.expectedClassificationVersion && view.classificationVersion !== next) return null
  if (view.classificationVersion === next && (view.changedInCompanyId === null || !sameCogsId(view.changedInCompanyId, companyId))) return null
  return { classification: view, isReplay: value.isReplay, originalCorrelationId: value.originalCorrelationId }
}
