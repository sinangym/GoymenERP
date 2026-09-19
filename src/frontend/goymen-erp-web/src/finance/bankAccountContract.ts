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
  type BankAccountView,
  type CommandOutcomeName,
  type Decoded,
  type ValidationIssue,
} from './contract'

export interface BankAccountMasterItem {
  readonly account: BankAccountView
  readonly isOwnProposal: boolean
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
    if (!isRecord(raw) || !hasExactKeys(raw, ['account', 'isOwnProposal']) || typeof raw.isOwnProposal !== 'boolean') return FAIL
    const account = decodeAccount(raw.account, companyId)
    if (account === null || seen.has(account.id)) return FAIL
    // Only a proposal (not active) can be the caller's pending one to wait on; an approved account
    // is shown the same to everyone.
    seen.add(account.id)
    items.push({ account, isOwnProposal: raw.isOwnProposal })
  }
  return ok({ companyId, items })
}

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
