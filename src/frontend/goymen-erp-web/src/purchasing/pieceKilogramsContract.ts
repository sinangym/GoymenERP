/**
 * AK-3b (BS-16/BS-17, golden TA4; `PurchaseGoodsReceiptPieceKilogramsV1`): how the weighed kilograms of a piece receipt compare
 * with the theoretical kilograms (pieces × the kg per piece recorded on the order line). The server evaluates it once and answers
 * the recorded value on a replay; the screen shows it as received and never recomputes a kg, a difference or a tolerance.
 */
export const PIECE_KILOGRAMS_OUTCOMES = ['Exact', 'WithinTolerance', 'OutsideTolerance', 'ToleranceNotSet'] as const
export type PieceKilogramsOutcome = typeof PIECE_KILOGRAMS_OUTCOMES[number]

export interface PieceKilograms {
  readonly pieceCount: number
  readonly kilogramsPerUnit: string
  readonly theoreticalQuantityKg: string
  readonly measuredQuantityKg: string
  /** Signed: weighed − theoretical. */
  readonly differenceKg: string
  readonly varianceOutcome: PieceKilogramsOutcome
  /** The Ayarlar tolerance read at the first attempt; null = not set. */
  readonly tolerancePercent: string | null
}

const FIELDS = ['pieceCount', 'kilogramsPerUnit', 'theoreticalQuantityKg', 'measuredQuantityKg', 'differenceKg', 'varianceOutcome',
  'tolerancePercent'] as const
const DECIMAL = /^(?:0|[1-9]\d*)(?:\.\d*[1-9])?$/
const SIGNED_DECIMAL = /^-?(?:0|[1-9]\d*)(?:\.\d*[1-9])?$/

const canonical = (value: unknown): value is string => typeof value === 'string' && DECIMAL.test(value)
const positive = (value: unknown): value is string => canonical(value) && value !== '0'

/**
 * Strict and fail closed: exactly the seven members; a whole piece count above zero; canonical decimal texts (never a number);
 * a known outcome that agrees with the difference and the tolerance (Exact ⇔ no difference, ToleranceNotSet ⇔ a difference
 * without a tolerance). Anything else is `null`, never a partial comparison.
 */
export function decodePieceKilograms(value: unknown): PieceKilograms | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return null
  const source = value as Record<string, unknown>
  const keys = Object.keys(source)
  if (keys.length !== FIELDS.length || !FIELDS.every((name) => Object.hasOwn(source, name))
    || typeof source.pieceCount !== 'number' || !Number.isSafeInteger(source.pieceCount) || source.pieceCount < 1
    || !positive(source.kilogramsPerUnit) || !canonical(source.theoreticalQuantityKg) || !positive(source.measuredQuantityKg)
    || typeof source.differenceKg !== 'string' || !SIGNED_DECIMAL.test(source.differenceKg) || source.differenceKg === '-0'
    || typeof source.varianceOutcome !== 'string' || !(PIECE_KILOGRAMS_OUTCOMES as readonly string[]).includes(source.varianceOutcome)
    || !(source.tolerancePercent === null || canonical(source.tolerancePercent))) return null
  if ((source.varianceOutcome === 'Exact') !== (source.differenceKg === '0')) return null
  if ((source.varianceOutcome === 'ToleranceNotSet') !== (source.tolerancePercent === null && source.differenceKg !== '0')) return null
  return source as unknown as PieceKilograms
}
