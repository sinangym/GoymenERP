import { isDateOnly } from './dateOnly'
import { isBoundUuid, isOpaqueVersion, isPositiveInt64Text, type Decoded } from './contract'
import type { DraftCurrencyContext, DraftPartyLabel } from './draftContext'

export interface ManualBankPaymentDraftContextRequest {
  readonly treasuryAccountId: string
  readonly partyId: string
  readonly companyAccountId: string
  readonly occurredOn: string
}

export interface ManualBankPaymentTreasuryAccount {
  readonly id: string
  readonly companyId: string
  readonly currencyId: string
  readonly isActive: true
  readonly isUsableForPayment: true
  readonly displayName: string
  readonly version: string
}

export interface ManualBankPaymentDraftContext {
  readonly companyId: string
  readonly occurredOn: string
  readonly treasuryAccount: ManualBankPaymentTreasuryAccount
  readonly currency: DraftCurrencyContext
  readonly party: DraftPartyLabel
}

const FAIL = { ok: false } as const
function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}
function hasExactKeys(value: Record<string, unknown>, keys: readonly string[]): boolean {
  const actual = Object.keys(value)
  return actual.length === keys.length && keys.every((key) => Object.hasOwn(value, key))
}
function isText(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0
}

function decodeTreasury(value: unknown, companyId: string,
  request: ManualBankPaymentDraftContextRequest): ManualBankPaymentTreasuryAccount | null {
  if (!isRecord(value) || !hasExactKeys(value,
    ['id', 'companyId', 'currencyId', 'isActive', 'isUsableForPayment', 'displayName', 'version'])) return null
  if (value.id !== request.treasuryAccountId || value.companyId !== companyId
    || !isBoundUuid(value.currencyId) || value.isActive !== true || value.isUsableForPayment !== true
    || !isText(value.displayName) || !isPositiveInt64Text(value.version)) return null
  return { id: request.treasuryAccountId, companyId, currencyId: value.currencyId, isActive: true,
    isUsableForPayment: true, displayName: value.displayName, version: value.version }
}

function decodeCurrency(value: unknown, currencyId: string, occurredOn: string): DraftCurrencyContext | null {
  if (!isRecord(value) || !hasExactKeys(value, ['currencyId', 'displayCode', 'profile'])) return null
  if (value.currencyId !== currencyId || !isText(value.displayCode)) return null
  const profile = value.profile
  if (!isRecord(profile) || !hasExactKeys(profile, ['reference', 'scale', 'effectiveFrom', 'effectiveThrough'])) return null
  const reference = profile.reference
  if (!isRecord(reference) || !hasExactKeys(reference, ['profileId', 'version'])) return null
  if (!isBoundUuid(reference.profileId) || !isPositiveInt64Text(reference.version)) return null
  if (typeof profile.scale !== 'number' || !Number.isInteger(profile.scale) || profile.scale < 0) return null
  if (typeof profile.effectiveFrom !== 'string' || !isDateOnly(profile.effectiveFrom)) return null
  const through = profile.effectiveThrough
  if (!(through === null || (typeof through === 'string' && isDateOnly(through)))) return null
  if (occurredOn < profile.effectiveFrom || (through !== null && occurredOn > through)) return null
  return { currencyId, displayCode: value.displayCode,
    profile: { reference: { profileId: reference.profileId, version: reference.version },
      scale: profile.scale, effectiveFrom: profile.effectiveFrom, effectiveThrough: through } }
}

function decodeParty(value: unknown, request: ManualBankPaymentDraftContextRequest): DraftPartyLabel | null {
  if (!isRecord(value) || !hasExactKeys(value,
    ['partyId', 'companyAccountId', 'code', 'legalName', 'tradeName', 'partyVersion', 'companyAccountVersion'])) return null
  if (value.partyId !== request.partyId || value.companyAccountId !== request.companyAccountId
    || !isText(value.code) || !isText(value.legalName) || !(value.tradeName === null || isText(value.tradeName))
    || !isOpaqueVersion(value.partyVersion) || !isOpaqueVersion(value.companyAccountVersion)) return null
  return { partyId: request.partyId, companyAccountId: request.companyAccountId, code: value.code,
    legalName: value.legalName, tradeName: value.tradeName as string | null,
    partyVersion: value.partyVersion, companyAccountVersion: value.companyAccountVersion }
}

export function decodeManualBankPaymentDraftContext(value: unknown, companyId: string,
  request: ManualBankPaymentDraftContextRequest): Decoded<ManualBankPaymentDraftContext> {
  if (!isRecord(value) || !hasExactKeys(value,
    ['companyId', 'occurredOn', 'treasuryAccount', 'currency', 'party'])) return FAIL
  if (value.companyId !== companyId || value.occurredOn !== request.occurredOn) return FAIL
  const treasuryAccount = decodeTreasury(value.treasuryAccount, companyId, request)
  if (treasuryAccount === null) return FAIL
  const currency = decodeCurrency(value.currency, treasuryAccount.currencyId, request.occurredOn)
  const party = decodeParty(value.party, request)
  if (currency === null || party === null) return FAIL
  return { ok: true, value: { companyId, occurredOn: request.occurredOn, treasuryAccount, currency, party } }
}

export function manualBankPaymentDraftContextRequestFor(treasuryAccountId: string, partyId: string,
  companyAccountId: string, occurredOn: string): ManualBankPaymentDraftContextRequest | null {
  const date = occurredOn.trim()
  if (!isBoundUuid(treasuryAccountId) || !isBoundUuid(partyId)
    || !isBoundUuid(companyAccountId) || !isDateOnly(date)) return null
  return { treasuryAccountId, partyId, companyAccountId, occurredOn: date }
}
