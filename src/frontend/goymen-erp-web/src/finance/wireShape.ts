/**
 * Exact wire shape and replay agreement for finance command/read decoders (K3 P2-3).
 *
 * `hasExactKeys`: every required OpenAPI property is present and no key outside the schema's property set appears.
 * Optional properties (not `required` in contracts/openapi/goymen-erp-v1.json) may be absent.
 *
 * `replayHeadersAgree`: a 200 command answer is trusted only when the replay headers say the same as the body —
 * `isReplay` ⇔ `Idempotency-Replayed: true`, and a replay names the same original correlation in both places.
 */
export function hasExactKeys(value: Record<string, unknown>, required: readonly string[],
  optional: readonly string[] = []): boolean {
  const keys = Object.keys(value)
  return required.every(key => Object.prototype.hasOwnProperty.call(value, key))
    && keys.every(key => required.includes(key) || optional.includes(key))
}

export function replayHeadersAgree(headers: Headers, isReplay: boolean, originalCorrelationId: string): boolean {
  const replayed = headers.get('Idempotency-Replayed')
  const original = headers.get('Idempotency-Original-Correlation-Id')
  if (isReplay) return replayed === 'true' && original === originalCorrelationId
  return replayed === null && original === null
}

export const COMMAND_RESULT_FIELDS = ['isReplay', 'issues', 'originalCorrelationId', 'outcome', 'resource'] as const
