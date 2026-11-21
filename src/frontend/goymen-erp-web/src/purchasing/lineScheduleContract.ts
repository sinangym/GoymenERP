import {
  isPurchaseOrderDeliverySchedule,
  isPurchasingGuid,
  isPurchasingInstant,
  isPurchasingVersion,
  type PurchaseOrderDeliverySlice,
} from './contract'

/**
 * F-027 (ALS-D94, ALS-D81): GET …/orders/{id}/lines/{lineId}/schedule-history — one line's delivery schedule revisions as
 * recorded, oldest first. A termin change on an approved order asks no re-approval, so the change stays visible here with its
 * recorder and time. Read only; the screen never edits or recomputes a schedule.
 */
export interface PurchaseOrderLineScheduleHistory {
  readonly orderId: string
  readonly lineId: string
  /** The order version the history was read beside; no revision runs past it. */
  readonly orderVersion: string
  readonly revisions: readonly PurchaseOrderLineScheduleRevision[]
}

/** One recorded schedule: the order version that recorded it, who, when (UTC) and the slices in slice order. */
export interface PurchaseOrderLineScheduleRevision {
  readonly orderVersion: string
  readonly recordedBy: string
  readonly recordedAtUtc: string
  readonly slices: readonly PurchaseOrderDeliverySlice[]
}

type JsonObject = Record<string, unknown>

function record(value: unknown): JsonObject | null {
  return typeof value === 'object' && value !== null && !Array.isArray(value) ? value as JsonObject : null
}

function exact(value: JsonObject, names: readonly string[]): boolean {
  const actual = Object.keys(value).sort()
  const expected = [...names].sort()
  return actual.length === expected.length && actual.every((name, index) => name === expected[index])
}

function nonBlank(value: unknown): value is string {
  return typeof value === 'string' && value.length > 0 && value.trim() === value
}

/**
 * Strict decoder, fail closed: exactly the four fields; the record must be the order and line asked for; every revision exactly
 * its four fields, a non-blank recorder, a valid instant and at least one slice measured by the detail's slice rule; revision
 * versions strictly rising and none past the order version read. Anything else is `null` (malformed), never a partial history.
 */
export function decodePurchaseOrderLineScheduleHistory(
  value: unknown,
  orderId: string,
  lineId: string,
): PurchaseOrderLineScheduleHistory | null {
  const source = record(value)
  if (source === null || !exact(source, ['orderId', 'lineId', 'orderVersion', 'revisions'])
    || !isPurchasingGuid(source.orderId) || source.orderId.toLowerCase() !== orderId.toLowerCase()
    || !isPurchasingGuid(source.lineId) || source.lineId.toLowerCase() !== lineId.toLowerCase()
    || !isPurchasingVersion(source.orderVersion) || !Array.isArray(source.revisions)) return null
  const ceiling = BigInt(source.orderVersion)
  let previous = 0n
  for (const item of source.revisions) {
    const revision = record(item)
    if (revision === null || !exact(revision, ['orderVersion', 'recordedBy', 'recordedAtUtc', 'slices'])
      || !isPurchasingVersion(revision.orderVersion) || !nonBlank(revision.recordedBy) || !isPurchasingInstant(revision.recordedAtUtc)
      || !isPurchaseOrderDeliverySchedule(revision.slices) || revision.slices.length === 0) return null
    const version = BigInt(revision.orderVersion)
    if (version <= previous || version > ceiling) return null
    previous = version
  }
  return source as unknown as PurchaseOrderLineScheduleHistory
}
