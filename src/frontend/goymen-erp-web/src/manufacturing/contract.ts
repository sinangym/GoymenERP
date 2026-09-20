/**
 * Wire contract of `GET /api/v1/manufacturing/orders/{orderId}` (`OrderViewV1`, MFG-API #313)
 * and of the fact commands' request bodies.
 *
 * Kilograms travel as lossless invariant TEXT (Ö2): a JSON number would lose digits in a
 * browser. They are decoded as text, displayed as text and sent as text; this layer never
 * turns one into a number, rounds it or pads it.
 *
 * Decoding fails CLOSED: a body that does not match the contract is `null`, and the screen
 * says the answer could not be read. A partially trusted order would put one roll's
 * figures beside another's.
 */

/** The five output classes of DEC-PRDCT-006, by their server names. */
export const OUTPUT_CLASSES = [
  'GoodProduct',
  'SecondQuality',
  'UsableRemnant',
  'Scrap',
  'ProcessLoss',
] as const

export type OutputClass = (typeof OUTPUT_CLASSES)[number]

export type OrderStatus = 'Open' | 'Closed'

export interface RollBalance {
  readonly parentRollId: string
  readonly plannedKg: string
  readonly issuedKg: string
  readonly returnedKg: string
  readonly netIssuedKg: string
  readonly outputsKg: Readonly<Record<OutputClass, string>>
  readonly workInProgressKg: string
  /** Signed: never folded into a class, never shown as 0 when it is not. */
  readonly unexplainedKg: string
  readonly isFullyAccounted: boolean
}

/** Only in the basis the plan declared; `basis === null` means unavailable, never 0. */
export interface RemainingTarget {
  readonly basis: string | null
  readonly targetKg: string | null
  readonly achievedKg: string | null
  readonly remainingKg: string | null
}

export interface ProductionOrderView {
  readonly orderId: string
  readonly companyId: string
  readonly planId: string
  readonly workCenterId: string
  readonly process: string
  readonly status: OrderStatus
  /** The version the next fact must name as `expectedVersion`. */
  readonly version: number
  readonly target: { readonly basis: string; readonly kg: string } | null
  readonly remainingTarget: RemainingTarget
  readonly rolls: readonly RollBalance[]
  /** `NotCalculated` in this slice: a status, never an amount. */
  readonly costStatus: 'NotCalculated'
  readonly closedByUserId: string | null
  readonly closedAtUtc: string | null
}

const GUID = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/
/** The server's own `Text(decimal)`: invariant, no grouping, no exponent. */
const KG_OUT = /^(0|[1-9][0-9]*)(\.[0-9]*[1-9])?$/
const SIGNED_KG_OUT = /^-?(0|[1-9][0-9]*)(\.[0-9]*[1-9])?$/
/**
 * The command's input grammar, the same as the server's (`KilogramText`): plain,
 * non-negative, dot decimal. The SCALE is not decided here — the company's kilogram
 * profile is the server's question, and more decimals than it allows are refused
 * there (`manufacturing_mass_scale_exceeded`), never rounded here.
 */
const KG_IN = /^(0|[1-9][0-9]{0,17})(\.[0-9]{1,20})?$/

export function isKilogramText(value: string): boolean {
  return KG_IN.test(value)
}

type Json = Record<string, unknown>

