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
 * The context whose read was issued most recently.
 *
 * WHY A SECOND VARIABLE. The slot alone cannot tell a CURRENT answer from a LATE one.
 * A read issued under the previous namespace can land after the user has moved and a
 * new read has already succeeded — and because the recording happens inside the
 * transport, it lands before any caller-side fence can discard it. Its body is valid
 * and acknowledged, so it looks like good news; it simply answers a question nobody is
 * asking any more.
 *
 * Recording the context at ISSUE time gives every observation something to be measured
 * against. Note this guards the SUCCESS side as much as the failure side: a stale
 * success that re-points the slot at an abandoned namespace takes readiness away from
 * the context the user is actually in, which is the same defect wearing the opposite
 * sign.
 */
let issuedKey: string | null = null

/**
 * How many reads have actually been RECORDED.
 *
 * A revalidation needs to prove that a fresh read landed, and "the refetch promise
 * resolved" does not prove that: a query can settle from cache, or be cancelled and
 * reverted, and still resolve. Counting recorded observations gives a caller
 * something it can compare across the await — if this has not moved, nothing was
 * read, whatever the promise said.
 *
 * Late and foreign observations do not count, for the same reason they do not grant:
 * they are not answers about the context being checked.
 */
let readSeq = 0

/** The number of reads recorded so far. Only ever compared, never interpreted. */
export function contractReadSeq(): number {
  return readSeq
}

/**
 * Declare that a read is being issued for this context.
 *
 * Called by the transports before the request goes out, so that whatever comes back
 * can be checked for currency rather than trusted for arriving.
 */
export function beginContractRead(context: ContractContext | null): void {
  if (context === null) return
  issuedKey = contractContextKey(context)
}

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
  const key = contractContextKey(context)

  /**
   * A LATE observation speaks for nobody.
   *
   * If a newer read has since been issued for a different context, this response is
   * about a namespace the client has left. It may not grant, and it may not revoke.
   */
  if (issuedKey !== null && issuedKey !== key) return

  const ready =
    observation.status === 200 && observation.bodyValidated && observation.ack !== null

  readSeq += 1

  if (ready) {
    grant = { key, ready: true }
    return
  }

  /**
   * A REVOKE only touches its own context.
   *
   * With one slot, writing `{key, ready:false}` unconditionally let a failure that
   * belonged to the context the user just LEFT overwrite the grant they are working
   * under now. A late 403 or a dropped connection from the previous company would
   * shut the gate on the current one — and, worse, the reverse shape would have let a
   * stale success reopen it. An observation may only speak about the context it was
   * issued in.
   */
  if (grant !== null && grant.key === key) {
    grant = { key, ready: false }
  }
}

/**
 * A read that never produced a usable response.
 *
 * `aborted` is OUR cancellation — the shell drops in-flight reads when the context
 * moves — so it is evidence about us, not about the server, and it must not be read as
 * a failure. Everything else (offline, a connection that dropped, a request that never
 * completed) is a server we can no longer reach, and readiness earned over a
 * connection that is gone cannot stand.
 */
export function recordContractReadFailure(
  context: ContractContext | null,
  reason: 'aborted' | 'unreachable',
): void {
  if (reason === 'aborted') return
  recordContractRead(context, { status: 0, bodyValidated: false, ack: null })
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
  issuedKey = null
  readSeq = 0
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
 * The identity of one command attempt.
 *
 * All three parts, because all three are what make a re-send the SAME send: the
 * context it was authorized in, the idempotency key the server deduplicates on, and a
 * fingerprint of the normalized payload. A form edited while an attempt was pending is
 * a different command, and must not be smuggled out under the old key.
 */
export interface CommandAttempt {
  readonly context: ContractContext
  readonly idempotencyKey: string
  /** Stable digest of the NORMALIZED payload — absent roles and 0 are one payload. */
  readonly payloadFingerprint: string
}

export function sameCommandAttempt(a: CommandAttempt, b: CommandAttempt): boolean {
  return (
    contractContextKey(a.context) === contractContextKey(b.context) &&
    a.idempotencyKey === b.idempotencyKey &&
    a.payloadFingerprint === b.payloadFingerprint
  )
}

/**
 * What came back, as far as the caller could actually read it.
 *
 * `validated` means the body decoded as this command's own result — the created or
 * linked record, or the stored result replayed for the same key. `undecodable` means
 * a response arrived that we could not read as that. `no-response` covers a timeout,
 * an abort and any transport failure.
 */
export type CommandResult =
  | { readonly kind: 'validated'; readonly status: number }
  | { readonly kind: 'undecodable'; readonly status: number }
  | { readonly kind: 'no-response' }

/** One attempt reporting back. */
export interface CommandSettlement {
  /** Whose response this is. */
  readonly attempt: CommandAttempt
  /** The context the client is in right now. */
  readonly context: ContractContext | null
  readonly response: Pick<Response, 'headers'> | null
  readonly result: CommandResult
}

/**
 * Whether an attempt already recorded as an unknown outcome may be CLOSED.
 *
 * The protocol acknowledgment is not a result. It proves the server understood the
 * declaration and nothing more — it is written before the handler runs, so it rides on
 * a 500 raised after the write just as readily as on one raised before it. Closing an
 * unknown outcome on the strength of the header alone would mark a command resolved
 * while its effect was still unknown, which is exactly the state the unknown outcome
 * exists to preserve.
 *
 * So closure requires a DEFINITIVE result, and every clause below is load-bearing:
 *
 *  - the same attempt: same context, same key, same normalized payload;
 *  - reported from that attempt's own context, which must be ready NOW — a grant from
 *    another company or a previous session cannot certify this one;
 *  - an acknowledged response, so we know which contract the answer is in;
 *  - a `validated` 2xx: the command's own result read back, or its replay.
 *
 * Everything else leaves the unknown outcome standing: a timeout, a 500, an
 * undecodable body, any 4xx, and a later compatibility 409 — which on a re-issue is
 * consistent both with "the first attempt landed" and with "it never did". Standing is
 * the safe state: it keeps the same key and forbids an automatic re-send.
 */
export function settlesUnknownOutcome(
  recorded: CommandAttempt,
  settlement: CommandSettlement,
): boolean {
  if (!sameCommandAttempt(recorded, settlement.attempt)) return false
  if (settlement.context === null) return false
  if (contractContextKey(settlement.context) !== contractContextKey(recorded.context)) return false
  if (!isCommandContractReady(settlement.context)) return false
  if (settlement.response === null) return false
  if (commandAckOutcome(settlement.response) !== 'acknowledged') return false
  if (settlement.result.kind !== 'validated') return false
  return settlement.result.status >= 200 && settlement.result.status < 300
}

/**
 * Whether the SAME attempt may be explicitly re-sent.
 *
 * Same context, and that context ready again. There is deliberately no function here
 * that mints a key: an unknown outcome is re-sent under the key it already has, or not
 * at all. A new key would turn one uncertain command into two.
 *
 * This never fires by itself — it answers a question for a caller acting on the user's
 * explicit retry, not a timer.
 */
export function mayRetrySameKey(
  attempt: CommandAttempt,
  current: ContractContext | null,
): boolean {
  if (current === null) return false
  if (contractContextKey(current) !== contractContextKey(attempt.context)) return false
  return isCommandContractReady(current)
}
