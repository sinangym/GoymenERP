/**
 * The Cari client-contract handshake — `X-ERP-Counterparty-Contract`.
 *
 * MIRRORS, never invents. Read from the exact handoff source, PR #230 commit
 * `080e7a1d24f74975b45546c5277f0529c13f06d1` and its accepted follow-up `880cae8`
 * (conditional error-acknowledgment clarification):
 *   - `src/backend/GoymenERP.Api/Contracts/CounterpartyClientContract.cs`
 *   - `src/backend/GoymenERP.Api/OpenApi/ErpOpenApiContract.cs`
 *   - `contracts/openapi/goymen-erp-v1.json`
 *
 * WHAT THE SERVER DOES, so that what this module does can be checked against it:
 *
 *  - A request carrying the EXACT declaration is served, and the response echoes the
 *    same header. That echo is the acknowledgment, and it is owned by the API — it is
 *    never a reflection of whatever the caller sent.
 *  - An empty, duplicated, combined or unsupported declaration is refused with `409`
 *    `client_contract_unsupported`. On a GET this replaces ONLY an authorized success:
 *    an unauthorized list stays `403`, with no acknowledgment and no account ids.
 *  - A request with NO declaration: every command is refused with `409`
 *    `client_contract_required`, running no handler and storing no business, audit or
 *    idempotency result. A read is served, unless its visible projection contains a
 *    `roles=0` account — then it too is refused, rather than returning a body an old
 *    client would misread.
 *  - An OLD server has none of this and simply answers `200` with no acknowledgment.
 *
 * WHY READINESS IS NOT "THE HEADER CAME BACK". The filter writes the acknowledgment
 * BEFORE the handler runs, so it also rides on handler-generated errors. An ack
 * therefore proves the server understood the declaration. It does not prove the
 * request succeeded, and it does not prove this client may start writing.
 *
 * Commands are enabled only by a FRESH, SUCCESSFUL, BODY-VALIDATED GET whose response
 * carried the matching acknowledgment, bound to the current API origin, actor/session,
 * selected company and capability generation. A 4xx that happens to carry the ack, a
 * 200 whose body failed validation, and a 200 from an old server with no ack are all
 * "not ready".
 *
 * And the direction that is easy to get backwards: a MISSING ack on an error is not
 * proof of an old server. Failures raised before the filter — authentication, company
 * resolution, body binding — carry no ack either. So a missing ack on an error tells
 * us nothing, and nothing here infers anything from it.
 */

/** The single declaration. One value; there is no negotiation range. */
export const COUNTERPARTY_CONTRACT_HEADER = 'X-ERP-Counterparty-Contract'
export const COUNTERPARTY_CONTRACT_VERSION = 'optional-trade-roles-v1'

/**
 * The API origin, shared by every Cari transport so that readiness earned against one
 * backend cannot be spent against another.
 */
export const API_BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? '/api/v1'

/**
 * The declaration every supported Cari exchange carries — exactly once.
 *
 * Returned as a fresh object per call so a caller spreading it into request headers
 * cannot mutate a shared one, and so the value can never be accumulated into the
 * duplicate/combined form the server refuses.
 */
export function contractRequestHeader(): Record<string, string> {
  return { [COUNTERPARTY_CONTRACT_HEADER]: COUNTERPARTY_CONTRACT_VERSION }
}

/**
 * The acknowledgment on a response, or null.
 *
 * Exact string equality — never a prefix, never case-insensitive. The version IS the
 * contract, and "close enough" is how a client ends up writing to a server that means
 * something else by the same words.
 *
 * `Headers.get` folds a repeated header into a comma-joined value, so a duplicated or
 * combined acknowledgment fails this comparison and is treated as absent. That is the
 * intended reading: the server promises exactly one.
 */
export function contractAckOf(response: Pick<Response, 'headers'>): string | null {
  const value = response.headers.get(COUNTERPARTY_CONTRACT_HEADER)
  return value === COUNTERPARTY_CONTRACT_VERSION ? value : null
}

