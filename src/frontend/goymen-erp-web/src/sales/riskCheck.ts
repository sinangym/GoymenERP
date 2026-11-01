/**
 * MOD-01 / API-06 / UI-15 (DEC-FIN-007, DEC-FIN-009; SAT-D46…D53, SAT-D80): the customer risk check of an order — the contract
 * of `GET /api/v1/sales/drafts/{id}/risk-check` (`GetSalesOrderDraftRiskCheckV1`, `SalesOrderDraftRiskCheckResponse`). The only
 * file that spells its wire names.
 *
 * Every number and the result are the SERVER's (`SalesCustomerRiskCalculator`); this file only reads what it answered and never
 * adds, subtracts or converts — in particular the remaining limit ("kullanılabilir limit") is not derived here: the read does
 * not carry it. Rules taken from the backend contract (`SalesDraftRiskCheckV1.cs`, the Sales README "Customer risk check"):
 *  - every member is present, nullable ones as explicit null; unknown members, results, states, kinds or tax bases are refused;
 *  - the answer is bound to the detail on screen: the same version, party and customer account;
 *  - `components` are the three stages of the gross risk, in that order; `thisOrder`, `creditLimit` and `collateral` carry
 *    their own kind;
 *  - a figure that is not `Available` has no tax basis and no amounts and names its reason; an `Available` one has no reason;
 *  - `Within` / `Over` carry no reason (the decision answered); `Unavailable` is fail-closed and lists why;
 *  - `Withheld` (no `sales.orders.price.read`): every amount — figures and totals — is null; the states, reasons and the
 *    result stay. A withheld answer carrying an amount is malformed, never shown.
 *
 * Decimal text stays text. A decoder returns null for anything it cannot vouch for; the caller fails closed.
 */

import { salesDraftDetailPath, type DraftDetail } from './contract'
import { isReferenceId, isVersionString } from './wirePrimitives'

/** API-06: the order's customer risk check (GET, under the draft read; amounts only with the price read). */
export const salesDraftRiskCheckPath = (id: string) => `${salesDraftDetailPath(id)}/risk-check`

/** `SalesDraftRiskResultsV1`. */
export const RISK_RESULTS = ['Within', 'Over', 'Unavailable'] as const
export type RiskResult = (typeof RISK_RESULTS)[number]

/** `SalesDraftRiskFigureStatesV1`. */
export const RISK_FIGURE_STATES = ['Available', 'NoSource', 'Unavailable'] as const
export type RiskFigureState = (typeof RISK_FIGURE_STATES)[number]

/** `SalesDraftRiskFigureKindsV1`. */
export const RISK_FIGURE_KIND = {
  openReceivables: 'OpenReceivables',
  shippedNotInvoiced: 'ShippedNotInvoiced',
  approvedUnshippedRemainder: 'ApprovedUnshippedRemainder',
  thisOrder: 'ThisOrder',
  creditLimit: 'CreditLimit',
  collateral: 'Collateral',
} as const
export type RiskFigureKind = (typeof RISK_FIGURE_KIND)[keyof typeof RISK_FIGURE_KIND]

/** The three stages of the gross risk (DEC-FIN-007), in the order the server answers them. */
export const RISK_COMPONENT_KINDS = [
  RISK_FIGURE_KIND.openReceivables,
  RISK_FIGURE_KIND.shippedNotInvoiced,
  RISK_FIGURE_KIND.approvedUnshippedRemainder,
] as const

/** `SalesRiskTaxBasis` as the wire spells it. */
export const RISK_TAX_BASES = ['IncludingTax', 'ExcludingTax'] as const
export type RiskTaxBasis = (typeof RISK_TAX_BASES)[number]

/** `SalesRiskComponents`: the part a reason is about (`Check` = the comparison of the parts, not one part). */
export const RISK_REASON_COMPONENTS = [...Object.values(RISK_FIGURE_KIND), 'Check'] as const
export type RiskReasonComponent = (typeof RISK_REASON_COMPONENTS)[number]

export type RiskAmountDisclosure = 'Visible' | 'Withheld'

