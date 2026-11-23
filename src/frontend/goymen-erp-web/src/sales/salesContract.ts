/**
 * S-11 (SAT-D28, NS-13, SAT-D346): the sales contract wire contract, decoded strictly.
 *
 *   GET  /api/v1/sales/contracts                          → `{ items }` (the whole list; the server sends no cursor)
 *   GET  /api/v1/sales/contracts/{id}                     → one contract
 *   POST /api/v1/sales/contracts                          → 201 the recorded contract
 *   POST /api/v1/sales/contracts/{id}/discount-approval   → 200 the approved (Active) contract (`{ expectedVersion }`)
 *   POST /api/v1/sales/contracts/{id}/discount-rejection  → 200 the rejected contract (`{ expectedVersion, reason }`)
 *
 * An answer counts only with exactly the documented keys, a known state and facts that agree with that state: a contract that
 * needed no discount approval is Active without a decision; one that needed it is pending without a decision, or decided by
 * someone other than its creator (an approval Active, a rejection DiscountRejected with its reason). A contract with no
 * discount rule never needs approval and one recorded while no limit was configured always did (SAT-D346). Anything else is
 * refused as a whole (`null`), never partly shown. Amounts are the server's exact text at the money profile's scale; the
 * planned total is the server's sum, never added up here.
 *
 * SAT-D361: the amounts (every installment amount and the planned total) come only beside `amountDisclosure: "Visible"` —
 * the server asks the sales order price read on every answer. `"Withheld"` carries every amount as null (and a Visible
 * answer carries none as null); a mixture is refused. Rates are not amounts and stay visible.
 * SAT-D362: `validThrough` is optional — null is an open-ended contract; a given end is never before the start.
 */
import { isDateOnly, isReferenceId, isVersionString } from './wirePrimitives'

export const SALES_CONTRACTS_PATH = '/sales/contracts'
export const salesContractPath = (id: string) => `${SALES_CONTRACTS_PATH}/${encodeURIComponent(id)}`

/** The two decisions on a contract whose discount waits for approval (NS-13). */
export type SalesContractDecision = 'discount-approval' | 'discount-rejection'
export const salesContractDecisionPath = (id: string, decision: SalesContractDecision) => `${salesContractPath(id)}/${decision}`

/**
 * The capability ids the contracts screen is meant to be published under. STATUS: NOT PUBLISHED — the backend shell provider
 * does not publish them yet (the shell registry regeneration is pending), so the container renders nothing until it does. A
 * capability is not a permission: the API measures `sales.contracts.*` afresh on every call.
 */
export const SALES_CONTRACT_CAPABILITY = {
  list: 'sales.contracts.list',
  create: 'sales.contracts.create',
  discountApprove: 'sales.contracts.discount-approve',
} as const

export const SALES_CONTRACT_STATE = {
  pending: 'PendingDiscountApproval',
  active: 'Active',
  rejected: 'DiscountRejected',
} as const
export type SalesContractState = (typeof SALES_CONTRACT_STATE)[keyof typeof SALES_CONTRACT_STATE]

export const SALES_CONTRACT_DISCOUNT_KIND = { line: 'Line', total: 'Total' } as const
export type SalesContractDiscountKind = (typeof SALES_CONTRACT_DISCOUNT_KIND)[keyof typeof SALES_CONTRACT_DISCOUNT_KIND]

/** The server's bounds (`SalesContract`); a longer text or a longer list is refused, never cut. */
export const SALES_CONTRACT_LIMITS = {
  clauses: 4000,
  reason: 4000,
  installments: 120,
  discountRules: 50,
  label: 200,
  /** `SalesContractApiV1.MaximumListed`. */
  listed: 200,
} as const

/** SAT-D361: whether the caller may see the contract's amounts (the sales order price read). Never absent. */
export type SalesContractAmountDisclosure = 'Visible' | 'Withheld'

export interface SalesContractInstallment {
  readonly installmentNo: number
  readonly dueDate: string
  /** Null only when the contract's `amountDisclosure` is Withheld (SAT-D361). */
  readonly amount: string | null
}

