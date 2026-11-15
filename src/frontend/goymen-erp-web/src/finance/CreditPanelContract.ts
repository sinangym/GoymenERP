/**
 * F018 plan §L3 — the wire shape of the Cari card's Finance panel (`GET /finance/party-credit-panel/…`; FNS-D27 (2),
 * FNS-D31 (4), FNS-D58), decoded strictly like ./CollateralContract.ts: exact member sets, the verified shell company,
 * the asked party and account, decimals as TEXT. An answer that does not decode is never shown as data.
 *
 * Source: `PartyCreditPanelV1.cs` (Finance.Contracts). The wire is camelCase; the figure state is its enum name; a
 * `DateOnly` is `YYYY-MM-DD`.
 *
 * Every figure is the server's, per currency, never added across currencies and never recomputed here:
 * - `Available`: the amounts are complete (an empty list is a recorded zero), no reason.
 * - `Unavailable` ("hesaplanamadı"), `NotSet` (only the credit limit: no approved limit) or `Withheld` (the caller may
 *   not read it): no amount, a stable reason code.
 * The gross risk and the limit headroom are not in this answer (they are the Sales risk calculator's).
 */

import { isBoundUuid, type Decoded } from './contract'
import { isDateOnly } from './dateOnly'
import { isNonNegativeDecimalText } from './decimalText'

export type CreditPanelFigureState = 'Available' | 'Unavailable' | 'NotSet' | 'Withheld'

export interface CreditPanelAmount {
  readonly currencyId: string
  readonly amount: string
}

export type CreditPanelFigure =
  | { readonly state: 'Available'; readonly amounts: readonly CreditPanelAmount[]; readonly reasonCode: null }
  | { readonly state: 'Unavailable' | 'NotSet' | 'Withheld'; readonly amounts: readonly []; readonly reasonCode: string }

export const CREDIT_PANEL_FIGURES = [
  'creditLimit',
  'openReceivables',
  'heldCollateral',
  'weightedCollateral',
  'collateralUtilization',
  'documentaryRisk',
] as const

export type CreditPanelFigureName = (typeof CREDIT_PANEL_FIGURES)[number]

export type CreditPanelView = {
  readonly companyId: string
  readonly partyId: string
  readonly companyAccountId: string
  readonly asOf: string
} & { readonly [K in CreditPanelFigureName]: CreditPanelFigure }

const FAIL = { ok: false } as const

const VIEW_FIELDS = ['companyId', 'partyId', 'companyAccountId', 'asOf', ...CREDIT_PANEL_FIGURES] as const
const FIGURE_FIELDS = ['state', 'amounts', 'reasonCode'] as const
const AMOUNT_FIELDS = ['currencyId', 'amount'] as const
const NOT_AVAILABLE_STATES: readonly string[] = ['Unavailable', 'NotSet', 'Withheld']

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function hasExactKeys(value: Record<string, unknown>, keys: readonly string[]): boolean {
  const own = Object.keys(value)
  return own.length === keys.length && keys.every((key) => Object.prototype.hasOwnProperty.call(value, key))
}

/** Only the credit limit can be "not set"; every other figure is either answered, unavailable or withheld. */
function decodeFigure(value: unknown, mayBeNotSet: boolean): CreditPanelFigure | null {
  if (!isRecord(value) || !hasExactKeys(value, FIGURE_FIELDS)) return null
  const { state, amounts, reasonCode } = value
  if (typeof state !== 'string' || !Array.isArray(amounts)) return null
  if (state !== 'Available') {
    if (!NOT_AVAILABLE_STATES.includes(state) || (state === 'NotSet' && !mayBeNotSet)) return null
    // A figure that cannot be shown carries its reason and no amount.
    if (amounts.length > 0 || typeof reasonCode !== 'string' || reasonCode.length === 0) return null
    return { state: state as 'Unavailable' | 'NotSet' | 'Withheld', amounts: [], reasonCode }
  }
  if (reasonCode !== null) return null
  const decoded: CreditPanelAmount[] = []
  const seen = new Set<string>()
  for (const raw of amounts) {
    if (!isRecord(raw) || !hasExactKeys(raw, AMOUNT_FIELDS)) return null
    // One line per currency, never negative.
    if (!isBoundUuid(raw.currencyId) || seen.has(raw.currencyId) || !isNonNegativeDecimalText(raw.amount)) return null
    seen.add(raw.currencyId)
    decoded.push({ currencyId: raw.currencyId, amount: raw.amount })
  }
  return { state: 'Available', amounts: decoded, reasonCode: null }
}

/** The panel of the account that was asked for, in the verified shell company. */
export function decodeCreditPanel(value: unknown, companyId: string, partyId: string,
  companyAccountId: string): Decoded<CreditPanelView> {
  if (!isRecord(value) || !hasExactKeys(value, VIEW_FIELDS)) return FAIL
  if (value.companyId !== companyId || value.partyId !== partyId || value.companyAccountId !== companyAccountId) {
    return FAIL
  }
  const { asOf } = value
  if (typeof asOf !== 'string' || !isDateOnly(asOf)) return FAIL
  const figures = {} as Record<CreditPanelFigureName, CreditPanelFigure>
  for (const name of CREDIT_PANEL_FIGURES) {
    const figure = decodeFigure(value[name], name === 'creditLimit')
    if (figure === null) return FAIL
    figures[name] = figure
  }
  return { ok: true, value: { companyId, partyId, companyAccountId, asOf, ...figures } }
}
