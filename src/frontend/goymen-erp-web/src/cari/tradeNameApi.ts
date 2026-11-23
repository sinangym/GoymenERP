/**
 * F-027 / C-02 transport — the Cari card's names.
 *
 * Same thin hand-written shape as ./customerGroupApi.ts, consuming the generated contract (see ./tradeName.ts):
 *  - The READ is a side read of the Cari card: it sends the client-contract declaration and the selected company, and
 *    deliberately does NOT move the readiness slot (the card's own detail read owns it).
 *  - The COMMAND shares the write gate (`isCommandContractReady`), one Idempotency-Key per attempt, the exact-204 rule
 *    and the "no acknowledgment = unknown outcome" rule through `putVersionedCommand`, so the PARTY version travels as
 *    lossless decimal text. `tradeName` is always sent (the contract requires the member); `null` clears it.
 */

import { API_BASE, contractRequestHeader } from './clientContract'
import { putVersionedCommand, type CommandContext, type CommandOutcome, type ReplayableFailures } from './commandApi'
import {
  TRADE_NAME_PROBLEM_CODE,
  decodeTradeNameView,
  tradeNamePath,
  tradeNameProblemCodeOf,
  type TradeNameView,
} from './tradeName'

const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY_ENABLED =
  import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'

function devHeaders(): Record<string, string> {
  return DEV_IDENTITY_ENABLED ? { 'X-Development-User': DEV_USER } : {}
}

export type TradeNameReadOutcome =
  | { readonly type: 'ok'; readonly data: TradeNameView }
  | { readonly type: 'http'; readonly status: number; readonly code: string | null }
  | { readonly type: 'malformed' }
  | { readonly type: 'offline' }
  | { readonly type: 'network-error' }
  | { readonly type: 'aborted' }

/** The selected company's view of the party's names (a side read of the Cari card). */
export async function fetchTradeName(
  partyId: string,
  companyId: string,
  signal?: AbortSignal,
): Promise<TradeNameReadOutcome> {
  let response: Response
  try {
    response = await fetch(`${API_BASE}${tradeNamePath(partyId)}`, {
      headers: { ...devHeaders(), ...contractRequestHeader(), 'X-Company-Id': companyId },
      signal,
    })
  } catch (error) {
    if (signal?.aborted) return { type: 'aborted' }
    if (!navigator.onLine) return { type: 'offline' }
    if (error instanceof TypeError) return { type: 'network-error' }
    throw error
  }

  let body: unknown = null
  try {
    body = await response.json()
  } catch {
    if (signal?.aborted) return { type: 'aborted' }
    body = null
  }

  if (response.status !== 200) {
    return { type: 'http', status: response.status, code: tradeNameProblemCodeOf(body) }
  }
  const data = decodeTradeNameView(body, { partyId, companyId })
  return data === null ? { type: 'malformed' } : { type: 'ok', data }
}

/** `UpdateCounterpartyTradeNameV1` stores 204, 404 (not found) and 409 (version conflict). */
export const TRADE_NAME_REPLAYABLE: ReplayableFailures = new Map([
  [404, new Set<string>([TRADE_NAME_PROBLEM_CODE.notFound])],
  [409, new Set<string>([TRADE_NAME_PROBLEM_CODE.versionConflict])],
])

/** Change or clear (`null`) the trade name; `expectedVersion` is the PARTY version of the read the form opened on. */
export function updateTradeName(
  context: CommandContext,
  partyId: string,
  expectedVersion: string,
  tradeName: string | null,
  signal?: AbortSignal,
): Promise<CommandOutcome<null>> {
  return putVersionedCommand(
    tradeNamePath(partyId),
    context,
    expectedVersion,
    { tradeName },
    signal,
    TRADE_NAME_REPLAYABLE,
  )
}
