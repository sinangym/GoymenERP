/**
 * UI-70b S-10 — the notice of a customer return attempt: the shared Sales attempt sentences (./poolAttempt.ts), with the
 * S-10 reason appended to a refusal that carries an S-10 code (the shared codes already carry their own reason there).
 *
 * A finalization refused with 503 `sales_customer_return_finance_port_unavailable` stays an unresolved attempt — the
 * house rule for every 5xx: the key is kept and only the same send may follow — but it says WHY (SAT-D294: no Finance
 * port is registered, so the return did not finalize).
 */
import type { CustomerReturnMutation } from './customerReturnApi'
import { CUSTOMER_RETURN_PROBLEM_CODE, CUSTOMER_RETURN_TEXT, customerReturnOwnProblemText } from './customerReturnText'
import type { OrderAttemptState } from './orderAttempt'
import { poolNotice, type PoolNotice } from './poolAttempt'

export function customerReturnNotice<P>(
  state: OrderAttemptState<P, CustomerReturnMutation>,
  options: {
    readonly noun: string
    readonly onRecord: boolean
    readonly blockedChange: boolean
    readonly completed: (result: CustomerReturnMutation) => string
    /** The problem code of the last 5xx answer, which the attempt machine does not keep. */
    readonly serviceCode: string | null
  },
): PoolNotice | null {
  if (
    state.phase === 'unknown'
    && state.reason === 'service-unavailable'
    && !options.blockedChange
    && options.serviceCode === CUSTOMER_RETURN_PROBLEM_CODE.financePortUnavailable
  ) {
    return { message: CUSTOMER_RETURN_TEXT.detail.financePortUnavailable, role: 'alert', offerRetry: true }
  }
  const base = poolNotice(state, options)
  if (base === null || state.phase !== 'failed' || state.unresolvedSince !== undefined) return base
  const reason = customerReturnOwnProblemText(state.problem?.code ?? null)
  return reason === null ? base : { ...base, message: `${base.message} ${reason}` }
}
