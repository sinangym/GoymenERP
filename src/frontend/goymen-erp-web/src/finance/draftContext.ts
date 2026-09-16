/**
 * Draft read context — the one PUBLISHED Finance HTTP read so far.
 *
 * SOURCE, pinned: Finance Backend #261 (head edbdf8d; contract unchanged since 448e420 —
 *   the first label here named 1c15d59, which the revert left on no branch; the blobs
 *   below are the actual pin and are the same on edbdf8d)
 *   GET /api/v1/finance/manual-receipts/draft-context
 *       ?treasuryAccountId=<uuid>&partyId=<uuid>&companyAccountId=<uuid>&occurredOn=YYYY-MM-DD
 *   header X-Company-Id (required); 200 DraftReadContextView | 400 | 401 | 403 | 404 | 500 | 503
 *   `DraftReadContext.cs` (blob 345d324), `examples/draft-read-context.normal.json` (blob ad8c39a),
 *   `examples/draft-read-context.http.md`, canonical `goymen-erp-v1.json` on the same head.
 *
 * What it gives the draft form, and nothing more: the exact bank account, the currency
 * DISPLAY code, the effective money profile (reference + approved scale + validity) for the
 * selected bank currency on `occurredOn`, and the Cari label. No amount, balance, source,
 * approval or write capability — a successful read is not permission to save.
 *
 * Fail-closed binding on top of the shape (the server already binds these; the client refuses
 * a body that does not answer THIS request in THIS company):
 *  - `companyId` = shell company; `occurredOn` = the requested date;
 *  - bank id = requested account, bank company = shell company, still active and usable;
 *  - currency id = bank currency; the requested date is inside the profile validity;
 *  - party/account ids = requested pair;
 *  - unknown members rejected; nullable members must be present.
 */

import { isDateOnly } from './dateOnly'
import { isBoundUuid, isOpaqueVersion, isPositiveInt64Text, type BankAccountView, type Decoded, type ProfileReference } from './contract'

export interface DraftContextRequest {
  readonly treasuryAccountId: string
  readonly partyId: string
  readonly companyAccountId: string
  readonly occurredOn: string
}

export interface DraftEffectiveProfile {
  readonly reference: ProfileReference
  /** Approved fraction-digit limit from Settings. Displayed and pre-checked; never used to round. */
  readonly scale: number
  readonly effectiveFrom: string
  readonly effectiveThrough: string | null
}

export interface DraftCurrencyContext {
  readonly currencyId: string
  readonly displayCode: string
  readonly profile: DraftEffectiveProfile
}

export interface DraftPartyLabel {
  readonly partyId: string
  readonly companyAccountId: string
  readonly code: string
  readonly legalName: string
  readonly tradeName: string | null
  readonly partyVersion: string
  readonly companyAccountVersion: string
}

