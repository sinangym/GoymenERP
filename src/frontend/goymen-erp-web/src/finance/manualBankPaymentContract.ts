export type ManualBankPaymentStage = 'Draft' | 'Approved' | 'Recorded'
export type ManualBankResultConfirmation = 'Unknown' | 'Occurred'

export interface ManualBankPaymentPreparation {
  readonly partyId: string
  readonly companyAccountId: string
  readonly treasuryAccountId: string
  readonly money: { readonly amount: string; readonly currencyId: string }
  readonly profile: { readonly profileId: string; readonly version: string }
  readonly occurredOn: string
  readonly valueOn: string | null
  readonly bankReference: string | null
  readonly documentId: string | null
  readonly description: string | null
}

export interface ManualBankPayment {
  readonly id: string
  readonly companyId: string
  readonly version: string
  readonly stage: ManualBankPaymentStage
  readonly preparation: ManualBankPaymentPreparation
  readonly preparedBy: string
  readonly preparedAtUtc: string
  readonly approvedBy: string | null
  readonly approvedAtUtc: string | null
  readonly recordedBy: string | null
  readonly recordedAtUtc: string | null
  readonly moneyEventId: string | null
}

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/
const VERSION = /^[1-9][0-9]*$/
const DATE = /^\d{4}-\d{2}-\d{2}$/
const MONEY = /^(?:0|[1-9][0-9]*)(?:\.[0-9]+)?$/

function object(value: unknown, name: string): Record<string, unknown> {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) throw new Error(`${name} malformed`)
  return value as Record<string, unknown>
}

function exact(value: Record<string, unknown>, keys: readonly string[], name: string) {
  if (Object.keys(value).sort().join('|') !== [...keys].sort().join('|')) throw new Error(`${name} fields malformed`)
}

function text(value: unknown, name: string): string {
  if (typeof value !== 'string' || value.length === 0 || value.trim() !== value) throw new Error(`${name} malformed`)
  return value
}

function nullableText(value: unknown, name: string): string | null {
  return value === null ? null : text(value, name)
}

function guid(value: unknown, name: string): string {
  const parsed = text(value, name)
  if (!GUID.test(parsed) || parsed === '00000000-0000-0000-0000-000000000000') throw new Error(`${name} malformed`)
  return parsed
}

function nullableGuid(value: unknown, name: string): string | null {
  return value === null ? null : guid(value, name)
}

function instant(value: unknown, name: string): string {
  const parsed = text(value, name)
  if (!/^\d{4}-\d{2}-\d{2}T.*(?:Z|\+00:00)$/.test(parsed) || Number.isNaN(Date.parse(parsed))) {
    throw new Error(`${name} malformed`)
  }
  return parsed
}

function nullableInstant(value: unknown, name: string): string | null {
  return value === null ? null : instant(value, name)
}

export function decodeManualBankPayment(value: unknown): ManualBankPayment {
  const root = object(value, 'manual bank payment')
  exact(root, ['id', 'companyId', 'version', 'stage', 'preparation', 'preparedBy', 'preparedAtUtc',
    'approvedBy', 'approvedAtUtc', 'recordedBy', 'recordedAtUtc', 'moneyEventId'], 'manual bank payment')
  const preparation = object(root.preparation, 'preparation')
  exact(preparation, ['partyId', 'companyAccountId', 'treasuryAccountId', 'money', 'profile', 'occurredOn',
    'valueOn', 'bankReference', 'documentId', 'description'], 'preparation')
  const money = object(preparation.money, 'money')
  exact(money, ['amount', 'currencyId'], 'money')
  const profile = object(preparation.profile, 'profile')
  exact(profile, ['profileId', 'version'], 'profile')
  const stage = text(root.stage, 'stage')
  if (!['Draft', 'Approved', 'Recorded'].includes(stage)) throw new Error('stage malformed')
  const version = text(root.version, 'version')
  const amount = text(money.amount, 'amount')
  const occurredOn = text(preparation.occurredOn, 'occurredOn')
  if (!VERSION.test(version) || !VERSION.test(text(profile.version, 'profile.version')) ||
      !MONEY.test(amount) || amount === '0' || !DATE.test(occurredOn)) throw new Error('numeric/date contract malformed')

  const approvedBy = nullableText(root.approvedBy, 'approvedBy')
  const approvedAtUtc = nullableInstant(root.approvedAtUtc, 'approvedAtUtc')
  const recordedBy = nullableText(root.recordedBy, 'recordedBy')
  const recordedAtUtc = nullableInstant(root.recordedAtUtc, 'recordedAtUtc')
  const moneyEventId = nullableGuid(root.moneyEventId, 'moneyEventId')
  if ((stage === 'Draft' && [approvedBy, approvedAtUtc, recordedBy, recordedAtUtc, moneyEventId].some(Boolean)) ||
      (stage === 'Approved' && (!approvedBy || !approvedAtUtc || recordedBy || recordedAtUtc || moneyEventId)) ||
      (stage === 'Recorded' && (!approvedBy || !approvedAtUtc || !recordedBy || !recordedAtUtc || !moneyEventId))) {
    throw new Error('stage trace malformed')
  }

  return {
    id: guid(root.id, 'id'), companyId: guid(root.companyId, 'companyId'), version,
    stage: stage as ManualBankPaymentStage,
    preparation: {
      partyId: guid(preparation.partyId, 'partyId'),
      companyAccountId: guid(preparation.companyAccountId, 'companyAccountId'),
      treasuryAccountId: guid(preparation.treasuryAccountId, 'treasuryAccountId'),
      money: { amount, currencyId: guid(money.currencyId, 'currencyId') },
      profile: { profileId: guid(profile.profileId, 'profileId'), version: text(profile.version, 'profile.version') },
      occurredOn, valueOn: nullableText(preparation.valueOn, 'valueOn'),
      bankReference: nullableText(preparation.bankReference, 'bankReference'),
      documentId: nullableGuid(preparation.documentId, 'documentId'),
      description: nullableText(preparation.description, 'description'),
    },
    preparedBy: text(root.preparedBy, 'preparedBy'), preparedAtUtc: instant(root.preparedAtUtc, 'preparedAtUtc'),
    approvedBy, approvedAtUtc, recordedBy, recordedAtUtc, moneyEventId,
  }
}

export function manualBankPaymentRecordRequest(payment: ManualBankPayment, occurred: boolean) {
  return {
    paymentId: payment.id,
    expectedVersion: payment.version,
    result: occurred ? 'Occurred' as const : 'Unknown' as const,
  }
}

