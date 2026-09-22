/**
 * F-04's wire shapes (FIN-UI-1a, #325), decoded strictly like ./contract.ts: exact member sets,
 * the verified shell company, bound identities, opaque versions. An answer that does not decode is
 * never shown as data.
 *
 * - `BankAccountMasterPage`: the company's accounts for F-04, each with whether the caller proposed
 *   it. A read-only caller sees approved accounts only (least privilege, server-side).
 * - `BankAccountCurrencyCandidates`: Settings' candidates as given — order kept, never a default.
 * - `BankAccountCommandResult`: propose / approve, the #258 envelope with a bank-account resource.
 */

import {
  decodeBankAccountPage,
  isBoundUuid,
  isInstant,
  type BankAccountView,
  type CommandOutcomeName,
  type Decoded,
  type ValidationIssue,
} from './contract'

/**
 * B3 (#66 5750700247): what the card SAYS about money movement. Three words and never absent —
 * `Undeclared` is a card written before the declaration existed, and it is NOT `NotEligible`: one is
 * unanswered, the other is answered "no", and Sinan asks for the first to be shown specifically.
 */
export type MoneyEligibility = 'Eligible' | 'NotEligible' | 'Undeclared'
const MONEY_ELIGIBILITIES: readonly MoneyEligibility[] = ['Eligible', 'NotEligible', 'Undeclared']

/** D4: an IBAN change waiting for its own Ö4 approval, or null when there is none. */
export interface PendingIbanChange {
  readonly iban: string
  readonly proposedBy: string
  readonly proposedAtUtc: string
  readonly reason: string
}

/** K2's card fields, carried only on the master — the receipt paths never see an IBAN. */
export interface BankAccountCard {
  readonly bankName: string
  readonly accountType: string
  readonly branchName: string | null
  readonly branchCode: string | null
  readonly iban: string | null
  readonly accountNumber: string | null
  readonly moneyEligibility: MoneyEligibility
  readonly pendingIbanChange: PendingIbanChange | null
}

export interface BankAccountMasterItem {
  readonly account: BankAccountView
  readonly isOwnProposal: boolean
  readonly card: BankAccountCard
  readonly isOwnPendingIbanProposal: boolean
}

export interface BankAccountMasterPage {
  readonly companyId: string
  readonly items: readonly BankAccountMasterItem[]
}

export interface CurrencyCandidate {
  readonly currencyId: string
  readonly code: string
  readonly displayLabel: string
}

export interface CurrencyCandidates {
  readonly companyId: string
  readonly items: readonly CurrencyCandidate[]
}

export type BankAccountCommandResult =
  | {
      readonly outcome: 'Succeeded'
      readonly resource: BankAccountView
      readonly isReplay: boolean
      readonly originalCorrelationId: string
      readonly issues: readonly ValidationIssue[]
    }
  | {
      readonly outcome: Exclude<CommandOutcomeName, 'Succeeded'>
      readonly resource: null
      readonly isReplay: boolean
      readonly originalCorrelationId: string
      readonly issues: readonly ValidationIssue[]
    }

/** The propose body: exactly `BankAccountProposeRequest` (the company is the server's). */
export interface BankAccountProposeRequest {
  readonly displayName: string
  readonly currencyId: string
  readonly bankName: string
  readonly accountType: string
  readonly moneyMovementEligible: boolean
  readonly branchName: string | null
  readonly branchCode: string | null
  readonly iban: string | null
  readonly accountNumber: string | null
}

/** D4: the IBAN-change body — exactly `BankAccountIbanChangeBody` (the account is the route's). */
export interface BankAccountIbanChangeBody {
  readonly expectedVersion: string
  readonly iban: string
  readonly reason: string
}

/** The approval body: exactly `BankAccountApprovalBody` (the account is the route's). */
export interface BankAccountApprovalBody {
  readonly expectedVersion: string
}

const FAIL = { ok: false } as const
const ok = <T,>(value: T): Decoded<T> => ({ ok: true, value })

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function hasExactKeys(value: Record<string, unknown>, keys: readonly string[]): boolean {
  const own = Object.keys(value)
  return own.length === keys.length && keys.every((key) => Object.prototype.hasOwnProperty.call(value, key))
}

/** One account, through the same strict decoder the receipt picker uses. */
function decodeAccount(value: unknown, companyId: string): BankAccountView | null {
  const page = decodeBankAccountPage({ companyId, items: [value] }, companyId)
  return page.ok ? page.value.items[0] : null
}

export function decodeBankAccountMasterPage(value: unknown, companyId: string): Decoded<BankAccountMasterPage> {
  if (!isRecord(value) || !hasExactKeys(value, ['companyId', 'items'])) return FAIL
  if (value.companyId !== companyId || !Array.isArray(value.items)) return FAIL
  const items: BankAccountMasterItem[] = []
  const seen = new Set<string>()
  for (const raw of value.items) {
    if (
      !isRecord(raw) ||
      !hasExactKeys(raw, ['account', 'isOwnProposal', 'card', 'isOwnPendingIbanProposal']) ||
      typeof raw.isOwnProposal !== 'boolean' ||
      typeof raw.isOwnPendingIbanProposal !== 'boolean'
    ) {
      return FAIL
    }

    const account = decodeAccount(raw.account, companyId)
    const card = decodeCard(raw.card)
    if (account === null || card === null || seen.has(account.id)) return FAIL
    // Only a proposal (not active) can be the caller's pending one to wait on; an approved account
    // is shown the same to everyone.
    seen.add(account.id)
    items.push({ account, isOwnProposal: raw.isOwnProposal, card, isOwnPendingIbanProposal: raw.isOwnPendingIbanProposal })
  }
  return ok({ companyId, items })
}

