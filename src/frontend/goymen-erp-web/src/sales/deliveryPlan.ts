/**
 * Requested delivery slices of one order line: exact, same-unit bookkeeping only.
 *
 * Source: #257 `CONTRACT_V1.md` §2 and `SalesDraftDeliveryPlanV1` @ 2c1239e, which the
 * server runs on create. This mirrors it so the form can refuse an over-plan before a
 * send and show what is still unplanned — the server stays the authority.
 *
 *  - `1000 = 400 + 600` leaves 0; `400 + 700` exceeds 1000 and is refused;
 *    `400 + 400` leaves 200 UNPLANNED — never "delivered", nothing was delivered;
 *  - a slice in another unit is refused, never converted;
 *  - no scale is applied and nothing is rounded: integer coefficients and powers of
 *    ten, exactly as the source does. Owner profile scale is a separate, later check.
 *
 * This is not a price, tax or tolerance formula; it is the subtraction the contract
 * itself defines for planning, and it has no business default in it.
 */

const DECIMAL_RE = /^[0-9]+(?:\.[0-9]+)?$/

interface Exact {
  readonly coefficient: bigint
  readonly scale: number
}

function toExact(text: string): Exact | null {
  if (!DECIMAL_RE.test(text)) return null
  const dot = text.indexOf('.')
  return {
    coefficient: BigInt(text.replace('.', '')),
    scale: dot < 0 ? 0 : text.length - dot - 1,
  }
}

function toText(value: bigint, scale: number): string {
  if (scale === 0) return value.toString()
  const digits = value.toString().padStart(scale + 1, '0')
  const text = `${digits.slice(0, digits.length - scale)}.${digits.slice(digits.length - scale)}`
  return text.replace(/0+$/, '').replace(/\.$/, '')
}

export interface PlanSlice {
  readonly orderUnitId: string
  readonly quantity: string
}

export type DeliveryPlanResult =
  | { readonly ok: true; readonly unplanned: string }
  | { readonly ok: false; readonly error: 'invalid-quantity' | 'foreign-unit' | 'exceeds-order' }

/**
 * What remains unplanned after the slices. `slices === null` is an unknown plan and
 * leaves the whole quantity unplanned, exactly like an empty plan — neither is a delivery.
 */
export function unplannedQuantity(
  line: { readonly orderUnitId: string; readonly quantity: string },
  slices: readonly PlanSlice[] | null,
): DeliveryPlanResult {
  const ordered = toExact(line.quantity)
  if (ordered === null || ordered.coefficient === 0n) return { ok: false, error: 'invalid-quantity' }
  const values: Exact[] = []
  for (const slice of slices ?? []) {
    if (slice.orderUnitId !== line.orderUnitId) return { ok: false, error: 'foreign-unit' }
    const value = toExact(slice.quantity)
    if (value === null || value.coefficient === 0n) return { ok: false, error: 'invalid-quantity' }
    values.push(value)
  }
  const scale = values.reduce((max, value) => Math.max(max, value.scale), ordered.scale)
  let remainder = ordered.coefficient * 10n ** BigInt(scale - ordered.scale)
  for (const value of values) remainder -= value.coefficient * 10n ** BigInt(scale - value.scale)
  if (remainder < 0n) return { ok: false, error: 'exceeds-order' }
  return { ok: true, unplanned: toText(remainder, scale) }
}