export interface SalesContractDiscountRule {
  readonly ruleNo: number
  readonly kind: SalesContractDiscountKind
  readonly label: string
  readonly ratePercent: string | null
}

export interface SalesContract {
  readonly id: string
  readonly contractNo: number
  readonly partyId: string
  readonly companyAccountId: string
  readonly currencyId: string
  readonly moneyScale: number
  readonly state: SalesContractState
  readonly version: string
  readonly validFrom: string
  /** SAT-D362: null = open-ended ("süresiz"). */
  readonly validThrough: string | null
  readonly amountDisclosure: SalesContractAmountDisclosure
  readonly installments: readonly SalesContractInstallment[]
  /** Null only when `amountDisclosure` is Withheld (SAT-D361). */
  readonly plannedTotalAmount: string | null
  readonly discountRules: readonly SalesContractDiscountRule[]
  readonly clauses: string | null
  readonly discountApprovalRequired: boolean
  /** SAT-D346: false = no limit was configured when the contract was recorded ("sınır tanımlı değil"). */
  readonly discountLimitDefined: boolean
  readonly discountLimitPercent: string | null
  readonly isInEffectToday: boolean
  readonly createdBy: string
  readonly createdAtUtc: string
  readonly decidedBy: string | null
  readonly decidedAtUtc: string | null
  readonly rejectionReason: string | null
}

/** `CreateSalesContractRequest`, every member present. */
export interface CreateSalesContractBody {
  readonly partyId: string
  readonly companyAccountId: string
  readonly currencyId: string
  readonly validFrom: string
  /** SAT-D362: null = open-ended; sent as null, never left out (the server reads every member). */
  readonly validThrough: string | null
  readonly installments: readonly { readonly dueDate: string; readonly amount: string }[]
  readonly discountRules: readonly { readonly kind: SalesContractDiscountKind; readonly label: string; readonly ratePercent: string }[]
  readonly clauses: string | null
}

/** `SalesDraftStateCommandRequest` for the approval: the version the screen read, no reason. */
export interface SalesContractApprovalBody {
  readonly expectedVersion: string
}

/** `SalesDraftStateCommandRequest` for the rejection: the version the screen read and the required reason. */
export interface SalesContractRejectionBody {
  readonly expectedVersion: string
  readonly reason: string
}

type Obj = Record<string, unknown>

const KEYS = [
  'id', 'contractNo', 'partyId', 'companyAccountId', 'currencyId', 'moneyScale', 'state', 'version', 'validFrom',
  'validThrough', 'amountDisclosure', 'installments', 'plannedTotalAmount', 'discountRules', 'clauses', 'discountApprovalRequired',
  'discountLimitDefined', 'discountLimitPercent', 'isInEffectToday', 'createdBy', 'createdAtUtc', 'decidedBy', 'decidedAtUtc',
  'rejectionReason',
] as const
const INSTALLMENT_KEYS = ['installmentNo', 'dueDate', 'amount'] as const
const RULE_KEYS = ['ruleNo', 'kind', 'label', 'ratePercent'] as const

const exact = (value: unknown, keys: readonly string[]): Obj | null => {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return null
  const object = value as Obj
  const actual = Object.keys(object)
  return actual.length === keys.length && actual.every((key) => keys.includes(key)) ? object : null
}
const text = (value: unknown): value is string => typeof value === 'string' && value.trim().length > 0
const instant = (value: unknown): value is string =>
  typeof value === 'string' && value.trim() !== '' && !Number.isNaN(Date.parse(value))
const ordinal = (value: unknown, expected: number) => typeof value === 'number' && Number.isInteger(value) && value === expected
const states: readonly string[] = Object.values(SALES_CONTRACT_STATE)
const kinds: readonly string[] = Object.values(SALES_CONTRACT_DISCOUNT_KIND)

/** The money profile's scale: the owner bounds it, so any non-negative integer is read here (no digit limit invented). */
const isScale = (value: unknown): value is number => typeof value === 'number' && Number.isInteger(value) && value >= 0