/**
 * K2's card, strictly. The two mandatory texts arrive as empty strings for a row written before K2 —
 * that is the server showing what it has rather than inventing a value, so the decoder accepts empty
 * and the screen decides how to say "not filled in". What it does NOT accept is a missing member, a
 * fourth word for the eligibility, or a half-written pending change.
 */
function decodeCard(value: unknown): BankAccountCard | null {
  if (
    !isRecord(value) ||
    !hasExactKeys(value, [
      'bankName', 'accountType', 'branchName', 'branchCode', 'iban', 'accountNumber',
      'moneyEligibility', 'pendingIbanChange',
    ])
  ) {
    return null
  }

  if (
    typeof value.bankName !== 'string' ||
    typeof value.accountType !== 'string' ||
    !isNullableText(value.branchName) ||
    !isNullableText(value.branchCode) ||
    !isNullableText(value.iban) ||
    !isNullableText(value.accountNumber) ||
    typeof value.moneyEligibility !== 'string' ||
    !(MONEY_ELIGIBILITIES as readonly string[]).includes(value.moneyEligibility)
  ) {
    return null
  }

  const pending = decodePendingIbanChange(value.pendingIbanChange)
  if (pending === undefined) return null
  return {
    bankName: value.bankName,
    accountType: value.accountType,
    branchName: value.branchName,
    branchCode: value.branchCode,
    iban: value.iban,
    accountNumber: value.accountNumber,
    moneyEligibility: value.moneyEligibility as MoneyEligibility,
    pendingIbanChange: pending,
  }
}

/** `null` is "no pending change"; `undefined` is "this is not the contract" — the two must not be confused. */
function decodePendingIbanChange(value: unknown): PendingIbanChange | null | undefined {
  if (value === null) return null
  if (!isRecord(value) || !hasExactKeys(value, ['iban', 'proposedBy', 'proposedAtUtc', 'reason'])) return undefined
  const { iban, proposedBy, proposedAtUtc, reason } = value
  if (
    typeof iban !== 'string' || iban.length === 0 || iban.length > 34 || iban.trim() !== iban ||
    typeof proposedBy !== 'string' || proposedBy.length === 0 || proposedBy.length > 128 || proposedBy.trim() !== proposedBy ||
    !isInstant(proposedAtUtc) ||
    typeof reason !== 'string' || reason.length === 0 || reason.length > 500 || reason.trim() !== reason
  ) {
    return undefined
  }
  return { iban, proposedBy, proposedAtUtc, reason }
}

const isNullableText = (value: unknown): value is string | null =>
  value === null || (typeof value === 'string' && value.length > 0)

export function decodeCurrencyCandidates(value: unknown, companyId: string): Decoded<CurrencyCandidates> {
  if (!isRecord(value) || !hasExactKeys(value, ['companyId', 'items'])) return FAIL
  if (value.companyId !== companyId || !Array.isArray(value.items)) return FAIL
  const items: CurrencyCandidate[] = []
  const seen = new Set<string>()
  for (const raw of value.items) {
    if (!isRecord(raw) || !hasExactKeys(raw, ['currencyId', 'code', 'displayLabel'])) return FAIL
    if (
      !isBoundUuid(raw.currencyId) ||
      seen.has(raw.currencyId) ||
      typeof raw.code !== 'string' ||
      raw.code.trim().length === 0 ||
      typeof raw.displayLabel !== 'string' ||
      raw.displayLabel.trim().length === 0
    ) {
      return FAIL
    }
    seen.add(raw.currencyId)
    items.push({ currencyId: raw.currencyId, code: raw.code, displayLabel: raw.displayLabel })
  }
  // As given: never filtered, reordered or defaulted.
  return ok({ companyId, items })
}

const COMMAND_OUTCOMES: readonly string[] = [
  'Unavailable', 'Denied', 'ValidationFailed', 'VersionConflict', 'IdempotencyConflict', 'Succeeded',
]

export function decodeBankAccountCommandResult(value: unknown, companyId: string): Decoded<BankAccountCommandResult> {
  if (!isRecord(value) || !hasExactKeys(value, ['outcome', 'resource', 'isReplay', 'originalCorrelationId', 'issues'])) {
    return FAIL
  }
  if (
    typeof value.outcome !== 'string' ||
    !COMMAND_OUTCOMES.includes(value.outcome) ||
    typeof value.isReplay !== 'boolean' ||
    typeof value.originalCorrelationId !== 'string' ||
    // A replay names the first attempt's correlation; a status-only answer (403/503) has none.
    (value.isReplay && value.originalCorrelationId.length === 0) ||
    !Array.isArray(value.issues)
  ) {
    return FAIL
  }
  const issues: ValidationIssue[] = []
  for (const raw of value.issues) {
    if (!isRecord(raw) || !hasExactKeys(raw, ['field', 'code'])) return FAIL
    if (typeof raw.field !== 'string' || typeof raw.code !== 'string' || raw.code.length === 0) return FAIL
    issues.push({ field: raw.field, code: raw.code })
  }
  const common = { isReplay: value.isReplay, originalCorrelationId: value.originalCorrelationId, issues }
  if (value.outcome === 'Succeeded') {
    const resource = decodeAccount(value.resource, companyId)
    return resource === null ? FAIL : ok({ outcome: 'Succeeded', resource, ...common })
  }
  if (value.resource !== null) return FAIL
  return ok({ outcome: value.outcome as Exclude<CommandOutcomeName, 'Succeeded'>, resource: null, ...common })
}
