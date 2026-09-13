/**
 * Form model for the first Finance flow: a MANUAL, REALISED, INCOMING BANK RECEIPT,
 * single currency, with no allocation.
 *
 * Source: `docs/features/FINANCE_MANUAL_SOURCE_JIT_SPEC_v1.md` (PR #254 @290624d)
 * §1–§4. Field names are the spec's LOGICAL candidates (§4 example); the Finance
 * Backend pins the route/DTO/limits. When that contract lands, only the wire mapping
 * of `ManualReceiptPayload` and the decoders change — the validation rules below are the spec's, not ours.
 *
 * What this model deliberately does NOT carry, each for a stated reason:
 *
 *  - no company and no actor. The company is the verified shell context and the actor
 *    is the server's (§2: "Body ile başka şirket/actor atama yok"). They enter the
 *    ATTEMPT BINDING, not the payload;
 *  - no direction/kind selector. The first definitive operation is only an incoming
 *    realised bank receipt (§2); offering "ödeme" here would hide a second operation
 *    behind a sign or a dropdown;
 *  - no scale, rounding, currency code list, period, approval or policy value. Those
 *    are server-owned (§2, §5); the model can only say "not checkable yet";
 *  - no due date and no reconciliation time. `dueOn` does not replace a realised
 *    record and a manual entry must not create a reconciled state (§2);
 *  - no unallocated amount. It is a server result (§3), never a client subtraction.
 */

import { parseAmountInput, type AmountInputError } from './decimalText'

/** What the person has typed or picked. Every value is a string until validated. */
export interface ManualReceiptFields {
  /** Picked from the authorized Cari read — never typed. */
  readonly partyId: string
  /** The SAME party's account in the CURRENT company (§2; roles 0 is valid). */
  readonly companyAccountId: string
  /** A Göymen bank account of the current company — not a Cari IBAN row (§2). */
  readonly treasuryAccountId: string
  /** A real currency id from the server; same as the account's currency (§2). */
  readonly currencyId: string
  readonly amount: string
  /** Realised bank transaction date, date-only, required. */
  readonly occurredOn: string
  /** Value date, date-only, optional — blank means unknown (`null`), not "same day". */
  readonly valueOn: string
  /** Optional; blank means none and unverified (§2). */
  readonly bankReference: string
}

export function emptyManualReceiptFields(): ManualReceiptFields {
  return {
    partyId: '',
    companyAccountId: '',
    treasuryAccountId: '',
    currencyId: '',
    amount: '',
    occurredOn: '',
    valueOn: '',
    bankReference: '',
  }
}

/** The normalised payload exactly as it would be sent. Frozen once an attempt starts. */
export interface ManualReceiptPayload {
  readonly partyId: string
  readonly companyAccountId: string
  readonly treasuryAccountId: string
  readonly currencyId: string
  /** Canonical wire decimal text, see `decimalText.ts`. */
  readonly amount: string
  readonly occurredOn: string
  readonly valueOn: string | null
  readonly bankReference: string | null
}

export type FieldError = AmountInputError | 'date-format'

export type ManualReceiptValidation =
  | { readonly ok: true; readonly payload: ManualReceiptPayload }
  | {
      readonly ok: false
      readonly errors: Readonly<Partial<Record<keyof ManualReceiptFields, FieldError>>>
    }

/**
 * `YYYY-MM-DD` naming a real calendar day. Checked arithmetically in UTC so no reader
 * timezone can shift the day. Whether the day is inside an open period, not in the
 * future, or not too far back is the owner's rule (§2), not a client check.
 */
export function isDateOnly(value: string): boolean {
  const match = /^([0-9]{4})-([0-9]{2})-([0-9]{2})$/.exec(value)
  if (match === null) return false
  const [year, month, day] = [Number(match[1]), Number(match[2]), Number(match[3])]
  const probe = new Date(Date.UTC(year, month - 1, day))
  return (
    probe.getUTCFullYear() === year && probe.getUTCMonth() === month - 1 && probe.getUTCDate() === day
  )
}

const PICKED_FIELDS = ['partyId', 'companyAccountId', 'treasuryAccountId', 'currencyId'] as const

/**
 * Client-side checks only for what the spec fixes as SHAPE. Everything that needs an
 * authority, a policy, a profile or another module's data is the server's answer.
 */
export function validateManualReceipt(fields: ManualReceiptFields): ManualReceiptValidation {
  const errors: Partial<Record<keyof ManualReceiptFields, FieldError>> = {}

  for (const name of PICKED_FIELDS) {
    if (fields[name].trim().length === 0) errors[name] = 'required'
  }

  const amount = parseAmountInput(fields.amount)
  if (!amount.ok) errors.amount = amount.error

  const occurredOn = fields.occurredOn.trim()
  if (occurredOn.length === 0) errors.occurredOn = 'required'
  else if (!isDateOnly(occurredOn)) errors.occurredOn = 'date-format'

  const valueOn = fields.valueOn.trim()
  if (valueOn.length > 0 && !isDateOnly(valueOn)) errors.valueOn = 'date-format'

  if (Object.keys(errors).length > 0 || !amount.ok) return { ok: false, errors }

  const bankReference = fields.bankReference.trim()
  return {
    ok: true,
    payload: {
      partyId: fields.partyId.trim(),
      companyAccountId: fields.companyAccountId.trim(),
      treasuryAccountId: fields.treasuryAccountId.trim(),
      currencyId: fields.currencyId.trim(),
      amount: amount.wire,
      occurredOn,
      valueOn: valueOn.length === 0 ? null : valueOn,
      bankReference: bankReference.length === 0 ? null : bankReference,
    },
  }
}

/**
 * The two logical writes the spec separates (§4 "DraftSave/Record"). A draft save is
 * not a money event; recording is. They are different commands and never share a key.
 */
export type ReceiptOperation = 'draft-save' | 'record'

/** Who, where and under which session an attempt was made. */
export interface ReceiptAttemptBinding {
  readonly sessionEpoch: number
  readonly actorId: string
  readonly companyId: string
  readonly operation: ReceiptOperation
}

/**
 * Identity of one attempt: binding + normalised payload.
 *
 * Spec §4: on an unknown outcome "aynı key/company/account/actor/payload tutulur". Two
 * submits with the same identity are the SAME attempt and must reuse one key; any
 * change — another company, actor, session, operation, account or a single amount digit
 * — is a different command. Payload keys are listed explicitly so their order can never
 * make equal payloads look different.
 */
export function receiptAttemptIdentity(
  binding: ReceiptAttemptBinding,
  payload: ManualReceiptPayload,
): string {
  return JSON.stringify([
    binding.sessionEpoch,
    binding.actorId,
    binding.companyId,
    binding.operation,
    payload.partyId,
    payload.companyAccountId,
    payload.treasuryAccountId,
    payload.currencyId,
    payload.amount,
    payload.occurredOn,
    payload.valueOn,
    payload.bankReference,
  ])
}
