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

import { decodePlanPreview, type PlanPreview } from './planPreviewContract'

/** The five output classes of DEC-PRDCT-006, by their server names. */
export const OUTPUT_CLASSES = [
  'GoodProduct',
  'SecondQuality',
  'UsableRemnant',
  'Scrap',
  'ProcessLoss',
] as const

export type OutputClass = (typeof OUTPUT_CLASSES)[number]

/** Output classes that create a real Inventory label/movement. ProcessLoss is explicitly non-stock. */
export const INVENTORY_OUTPUT_CLASSES = [
  'GoodProduct',
  'SecondQuality',
  'UsableRemnant',
  'Scrap',
] as const satisfies readonly OutputClass[]

export type InventoryOutputClass = (typeof INVENTORY_OUTPUT_CLASSES)[number]

export interface ManufacturingOutputProductCandidate {
  readonly productId: string
  readonly productVersion: string
  readonly code: string
  readonly name: string
  readonly stockUnitCode: string
}

export interface ManufacturingOutputProductCandidates {
  readonly items: readonly ManufacturingOutputProductCandidate[]
  readonly searchWindowFull: boolean
}

export interface ManufacturingOutputLocationCandidate {
  readonly locationId: string
  readonly locationCode: string
  readonly locationName: string
  readonly siteId: string
  readonly siteCode: string
  readonly siteName: string
}

export interface ManufacturingOutputLocationCandidates {
  readonly items: readonly ManufacturingOutputLocationCandidate[]
  readonly searchWindowFull: boolean
}

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

export type PlanStatus = 'Draft' | 'Approved'

export interface CapturedPlanTechnicalSnapshot {
  readonly productId: string
  readonly productVersion: string
  readonly family: { readonly familyId: string; readonly familyCode: string; readonly sourceRevision: string }
  readonly template: {
    readonly templateId: string
    readonly templateVersion: string
    readonly templateCode: string
    readonly sourceRevision: string
  }
  readonly thicknessMm: string
  readonly widthMm: string
  readonly unitKg: {
    readonly companyId: string
    readonly unitId: string
    readonly unitCode: string
    readonly profileId: string
    readonly profileVersion: string
  }
  readonly effectiveOn: string
}

/** One planned parent roll of a plan: the kilogram is the plan's figure, not a measured one. */
export interface PlannedInput {
  readonly parentRollId: string
  readonly plannedKg: string
  /** Null for a legacy stored plan; never filled from the current Product card by the client. */
  readonly technicalSnapshot: CapturedPlanTechnicalSnapshot | null
}

export interface SlitLineView {
  readonly bandWidthMm: string
  readonly count: number
}

/** Immutable ordered slitting content stored with a Slitting draft. Null also covers legacy omitted JSON. */
export interface SlittingSpecView {
  readonly lines: readonly SlitLineView[]
  readonly windingTargetKg: string | null
}

/**
 * Wire contract of `GET /api/v1/manufacturing/plans/{planId}` (`PlanViewV1`).
 *
 * The same shape the create and approve answers return — by construction on the server, not by
 * two projections kept in step — so a reopened plan and a just-created one decode identically.
 */
export interface ProductionPlanView {
  readonly planId: string
  readonly companyId: string
  readonly workCenterId: string
  readonly process: string
  readonly status: PlanStatus
  /** The version an approval must name as `expectedVersion`. */
  readonly version: number
  readonly inputs: readonly PlannedInput[]
  readonly target: { readonly basis: string; readonly kg: string } | null
  readonly approvedByUserId: string | null
  readonly approvedAtUtc: string | null
  /** Null only when replaying a historical command JSON that predates the additive projection. */
  readonly calculation: PlanPreview | null
  /** Null for plans without a slitting spec and for legacy command JSON that omitted the additive field. */
  readonly slittingSpec: SlittingSpecView | null
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
const NIL_GUID = '00000000-0000-0000-0000-000000000000'
/** The server's own `Text(decimal)`: invariant, no grouping, no exponent. */
const KG_OUT = /^(0|[1-9][0-9]*)(\.[0-9]*[1-9])?$/
const MM_OUT = /^(0|[1-9][0-9]*)(\.[0-9]{0,2}[1-9])?$/
const INT32_MAX = 2_147_483_647
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

function hasExactKeys(value: Json, keys: readonly string[]): boolean {
  const actual = Object.keys(value).sort()
  const expected = [...keys].sort()
  return actual.length === expected.length && actual.every((key, index) => key === expected[index])
}

function requiredText(value: unknown): value is string {
  return typeof value === 'string' && value.length > 0 && value === value.trim()
}

function boundedSnapshotText(value: unknown, maximumLength: number): value is string {
  return requiredText(value) && value.length <= maximumLength && !/[\u0000-\u001f\u007f-\u009f]/.test(value)
}

function snapshotMeasure(value: unknown): string | null {
  const decoded = kg(value)
  if (decoded === null || decoded === '0') return null
  const separator = decoded.indexOf('.')
  return separator < 0 || decoded.length - separator - 1 <= 3 ? decoded : null
}

function calendarDate(value: unknown): value is string {
  if (typeof value !== 'string') return false
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value)
  if (match === null) return false
  const year = Number(match[1])
  const month = Number(match[2])
  const day = Number(match[3])
  if (year < 1 || month < 1 || month > 12 || day < 1 || (year === 1 && month === 1 && day === 1)) return false
  const leap = year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0)
  const days = [31, leap ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31]
  return day <= days[month - 1]
}

