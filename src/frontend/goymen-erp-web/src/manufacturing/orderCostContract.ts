/**
 * Wire contract of the order cost view (URT-S17, SAT-D317):
 * `GET /api/v1/manufacturing/orders/{orderId}/production-cost` (`ProductionCostViewV1`).
 *
 * Every figure is the production cost port's RECORDED figure as the server's lossless invariant text: the kg cost at 4
 * decimals, each TL amount at 2 decimals (a half away from zero, URT-D78), the rounding remainder on the good product
 * line with the most kg (URT-D80). Nothing is converted, summed, rounded or recomputed here.
 *
 * Decoding fails CLOSED and is strict: exact keys on every object, figures only as text (never a JSON number), the
 * order that was asked for, and the status rules of the port:
 *  - only `available` carries figures, lines and currency lines, and no reason beside them; the TL that went in equals
 *    the TL distributed, and the remainder sits on exactly the line it names (or on none when it is `0`);
 *  - every other status carries no figure at all (never a zero) and names its reason; an unreadable answer may name the
 *    issues that keep the cost from being computed.
 * A status this screen does not know is not this contract.
 */

export const ORDER_COST_CONTRACT_VERSION = 1

export const ORDER_COST_STATUSES = ['available', 'pending', 'unreadable', 'unavailable'] as const
export type OrderCostStatus = (typeof ORDER_COST_STATUSES)[number]

export interface OrderCostLine {
  readonly outputLabelId: string
  readonly outputClass: string
  readonly kilograms: string
  /** Null on a line the cost is not allocated to (scrap); `notAllocatedCode` then says why. */
  readonly unitCostPerKgTl: string | null
  readonly amountTl: string | null
  readonly separateScrapExpenseTl: string | null
  /** The rounding remainder this line carries, already inside `amountTl`; `0` on every line but one. May be signed. */
  readonly remainderTl: string
  readonly allocationBasis: string | null
  readonly notAllocatedCode: string | null
}

export interface OrderCostCurrencyLine {
  readonly issueMovementId: string
  readonly currencyId: string
  readonly foreignUnitCostPerKg: string | null
  readonly exchangeRate: string | null
  readonly tlUnitCostPerKg: string
  readonly keptKilograms: string
  /** Display only (URT-D55 (2)). */
  readonly foreignAmount: string | null
  readonly foreignUnitCostRatio?: { readonly numerator: string; readonly denominator: string }
  readonly foreignAmountRatio?: { readonly numerator: string; readonly denominator: string }
  readonly exchangeRateRatio?: { readonly numerator: string; readonly denominator: string }
}

export interface OrderCostIssueReason {
  readonly issueMovementId: string
  readonly reasonCode: string
}

export interface OrderCostView {
  readonly contractVersion: number
  readonly orderId: string
  readonly orderStatus: string
  readonly orderVersion: number
  readonly status: OrderCostStatus
  readonly costBasis: string | null
  readonly costPerKgTl: string | null
  readonly rawMaterialCostPerKgTl: string | null
  readonly inputTotalTl: string | null
  readonly weighedScrapExpenseTl: string | null
  readonly distributedTotalTl: string | null
  readonly remainderTl: string | null
  readonly remainderLabelId: string | null
  readonly lines: readonly OrderCostLine[]
  readonly currencyLines: readonly OrderCostCurrencyLine[]
  readonly issueReasons: readonly OrderCostIssueReason[]
  readonly noteCodes: readonly string[]
  readonly reasonCode: string | null
}

const VIEW_KEYS = [
  'contractVersion',
  'orderId',
  'orderStatus',
  'orderVersion',
  'status',
  'costBasis',
  'costPerKgTl',
  'rawMaterialCostPerKgTl',
  'inputTotalTl',
  'weighedScrapExpenseTl',
  'distributedTotalTl',
  'remainderTl',
  'remainderLabelId',
  'lines',
  'currencyLines',
  'issueReasons',
  'noteCodes',
  'reasonCode',
] as const
const LINE_KEYS = [
  'outputLabelId',
  'outputClass',
  'kilograms',
  'unitCostPerKgTl',
  'amountTl',
  'separateScrapExpenseTl',
  'remainderTl',
  'allocationBasis',
  'notAllocatedCode',
] as const
const CURRENCY_LINE_KEYS = [
  'issueMovementId',
  'currencyId',
  'foreignUnitCostPerKg',
  'exchangeRate',
  'tlUnitCostPerKg',
  'keptKilograms',
  'foreignAmount',
] as const
const ISSUE_REASON_KEYS = ['issueMovementId', 'reasonCode'] as const