export interface RiskAmount {
  readonly currencyId: string
  readonly amount: string
}

export interface RiskFigure {
  readonly kind: RiskFigureKind
  readonly source: string
  readonly state: RiskFigureState
  readonly taxBasis: RiskTaxBasis | null
  /** null: not available or withheld; [] is a recorded zero (the owner answered "nothing"). */
  readonly amounts: readonly RiskAmount[] | null
  readonly reasonCode: string | null
}

export interface RiskTotals {
  readonly currencyId: string
  readonly grossRisk: string | null
  readonly grossRiskWithThisOrder: string | null
}

export interface RiskReason {
  readonly component: RiskReasonComponent
  readonly code: string
}

export interface RiskCheck {
  readonly draftVersion: string
  readonly partyId: string
  readonly companyAccountId: string
  readonly result: RiskResult
  readonly reasons: readonly RiskReason[]
  readonly amountDisclosure: RiskAmountDisclosure
  readonly inLocalCurrency: RiskTotals | null
  readonly inLimitCurrency: RiskTotals | null
  readonly components: readonly RiskFigure[]
  readonly thisOrder: RiskFigure
  readonly creditLimit: RiskFigure
  readonly collateral: RiskFigure
}

type Obj = Record<string, unknown>

function exact(value: unknown, keys: readonly string[]): Obj | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return null
  const o = value as Obj
  if (Object.keys(o).length !== keys.length || keys.some((key) => !Object.hasOwn(o, key))) return null
  return o
}

const oneOf = <T extends string>(values: readonly T[], v: unknown): v is T => typeof v === 'string' && (values as readonly string[]).includes(v)
/** Amounts are exact invariant text, never negative (a negative owner amount is the server's `amount_invalid`). */
const isAmountText = (v: unknown): v is string => typeof v === 'string' && v.length <= 64 && /^(0|[1-9][0-9]*)(\.[0-9]+)?$/.test(v)
/** A stable code (`snake_case`) or source name (`dotted.kebab-case`); bounded, never free text. */
const isCode = (v: unknown): v is string => typeof v === 'string' && v.length <= 128 && /^[a-z][a-z0-9_]*$/.test(v)
const isSource = (v: unknown): v is string => typeof v === 'string' && v.length <= 128 && /^[a-z][a-z0-9.-]*$/.test(v)

function sameId(a: string, b: string): boolean {
  return a.toLowerCase() === b.toLowerCase()
}

function decodeAmount(value: unknown): RiskAmount | null {
  const o = exact(value, ['currencyId', 'amount'])
  if (o === null || !isReferenceId(o.currencyId) || !isAmountText(o.amount)) return null
  return { currencyId: o.currencyId, amount: o.amount }
}

function decodeFigure(value: unknown, kind: RiskFigureKind, visible: boolean): RiskFigure | null {
  const o = exact(value, ['kind', 'source', 'state', 'taxBasis', 'amounts', 'reasonCode'])
  if (o === null || o.kind !== kind || !isSource(o.source) || !oneOf(RISK_FIGURE_STATES, o.state)) return null
  const available = o.state === 'Available'
  if (available) {
    if (o.reasonCode !== null || !(o.taxBasis === null || oneOf(RISK_TAX_BASES, o.taxBasis))) return null
  } else if (o.taxBasis !== null || !isCode(o.reasonCode)) {
    return null
  }
  let amounts: RiskAmount[] | null = null
  if (available && visible) {
    if (!Array.isArray(o.amounts)) return null
    amounts = []
    for (const entry of o.amounts) {
      const amount = decodeAmount(entry)
      if (amount === null || amounts.some((seen) => sameId(seen.currencyId, amount.currencyId))) return null
      amounts.push(amount)
    }
  } else if (o.amounts !== null) {
    // Not available, or withheld from this caller: an amount here is a leak or a contradiction — never shown.
    return null
  }
  return {
    kind, source: o.source, state: o.state, taxBasis: o.taxBasis as RiskTaxBasis | null, amounts,
    reasonCode: o.reasonCode as string | null,
  }
}

