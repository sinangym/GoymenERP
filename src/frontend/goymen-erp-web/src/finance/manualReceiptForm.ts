/**
 * Form model for the first Finance flow: a MANUAL, REALISED, INCOMING BANK RECEIPT,
 * single currency, with no allocation — the FS1 preparation (draft) step.
 *
 * Sources: Business spec `FINANCE_MANUAL_SOURCE_JIT_SPEC_v1.md` (#254 @6f0cd10) §2–§4;
 * wire shape `ReceiptPreparation` / `DraftSaveRequest` (#258 @fb482de, see ./contract.ts).
 *
 * What the person edits vs what the server or context supplies:
 *
 *  - EDITED: bank account (picked from the FS0 reader), amount, transaction date, value
 *    date, bank reference, description;
 *  - FROM THE ENTRY CONTEXT: party + company account. There is no Finance party reader in
 *    #258 (`IFinancePartyAccountPort` is server-side only), so the form never offers a
 *    typed id — the caller supplies an already chosen, authorized pair;
 *  - DERIVED: `money.currencyId` is the picked bank account's currency. Spec §2 "Hesap/
 *    işlem parası aynı"; a separate currency picker would only create mismatches;
 *  - SERVER-OWNED, NEVER TYPED: `profile` (profileId + version) and its approved scale, from
 *    the published draft read context (#261) for the exact bank/party/account/date — for a
 *    create AND an update, so a changed date never reuses an older profile;
 *  - NOT SENT: company and actor (bound by the authenticated host), scale, direction,
 *    unallocated amount, period, approval.
 *
 * DEC-FIN-014 customer POS collection: the same form with `collectionChannel = 'pos'`. The typed
 * amount is then the GROSS card amount (the customer's debt closes at it); the person adds the
 * bank's commission (zero allowed) and who bears it. The bank net is only PREVIEWED here —
 * the server derives and stores it; it is never sent.
 */

import {
  amountInputFromWire,
  fractionDigits,
  parseAmountInput,
  parseNonNegativeAmountInput,
  positiveDifference,
  type AmountInputError,
} from './decimalText'
import { isDateOnly } from './dateOnly'
import type {
  BankAccountView,
  DraftIdentity,
  DraftSaveRequest,
  PosCollectionInput,
  PosCommissionBearer,
  ProfileReference,
  SourceDetail,
} from './contract'

/** How the customer paid: a bank transfer (the #258 receipt) or our customer POS (DEC-FIN-014). */
export type CollectionChannel = 'bank-transfer' | 'pos'

export interface ManualReceiptFields {
  readonly treasuryAccountId: string
  readonly amount: string
  readonly occurredOn: string
  /** Blank means unknown (`null`), not "same day". */
  readonly valueOn: string
  /** Blank means none — and never "verified by the bank" (§2). */
  readonly bankReference: string
  readonly description: string
  readonly collectionChannel: CollectionChannel
  /** POS only: the bank's commission as typed; `0` is an answer, blank is not. */
  readonly commissionAmount: string
  /** POS only: blank until the person chooses — never defaulted to the company. */
  readonly commissionBearer: '' | PosCommissionBearer
}

export function emptyManualReceiptFields(): ManualReceiptFields {
  return {
    treasuryAccountId: '',
    amount: '',
    occurredOn: '',
    valueOn: '',
    bankReference: '',
    description: '',
    collectionChannel: 'bank-transfer',
    commissionAmount: '',
    commissionBearer: '',
  }
}

/** Editing an existing draft starts from its persisted values, digit-for-digit. */
export function fieldsFromDraft(detail: SourceDetail): ManualReceiptFields {
  const amount = detail.source.amount.disclosure === 'Visible' ? amountInputFromWire(detail.source.amount.money.amount) : ''
  const pos = detail.pos
  return {
    treasuryAccountId: detail.source.treasuryAccountId,
    amount,
    occurredOn: detail.source.occurredOn,
    valueOn: detail.source.valueOn ?? '',
    bankReference: detail.bankReference ?? '',
    description: detail.description ?? '',
    collectionChannel: pos === null ? 'bank-transfer' : 'pos',
    commissionAmount: pos !== null && pos.commission.disclosure === 'Visible' ? amountInputFromWire(pos.commission.money.amount) : '',
    commissionBearer: pos === null ? '' : pos.commissionBearer,
  }
}