export interface DraftContext {
  readonly companyId: string
  readonly occurredOn: string
  readonly bankAccount: BankAccountView
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

function decodeBank(value: unknown, companyId: string, request: DraftContextRequest): BankAccountView | null {
  if (!isRecord(value) || !hasExactKeys(value, ['id', 'companyId', 'currencyId', 'displayName', 'isActive', 'isUsableForReceipt', 'version'])) {
    return null
  }
  if (
    value.id !== request.treasuryAccountId ||
    value.companyId !== companyId ||
    !isBoundUuid(value.currencyId) ||
    !isText(value.displayName) ||
    value.isActive !== true ||
    value.isUsableForReceipt !== true ||
    !isOpaqueVersion(value.version)
  ) {
    return null
  }
  return {
    id: request.treasuryAccountId,
    companyId,
    currencyId: value.currencyId,
    displayName: value.displayName,
    isActive: true,
    isUsableForReceipt: true,
    version: value.version,
  }
}

function decodeCurrency(value: unknown, bank: BankAccountView, occurredOn: string): DraftCurrencyContext | null {
  if (!isRecord(value) || !hasExactKeys(value, ['currencyId', 'displayCode', 'profile'])) return null
  if (value.currencyId !== bank.currencyId || !isText(value.displayCode)) return null
  const profile = value.profile
  if (!isRecord(profile) || !hasExactKeys(profile, ['reference', 'scale', 'effectiveFrom', 'effectiveThrough'])) return null
  const reference = profile.reference
  if (!isRecord(reference) || !hasExactKeys(reference, ['profileId', 'version'])) return null
  if (!isBoundUuid(reference.profileId) || !isPositiveInt64Text(reference.version)) return null
  if (typeof profile.scale !== 'number' || !Number.isInteger(profile.scale) || profile.scale < 0) return null
  if (typeof profile.effectiveFrom !== 'string' || !isDateOnly(profile.effectiveFrom)) return null
  const through = profile.effectiveThrough
  if (!(through === null || (typeof through === 'string' && isDateOnly(through)))) return null
  // YYYY-MM-DD compares correctly as text.
  if (occurredOn < profile.effectiveFrom || (through !== null && occurredOn > through)) return null
  return {
    currencyId: bank.currencyId,
    displayCode: value.displayCode,
    profile: {
      reference: { profileId: reference.profileId, version: reference.version },
      scale: profile.scale,
      effectiveFrom: profile.effectiveFrom,
      effectiveThrough: through,
    },
  }
}

function decodeParty(value: unknown, request: DraftContextRequest): DraftPartyLabel | null {
  if (
    !isRecord(value) ||
    !hasExactKeys(value, ['partyId', 'companyAccountId', 'code', 'legalName', 'tradeName', 'partyVersion', 'companyAccountVersion'])
  ) {
    return null
  }
  if (
    value.partyId !== request.partyId ||
    value.companyAccountId !== request.companyAccountId ||
    !isText(value.code) ||
    !isText(value.legalName) ||
    !(value.tradeName === null || isText(value.tradeName)) ||
    !isOpaqueVersion(value.partyVersion) ||
    !isOpaqueVersion(value.companyAccountVersion)
  ) {
    return null
  }
  return {
    partyId: request.partyId,
    companyAccountId: request.companyAccountId,
    code: value.code,
    legalName: value.legalName,
    tradeName: value.tradeName as string | null,
    partyVersion: value.partyVersion,
    companyAccountVersion: value.companyAccountVersion,
  }
}

export function decodeDraftContext(value: unknown, companyId: string, request: DraftContextRequest): Decoded<DraftContext> {
  if (!isRecord(value) || !hasExactKeys(value, ['companyId', 'occurredOn', 'bankAccount', 'currency', 'party'])) return FAIL
  if (value.companyId !== companyId || value.occurredOn !== request.occurredOn) return FAIL
  const bankAccount = decodeBank(value.bankAccount, companyId, request)
  if (bankAccount === null) return FAIL
  const currency = decodeCurrency(value.currency, bankAccount, request.occurredOn)
  const party = decodeParty(value.party, request)
  if (currency === null || party === null) return FAIL
  return { ok: true, value: { companyId, occurredOn: request.occurredOn, bankAccount, currency, party } }
}

/** A request is only made for a complete, well-formed selection. */
export function draftContextRequestFor(
  treasuryAccountId: string,
  party: { readonly partyId: string; readonly companyAccountId: string },
  occurredOn: string,
): DraftContextRequest | null {
  const date = occurredOn.trim()
  if (!isBoundUuid(treasuryAccountId) || !isBoundUuid(party.partyId) || !isBoundUuid(party.companyAccountId) || !isDateOnly(date)) {
    return null
  }
  return { treasuryAccountId, partyId: party.partyId, companyAccountId: party.companyAccountId, occurredOn: date }
}

/** The label a person reads for the chosen Cari account. Server text only. */
export function partyLabelText(party: DraftPartyLabel): string {
  return party.tradeName === null ? `${party.code} — ${party.legalName}` : `${party.code} — ${party.legalName} (${party.tradeName})`
}