function decodeTotals(value: unknown, visible: boolean): RiskTotals | null | undefined {
  if (value === null) return null
  const o = exact(value, ['currencyId', 'grossRisk', 'grossRiskWithThisOrder'])
  if (o === null || !isReferenceId(o.currencyId)) return undefined
  for (const member of [o.grossRisk, o.grossRiskWithThisOrder]) {
    if (member === null) continue
    if (!visible || !isAmountText(member)) return undefined
  }
  return {
    currencyId: o.currencyId, grossRisk: o.grossRisk as string | null, grossRiskWithThisOrder: o.grossRiskWithThisOrder as string | null,
  }
}

const RESPONSE_KEYS = ['draftVersion', 'partyId', 'companyAccountId', 'result', 'reasons', 'amountDisclosure', 'inLocalCurrency',
  'inLimitCurrency', 'components', 'thisOrder', 'creditLimit', 'collateral'] as const

/**
 * The risk check of `detail`, or null when the body is malformed or describes anything but exactly that detail (another
 * version, party or customer account).
 */
export function decodeRiskCheck(value: unknown, detail: DraftDetail): RiskCheck | null {
  const o = exact(value, RESPONSE_KEYS)
  if (o === null || !isVersionString(o.draftVersion) || o.draftVersion !== detail.draft.version
    || !isReferenceId(o.partyId) || !sameId(o.partyId, detail.draft.partyId)
    || !isReferenceId(o.companyAccountId) || !sameId(o.companyAccountId, detail.draft.companyAccountId)
    || !oneOf(RISK_RESULTS, o.result) || (o.amountDisclosure !== 'Visible' && o.amountDisclosure !== 'Withheld')
    || !Array.isArray(o.reasons) || !Array.isArray(o.components) || o.components.length !== RISK_COMPONENT_KINDS.length) return null
  const visible = o.amountDisclosure === 'Visible'
  const reasons: RiskReason[] = []
  for (const entry of o.reasons) {
    const reason = exact(entry, ['component', 'code'])
    if (reason === null || !oneOf(RISK_REASON_COMPONENTS, reason.component) || !isCode(reason.code)) return null
    // The server lists every reason of the decision once.
    if (reasons.some((seen) => seen.component === reason.component && seen.code === reason.code)) return null
    reasons.push({ component: reason.component, code: reason.code })
  }
  // A decided answer carries no reason; a fail-closed one says why.
  if ((o.result === 'Unavailable') !== (reasons.length > 0)) return null
  const components: RiskFigure[] = []
  for (const [index, kind] of RISK_COMPONENT_KINDS.entries()) {
    const figure = decodeFigure(o.components[index], kind, visible)
    if (figure === null) return null
    components.push(figure)
  }
  const thisOrder = decodeFigure(o.thisOrder, RISK_FIGURE_KIND.thisOrder, visible)
  const creditLimit = decodeFigure(o.creditLimit, RISK_FIGURE_KIND.creditLimit, visible)
  const collateral = decodeFigure(o.collateral, RISK_FIGURE_KIND.collateral, visible)
  const inLocalCurrency = decodeTotals(o.inLocalCurrency, visible)
  const inLimitCurrency = decodeTotals(o.inLimitCurrency, visible)
  if (thisOrder === null || creditLimit === null || collateral === null || inLocalCurrency === undefined
    || inLimitCurrency === undefined) return null
  // The decision is made in the limit's currency alone (SAT-D53): a decided answer has those totals.
  if (o.result !== 'Unavailable' && (inLimitCurrency === null || creditLimit.state !== 'Available')) return null
  if (visible && o.result !== 'Unavailable' && (inLimitCurrency?.grossRiskWithThisOrder ?? null) === null) return null
  return {
    draftVersion: o.draftVersion, partyId: detail.draft.partyId, companyAccountId: detail.draft.companyAccountId, result: o.result,
    reasons, amountDisclosure: o.amountDisclosure, inLocalCurrency, inLimitCurrency, components, thisOrder, creditLimit, collateral,
  }
}
