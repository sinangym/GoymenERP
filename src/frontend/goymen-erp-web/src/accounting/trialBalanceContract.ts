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

type DecimalValue = readonly [coefficient: bigint, scale: number]

const decimalValue = (value: string): DecimalValue => {
  const [whole, fraction = ''] = value.split('.')
  return [BigInt(`${whole}${fraction}`), fraction.length]
}

const scaledCoefficient = ([coefficient, scale]: DecimalValue, targetScale: number): bigint =>
  coefficient * (10n ** BigInt(targetScale - scale))

const compareDecimal = (left: DecimalValue, right: DecimalValue): -1 | 0 | 1 => {
  const scale = Math.max(left[1], right[1])
  const difference = scaledCoefficient(left, scale) - scaledCoefficient(right, scale)
  return difference < 0n ? -1 : difference > 0n ? 1 : 0
}

const addDecimal = (left: DecimalValue, right: DecimalValue): DecimalValue => {
  const scale = Math.max(left[1], right[1])
  return [scaledCoefficient(left, scale) + scaledCoefficient(right, scale), scale]
}

const differenceMagnitude = (left: DecimalValue, right: DecimalValue): DecimalValue => {
  const scale = Math.max(left[1], right[1])
  const difference = scaledCoefficient(left, scale) - scaledCoefficient(right, scale)
  return [difference < 0n ? -difference : difference, scale]
}

const ZERO: DecimalValue = [0n, 0]

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
    const debit = decimalValue(line.debit)
    const credit = decimalValue(line.credit)
    const balance = decimalValue(line.balance)
    const direction = compareDecimal(debit, credit)
    const expectedSide: TrialBalanceSide = direction > 0 ? 'Debit' : direction < 0 ? 'Credit' : 'Balanced'
    if (line.balanceSide !== expectedSide || compareDecimal(balance, differenceMagnitude(debit, credit)) !== 0) {
      return null
    }
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

  for (const total of currencyTotals) {
    const sums = lines
      .filter((line) => line.currencyId === total.currencyId)
      .reduce(
        (current, line) => ({
          debit: addDecimal(current.debit, decimalValue(line.debit)),
          credit: addDecimal(current.credit, decimalValue(line.credit)),
        }),
        { debit: ZERO, credit: ZERO },
      )
    const totalDebit = decimalValue(total.debit)
    const totalCredit = decimalValue(total.credit)
    if (compareDecimal(totalDebit, sums.debit) !== 0
      || compareDecimal(totalCredit, sums.credit) !== 0
      || total.isBalanced !== (compareDecimal(totalDebit, totalCredit) === 0)) return null
  }

  return {
    fromAccountingDate: expected.fromAccountingDate,
    throughAccountingDate: expected.throughAccountingDate,
    currencyId: expected.currencyId,
    lines,
    currencyTotals,
  }
}