/** An amount exactly as the server writes it: invariant text with exactly `scale` decimals (`F{scale}`), never a JSON number. */
export function isAmountAtScale(value: unknown, scale: number, positive: boolean): value is string {
  if (typeof value !== 'string') return false
  const pattern = scale === 0 ? /^(0|[1-9]\d*)$/ : new RegExp(`^(0|[1-9]\\d*)\\.\\d{${scale}}$`)
  return pattern.test(value) && (!positive || /[1-9]/.test(value))
}

/** A percentage as the server writes it (`0.####`): canonical text, at most 4 decimals, no trailing zero, at most 100. */
export function isRateText(value: unknown, positive: boolean): value is string {
  if (typeof value !== 'string' || !/^(0|[1-9]\d{0,2})(\.\d{0,3}[1-9])?$/.test(value)) return false
  const [integer, fraction = ''] = value.split('.')
  if (Number(integer) > 100 || (Number(integer) === 100 && fraction !== '')) return false
  return !positive || /[1-9]/.test(value)
}

function decodeInstallment(value: unknown, index: number, scale: number, visible: boolean): SalesContractInstallment | null {
  const o = exact(value, INSTALLMENT_KEYS)
  if (o === null || !ordinal(o.installmentNo, index + 1) || !isDateOnly(o.dueDate)
    || !(visible ? isAmountAtScale(o.amount, scale, true) : o.amount === null)) return null
  return { installmentNo: o.installmentNo as number, dueDate: o.dueDate, amount: o.amount as string | null }
}

const disclosures: readonly string[] = ['Visible', 'Withheld']

function decodeRule(value: unknown, index: number, visible: boolean): SalesContractDiscountRule | null {
  const o = exact(value, RULE_KEYS)
  if (o === null || !ordinal(o.ruleNo, index + 1) || typeof o.kind !== 'string' || !kinds.includes(o.kind) || !text(o.label)
    || o.label.length > SALES_CONTRACT_LIMITS.label
    || !(visible ? isRateText(o.ratePercent, true) : o.ratePercent === null)) return null
  return { ruleNo: o.ruleNo as number, kind: o.kind as SalesContractDiscountKind, label: o.label, ratePercent: o.ratePercent as string | null }
}

