/**
 * Wire contract of the order's cost trace (URT-S19, B-27, DEC-PRDCT-009, API-42):
 * `GET /api/v1/manufacturing/orders/{orderId}/cost-trace` (`CostTraceViewV1`).
 *
 * Decoding fails CLOSED and is strict: every object carries exactly the contract's keys, every kilogram and TL figure is
 * the server's lossless invariant text (never a JSON number, never converted or summed here), and the answer must be the
 * order that was asked for. A trace with one part that is not this contract is `null` — a half-trusted trace would show
 * a lot or a cost beside the wrong order.
 *
 * The cost part: only `available` carries figures; every other status carries none (never a zero) and names its reason.
 * A status this screen does not know is not this contract.
 */

export const COST_TRACE_CONTRACT_VERSION = 1

export const COST_TRACE_COST_STATUSES = ['available', 'pending', 'unreadable', 'unavailable'] as const
export type CostTraceCostStatus = (typeof COST_TRACE_COST_STATUSES)[number]

export interface CostTraceLot {
  readonly parentRollId: string
  readonly plannedKg: string
  readonly issuedKg: string
  readonly returnedKg: string
  readonly consumedKg: string
  readonly issueCount: number
  readonly returnCount: number
  readonly firstIssuedAtUtc: string | null
  readonly lastIssuedAtUtc: string | null
  /** The planned input's commercial owner (subcontract); null for a company-owned lot. */
  readonly commercialPartyId: string | null
  readonly commercialPartyAccountId: string | null
}

export interface CostTraceOutput {
  readonly outputClass: string
  readonly netKg: string
  readonly declarationCount: number
  readonly reversalCount: number
}

export interface CostTraceTimeline {
  readonly firstFactAtUtc: string | null
  readonly lastFactAtUtc: string | null
  readonly closedAtUtc: string | null
  /** A code while no time record exists (API-33), never a duration invented here. */
  readonly stoppageCode: string
  readonly durationCode: string
}

export interface CostTraceSalesOrderLink {
  readonly salesOrderId: string
  readonly salesOrderLineId: string
  readonly customerPartyId: string
  readonly productId: string
  readonly lineRevision: number
  readonly linkedAtUtc: string
}

export interface CostTraceCost {
  readonly status: CostTraceCostStatus
  readonly costBasis: string | null
  readonly costPerKgTl: string | null
  readonly rawMaterialCostPerKgTl: string | null
  readonly weighedScrapExpenseTl: string | null
  readonly noteCodes: readonly string[]
  readonly reasonCode: string | null
}

export interface CostTraceView {
  readonly contractVersion: number
  readonly methodCode: string
  readonly orderId: string
  readonly planId: string
  readonly workCenterId: string
  readonly process: string
  readonly status: string
  readonly version: number
  readonly lots: readonly CostTraceLot[]
  readonly totalIssuedKg: string
  readonly totalReturnedKg: string
  readonly totalConsumedKg: string
  readonly outputs: readonly CostTraceOutput[]
  readonly timeline: CostTraceTimeline
  readonly salesOrderLink: CostTraceSalesOrderLink | null
  readonly cost: CostTraceCost
}

const VIEW_KEYS = [
  'contractVersion',
  'methodCode',
  'orderId',
  'planId',
  'workCenterId',
  'process',
  'status',
  'version',
  'lots',
  'totalIssuedKg',
  'totalReturnedKg',
  'totalConsumedKg',
  'outputs',
  'timeline',
  'salesOrderLink',
  'cost',
] as const
const LOT_KEYS = [
  'parentRollId',
  'plannedKg',
  'issuedKg',
  'returnedKg',
  'consumedKg',
  'issueCount',
  'returnCount',
  'firstIssuedAtUtc',
  'lastIssuedAtUtc',
  'commercialPartyId',
  'commercialPartyAccountId',
] as const
const OUTPUT_KEYS = ['outputClass', 'netKg', 'declarationCount', 'reversalCount'] as const
const TIMELINE_KEYS = ['firstFactAtUtc', 'lastFactAtUtc', 'closedAtUtc', 'stoppageCode', 'durationCode'] as const
const LINK_KEYS = [
  'salesOrderId',
  'salesOrderLineId',
  'customerPartyId',
  'productId',
  'lineRevision',
  'linkedAtUtc',
] as const
const COST_KEYS = [
  'status',
  'costBasis',
  'costPerKgTl',
  'rawMaterialCostPerKgTl',
  'weighedScrapExpenseTl',
  'noteCodes',
  'reasonCode',
] as const

const GUID = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/
const NIL_GUID = '00000000-0000-0000-0000-000000000000'
/** The server's own `Text(decimal)`: invariant, no grouping, no exponent, no trailing fractional zero; never negative. */
const AMOUNT = /^(0|[1-9][0-9]*)(\.[0-9]*[1-9])?$/
/** An ISO instant with its offset, as the server writes a `DateTimeOffset`. */
const INSTANT = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})$/
const INSTANT_MAXIMUM_LENGTH = 64
/** A stable machine code: lower-case words joined by `_`, `-` or `.`. */
const CODE = /^[a-z][a-z0-9]*([._-][a-z0-9]+)*$/
const CODE_MAXIMUM_LENGTH = 128
/** An enum name as the server writes it (`GoodProduct`, `Slitting`, `Open`). */
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

