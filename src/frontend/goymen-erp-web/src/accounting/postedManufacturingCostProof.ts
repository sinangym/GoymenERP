import { isDateOnly } from '../finance/dateOnly'

export interface PostedManufacturingCostShare {
  readonly companyId: string; readonly sourceEventId: string; readonly outputId: string; readonly generationId: string
  readonly generationVersion: string; readonly receiptMovementId: string; readonly journalId: string; readonly currencyId: string
  readonly cogsAccountId: string; readonly counterAccountId: string; readonly journalTotal: string; readonly signedAmount: string
  readonly signedRoundingResidue: string; readonly exactSignedAmount: { readonly numerator: string; readonly denominator: string }
  readonly moneyProfileId: string; readonly moneyProfileVersion: number; readonly moneyScale: number; readonly profileAccountingDate: string
  readonly sourcePayloadSha256: string; readonly lineageSha256: string; readonly historySha256: string; readonly fingerprint: string
  readonly shipment: { readonly shipmentId: string; readonly movementId: string; readonly movementLineNumber: number
    readonly productId: string; readonly physicalQuantityKg: number; readonly recordedAtUtc: string }
}
const record = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === 'object' && !Array.isArray(value)
const guid = (value: unknown): value is string => typeof value === 'string' && /^[a-f0-9]{8}(-[a-f0-9]{4}){3}-[a-f0-9]{12}$/i.test(value) && !/^0{8}(-0{4}){3}-0{12}$/.test(value)
const same = (value: unknown, expected: string) => guid(value) && value.toLowerCase() === expected.toLowerCase()
const hash = (value: unknown) => typeof value === 'string' && /^[a-f0-9]{64}$/i.test(value)
const decimal = (value: unknown) => typeof value === 'string' && /^-?(0|[1-9][0-9]*)(\.[0-9]{0,27}[1-9])?$/.test(value) && value !== '-0'
const positiveInteger = (value: unknown) => Number.isSafeInteger(value) && (value as number) > 0
const ratio = (value: unknown) => record(value) && typeof value.numerator === 'string' && /^-?(0|[1-9][0-9]*)$/.test(value.numerator)
  && value.numerator !== '-0' && typeof value.denominator === 'string' && /^[1-9][0-9]*$/.test(value.denominator)

// Shape and identity validation only. The browser never computes a monetary share, sum or remainder.
export function postedManufacturingCostShare(value: unknown, company: string, product?: string): value is PostedManufacturingCostShare {
  if (!record(value) || !same(value.companyId, company)
    || !['sourceEventId', 'outputId', 'generationId', 'receiptMovementId', 'journalId', 'currencyId', 'cogsAccountId', 'counterAccountId', 'moneyProfileId'].every(key => guid(value[key]))
    || same(value.cogsAccountId, value.counterAccountId as string) || typeof value.generationVersion !== 'string' || !/^[1-9][0-9]*$/.test(value.generationVersion)
    || !['journalTotal', 'signedAmount', 'signedRoundingResidue'].every(key => decimal(value[key]))
    || (value.journalTotal as string).startsWith('-') || value.journalTotal === '0' || !ratio(value.exactSignedAmount)
    || !positiveInteger(value.moneyProfileVersion) || !Number.isSafeInteger(value.moneyScale) || (value.moneyScale as number) < 0 || (value.moneyScale as number) > 28
    || typeof value.profileAccountingDate !== 'string' || !isDateOnly(value.profileAccountingDate)
    || !['sourcePayloadSha256', 'lineageSha256', 'historySha256', 'fingerprint'].every(key => hash(value[key]))
    || !record(value.shipment)) return false
  const shipment = value.shipment
  return ['shipmentId', 'movementId', 'productId'].every(key => guid(shipment[key]))
    && (product === undefined || same(shipment.productId, product)) && positiveInteger(shipment.movementLineNumber)
    && typeof shipment.physicalQuantityKg === 'number' && Number.isFinite(shipment.physicalQuantityKg) && shipment.physicalQuantityKg > 0
    && typeof shipment.recordedAtUtc === 'string' && /(?:Z|\+00:00)$/.test(shipment.recordedAtUtc) && Number.isFinite(Date.parse(shipment.recordedAtUtc))
}