const GUID = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/
const NIL_GUID = '00000000-0000-0000-0000-000000000000'
/** The server's own `Text(decimal)`: invariant, no grouping, no exponent, no trailing fractional zero; never negative. */
const AMOUNT = /^(0|[1-9][0-9]*)(\.[0-9]*[1-9])?$/
/** The same text with an optional minus sign (the rounding remainder); never `-0`. */
const SIGNED_AMOUNT = /^-?(0|[1-9][0-9]*)(\.[0-9]*[1-9])?$/
const ZERO = '0'
/** A stable machine code: lower-case words joined by `_`, `-` or `.`. */
const CODE = /^[a-z][a-z0-9]*([._-][a-z0-9]+)*$/
const CODE_MAXIMUM_LENGTH = 128
/** An enum name as the server writes it (`GoodProduct`, `Open`). */
const ENUM_NAME = /^[A-Z][A-Za-z0-9]*$/
const ENUM_NAME_MAXIMUM_LENGTH = 64

type Json = Record<string, unknown>

function isObject(value: unknown): value is Json {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function hasExactKeys(value: Json, keys: readonly string[]): boolean {
  const actual = Object.keys(value).sort()
  const expected = [...keys].sort()
  return actual.length === expected.length && actual.every((key, index) => key === expected[index])
}

function nonNilGuid(value: unknown): string | null {
  return typeof value === 'string' && GUID.test(value) && value.toLowerCase() !== NIL_GUID ? value : null
}

function amount(value: unknown): string | null {
  return typeof value === 'string' && AMOUNT.test(value) ? value : null
}

function signedAmount(value: unknown): string | null {
  return typeof value === 'string' && SIGNED_AMOUNT.test(value) && value !== `-${ZERO}` ? value : null
}

function count(value: unknown): number | null {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0 ? value : null
}

function code(value: unknown): string | null {
  return typeof value === 'string' && value.length <= CODE_MAXIMUM_LENGTH && CODE.test(value) ? value : null
}

function enumName(value: unknown): string | null {
  return typeof value === 'string' && value.length <= ENUM_NAME_MAXIMUM_LENGTH && ENUM_NAME.test(value)
    ? value
    : null
}

/** `undefined` marks a value that is neither null nor valid. */
function optional<T>(value: unknown, decode: (raw: unknown) => T | null): T | null | undefined {
  if (value === null) return null
  return decode(value) ?? undefined
}

function isStatus(value: unknown): value is OrderCostStatus {
  return typeof value === 'string' && (ORDER_COST_STATUSES as readonly string[]).includes(value)
}

function decodeArray<T>(value: unknown, decode: (raw: unknown) => T | null): T[] | null {
  if (!Array.isArray(value)) return null
  const items: T[] = []
  for (const raw of value) {
    const item = decode(raw)
    if (item === null) return null
    items.push(item)
  }
  return items
}

function decodeLine(value: unknown): OrderCostLine | null {
  if (!isObject(value) || !hasExactKeys(value, LINE_KEYS)) return null
  const outputLabelId = nonNilGuid(value.outputLabelId)
  const outputClass = enumName(value.outputClass)
  const kilograms = amount(value.kilograms)
  const unitCostPerKgTl = optional(value.unitCostPerKgTl, amount)
  const amountTl = optional(value.amountTl, amount)
  const separateScrapExpenseTl = optional(value.separateScrapExpenseTl, amount)
  const remainderTl = signedAmount(value.remainderTl)
  const allocationBasis = optional(value.allocationBasis, code)
  const notAllocatedCode = optional(value.notAllocatedCode, code)
  if (
    outputLabelId === null ||
    outputClass === null ||
    kilograms === null ||
    unitCostPerKgTl === undefined ||
    amountTl === undefined ||
    separateScrapExpenseTl === undefined ||
    remainderTl === null ||
    allocationBasis === undefined ||
    notAllocatedCode === undefined
  ) {
    return null
  }
  // A unit cost comes with its amount; a line without them says why and carries no remainder.
  if ((unitCostPerKgTl === null) !== (amountTl === null)) return null
  if (amountTl === null && (notAllocatedCode === null || remainderTl !== ZERO)) return null
  return {
    outputLabelId,
    outputClass,
    kilograms,
    unitCostPerKgTl,
    amountTl,
    separateScrapExpenseTl,
    remainderTl,
    allocationBasis,
    notAllocatedCode,
  }
}

export function decodeCurrencyLine(value: unknown): OrderCostCurrencyLine | null {
  if (!isObject(value)) return null
  const exact = Object.hasOwn(value, 'foreignUnitCostRatio') || Object.hasOwn(value, 'foreignAmountRatio')
  const exactRate = Object.hasOwn(value, 'exchangeRateRatio')
  if (!hasExactKeys(value, [...CURRENCY_LINE_KEYS, ...(exact ? ['foreignUnitCostRatio', 'foreignAmountRatio'] : []), ...(exactRate ? ['exchangeRateRatio'] : [])])) return null
  const issueMovementId = nonNilGuid(value.issueMovementId)
  const currencyId = nonNilGuid(value.currencyId)
  const foreignUnitCostPerKg = amount(value.foreignUnitCostPerKg)
  const exchangeRate = amount(value.exchangeRate)
  const exchangeRateRatio = exactRate ? ratio(value.exchangeRateRatio, false) : null
  const tlUnitCostPerKg = amount(value.tlUnitCostPerKg)
  const keptKilograms = amount(value.keptKilograms)
  const foreignAmount = amount(value.foreignAmount)
  if (
    issueMovementId === null ||
    currencyId === null ||
    (!exact && foreignUnitCostPerKg === null) ||
    (exactRate ? exchangeRateRatio === null || (value.exchangeRate !== null && (exchangeRate === null || !ratioMatchesDecimal(exchangeRateRatio, exchangeRate))) : exchangeRate === null) ||
    exchangeRate === '0' ||
    tlUnitCostPerKg === null ||
    keptKilograms === null ||
    (!exact && foreignAmount === null)
  ) {
    return null
  }
  if (exact) {
    const foreignUnitCostRatio = ratio(value.foreignUnitCostRatio, false); const foreignAmountRatio = ratio(value.foreignAmountRatio, true)
    if (value.foreignUnitCostPerKg !== null || value.foreignAmount !== null || foreignUnitCostRatio === null || foreignAmountRatio === null) return null
    return { issueMovementId, currencyId, foreignUnitCostPerKg: null, exchangeRate, tlUnitCostPerKg, keptKilograms,
      foreignAmount: null, foreignUnitCostRatio, foreignAmountRatio, ...(exchangeRateRatio ? { exchangeRateRatio } : {}) }
  }
  return { issueMovementId, currencyId, foreignUnitCostPerKg, exchangeRate, tlUnitCostPerKg, keptKilograms, foreignAmount, ...(exchangeRateRatio ? { exchangeRateRatio } : {}) }
}

function ratio(value: unknown, allowZero: boolean): { readonly numerator: string; readonly denominator: string } | null {
  if (!isObject(value) || !hasExactKeys(value, ['numerator', 'denominator']) || typeof value.numerator !== 'string'
    || typeof value.denominator !== 'string' || value.numerator.length > 160 || value.denominator.length > 160
    || !(allowZero ? /^(0|[1-9]\d*)$/ : /^[1-9]\d*$/).test(value.numerator) || !/^[1-9]\d*$/.test(value.denominator)) return null
  let left = BigInt(value.numerator), right = BigInt(value.denominator)
  while (right !== 0n) { const next = left % right; left = right; right = next }
  return left === 1n ? { numerator: value.numerator, denominator: value.denominator } : null
}

// Validate two representations of owner evidence only; no price, conversion or rounding is calculated in the UI.
function ratioMatchesDecimal(value: { readonly numerator: string; readonly denominator: string }, scalar: string): boolean {
  const [whole, fractional = ''] = scalar.split('.')
  return BigInt(value.numerator) * (10n ** BigInt(fractional.length)) === BigInt(`${whole}${fractional}`) * BigInt(value.denominator)
}

function decodeIssueReason(value: unknown): OrderCostIssueReason | null {
  if (!isObject(value) || !hasExactKeys(value, ISSUE_REASON_KEYS)) return null
  const issueMovementId = nonNilGuid(value.issueMovementId)
  const reasonCode = code(value.reasonCode)
  return issueMovementId === null || reasonCode === null ? null : { issueMovementId, reasonCode }
}

/** The remainder sits on exactly the line it names, every other line carries `0`; a `0` remainder names no line. */
function remainderIsPlaced(
  remainderTl: string,
  remainderLabelId: string | null,
  lines: readonly OrderCostLine[],
): boolean {
  if (remainderTl === ZERO) {
    return remainderLabelId === null && lines.every((line) => line.remainderTl === ZERO)
  }
  if (remainderLabelId === null) return false
  const carriers = lines.filter((line) => line.outputLabelId.toLowerCase() === remainderLabelId.toLowerCase())
  if (carriers.length !== 1 || carriers[0].remainderTl !== remainderTl || carriers[0].amountTl === null) return false
  return lines.every((line) => line === carriers[0] || line.remainderTl === ZERO)
}

/** The cost of `requestedOrderId`, version 1, or `null` when any part of it is not this contract. */
export function decodeOrderCost(value: unknown, requestedOrderId: string): OrderCostView | null {
  if (!isObject(value) || !hasExactKeys(value, VIEW_KEYS)) return null
  if (value.contractVersion !== ORDER_COST_CONTRACT_VERSION || !isStatus(value.status)) return null
  const status = value.status
  const orderId = nonNilGuid(value.orderId)
  const orderStatus = enumName(value.orderStatus)
  const orderVersion = count(value.orderVersion)
  const costBasis = optional(value.costBasis, code)
  const costPerKgTl = optional(value.costPerKgTl, amount)
  const rawMaterialCostPerKgTl = optional(value.rawMaterialCostPerKgTl, amount)
  const inputTotalTl = optional(value.inputTotalTl, amount)
  const weighedScrapExpenseTl = optional(value.weighedScrapExpenseTl, amount)
  const distributedTotalTl = optional(value.distributedTotalTl, amount)
  const remainderTl = optional(value.remainderTl, signedAmount)
  const remainderLabelId = optional(value.remainderLabelId, nonNilGuid)
  const lines = decodeArray(value.lines, decodeLine)
  const currencyLines = decodeArray(value.currencyLines, decodeCurrencyLine)
  const issueReasons = decodeArray(value.issueReasons, decodeIssueReason)
  const noteCodes = decodeArray(value.noteCodes, code)
  const reasonCode = optional(value.reasonCode, code)
  if (
    orderId === null ||
    orderId.toLowerCase() !== requestedOrderId.toLowerCase() ||
    orderStatus === null ||
    orderVersion === null ||
    orderVersion < 1 ||
    costBasis === undefined ||
    costPerKgTl === undefined ||
    rawMaterialCostPerKgTl === undefined ||
    inputTotalTl === undefined ||
    weighedScrapExpenseTl === undefined ||
    distributedTotalTl === undefined ||
    remainderTl === undefined ||
    remainderLabelId === undefined ||
    lines === null ||
    currencyLines === null ||
    issueReasons === null ||
    noteCodes === null ||
    reasonCode === undefined
  ) {
    return null
  }
  const labels = new Set(lines.map((line) => line.outputLabelId.toLowerCase()))
  if (labels.size !== lines.length) return null
  if (status === 'available') {
    // Figures only here, no reason beside them, giren TL = dağıtılan TL and the remainder on the line it names.
    if (
      costPerKgTl === null ||
      rawMaterialCostPerKgTl === null ||
      inputTotalTl === null ||
      distributedTotalTl === null ||
      remainderTl === null ||
      inputTotalTl !== distributedTotalTl ||
      !remainderIsPlaced(remainderTl, remainderLabelId, lines) ||
      issueReasons.length > 0 ||
      reasonCode !== null
    ) {
      return null
    }
  } else if (
    // No figure at all — never a zero — and the reason why.
    costBasis !== null ||
    costPerKgTl !== null ||
    rawMaterialCostPerKgTl !== null ||
    inputTotalTl !== null ||
    weighedScrapExpenseTl !== null ||
    distributedTotalTl !== null ||
    remainderTl !== null ||
    remainderLabelId !== null ||
    lines.length > 0 ||
    currencyLines.length > 0 ||
    reasonCode === null
  ) {
    return null
  }
  return {
    contractVersion: ORDER_COST_CONTRACT_VERSION,
    orderId,
    orderStatus,
    orderVersion,
    status,
    costBasis,
    costPerKgTl,
    rawMaterialCostPerKgTl,
    inputTotalTl,
    weighedScrapExpenseTl,
    distributedTotalTl,
    remainderTl,
    remainderLabelId,
    lines,
    currencyLines,
    issueReasons,
    noteCodes,
    reasonCode,
  }
}