function isObject(value: unknown): value is Json {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function guid(value: unknown): string | null {
  return typeof value === 'string' && GUID.test(value) ? value : null
}

function nullableGuid(value: unknown): string | null | undefined {
  if (value === null) return null
  return guid(value) ?? undefined
}

function kg(value: unknown, signed = false): string | null {
  return typeof value === 'string' && (signed ? SIGNED_KG_OUT : KG_OUT).test(value) ? value : null
}

function nullableKg(value: unknown): string | null | undefined {
  if (value === null) return null
  return kg(value) ?? undefined
}

function decodeOutputs(value: unknown): Record<OutputClass, string> | null {
  if (!isObject(value)) return null
  const keys = Object.keys(value)
  if (keys.length !== OUTPUT_CLASSES.length) return null
  const outputs = {} as Record<OutputClass, string>
  for (const outputClass of OUTPUT_CLASSES) {
    const mass = kg(value[outputClass])
    if (mass === null) return null
    outputs[outputClass] = mass
  }
  return outputs
}

function decodeRoll(value: unknown): RollBalance | null {
  if (!isObject(value)) return null
  const parentRollId = guid(value.parentRollId)
  const plannedKg = kg(value.plannedKg)
  const issuedKg = kg(value.issuedKg)
  const returnedKg = kg(value.returnedKg)
  const netIssuedKg = kg(value.netIssuedKg)
  const outputsKg = decodeOutputs(value.outputsKg)
  const workInProgressKg = kg(value.workInProgressKg)
  const unexplainedKg = kg(value.unexplainedKg, true)
  if (
    parentRollId === null ||
    plannedKg === null ||
    issuedKg === null ||
    returnedKg === null ||
    netIssuedKg === null ||
    outputsKg === null ||
    workInProgressKg === null ||
    unexplainedKg === null ||
    typeof value.isFullyAccounted !== 'boolean'
  ) {
    return null
  }
  return {
    parentRollId,
    plannedKg,
    issuedKg,
    returnedKg,
    netIssuedKg,
    outputsKg,
    workInProgressKg,
    unexplainedKg,
    isFullyAccounted: value.isFullyAccounted,
  }
}

function decodeRemaining(value: unknown): RemainingTarget | null {
  if (!isObject(value)) return null
  const basis = value.basis
  const targetKg = nullableKg(value.targetKg)
  const achievedKg = nullableKg(value.achievedKg)
  // Remaining may be negative once the target is exceeded; it is the server's figure.
  const remainingKg =
    value.remainingKg === null ? null : (kg(value.remainingKg, true) ?? undefined)
  if (targetKg === undefined || achievedKg === undefined || remainingKg === undefined) return null
  if (basis === null) {
    // Unavailable is all-null: a half-filled answer is not a contract shape.
    return targetKg === null && achievedKg === null && remainingKg === null
      ? { basis: null, targetKg: null, achievedKg: null, remainingKg: null }
      : null
  }
  if (typeof basis !== 'string' || targetKg === null || achievedKg === null || remainingKg === null) {
    return null
  }
  return { basis, targetKg, achievedKg, remainingKg }
}

function decodeTarget(value: unknown): ProductionOrderView['target'] | undefined {
  if (value === null) return null
  if (!isObject(value) || typeof value.basis !== 'string') return undefined
  const mass = kg(value.kg)
  return mass === null ? undefined : { basis: value.basis, kg: mass }
}

/** A positive safe integer: the version is compared and sent back, never computed. */
function version(value: unknown): number | null {
  return typeof value === 'number' && Number.isSafeInteger(value) && value > 0 ? value : null
}

export function decodeOrder(body: unknown): ProductionOrderView | null {
  if (!isObject(body)) return null
  const orderId = guid(body.orderId)
  const companyId = guid(body.companyId)
  const planId = guid(body.planId)
  const workCenterId = guid(body.workCenterId)
  const orderVersion = version(body.version)
  const target = decodeTarget(body.target)
  const remainingTarget = decodeRemaining(body.remainingTarget)
  const closedByUserId = nullableGuid(body.closedByUserId)
  const closedAtUtc =
    body.closedAtUtc === null
      ? null
      : typeof body.closedAtUtc === 'string' && !Number.isNaN(Date.parse(body.closedAtUtc))
        ? body.closedAtUtc
        : undefined
  if (
    orderId === null ||
    companyId === null ||
    planId === null ||
    workCenterId === null ||
    orderVersion === null ||
    target === undefined ||
    remainingTarget === null ||
    closedByUserId === undefined ||
    closedAtUtc === undefined ||
    typeof body.process !== 'string' ||
    (body.status !== 'Open' && body.status !== 'Closed') ||
    body.costStatus !== 'NotCalculated' ||
    !Array.isArray(body.rolls)
  ) {
    return null
  }
  const rolls: RollBalance[] = []
  for (const raw of body.rolls) {
    const roll = decodeRoll(raw)
    if (roll === null) return null
    rolls.push(roll)
  }
  return {
    orderId,
    companyId,
    planId,
    workCenterId,
    process: body.process,
    status: body.status,
    version: orderVersion,
    target,
    remainingTarget,
    rolls,
    costStatus: 'NotCalculated',
    closedByUserId,
    closedAtUtc,
  }
}

/** The four fact commands of the order, by route segment. */
export const FACT_KINDS = ['input-issues', 'input-returns', 'wip-measurements', 'outputs'] as const

export type FactKind = (typeof FACT_KINDS)[number]

/**
 * One fact as the caller froze it at submit. `factId` and the Idempotency-Key are chosen
 * once per snapshot: re-sending an unresolved snapshot sends both unchanged, so the
 * server answers it as the same command (ADR-011) and the same fact (R3).
 */
export interface FactSnapshot {
  readonly kind: FactKind
  readonly factId: string
  readonly idempotencyKey: string
  readonly expectedVersion: number
  readonly parentRollId: string
  readonly kg: string
  /** ISO-8601 UTC (`Z`): the server requires a UTC fact time. */
  readonly occurredAtUtc: string
  /** Only for `outputs`. */
  readonly outputClass: OutputClass | null
}

/** The request body exactly as `RecordFactRequestV1` / `DeclareOutputRequestV1` name it. */
export function factBody(snapshot: FactSnapshot): Record<string, unknown> {
  const body: Record<string, unknown> = {
    factId: snapshot.factId,
    expectedVersion: snapshot.expectedVersion,
    parentRollId: snapshot.parentRollId,
    kg: snapshot.kg,
    occurredAtUtc: snapshot.occurredAtUtc,
  }
  if (snapshot.kind === 'outputs') body.outputClass = snapshot.outputClass
  return body
}

// ---- GDC-BAL/1: `GET /api/v1/manufacturing/orders/{orderId}/mass-balance` (MassBalanceViewV1) ----------------

export type EngineOutcome = 'Computed' | 'Unavailable' | 'Refused'

/**
 * One engine answer exactly as the server gave it. `Unavailable` names every missing input and carries no number;
 * the screen shows that, never a 0 or an estimate. `value` is lossless text (unrounded, the server's `Text`).
 */
export interface EngineAnswer {
  readonly outcome: EngineOutcome
  readonly value: string | null
  readonly missingParameters: readonly string[]
  readonly refusalCode: string | null
  readonly refusedParameter: string | null
}

/** BAL-003 of one parent roll: the actual input, what the categories account for, and the SIGNED difference. */
export interface MassDifference {
  readonly actualInputKg: string
  readonly accountedKg: string
  readonly differenceKg: string
  readonly isFullyAccounted: boolean
}

export interface RollMassBalance {
  readonly parentRollId: string
  readonly difference: MassDifference
  /** BAL-004 (fire %). */
  readonly scrapPercent: EngineAnswer
  /** BAL-006 (actual unit kg). */
  readonly actualUnitKg: EngineAnswer
}

export interface MassBalanceView {
  readonly orderId: string
  readonly version: number
  readonly formulaVersion: string
  readonly rolls: readonly RollMassBalance[]
}

/** The server's `Text(decimal)` of an unrounded engine value (a percent may carry many decimals). */
const DECIMAL_OUT = /^-?(0|[1-9][0-9]*)(\.[0-9]*[1-9])?$/

function nullableText(value: unknown): string | null | undefined {
  if (value === null) return null
  return typeof value === 'string' && value.length > 0 ? value : undefined
}

function decodeAnswer(value: unknown): EngineAnswer | null {
  if (!isObject(value)) return null
  const outcome = value.outcome
  if (outcome !== 'Computed' && outcome !== 'Unavailable' && outcome !== 'Refused') return null
  const answerValue = value.value === null ? null : typeof value.value === 'string' && DECIMAL_OUT.test(value.value) ? value.value : undefined
  const refusalCode = nullableText(value.refusalCode)
  const refusedParameter = nullableText(value.refusedParameter)
  const missing = value.missingParameters
  if (
    answerValue === undefined ||
    refusalCode === undefined ||
    refusedParameter === undefined ||
    !Array.isArray(missing) ||
    !missing.every((name) => typeof name === 'string' && name.length > 0)
  ) {
    return null
  }
  // The outcome and its payload must agree: a number only when computed, a name list only when unavailable.
  if ((outcome === 'Computed') !== (answerValue !== null)) return null
  if ((outcome === 'Unavailable') !== (missing.length > 0)) return null
  if ((outcome === 'Refused') !== (refusalCode !== null)) return null
  return { outcome, value: answerValue, missingParameters: missing as string[], refusalCode, refusedParameter }
}

function decodeDifference(value: unknown): MassDifference | null {
  if (!isObject(value)) return null
  const actualInputKg = kg(value.actualInputKg)
  const accountedKg = kg(value.accountedKg, true)
  const differenceKg = kg(value.differenceKg, true)
  if (actualInputKg === null || accountedKg === null || differenceKg === null || typeof value.isFullyAccounted !== 'boolean') {
    return null
  }
  return { actualInputKg, accountedKg, differenceKg, isFullyAccounted: value.isFullyAccounted }
}

/** Fails CLOSED like {@link decodeOrder}: a body that is not the contract is `null`, never a partial balance. */
export function decodeMassBalance(body: unknown): MassBalanceView | null {
  if (!isObject(body)) return null
  const orderId = guid(body.orderId)
  const balanceVersion = version(body.version)
  if (
    orderId === null ||
    balanceVersion === null ||
    typeof body.formulaVersion !== 'string' ||
    body.formulaVersion.length === 0 ||
    !Array.isArray(body.rolls)
  ) {
    return null
  }
  const rolls: RollMassBalance[] = []
  for (const raw of body.rolls) {
    if (!isObject(raw)) return null
    const parentRollId = guid(raw.parentRollId)
    const difference = decodeDifference(raw.difference)
    const scrapPercent = decodeAnswer(raw.scrapPercent)
    const actualUnitKg = decodeAnswer(raw.actualUnitKg)
    if (parentRollId === null || difference === null || scrapPercent === null || actualUnitKg === null) return null
    rolls.push({ parentRollId, difference, scrapPercent, actualUnitKg })
  }
  return { orderId, version: balanceVersion, formulaVersion: body.formulaVersion, rolls }
}

// ---- MFG-ORD: `GET /api/v1/manufacturing/orders` (OrderListViewV1) and the close command ----------------------

/**
 * One order in the list, exactly as the server summarises it: the stored row and its input
 * snapshot. No balance and no difference — those are the order read's, roll by roll — and no roll
 * label: the label is Inventory's read (ÜRÜN / T15), not this module's, so the list shows none.
 */
export interface OrderSummary {
  readonly orderId: string
  readonly planId: string
  readonly workCenterId: string
  readonly process: string
  readonly status: OrderStatus
  readonly version: number
  readonly plannedRollCount: number
  readonly plannedInputKg: string
  readonly target: { readonly basis: string; readonly kg: string } | null
  readonly createdAtUtc: string
  readonly closedAtUtc: string | null
}

/** `nextCursor === null` is the end of the list; the cursor is opaque and is sent back unchanged. */
export interface OrderListView {
  readonly orders: readonly OrderSummary[]
  readonly nextCursor: string | null
}

/** A non-negative safe integer count. */
function count(value: unknown): number | null {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0 ? value : null
}

function utc(value: unknown): string | null | undefined {
  if (value === null) return null
  return typeof value === 'string' && !Number.isNaN(Date.parse(value)) ? value : undefined
}

function decodeSummary(value: unknown): OrderSummary | null {
  if (!isObject(value)) return null
  const orderId = guid(value.orderId)
  const planId = guid(value.planId)
  const workCenterId = guid(value.workCenterId)
  const orderVersion = version(value.version)
  const plannedRollCount = count(value.plannedRollCount)
  const plannedInputKg = kg(value.plannedInputKg)
  const target = decodeTarget(value.target)
  const createdAtUtc = utc(value.createdAtUtc)
  const closedAtUtc = utc(value.closedAtUtc)
  if (
    orderId === null ||
    planId === null ||
    workCenterId === null ||
    orderVersion === null ||
    plannedRollCount === null ||
    plannedInputKg === null ||
    target === undefined ||
    createdAtUtc === undefined ||
    createdAtUtc === null ||
    closedAtUtc === undefined ||
    typeof value.process !== 'string' ||
    (value.status !== 'Open' && value.status !== 'Closed')
  ) {
    return null
  }
  // A closed order carries its close time and an open one carries none: a half-told status is not
  // a contract shape, and the screen never infers one from the other.
  if ((value.status === 'Closed') !== (closedAtUtc !== null)) return null
  return {
    orderId,
    planId,
    workCenterId,
    process: value.process,
    status: value.status,
    version: orderVersion,
    plannedRollCount,
    plannedInputKg,
    target,
    createdAtUtc,
    closedAtUtc,
  }
}

export function decodeOrderList(body: unknown): OrderListView | null {
  if (!isObject(body) || !Array.isArray(body.orders)) return null
  const nextCursor =
    body.nextCursor === null
      ? null
      : typeof body.nextCursor === 'string' && body.nextCursor.length > 0
        ? body.nextCursor
        : undefined
  if (nextCursor === undefined) return null
  const orders: OrderSummary[] = []
  for (const raw of body.orders) {
    const summary = decodeSummary(raw)
    if (summary === null) return null
    orders.push(summary)
  }
  return { orders, nextCursor }
}

/**
 * One close as the caller froze it: the version the screen read and the key that names this
 * attempt. Re-sending the SAME snapshot sends both unchanged, so the server answers it as the
 * same command (ADR-011 §4) instead of closing twice.
 */
export interface CloseSnapshot {
  readonly idempotencyKey: string
  readonly expectedVersion: number
}

/** The request body exactly as `CloseOrderRequestV1` names it. */
export function closeBody(snapshot: CloseSnapshot): Record<string, unknown> {
  return { expectedVersion: snapshot.expectedVersion }
}
