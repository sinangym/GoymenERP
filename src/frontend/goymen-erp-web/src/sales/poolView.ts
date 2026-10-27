/** Presentation helpers of the S-01 screens shared across files (kept out of the component modules). */

import { POOL_TEXT, poolProblemText } from './poolText'
import { poolFieldForServerPath } from './poolForm'
import { QUOTE_STATE, REQUEST_STATE, type QuoteState, type RequestState } from './poolContract'
import type { StatusTone } from '../ui/StatusIndicator'

/** A DOM id for a field key (`lines.<uuid>.quantity` → `pool-lines-<uuid>-quantity`). */
export const cssId = (key: string) => `pool-${key.replace(/[^A-Za-z0-9_-]/g, '-')}`

/** The dot's colour only — the state text carries the meaning. */
export const REQUEST_TONE: Record<RequestState, StatusTone> = {
  [REQUEST_STATE.submitted]: 'warning',
  [REQUEST_STATE.approved]: 'success',
  [REQUEST_STATE.rejected]: 'danger',
}

export const QUOTE_TONE: Record<QuoteState, StatusTone> = {
  [QUOTE_STATE.open]: 'info',
  [QUOTE_STATE.converted]: 'success',
  [QUOTE_STATE.lost]: 'neutral',
}

/**
 * Where the server's field errors of a refused create/revision belong: each named field on the form (resolved against the
 * SENT lines), anything else in the summary under its own key — nothing goes silent.
 */
export function serverFieldErrors(
  problem: { readonly errors: readonly { readonly field: string; readonly code: string }[] } | null,
  sentLines: readonly { readonly lineId: string }[],
): Record<string, string> {
  const errors: Record<string, string> = {}
  problem?.errors.forEach((entry, index) => {
    const message = poolProblemText(entry.code) ?? POOL_TEXT.form.serverFieldError
    const field = poolFieldForServerPath(entry.field, sentLines) ?? `server.${index}`
    if (errors[field] === undefined) errors[field] = message
  })
  return errors
}
