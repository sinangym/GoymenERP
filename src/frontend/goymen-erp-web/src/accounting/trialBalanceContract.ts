import { isDateOnly } from '../finance/dateOnly'
import { isNonNegativeDecimalText } from '../finance/decimalText'

export type TrialBalanceSide = 'Debit' | 'Credit' | 'Balanced'

export interface TrialBalanceQuery {
  readonly fromAccountingDate: string
  readonly throughAccountingDate: string
  readonly currencyId: string | null
}

export interface TrialBalanceLine {
  readonly accountId: string
  readonly currencyId: string
  readonly debit: string
  readonly credit: string
  readonly balanceSide: TrialBalanceSide
  readonly balance: string
}

export interface TrialBalanceCurrencyTotal {
  readonly currencyId: string
  readonly debit: string
  readonly credit: string
  readonly isBalanced: boolean
}

export interface TrialBalanceReport extends TrialBalanceQuery {
  readonly lines: readonly TrialBalanceLine[]
  readonly currencyTotals: readonly TrialBalanceCurrencyTotal[]
}

type Json = Record<string, unknown>

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const NIL_GUID = '00000000-0000-0000-0000-000000000000'
const SIDES = new Set<TrialBalanceSide>(['Debit', 'Credit', 'Balanced'])

const record = (value: unknown): Json | null =>
  typeof value === 'object' && value !== null && !Array.isArray(value) ? (value as Json) : null

const exact = (value: Json, keys: readonly string[]): boolean => {
  const actual = Object.keys(value)
  return actual.length === keys.length && keys.every((key) => Object.prototype.hasOwnProperty.call(value, key))
}

export const isTrialBalanceGuid = (value: unknown): value is string =>
  typeof value === 'string' && GUID.test(value) && value.toLowerCase() !== NIL_GUID

export function isTrialBalanceQuery(value: TrialBalanceQuery): boolean {
  return isDateOnly(value.fromAccountingDate)
    && isDateOnly(value.throughAccountingDate)
    && value.fromAccountingDate <= value.throughAccountingDate
    && (value.currencyId === null || isTrialBalanceGuid(value.currencyId))
}

export function decodeTrialBalanceReport(value: unknown, expected: TrialBalanceQuery): TrialBalanceReport | null {
  const source = record(value)
  if (source === null || !exact(source,
    ['fromAccountingDate', 'throughAccountingDate', 'currencyId', 'lines', 'currencyTotals'])) return null
  if (!isTrialBalanceQuery(expected)
    || source.fromAccountingDate !== expected.fromAccountingDate
    || source.throughAccountingDate !== expected.throughAccountingDate
    || source.currencyId !== expected.currencyId
    || !Array.isArray(source.lines)
    || !Array.isArray(source.currencyTotals)) return null

  const lines: TrialBalanceLine[] = []
  const lineKeys = new Set<string>()
  for (const raw of source.lines) {
    const line = record(raw)
    if (line === null || !exact(line, ['accountId', 'currencyId', 'debit', 'credit', 'balanceSide', 'balance'])) {
      return null
    }
    if (!isTrialBalanceGuid(line.accountId) || !isTrialBalanceGuid(line.currencyId)
      || (expected.currencyId !== null && line.currencyId !== expected.currencyId)
      || !isNonNegativeDecimalText(line.debit) || !isNonNegativeDecimalText(line.credit)
      || typeof line.balanceSide !== 'string' || !SIDES.has(line.balanceSide as TrialBalanceSide)
      || !isNonNegativeDecimalText(line.balance)) return null
    if ((line.balanceSide === 'Balanced') !== (line.balance === '0')) return null
    const key = `${line.currencyId}:${line.accountId}`
    if (lineKeys.has(key)) return null
    lineKeys.add(key)
    lines.push(line as unknown as TrialBalanceLine)
  }

  const currencyTotals: TrialBalanceCurrencyTotal[] = []
  const totalCurrencies = new Set<string>()
  for (const raw of source.currencyTotals) {
    const total = record(raw)
    if (total === null || !exact(total, ['currencyId', 'debit', 'credit', 'isBalanced'])) return null
    if (!isTrialBalanceGuid(total.currencyId)
      || (expected.currencyId !== null && total.currencyId !== expected.currencyId)
      || !isNonNegativeDecimalText(total.debit) || !isNonNegativeDecimalText(total.credit)
      || typeof total.isBalanced !== 'boolean' || totalCurrencies.has(total.currencyId)) return null
    totalCurrencies.add(total.currencyId)
    currencyTotals.push(total as unknown as TrialBalanceCurrencyTotal)
  }

  const lineCurrencies = new Set(lines.map((line) => line.currencyId))
  if (lineCurrencies.size !== totalCurrencies.size
    || [...lineCurrencies].some((currencyId) => !totalCurrencies.has(currencyId))) return null

  return {
    fromAccountingDate: expected.fromAccountingDate,
    throughAccountingDate: expected.throughAccountingDate,
    currencyId: expected.currencyId,
    lines,
    currencyTotals,
  }
}