/**
 * The context readiness is bound to.
 *
 * `namespace` is the shell context key — actor, scope mode, selected company,
 * capability generation and committed revision. With the API origin it covers exactly
 * what the contract names: origin, actor/session, company, generation.
 *
 * Binding matters because readiness decides whether this client may begin issuing
 * commands. Carrying it across a company switch, a re-login or a capability
 * regeneration would be spending an answer the server gave about someone else.
 */
export interface ContractContext {
  readonly origin: string
  readonly namespace: string
}

export function contractContextKey(context: ContractContext): string {
  return `${context.origin} ${context.namespace}`
}

/**
 * What a read observed. `bodyValidated` is the DECODER's verdict, not the status code:
 * a 200 whose body the decoder rejected is a server we could not read, and treating
 * that as licence to start writing to it would be the wrong way round.
 */
export interface ReadObservation {
  readonly status: number
  readonly bodyValidated: boolean
  readonly ack: string | null
}

/**
 * One slot, not a map.
 *
 * A map would accumulate readiness for contexts the user has left, and every one of
 * those entries is an answer about an actor or company that is no longer current.
 * Keeping only the context in use makes a stale grant unreachable rather than merely
 * unused — the same reasoning as the remembered list view, and the reason the handoff
 * asks for a bound readiness instead of "a global cached Boolean".
 */
let grant: { readonly key: string; readonly ready: boolean } | null = null

/**
 * Record what a read observed, for the context it was issued in.
 *
 * Every read writes the slot, including the ones that REVOKE readiness: a server that
 * stops acknowledging, or starts failing, must not leave an earlier "yes" standing.
 */
export function recordContractRead(
  context: ContractContext | null,
  observation: ReadObservation,
): void {
  if (context === null) return
  const ready =
    observation.status === 200 && observation.bodyValidated && observation.ack !== null
  grant = { key: contractContextKey(context), ready }
}

/**
 * Whether commands may be issued in this context.
 *
 * Defaults to NO. An unknown context, a context that has not read yet, and a context
 * whose last read did not acknowledge all answer false. The gate opens on evidence,
 * never on the absence of contrary evidence.
 */
export function isCommandContractReady(context: ContractContext | null): boolean {
  if (context === null || grant === null) return false
  return grant.key === contractContextKey(context) && grant.ready
}

/** Drops the recorded grant. Tests call this; the slot outlives a render on purpose. */
export function resetContractReadiness(): void {
  grant = null
}

/**
 * What a COMMAND response's acknowledgment means for the attempt.
 *
 * `acknowledged` — this server understood the exact declaration and ran the command,
 * so the status code means what it says.
 *
 * `unknown-outcome` — the ack is absent or does not match. The command may have been
 * applied by a server that did not tell us, or refused by one that never saw it, and
 * the body cannot settle which. The attempt, its context, its normalized payload and
 * its idempotency key are all preserved; nothing is retried automatically, and never
 * under a new key.
 *
 * The rule that is easiest to lose: a LATER response — including a compatibility `409`
 * — does not erase a recorded unknown outcome. A 409 on a re-issue is consistent both
 * with "the first attempt landed" and with "it never did", so it is not an answer
 * either. Only an explicit same-key retry, after readiness has been re-established in a
 * validated context, can resolve it; `settlesUnknownOutcome` is narrow for that reason.
 */
export type CommandAckOutcome = 'acknowledged' | 'unknown-outcome'

export function commandAckOutcome(response: Pick<Response, 'headers'>): CommandAckOutcome {
  return contractAckOf(response) === null ? 'unknown-outcome' : 'acknowledged'
}

/**
 * Whether a later response may CLOSE an attempt already recorded as an unknown outcome.
 *
 * Only an acknowledged response, in a context that is itself currently ready. Anything
 * else leaves the unknown outcome standing — which is the safe state, because it is the
 * one that keeps the same key and forbids an automatic re-send.
 */
export function settlesUnknownOutcome(
  context: ContractContext | null,
  response: Pick<Response, 'headers'>,
): boolean {
  return isCommandContractReady(context) && commandAckOutcome(response) === 'acknowledged'
}