function exactDecimalSum(values: readonly string[]): string {
  const parts = values.map((value) => {
    const [integer, fraction = ''] = value.split('.')
    return { integer, fraction }
  })
  const scale = parts.reduce((maximum, part) => Math.max(maximum, part.fraction.length), 0)
  const factor = 10n ** BigInt(scale)
  const total = parts.reduce((sum, part) =>
    sum + BigInt(part.integer) * factor
      + BigInt((part.fraction + '0'.repeat(scale)).slice(0, scale) || '0'), 0n)
  if (scale === 0) return total.toString()
  const raw = total.toString().padStart(scale + 1, '0')
  const fraction = raw.slice(-scale).replace(/0+$/, '')
  return fraction.length === 0 ? raw.slice(0, -scale) : `${raw.slice(0, -scale)}.${fraction}`
}

function guid(value: unknown): string | null {
  return typeof value === 'string' && GUID.test(value) ? value : null
}

function nonNilGuid(value: unknown): string | null {
  const decoded = guid(value)
  return decoded === null || decoded.toLowerCase() === NIL_GUID ? null : decoded
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

export function decodeManufacturingOutputProductCandidates(
  value: unknown,
): ManufacturingOutputProductCandidates | null {
  if (!isObject(value) || !hasExactKeys(value, ['items', 'searchWindowFull'])
    || !Array.isArray(value.items) || typeof value.searchWindowFull !== 'boolean') return null
  const items: ManufacturingOutputProductCandidate[] = []
  const ids = new Set<string>()
  for (const raw of value.items) {
    if (!isObject(raw) || !hasExactKeys(raw, ['productId', 'productVersion', 'code', 'name', 'stockUnitCode'])) {
      return null
    }
    const productId = nonNilGuid(raw.productId)
    if (productId === null || ids.has(productId) || !requiredText(raw.productVersion)
      || !/^[1-9][0-9]*$/.test(raw.productVersion) || !requiredText(raw.code)
      || !requiredText(raw.name) || !requiredText(raw.stockUnitCode)) return null
    ids.add(productId)
    items.push({
      productId,
      productVersion: raw.productVersion,
      code: raw.code,
      name: raw.name,
      stockUnitCode: raw.stockUnitCode,
    })
  }
  return { items, searchWindowFull: value.searchWindowFull }
}

export function decodeManufacturingOutputLocationCandidates(
  value: unknown,
): ManufacturingOutputLocationCandidates | null {
  if (!isObject(value) || !hasExactKeys(value, ['items', 'searchWindowFull'])
    || !Array.isArray(value.items) || typeof value.searchWindowFull !== 'boolean') return null
  const items: ManufacturingOutputLocationCandidate[] = []
  const ids = new Set<string>()
  for (const raw of value.items) {
    if (!isObject(raw) || !hasExactKeys(raw, [
      'locationId', 'locationCode', 'locationName', 'siteId', 'siteCode', 'siteName',
    ])) return null
    const locationId = nonNilGuid(raw.locationId)
    const siteId = nonNilGuid(raw.siteId)
    if (locationId === null || siteId === null || ids.has(locationId)
      || !requiredText(raw.locationCode) || !requiredText(raw.locationName)
      || !requiredText(raw.siteCode) || !requiredText(raw.siteName)) return null
    ids.add(locationId)
    items.push({
      locationId,
      locationCode: raw.locationCode,
      locationName: raw.locationName,
      siteId,
      siteCode: raw.siteCode,
      siteName: raw.siteName,
    })
  }
  return { items, searchWindowFull: value.searchWindowFull }
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

function decodeSlittingSpec(value: unknown): SlittingSpecView | null | undefined {
  // The field is additive: an older stored command answer legitimately omits it.
  if (value === undefined || value === null) return null
  if (!isObject(value) || !Array.isArray(value.lines) || value.lines.length === 0) return undefined
  const windingTargetKg = value.windingTargetKg === null
    ? null
    : typeof value.windingTargetKg === 'string' && KG_OUT.test(value.windingTargetKg) && value.windingTargetKg !== '0'
      ? value.windingTargetKg
      : undefined
  if (windingTargetKg === undefined) return undefined
  const lines: SlitLineView[] = []
  for (const raw of value.lines) {
    if (!isObject(raw)
      || typeof raw.bandWidthMm !== 'string'
      || !MM_OUT.test(raw.bandWidthMm)
      || raw.bandWidthMm === '0'
      || typeof raw.count !== 'number'
      || !Number.isSafeInteger(raw.count)
      || raw.count <= 0
      || raw.count > INT32_MAX) return undefined
    lines.push({ bandWidthMm: raw.bandWidthMm, count: raw.count })
  }
  return { lines, windingTargetKg }
}

/** A positive safe integer: the version is compared and sent back, never computed. */
function version(value: unknown): number | null {
  return typeof value === 'number' && Number.isSafeInteger(value) && value > 0 ? value : null
}

/** `undefined` = not a contract value; `null` = the contract's own absent value. */
function nullableInstant(value: unknown): string | null | undefined {
  if (value === null) return null
  return typeof value === 'string' && !Number.isNaN(Date.parse(value)) ? value : undefined
}

function decodePlannedInput(value: unknown): PlannedInput | null {
  if (!isObject(value)) return null
  const parentRollId = guid(value.parentRollId)
  const plannedKg = kg(value.plannedKg)
  const technicalSnapshot = value.technicalSnapshot === undefined || value.technicalSnapshot === null
    ? null
    : decodeTechnicalSnapshot(value.technicalSnapshot)
  return parentRollId === null || plannedKg === null || technicalSnapshot === undefined
    ? null
    : { parentRollId, plannedKg, technicalSnapshot }
}

export function decodeTechnicalSnapshot(value: unknown): CapturedPlanTechnicalSnapshot | undefined {
  if (!isObject(value) || !hasExactKeys(value, [
    'productId', 'productVersion', 'family', 'template', 'thicknessMm', 'widthMm', 'unitKg', 'effectiveOn',
  ])) return undefined
  if (!isObject(value.family) || !hasExactKeys(value.family, ['familyId', 'familyCode', 'sourceRevision'])) return undefined
  if (!isObject(value.template) || !hasExactKeys(value.template,
    ['templateId', 'templateVersion', 'templateCode', 'sourceRevision'])) return undefined
  if (!isObject(value.unitKg) || !hasExactKeys(value.unitKg,
    ['companyId', 'unitId', 'unitCode', 'profileId', 'profileVersion'])) return undefined
  const productId = nonNilGuid(value.productId)
  const familyId = nonNilGuid(value.family.familyId)
  const templateId = nonNilGuid(value.template.templateId)
  const companyId = nonNilGuid(value.unitKg.companyId)
  const unitId = nonNilGuid(value.unitKg.unitId)
  const profileId = nonNilGuid(value.unitKg.profileId)
  const thicknessMm = snapshotMeasure(value.thicknessMm)
  const widthMm = snapshotMeasure(value.widthMm)
  if (productId === null || familyId === null || templateId === null || companyId === null
    || unitId === null || profileId === null || thicknessMm === null || widthMm === null
    || !boundedSnapshotText(value.productVersion, 128)
    || !boundedSnapshotText(value.family.familyCode, 64)
    || !boundedSnapshotText(value.family.sourceRevision, 64)
    || !boundedSnapshotText(value.template.templateVersion, 64)
    || !boundedSnapshotText(value.template.templateCode, 64)
    || !boundedSnapshotText(value.template.sourceRevision, 64)
    || !boundedSnapshotText(value.unitKg.unitCode, 64)
    || !boundedSnapshotText(value.unitKg.profileVersion, 64)
    || !calendarDate(value.effectiveOn)) return undefined
  return {
    productId,
    productVersion: value.productVersion,
    family: { familyId, familyCode: value.family.familyCode, sourceRevision: value.family.sourceRevision },
    template: {
      templateId,
      templateVersion: value.template.templateVersion,
      templateCode: value.template.templateCode,
      sourceRevision: value.template.sourceRevision,
    },
    thicknessMm,
    widthMm,
    unitKg: {
      companyId,
      unitId,
      unitCode: value.unitKg.unitCode,
      profileId,
      profileVersion: value.unitKg.profileVersion,
    },
    effectiveOn: value.effectiveOn,
  }
}

/**
 * Fails closed, like {@link decodeOrder}: a body that is not this contract is `null` and the screen
 * says the answer could not be read. A half-trusted plan would show one roll's planned kilogram
 * beside another's.
 *
 * Approval is decoded as a status-bound pair and never half-believed: Draft has neither approval
 * value and Approved has both. Any other combination is not a shape this contract has — the
 * server's own check constraint says status and the pair move together — so it is refused rather
 * than displayed as a contradictory or partial approval.
 */
export function decodePlan(body: unknown): ProductionPlanView | null {
  if (!isObject(body)) return null
  const planId = guid(body.planId)
  const companyId = guid(body.companyId)
  const workCenterId = guid(body.workCenterId)
  const planVersion = version(body.version)
  const target = decodeTarget(body.target)
  const approvedByUserId = nullableGuid(body.approvedByUserId)
  const approvedAtUtc = nullableInstant(body.approvedAtUtc)
  const calculation = body.calculation === undefined || body.calculation === null
    ? null
    : decodePlanPreview(body.calculation)
  const slittingSpec = decodeSlittingSpec(body.slittingSpec)
  if (
    planId === null ||
    companyId === null ||
    workCenterId === null ||
    planVersion === null ||
    target === undefined ||
    approvedByUserId === undefined ||
    approvedAtUtc === undefined ||
    slittingSpec === undefined ||
    (body.calculation !== undefined && body.calculation !== null && calculation === null) ||
    typeof body.process !== 'string' ||
    (body.status !== 'Draft' && body.status !== 'Approved') ||
    !Array.isArray(body.inputs)
  ) {
    return null
  }
  // CK_Plans_Approval: Draft has neither value; Approved has both. No other combination is trusted.
  if (body.status === 'Draft') {
    if (approvedByUserId !== null || approvedAtUtc !== null) return null
  } else if (approvedByUserId === null || approvedAtUtc === null) {
    return null
  }
  const inputs: PlannedInput[] = []
  for (const raw of body.inputs) {
    const input = decodePlannedInput(raw)
    if (input === null) return null
    inputs.push(input)
  }
  if (inputs.some((input) => input.technicalSnapshot !== null
    && input.technicalSnapshot.unitKg.companyId.toLowerCase() !== companyId.toLowerCase())) return null
  if (calculation !== null) {
    if (calculation.workCenterId.toLowerCase() !== workCenterId.toLowerCase()
      || calculation.process !== body.process || calculation.plannedRollCount !== inputs.length
      || calculation.rolls.length !== inputs.length
      || calculation.plannedInputKg !== exactDecimalSum(inputs.map((input) => input.plannedKg))) return null
    if ((target === null) !== (calculation.target === null)) return null
    if (target !== null && calculation.target !== null
      && (calculation.target.basis !== target.basis || calculation.target.kg !== target.kg)) return null
    for (let index = 0; index < inputs.length; index += 1) {
      if (calculation.rolls[index].parentRollId.toLowerCase() !== inputs[index].parentRollId.toLowerCase()
        || calculation.rolls[index].plannedKg !== inputs[index].plannedKg) return null
    }
  }
  return {
    planId,
    companyId,
    workCenterId,
    process: body.process,
    status: body.status,
    version: planVersion,
    inputs,
    target,
    approvedByUserId,
    approvedAtUtc,
    calculation,
    slittingSpec,
  }
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

/**
 * The realization commands this screen can complete from explicit server-backed selections. An output's
 * owner and custodian are never chosen here: they are DERIVED from this order's own immutable
 * `manufacturing.input-issue` trace on the parent label (one complete, untruncated position), shown read-only,
 * and frozen into the command; the server re-checks them against the same trace. The issue admits either the
 * legacy company-owned, company-custodied path or a captured Counterparty owner with the current company as
 * custodian; every other owner, custodian, or context combination is refused. InitialBlock is an explicit user
 * selection, never a client default.
 */
export const FACT_KINDS = [
  'input-issues',
  'input-returns',
  'wip-measurements',
  'process-losses',
  'outputs',
] as const

export type FactKind = (typeof FACT_KINDS)[number]

interface FactSnapshotBase {
  readonly factId: string
  readonly idempotencyKey: string
  readonly expectedVersion: number
  readonly parentRollId: string
  readonly kg: string
  /** ISO-8601 UTC (`Z`): the server requires a UTC fact time. */
  readonly occurredAtUtc: string
}

/**
 * One fact as the caller froze it at submit. Every owner id, movement id, label version and
 * Idempotency-Key is chosen/read once and re-sent unchanged after an unknown outcome.
 */
export type FactSnapshot =
  | (FactSnapshotBase & {
      readonly kind: 'input-issues'
      readonly movementId: string
      readonly expectedParentLabelVersion: null
    })
  | (FactSnapshotBase & {
      readonly kind: 'input-returns'
      readonly movementId: string
      readonly expectedParentLabelVersion: string
    })
  | (FactSnapshotBase & {
      readonly kind: 'wip-measurements'
      readonly movementId: null
      readonly expectedParentLabelVersion: null
    })
  | (FactSnapshotBase & {
      readonly kind: 'process-losses'
      readonly movementId: null
      readonly expectedParentLabelVersion: null
    })
  | (FactSnapshotBase & {
      readonly kind: 'outputs'
      readonly movementId: string
      readonly expectedParentLabelVersion: string
      readonly outputClass: InventoryOutputClass
      readonly productId: string
      readonly expectedProductVersion: string
      readonly lotId: string
      readonly labelId: string
      readonly labelCode: string
      readonly heatNumber: string | null
      readonly coilNumber: string | null
      readonly requiredDocuments: readonly string[]
      readonly ownerKind: 'Company' | 'Counterparty'
      readonly ownerId: string
      readonly custodianKind: 'Company' | 'Counterparty'
      readonly custodianId: string
      readonly locationId: string
      readonly initialBlock: 'Free' | 'Blocked'
    })

/** The exact strict request body selected by the route segment. */
export function factBody(snapshot: FactSnapshot): Record<string, unknown> {
  const common = {
    expectedVersion: snapshot.expectedVersion,
    parentRollId: snapshot.parentRollId,
    kg: snapshot.kg,
    occurredAtUtc: snapshot.occurredAtUtc,
  }
  if (snapshot.kind === 'input-issues') {
    return { issueId: snapshot.factId, ...common, movementId: snapshot.movementId }
  }
  if (snapshot.kind === 'input-returns') {
    return {
      returnId: snapshot.factId,
      ...common,
      expectedParentLabelVersion: snapshot.expectedParentLabelVersion,
      movementId: snapshot.movementId,
    }
  }
  if (snapshot.kind === 'outputs') {
    return {
      outputId: snapshot.factId,
      ...common,
      expectedParentLabelVersion: snapshot.expectedParentLabelVersion,
      outputClass: snapshot.outputClass,
      productId: snapshot.productId,
      expectedProductVersion: snapshot.expectedProductVersion,
      lotId: snapshot.lotId,
      labelId: snapshot.labelId,
      labelCode: snapshot.labelCode,
      heatNumber: snapshot.heatNumber,
      coilNumber: snapshot.coilNumber,
      requiredDocuments: snapshot.requiredDocuments,
      ownerKind: snapshot.ownerKind,
      ownerId: snapshot.ownerId,
      custodianKind: snapshot.custodianKind,
      custodianId: snapshot.custodianId,
      locationId: snapshot.locationId,
      initialBlock: snapshot.initialBlock,
      movementId: snapshot.movementId,
    }
  }
  return { factId: snapshot.factId, ...common }
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
  /** Present only after the server requires a dedicated, reasoned exception approval. */
  readonly exceptionReason?: string | null
}

/** The request body exactly as `CloseOrderRequestV1` names it. */
export function closeBody(snapshot: CloseSnapshot): Record<string, unknown> {
  return {
    expectedVersion: snapshot.expectedVersion,
    exceptionReason: snapshot.exceptionReason ?? null,
  }
}