/**
 * The bank net the server will derive, for display while typing: gross − commission, exact, or
 * `null` while either is not yet a valid amount or the commission is not below the gross.
 */
export function bankNetPreview(fields: ManualReceiptFields): string | null {
  if (fields.collectionChannel !== 'pos') return null
  const gross = parseAmountInput(fields.amount)
  const commission = parseNonNegativeAmountInput(fields.commissionAmount)
  return gross.ok && commission.ok ? positiveDifference(gross.wire, commission.wire) : null
}

/** The authorized party/account pair the screen was opened for. */
export interface ReceiptParty {
  readonly partyId: string
  readonly companyAccountId: string
}

export type FieldError =
  | AmountInputError
  | 'date-format'
  | 'bank-account-ineligible'
  | 'scale-exceeded'
  | 'commission-not-below-gross'
  | 'bearer-required'

export type FieldErrors = Readonly<Partial<Record<keyof ManualReceiptFields, FieldError>>>

export type DraftSaveBuild =
  | { readonly ok: true; readonly request: DraftSaveRequest }
  | { readonly ok: false; readonly errors: FieldErrors }

/**
 * Client checks only for what the contract fixes as SHAPE, plus the one eligibility fact
 * the FS0 reader already told us (an inactive / not-usable account is not offered).
 * Everything needing authority, a profile, a period or another module's data is the
 * server's issue list.
 */
export function buildDraftSaveRequest(
  fields: ManualReceiptFields,
  context: {
    readonly party: ReceiptParty
    readonly identity: DraftIdentity
    readonly profile: ProfileReference
    readonly bankAccounts: readonly BankAccountView[]
    /** Approved fraction-digit limit from the draft read context, when known. Never used to round. */
    readonly scale?: number
  },
): DraftSaveBuild {
  const errors: Partial<Record<keyof ManualReceiptFields, FieldError>> = {}

  const bank = context.bankAccounts.find((account) => account.id === fields.treasuryAccountId)
  if (fields.treasuryAccountId.length === 0) errors.treasuryAccountId = 'required'
  else if (bank === undefined || !bank.isActive || !bank.isUsableForReceipt) errors.treasuryAccountId = 'bank-account-ineligible'

  const amount = parseAmountInput(fields.amount)
  if (!amount.ok) errors.amount = amount.error
  else if (context.scale !== undefined && fractionDigits(amount.wire) > context.scale) errors.amount = 'scale-exceeded'

  const occurredOn = fields.occurredOn.trim()
  if (occurredOn.length === 0) errors.occurredOn = 'required'
  else if (!isDateOnly(occurredOn)) errors.occurredOn = 'date-format'

  const valueOn = fields.valueOn.trim()
  if (valueOn.length > 0 && !isDateOnly(valueOn)) errors.valueOn = 'date-format'

  let pos: PosCollectionInput | undefined
  if (fields.collectionChannel === 'pos') {
    const commission = parseNonNegativeAmountInput(fields.commissionAmount)
    if (!commission.ok) errors.commissionAmount = commission.error
    else if (context.scale !== undefined && fractionDigits(commission.wire) > context.scale) errors.commissionAmount = 'scale-exceeded'
    else if (amount.ok && positiveDifference(amount.wire, commission.wire) === null) errors.commissionAmount = 'commission-not-below-gross'
    if (fields.commissionBearer === '') errors.commissionBearer = 'bearer-required'
    if (commission.ok && fields.commissionBearer !== '') {
      pos = { commissionAmount: commission.wire, commissionBearer: fields.commissionBearer }
    }
  }

  if (Object.keys(errors).length > 0 || !amount.ok || bank === undefined) return { ok: false, errors }

  const bankReference = fields.bankReference.trim()
  const description = fields.description.trim()
  return {
    ok: true,
    request: {
      draftId: context.identity.draftId,
      expectedVersion: context.identity.expectedVersion,
      preparation: {
        partyId: context.party.partyId,
        companyAccountId: context.party.companyAccountId,
        treasuryAccountId: bank.id,
        money: { amount: amount.wire, currencyId: bank.currencyId },
        profile: context.profile,
        occurredOn,
        valueOn: valueOn.length === 0 ? null : valueOn,
        bankReference: bankReference.length === 0 ? null : bankReference,
        description: description.length === 0 ? null : description,
        // A bank transfer sends no `pos` member at all: the exact #258 request and its idempotency hash.
        ...(pos === undefined ? {} : { pos }),
      },
    },
  }
}