function count(value: unknown): number | null {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0 ? value : null
}

function instant(value: unknown): string | null {
  return typeof value === 'string' &&
    value.length <= INSTANT_MAXIMUM_LENGTH &&
    INSTANT.test(value) &&
    !Number.isNaN(Date.parse(value))
    ? value
    : null
}

/** `undefined` marks a value that is neither null nor a valid instant. */
function optionalInstant(value: unknown): string | null | undefined {
  if (value === null) return null
  return instant(value) ?? undefined
}

/** `undefined` marks a value that is neither null nor a valid id. */
function optionalGuid(value: unknown): string | null | undefined {
  if (value === null) return null
  return nonNilGuid(value) ?? undefined
}

function optionalAmount(value: unknown): string | null | undefined {
  if (value === null) return null
  return amount(value) ?? undefined
}

function code(value: unknown): string | null {
  return typeof value === 'string' && value.length <= CODE_MAXIMUM_LENGTH && CODE.test(value) ? value : null
}

function optionalCode(value: unknown): string | null | undefined {
  if (value === null) return null
  return code(value) ?? undefined
}

function enumName(value: unknown): string | null {
  return typeof value === 'string' && value.length <= ENUM_NAME_MAXIMUM_LENGTH && ENUM_NAME.test(value)
    ? value
    : null
}

function isCostStatus(value: unknown): value is CostTraceCostStatus {
  return typeof value === 'string' && (COST_TRACE_COST_STATUSES as readonly string[]).includes(value)
}

function decodeLot(value: unknown): CostTraceLot | null {
  if (!isObject(value) || !hasExactKeys(value, LOT_KEYS)) return null
  const parentRollId = nonNilGuid(value.parentRollId)
  const plannedKg = amount(value.plannedKg)
  const issuedKg = amount(value.issuedKg)
  const returnedKg = amount(value.returnedKg)
  const consumedKg = amount(value.consumedKg)
  const issueCount = count(value.issueCount)
  const returnCount = count(value.returnCount)
  const firstIssuedAtUtc = optionalInstant(value.firstIssuedAtUtc)
  const lastIssuedAtUtc = optionalInstant(value.lastIssuedAtUtc)
  const commercialPartyId = optionalGuid(value.commercialPartyId)
  const commercialPartyAccountId = optionalGuid(value.commercialPartyAccountId)
  if (
    parentRollId === null ||
    plannedKg === null ||
    issuedKg === null ||
    returnedKg === null ||
    consumedKg === null ||
    issueCount === null ||
    returnCount === null ||
    firstIssuedAtUtc === undefined ||
    lastIssuedAtUtc === undefined ||
    commercialPartyId === undefined ||
    commercialPartyAccountId === undefined
  ) {
    return null
  }
  // An issued lot has both moments, an unissued one has neither; an owner comes with its account.
  if ((issueCount === 0) !== (firstIssuedAtUtc === null) || (firstIssuedAtUtc === null) !== (lastIssuedAtUtc === null)) {
    return null
  }
  if ((commercialPartyId === null) !== (commercialPartyAccountId === null)) return null
  return {
    parentRollId,
    plannedKg,
    issuedKg,
    returnedKg,
    consumedKg,
    issueCount,
    returnCount,
    firstIssuedAtUtc,
    lastIssuedAtUtc,
    commercialPartyId,
    commercialPartyAccountId,
  }
}

function decodeOutput(value: unknown): CostTraceOutput | null {
  if (!isObject(value) || !hasExactKeys(value, OUTPUT_KEYS)) return null
  const outputClass = enumName(value.outputClass)
  const netKg = amount(value.netKg)
  const declarationCount = count(value.declarationCount)
  const reversalCount = count(value.reversalCount)
  if (outputClass === null || netKg === null || declarationCount === null || reversalCount === null) return null
  return { outputClass, netKg, declarationCount, reversalCount }
}

function decodeTimeline(value: unknown): CostTraceTimeline | null {
  if (!isObject(value) || !hasExactKeys(value, TIMELINE_KEYS)) return null
  const firstFactAtUtc = optionalInstant(value.firstFactAtUtc)
  const lastFactAtUtc = optionalInstant(value.lastFactAtUtc)
  const closedAtUtc = optionalInstant(value.closedAtUtc)
  const stoppageCode = code(value.stoppageCode)
  const durationCode = code(value.durationCode)
  if (
    firstFactAtUtc === undefined ||
    lastFactAtUtc === undefined ||
    closedAtUtc === undefined ||
    stoppageCode === null ||
    durationCode === null ||
    (firstFactAtUtc === null) !== (lastFactAtUtc === null)
  ) {
    return null
  }
  return { firstFactAtUtc, lastFactAtUtc, closedAtUtc, stoppageCode, durationCode }
}

