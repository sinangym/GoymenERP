import { isDateOnly } from '../finance/dateOnly'
import { exactKeys } from './exactKeys'

export interface PostedManufacturingCostShare {
  readonly companyId: string; readonly sourceEventId: string; readonly outputId: string; readonly generationId: string
  readonly generationVersion: string; readonly receiptMovementId: string; readonly journalId: string; readonly currencyId: string
  readonly cogsAccountId: string; readonly counterAccountId: string; readonly journalTotal: string; readonly signedAmount: string
  readonly signedRoundingResidue: string; readonly exactSignedAmount: { readonly numerator: string; readonly denominator: string }
  readonly moneyProfileId: string; readonly moneyProfileVersion: number; readonly moneyScale: number; readonly profileAccountingDate: string
  readonly sourcePayloadSha256: string; readonly lineageSha256: string; readonly historySha256: string; readonly fingerprint: string
  readonly shipment: { readonly shipmentId: string; readonly movementId: string; readonly movementLineNumber: number
    readonly productId: string; readonly physicalQuantityKg: number; readonly recordedAtUtc: string; readonly hasReturns?: boolean
    readonly returns?: readonly { readonly returnId: string; readonly movementId: string; readonly movementLineNumber: number
      readonly sourceIssueMovementId: string; readonly quantityKg: number; readonly recordedAtUtc: string }[] }
}
const record = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === 'object' && !Array.isArray(value)
const guid = (value: unknown): value is string => typeof value === 'string' && /^[a-f0-9]{8}(-[a-f0-9]{4}){3}-[a-f0-9]{12}$/i.test(value) && !/^0{8}(-0{4}){3}-0{12}$/.test(value)
const same = (value: unknown, expected: string) => guid(value) && value.toLowerCase() === expected.toLowerCase()
const hash = (value: unknown) => typeof value === 'string' && /^[a-f0-9]{64}$/i.test(value)
const decimal = (value: unknown) => typeof value === 'string' && /^-?(0|[1-9][0-9]*)(\.[0-9]{0,27}[1-9])?$/.test(value) && value !== '-0'
const positiveInteger = (value: unknown) => Number.isSafeInteger(value) && (value as number) > 0
const utc = (value: unknown) => typeof value === 'string' && /(?:Z|\+00:00)$/.test(value) && Number.isFinite(Date.parse(value))
const ratio = (value: unknown) => record(value) && exactKeys(value, ['numerator', 'denominator']) && typeof value.numerator === 'string' && /^-?(0|[1-9][0-9]*)$/.test(value.numerator)
  && value.numerator !== '-0' && typeof value.denominator === 'string' && /^[1-9][0-9]*$/.test(value.denominator)
// OpenAPI: PostedManufacturingCostShareV1, ManufacturingAttributionShipmentV1 (returns is not in `required`),
// ManufacturingAttributionReturnV1, AccountingInventoryCostRatioV1.
const SHARE_KEYS = ['companyId', 'sourceEventId', 'outputId', 'generationId', 'generationVersion', 'receiptMovementId', 'journalId',
  'currencyId', 'cogsAccountId', 'counterAccountId', 'journalTotal', 'signedAmount', 'exactSignedAmount', 'signedRoundingResidue',
  'moneyProfileId', 'moneyProfileVersion', 'moneyScale', 'profileAccountingDate', 'shipment', 'sourcePayloadSha256', 'lineageSha256',
  'historySha256', 'fingerprint'] as const
const SHIPMENT_KEYS = ['shipmentId', 'movementId', 'movementLineNumber', 'loadingInstructionLineId', 'productId', 'labelId',
  'physicalQuantityKg', 'rootShare', 'netRootQuantityKg', 'recordedAtUtc', 'provenOriginalSequence', 'hasReturns', 'physicalProofJson'] as const
const SHIPMENT_OPTIONAL_KEYS = ['returns'] as const
const RETURN_KEYS = ['returnId', 'movementId', 'movementLineNumber', 'sourceIssueMovementId', 'quantityKg', 'recordedAtUtc'] as const

// Shape and identity validation only. The browser never computes a monetary share, sum or remainder.
export function postedManufacturingCostShare(value: unknown, company: string, product?: string): value is PostedManufacturingCostShare {
  if (!record(value) || !exactKeys(value, SHARE_KEYS) || !same(value.companyId, company)
    || !['sourceEventId', 'outputId', 'generationId', 'receiptMovementId', 'journalId', 'currencyId', 'cogsAccountId', 'counterAccountId', 'moneyProfileId'].every(key => guid(value[key]))
    || same(value.cogsAccountId, value.counterAccountId as string) || typeof value.generationVersion !== 'string' || !/^[1-9][0-9]*$/.test(value.generationVersion)
    || !['journalTotal', 'signedAmount', 'signedRoundingResidue'].every(key => decimal(value[key]))
    || (value.journalTotal as string).startsWith('-') || value.journalTotal === '0' || !ratio(value.exactSignedAmount)
    || !positiveInteger(value.moneyProfileVersion) || !Number.isSafeInteger(value.moneyScale) || (value.moneyScale as number) < 0 || (value.moneyScale as number) > 28
    || typeof value.profileAccountingDate !== 'string' || !isDateOnly(value.profileAccountingDate)
    || !['sourcePayloadSha256', 'lineageSha256', 'historySha256', 'fingerprint'].every(key => hash(value[key]))
    || !record(value.shipment)) return false
  const shipment = value.shipment
  if (!exactKeys(shipment, SHIPMENT_KEYS, SHIPMENT_OPTIONAL_KEYS) || !guid(shipment.movementId) || !positiveInteger(shipment.movementLineNumber)
    || !guid(shipment.loadingInstructionLineId) || !guid(shipment.labelId) || !ratio(shipment.rootShare) || !ratio(shipment.netRootQuantityKg)
    || shipment.provenOriginalSequence !== null && !positiveInteger(shipment.provenOriginalSequence)
    || typeof shipment.physicalProofJson !== 'string' || typeof shipment.hasReturns !== 'boolean') return false
  if (shipment.hasReturns === true) {
    if (!Array.isArray(shipment.returns) || shipment.returns.length === 0 || shipment.returns.length > 65536) return false
    const ids = new Set<string>(); const movements = new Set<string>([`${shipment.movementId}:${shipment.movementLineNumber}`.toLowerCase()])
    for (const item of shipment.returns) {
      if (!record(item) || !exactKeys(item, RETURN_KEYS) || !guid(item.returnId) || !guid(item.movementId) || !positiveInteger(item.movementLineNumber)
        || !same(item.sourceIssueMovementId, shipment.movementId as string)
        || typeof item.quantityKg !== 'number' || !Number.isFinite(item.quantityKg) || item.quantityKg <= 0 || !utc(item.recordedAtUtc)) return false
      const identity = item.returnId.toLowerCase(); const movement = `${item.movementId}:${item.movementLineNumber}`.toLowerCase()
      if (ids.has(identity) || movements.has(movement)) return false
      ids.add(identity); movements.add(movement)
    }
  } else if ((shipment.hasReturns !== undefined && shipment.hasReturns !== false) || shipment.returns !== undefined) return false
  return ['shipmentId', 'movementId', 'productId'].every(key => guid(shipment[key]))
    && (product === undefined || same(shipment.productId, product)) && positiveInteger(shipment.movementLineNumber)
    && typeof shipment.physicalQuantityKg === 'number' && Number.isFinite(shipment.physicalQuantityKg) && shipment.physicalQuantityKg > 0
    && utc(shipment.recordedAtUtc)
}