/** One contract; null unless every key is present, typed and consistent with the state. `expectedId` pins a detail read. */
export function decodeSalesContract(value: unknown, expectedId?: string): SalesContract | null {
  const o = exact(value, KEYS)
  if (o === null || !isReferenceId(o.id) || (expectedId !== undefined && o.id.toLowerCase() !== expectedId.toLowerCase())
    || typeof o.contractNo !== 'number' || !Number.isInteger(o.contractNo) || o.contractNo < 1
    || !isReferenceId(o.partyId) || !isReferenceId(o.companyAccountId) || !isReferenceId(o.currencyId) || !isScale(o.moneyScale)
    || typeof o.state !== 'string' || !states.includes(o.state) || !isVersionString(o.version)
    || !isDateOnly(o.validFrom) || !(o.validThrough === null || (isDateOnly(o.validThrough) && o.validThrough >= o.validFrom))
    || typeof o.amountDisclosure !== 'string' || !disclosures.includes(o.amountDisclosure)
    || !Array.isArray(o.installments) || o.installments.length < 1 || o.installments.length > SALES_CONTRACT_LIMITS.installments
    || !(o.amountDisclosure === 'Visible' ? isAmountAtScale(o.plannedTotalAmount, o.moneyScale, true) : o.plannedTotalAmount === null)
    || !Array.isArray(o.discountRules) || o.discountRules.length > SALES_CONTRACT_LIMITS.discountRules
    || !(o.clauses === null || (text(o.clauses) && o.clauses.length <= SALES_CONTRACT_LIMITS.clauses))
    || typeof o.discountApprovalRequired !== 'boolean' || typeof o.discountLimitDefined !== 'boolean'
    || typeof o.isInEffectToday !== 'boolean' || !text(o.createdBy) || !instant(o.createdAtUtc)) return null
  const scale = o.moneyScale
  const visible = o.amountDisclosure === 'Visible'
  const installments: SalesContractInstallment[] = []
  for (const [index, item] of o.installments.entries()) {
    const installment = decodeInstallment(item, index, scale, visible)
    if (installment === null || (index > 0 && installment.dueDate < installments[index - 1].dueDate)) return null
    installments.push(installment)
  }
  const rules: SalesContractDiscountRule[] = []
  for (const [index, item] of o.discountRules.entries()) {
    const rule = decodeRule(item, index, visible)
    if (rule === null) return null
    rules.push(rule)
  }
  if (rules.filter((rule) => rule.kind === SALES_CONTRACT_DISCOUNT_KIND.total).length > 1) return null

  // SAT-D346/D363: the presence flag survives redaction; the percentage is present only for an authorized reader.
  const limit = o.discountLimitPercent
  if (visible && o.discountLimitDefined ? !isRateText(limit, false) : limit !== null) return null
  // NS-13: no rule never needs approval; rules recorded without a configured limit always do. Whether a rule exceeds a configured
  // limit is the server's comparison and is not redone here.
  if (rules.length === 0 && o.discountApprovalRequired) return null
  if (rules.length > 0 && !o.discountLimitDefined && !o.discountApprovalRequired) return null

  const state = o.state as SalesContractState
  const undecided = o.decidedBy === null && o.decidedAtUtc === null && o.rejectionReason === null
  const decided = text(o.decidedBy) && instant(o.decidedAtUtc) && o.decidedBy.trim() !== o.createdBy.trim()
  let consistent: boolean
  if (state === SALES_CONTRACT_STATE.pending) {
    consistent = o.discountApprovalRequired && undecided && o.version === '1' && !o.isInEffectToday
  } else if (state === SALES_CONTRACT_STATE.active) {
    consistent = o.discountApprovalRequired
      ? decided && o.rejectionReason === null && o.version !== '1'
      : undecided && o.version === '1'
  } else {
    consistent = o.discountApprovalRequired && decided && text(o.rejectionReason)
      && o.rejectionReason.length <= SALES_CONTRACT_LIMITS.reason && o.version !== '1' && !o.isInEffectToday
  }
  if (!consistent) return null

  return {
    id: o.id,
    contractNo: o.contractNo,
    partyId: o.partyId,
    companyAccountId: o.companyAccountId,
    currencyId: o.currencyId,
    moneyScale: scale,
    state,
    version: o.version,
    validFrom: o.validFrom,
    validThrough: o.validThrough as string | null,
    amountDisclosure: o.amountDisclosure as SalesContractAmountDisclosure,
    installments,
    plannedTotalAmount: o.plannedTotalAmount as string | null,
    discountRules: rules,
    clauses: o.clauses as string | null,
    discountApprovalRequired: o.discountApprovalRequired,
    discountLimitDefined: o.discountLimitDefined,
    discountLimitPercent: limit as string | null,
    isInEffectToday: o.isInEffectToday,
    createdBy: o.createdBy,
    createdAtUtc: o.createdAtUtc,
    decidedBy: o.decidedBy as string | null,
    decidedAtUtc: o.decidedAtUtc as string | null,
    rejectionReason: o.rejectionReason as string | null,
  }
}

/** The list (`SalesContractListResponse`): exactly `{ items }`, each item decodable, ids unique, at most the server's bound. */
export function decodeSalesContractList(value: unknown): readonly SalesContract[] | null {
  const o = exact(value, ['items'])
  if (o === null || !Array.isArray(o.items) || o.items.length > SALES_CONTRACT_LIMITS.listed) return null
  const items: SalesContract[] = []
  const ids = new Set<string>()
  const numbers = new Set<number>()
  for (const item of o.items) {
    const contract = decodeSalesContract(item)
    if (contract === null || ids.has(contract.id.toLowerCase()) || numbers.has(contract.contractNo)) return null
    ids.add(contract.id.toLowerCase())
    numbers.add(contract.contractNo)
    items.push(contract)
  }
  return items
}
