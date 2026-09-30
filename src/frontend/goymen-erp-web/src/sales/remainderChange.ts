import { isReferenceId, isVersionString } from './wirePrimitives'

export const REMAINDER_KIND = { cancel: 'Cancel', revise: 'Revise' } as const
export const REMAINDER_STATE = { pending: 'Pending', approved: 'Approved', rejected: 'Rejected' } as const
export const LINE_REMAINDER = { open: 'Open', revised: 'Revised', cancelled: 'Cancelled' } as const
export const REMAINDER_FLOOR = { readable: 'Readable', unreadable: 'Unreadable' } as const

export interface RemainderChangeRequest {
  readonly expectedVersion: string
  readonly lineId: string
  readonly kind: (typeof REMAINDER_KIND)[keyof typeof REMAINDER_KIND]
  readonly quantity: string | null
  readonly reason: string
}

export interface RemainderDecisionRequest {
  readonly expectedVersion: string
  readonly reason: string | null
}

export interface RemainderLine {
  readonly lineId: string
  readonly lineNo: number
  readonly orderUnitId: string
  readonly quantity: string
  readonly remainder: (typeof LINE_REMAINDER)[keyof typeof LINE_REMAINDER]
  readonly loaded: string | null
  readonly shipped: string | null
  readonly delivered: string | null
  readonly minimumQuantity: string | null
}

export interface RemainderChange {
  readonly changeNo: number
  readonly revision: string
  readonly lineId: string
  readonly lineNo: number
  readonly kind: (typeof REMAINDER_KIND)[keyof typeof REMAINDER_KIND]
  readonly state: (typeof REMAINDER_STATE)[keyof typeof REMAINDER_STATE]
  readonly orderedQuantity: string
  readonly requestedQuantity: string | null
  readonly reason: string
  readonly requestedBy: string
  readonly requestedAtUtc: string
  readonly loadedAtRequest: string
  readonly shippedAtRequest: string
  readonly deliveredAtRequest: string
  readonly decidedBy: string | null
  readonly decidedAtUtc: string | null
  readonly decisionReason: string | null
  readonly resultQuantity: string | null
  readonly loadedAtDecision: string | null
}

export interface RemainderChanges {
  readonly draftVersion: string
  readonly floorStatus: (typeof REMAINDER_FLOOR)[keyof typeof REMAINDER_FLOOR]
  readonly lines: readonly RemainderLine[]
  readonly items: readonly RemainderChange[]
}

export const remainderChangesPath = (draftId: string) => `/sales/drafts/${encodeURIComponent(draftId)}/remainder-changes`
export const remainderDecisionPath = (draftId: string, changeNo: number, decision: 'approval' | 'rejection') =>
  `${remainderChangesPath(draftId)}/${changeNo}/${decision}`

type Obj = Record<string, unknown>
const DECIMAL = /^(?:0|[1-9]\d*)(?:\.\d+)?$/
const REVISION = /^Rev\.(?:0|[1-9]\d*)$/

const exact = (value: unknown, keys: readonly string[]): Obj | null => {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return null
  const object = value as Obj
  const actual = Object.keys(object)
  return actual.length === keys.length && actual.every((key) => keys.includes(key)) ? object : null
}
const text = (value: unknown, allowEmpty = false) => typeof value === 'string' && (allowEmpty || value.trim().length > 0)
const decimal = (value: unknown) => typeof value === 'string' && DECIMAL.test(value)
const nullableDecimal = (value: unknown) => value === null || decimal(value)
const instant = (value: unknown) => typeof value === 'string' && value.trim() !== '' && !Number.isNaN(Date.parse(value))
const nullableInstant = (value: unknown) => value === null || instant(value)
const oneOf = <T extends string>(value: unknown, choices: readonly T[]): value is T =>
  typeof value === 'string' && choices.includes(value as T)
const compareDecimal = (left: string, right: string): number => {
  const [leftWhole, leftFraction = ''] = left.split('.')
  const [rightWhole, rightFraction = ''] = right.split('.')
  const scale = Math.max(leftFraction.length, rightFraction.length)
  const scaledLeft = BigInt(`${leftWhole}${leftFraction.padEnd(scale, '0')}`)
  const scaledRight = BigInt(`${rightWhole}${rightFraction.padEnd(scale, '0')}`)
  return scaledLeft < scaledRight ? -1 : scaledLeft > scaledRight ? 1 : 0
}
const validFloor = (loaded: string, shipped: string, delivered: string) =>
  compareDecimal(delivered, shipped) <= 0 && compareDecimal(shipped, loaded) <= 0