function decodeLink(value: unknown): CostTraceSalesOrderLink | null | undefined {
  if (value === null) return null
  if (!isObject(value) || !hasExactKeys(value, LINK_KEYS)) return undefined
  const salesOrderId = nonNilGuid(value.salesOrderId)
  const salesOrderLineId = nonNilGuid(value.salesOrderLineId)
  const customerPartyId = nonNilGuid(value.customerPartyId)
  const productId = nonNilGuid(value.productId)
  const lineRevision = count(value.lineRevision)
  const linkedAtUtc = instant(value.linkedAtUtc)
  if (
    salesOrderId === null ||
    salesOrderLineId === null ||
    customerPartyId === null ||
    productId === null ||
    lineRevision === null ||
    lineRevision < 1 ||
    linkedAtUtc === null
  ) {
    return undefined
  }
  return { salesOrderId, salesOrderLineId, customerPartyId, productId, lineRevision, linkedAtUtc }
}

function decodeCost(value: unknown): CostTraceCost | null {
  if (!isObject(value) || !hasExactKeys(value, COST_KEYS) || !isCostStatus(value.status)) return null
  if (!Array.isArray(value.noteCodes)) return null
  const noteCodes: string[] = []
  for (const raw of value.noteCodes) {
    const note = code(raw)
    if (note === null) return null
    noteCodes.push(note)
  }
  const costBasis = optionalCode(value.costBasis)
  const costPerKgTl = optionalAmount(value.costPerKgTl)
  const rawMaterialCostPerKgTl = optionalAmount(value.rawMaterialCostPerKgTl)
  const weighedScrapExpenseTl = optionalAmount(value.weighedScrapExpenseTl)
  const reasonCode = optionalCode(value.reasonCode)
  if (
    costBasis === undefined ||
    costPerKgTl === undefined ||
    rawMaterialCostPerKgTl === undefined ||
    weighedScrapExpenseTl === undefined ||
    reasonCode === undefined
  ) {
    return null
  }
  const status = value.status
  if (status === 'available') {
    // Figures only here, and no reason beside them.
    if (costPerKgTl === null || rawMaterialCostPerKgTl === null || reasonCode !== null) return null
  } else {
    // No figure at all — never a zero — and the reason why.
    if (
      costBasis !== null ||
      costPerKgTl !== null ||
      rawMaterialCostPerKgTl !== null ||
      weighedScrapExpenseTl !== null ||
      reasonCode === null
    ) {
      return null
    }
  }
  return { status, costBasis, costPerKgTl, rawMaterialCostPerKgTl, weighedScrapExpenseTl, noteCodes, reasonCode }
}

/** The trace of `requestedOrderId`, version 1, or `null` when any part of it is not this contract. */
export function decodeCostTrace(value: unknown, requestedOrderId: string): CostTraceView | null {
  if (!isObject(value) || !hasExactKeys(value, VIEW_KEYS)) return null
  if (value.contractVersion !== COST_TRACE_CONTRACT_VERSION) return null
  const methodCode = code(value.methodCode)
  const orderId = nonNilGuid(value.orderId)
  const planId = nonNilGuid(value.planId)
  const workCenterId = nonNilGuid(value.workCenterId)
  const process = enumName(value.process)
  const status = enumName(value.status)
  const version = count(value.version)
  const totalIssuedKg = amount(value.totalIssuedKg)
  const totalReturnedKg = amount(value.totalReturnedKg)
  const totalConsumedKg = amount(value.totalConsumedKg)
  const timeline = decodeTimeline(value.timeline)
  const salesOrderLink = decodeLink(value.salesOrderLink)
  const cost = decodeCost(value.cost)
  if (
    methodCode === null ||
    orderId === null ||
    orderId.toLowerCase() !== requestedOrderId.toLowerCase() ||
    planId === null ||
    workCenterId === null ||
    process === null ||
    status === null ||
    version === null ||
    version < 1 ||
    totalIssuedKg === null ||
    totalReturnedKg === null ||
    totalConsumedKg === null ||
    timeline === null ||
    salesOrderLink === undefined ||
    cost === null ||
    !Array.isArray(value.lots) ||
    !Array.isArray(value.outputs)
  ) {
    return null
  }
  const lots: CostTraceLot[] = []
  const rolls = new Set<string>()
  for (const raw of value.lots) {
    const lot = decodeLot(raw)
    if (lot === null || rolls.has(lot.parentRollId.toLowerCase())) return null
    rolls.add(lot.parentRollId.toLowerCase())
    lots.push(lot)
  }
  const outputs: CostTraceOutput[] = []
  const classes = new Set<string>()
  for (const raw of value.outputs) {
    const output = decodeOutput(raw)
    if (output === null || classes.has(output.outputClass)) return null
    classes.add(output.outputClass)
    outputs.push(output)
  }
  return {
    contractVersion: COST_TRACE_CONTRACT_VERSION,
    methodCode,
    orderId,
    planId,
    workCenterId,
    process,
    status,
    version,
    lots,
    totalIssuedKg,
    totalReturnedKg,
    totalConsumedKg,
    outputs,
    timeline,
    salesOrderLink,
    cost,
  }
}