function decodeLine(value: unknown): RemainderLine | null {
  const o = exact(value, ['lineId', 'lineNo', 'orderUnitId', 'quantity', 'remainder', 'loaded', 'shipped', 'delivered', 'minimumQuantity'])
  if (o === null || !isReferenceId(o.lineId) || typeof o.lineNo !== 'number' || !Number.isInteger(o.lineNo) || o.lineNo < 1
    || !isReferenceId(o.orderUnitId) || !decimal(o.quantity)
    || !oneOf(o.remainder, Object.values(LINE_REMAINDER)) || !nullableDecimal(o.loaded) || !nullableDecimal(o.shipped)
    || !nullableDecimal(o.delivered) || !nullableDecimal(o.minimumQuantity)) return null
  if (o.loaded !== null && o.shipped !== null && o.delivered !== null && o.minimumQuantity !== null
    && (!validFloor(o.loaded, o.shipped, o.delivered) || compareDecimal(o.loaded, o.minimumQuantity) !== 0)) return null
  return o as unknown as RemainderLine
}

function decodeChange(value: unknown): RemainderChange | null {
  const o = exact(value, [
    'changeNo', 'revision', 'lineId', 'lineNo', 'kind', 'state', 'orderedQuantity', 'requestedQuantity', 'reason',
    'requestedBy', 'requestedAtUtc', 'loadedAtRequest', 'shippedAtRequest', 'deliveredAtRequest', 'decidedBy',
    'decidedAtUtc', 'decisionReason', 'resultQuantity', 'loadedAtDecision',
  ])
  if (o === null || typeof o.changeNo !== 'number' || !Number.isInteger(o.changeNo) || o.changeNo < 1
    || typeof o.revision !== 'string' || !REVISION.test(o.revision) || !isReferenceId(o.lineId)
    || typeof o.lineNo !== 'number' || !Number.isInteger(o.lineNo) || o.lineNo < 1
    || !oneOf(o.kind, Object.values(REMAINDER_KIND)) || !oneOf(o.state, Object.values(REMAINDER_STATE))
    || !decimal(o.orderedQuantity) || !nullableDecimal(o.requestedQuantity) || !text(o.reason) || !text(o.requestedBy)
    || !instant(o.requestedAtUtc) || !decimal(o.loadedAtRequest) || !decimal(o.shippedAtRequest) || !decimal(o.deliveredAtRequest)
    || !(o.decidedBy === null || text(o.decidedBy)) || !nullableInstant(o.decidedAtUtc)
    || !(o.decisionReason === null || text(o.decisionReason, true)) || !nullableDecimal(o.resultQuantity)
    || !nullableDecimal(o.loadedAtDecision)) return null
  if ((o.kind === REMAINDER_KIND.cancel) !== (o.requestedQuantity === null)
    || !validFloor(o.loadedAtRequest as string, o.shippedAtRequest as string, o.deliveredAtRequest as string)) return null
  const noDecision = o.decidedBy === null && o.decidedAtUtc === null && o.decisionReason === null
    && o.resultQuantity === null && o.loadedAtDecision === null
  const approved = text(o.decidedBy) && instant(o.decidedAtUtc) && o.decisionReason === null
    && decimal(o.resultQuantity) && decimal(o.loadedAtDecision)
    && compareDecimal(o.loadedAtDecision as string, o.resultQuantity as string) <= 0
    && (o.kind === REMAINDER_KIND.cancel
      ? compareDecimal(o.resultQuantity as string, o.loadedAtDecision as string) === 0
      : o.requestedQuantity !== null && compareDecimal(o.resultQuantity as string, o.requestedQuantity) === 0)
  const rejected = text(o.decidedBy) && instant(o.decidedAtUtc)
    && (o.decisionReason === null || text(o.decisionReason, true)) && o.resultQuantity === null && o.loadedAtDecision === null
  if ((o.state === REMAINDER_STATE.pending && !noDecision)
    || (o.state === REMAINDER_STATE.approved && !approved)
    || (o.state === REMAINDER_STATE.rejected && !rejected)) return null
  return o as unknown as RemainderChange
}

export function decodeRemainderChanges(value: unknown): RemainderChanges | null {
  const o = exact(value, ['draftVersion', 'floorStatus', 'lines', 'items'])
  if (o === null || !isVersionString(o.draftVersion) || !oneOf(o.floorStatus, Object.values(REMAINDER_FLOOR))
    || !Array.isArray(o.lines) || !Array.isArray(o.items)) return null
  const lines = o.lines.map(decodeLine)
  const items = o.items.map(decodeChange)
  if (lines.some((line) => line === null) || items.some((item) => item === null)) return null
  const lineIds = new Set(lines.map((line) => line!.lineId))
  if (lineIds.size !== lines.length || items.some((item) => !lineIds.has(item!.lineId))) return null
  if (o.floorStatus === REMAINDER_FLOOR.readable && lines.some((line) =>
    line!.loaded === null || line!.shipped === null || line!.delivered === null || line!.minimumQuantity === null)) return null
  if (o.floorStatus === REMAINDER_FLOOR.unreadable && lines.some((line) =>
    line!.loaded !== null || line!.shipped !== null || line!.delivered !== null || line!.minimumQuantity !== null)) return null
  return { draftVersion: o.draftVersion, floorStatus: o.floorStatus, lines: lines as RemainderLine[], items: items as RemainderChange[] }
}

