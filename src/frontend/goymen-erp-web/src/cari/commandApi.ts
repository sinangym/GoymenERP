/**
 * Cari command transport — C-02 create, CARI-LINK candidates and link.
 *
 * Same thin hand-written shape as ./api.ts and ./detailApi.ts (HANDOFF: replaced
 * by the generated client, Issue #122; the decoders survive that move). Mirrors
 * `CreateCounterpartyV1`, `GetCounterpartyCompanyAccountLinkCandidatesV1` and
 * `CreateCounterpartyCompanyAccountV1` in `contracts/openapi/goymen-erp-v1.json`
 * (SHA-256 3e583a4e…a9721 on main 9f5b9bf).
 *
 * The transport carries no business logic and invents no field:
 *  - ONE `X-Company-Id`, from the shell's committed context. Nothing in body,
 *    query or path can name a company (spec §3.2, §7.4);
 *  - ONE `Idempotency-Key` per logical attempt, supplied by the caller. The
 *    transport never mints or rotates one (spec §4.3);
 *  - success is EXACTLY 201. A 200 or 202 for a POST is not in the contract and
 *    is reported as an unknown outcome, never as success;
 *  - replay is read from `Idempotency-Replayed` / `Idempotency-Original-Correlation-Id`
 *    and surfaced so the screen can avoid a second toast or navigation (§4.4).
 */

import { decodeCounterpartyMutation, type CounterpartyMutationResult } from './mutationContract'
import { decodeLinkCandidatePage, type LinkCandidatePage } from './candidateContract'
import { accountStatusPath, type AccountStatusPayload } from './accountStatusContract'
import { problemCodeOf, type PartyKind, type ProblemCode } from './contract'
import { isLosslessVersion } from './wireGuards'
import {
  beginContractRead,
  commandAckOutcome,
  contractAckOf,
  contractRequestHeader,
  isCommandContractReady,
  recordContractRead,
  recordContractReadFailure,
  type ContractContext,
} from './clientContract'
import type { ViewOutcome } from '../shell/uiState'

const API_BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? '/api/v1'
const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY_ENABLED =
  import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'

function devHeaders(): Record<string, string> {
  return DEV_IDENTITY_ENABLED ? { 'X-Development-User': DEV_USER } : {}
}

/** The C-02A request body — exactly these four fields (spec §3, §3.1). */
export interface CreateCounterpartyPayload {
  readonly kind: PartyKind
  readonly legalName: string
  readonly tradeName: string | null
  readonly roles: number
}

/** The link request body — roles only. No reason, no company, no version (§7.4). */
export interface LinkCompanyAccountPayload {
  readonly roles: number
}

/**
 * What one command request produced. Unlike a read, a write has THREE classes of
 * result and the difference is load-bearing for the retry rule (spec §4.3, §5):
 *
 *  - `created`: the server confirmed the business record (possibly as a replay);
 *  - `http`: a DEFINITE answer that is not success — the stable `code` says what;
 *  - everything else: the outcome is UNKNOWN. The request may or may not have been
 *    processed, so the same logical attempt is retried with the SAME key.
 */
export type CommandOutcome<T> =
  /**
   * The compatibility gate refused before any request existed. No handler ran, no key
   * was consumed, nothing is uncertain — this is a definite non-event.
   */
  | { readonly type: 'contract-not-ready' }
  /**
   * A response arrived without the server's acknowledgment, so we cannot tell what
   * contract answered. Unknown, never success.
   */
  | { readonly type: 'unacknowledged'; readonly status: number }
  | {
      readonly type: 'created'
      readonly data: T
      readonly replayed: boolean
      readonly originalCorrelationId: string | null
    }
  /**
   * A versioned update the server APPLIED (204), possibly as a replay. `data` is
   * `null` for every one of these: a 204 carries no body, so there is nothing the
   * server has told us about the record's new state. See `putVersionedCommand`.
   */
  | {
      readonly type: 'applied'
      readonly data: T
      readonly replayed: boolean
      readonly originalCorrelationId: string | null
    }
  /**
   * A versioned update whose row-version fence was not a usable concurrency token,
   * so the client refused to send it. Definite, like `contract-not-ready`: no
   * request existed and no key was spent.
   */
  | { readonly type: 'unfenced' }
  | {
      readonly type: 'http'
      readonly status: number
      readonly code: ProblemCode | null
      readonly body: unknown
      /**
       * `Idempotency-Replayed` on a DEFINITE non-success answer. A replayed 409 is the
       * stored terminal answer sent again verbatim, so any version it names is the one
       * from the FIRST run — a screen must not present it as the record's current state.
       */
      readonly replayed?: boolean
    }
  /** A 201 arrived and its body did not match the contract. The record may exist. */
  | { readonly type: 'malformed' }
  | { readonly type: 'offline' }
  /** The fetch failed before a response; nothing is known about the server side. */
  | { readonly type: 'network-error' }
  /** Cancelled by the caller (unmount / discard). Nothing is known either. */
  | { readonly type: 'aborted' }

export interface CommandContext {
  /** The committed CompanyContext this attempt belongs to. */
  readonly companyId: string
  /** The attempt's single key. Created once per snapshot, never per click. */
  readonly idempotencyKey: string
  /**
   * The API origin + shell namespace this attempt is authorized under.
   *
   * The write gate reads it: a command may only go out where a fresh, successful,
   * body-validated, acknowledged GET has already been seen for THIS origin, actor,
   * session, company and generation (spec §4.2). Readiness earned in company A never
   * opens a write to company B.
   */
  readonly contract: ContractContext
}

function readReplayed(headers: Headers): boolean {
  return (headers.get('Idempotency-Replayed') ?? '').trim().toLowerCase() === 'true'
}

function readOriginalCorrelationId(headers: Headers): string | null {
  const value = headers.get('Idempotency-Original-Correlation-Id')
  return value !== null && value.trim().length > 0 ? value.trim() : null
}

async function postCommand<T>(
  path: string,
  context: CommandContext,
  body: unknown,
  decode: (raw: unknown) => T | null,
  signal?: AbortSignal,
): Promise<CommandOutcome<T>> {
  /**
   * THE WRITE GATE. Nothing leaves the client until compatibility has been proven in
   * this exact context.
   *
   * An old server answers a GET with 200 and a valid body and simply never
   * acknowledges; posting to it is how a role-less record reaches a node that cannot
   * represent one. So the refusal happens HERE, before the request exists — no
   * handler runs, no key is consumed, and there is nothing to be uncertain about
   * afterwards. That is why this is a failure and not an unknown outcome.
   */
  if (!isCommandContractReady(context.contract)) return { type: 'contract-not-ready' }

  const headers: Record<string, string> = {
    ...devHeaders(),
    ...contractRequestHeader(),
    'Content-Type': 'application/json',
    'X-Company-Id': context.companyId,
    'Idempotency-Key': context.idempotencyKey,
  }

  let response: Response
  try {
    response = await fetch(`${API_BASE}${path}`, {
      method: 'POST',
      headers,
      body: JSON.stringify(body),
      signal,
    })
  } catch {
    if (signal?.aborted) return { type: 'aborted' }
    if (!navigator.onLine) return { type: 'offline' }
    // No response came back. For a WRITE that is not "it did not happen" — the
    // request may have reached the server. The caller keeps the same key.
    return { type: 'network-error' }
  }

  let parsed: unknown = null
  try {
    parsed = await response.json()
  } catch {
    parsed = null
  }

  // Exact 201 only. Everything else is reported with its stable code (or null when
  // the body carried none — `x-erp-empty-body-possible` on 400).
  if (response.status !== 201) {
    return {
      type: 'http',
      status: response.status,
      code: problemCodeOf(parsed),
      body: parsed,
      replayed: readReplayed(response.headers),
    }
  }

  /**
   * A 201 the server did not acknowledge is NOT success.
   *
   * The acknowledgment is written by the compatibility filter before the handler
   * runs, so a genuine 201 from a compatible node always carries it. Its absence on
   * a 201 means we cannot tell which node answered or under which contract — and the
   * one reading that must never happen is "created". Unknown keeps the same key and
   * forbids an automatic re-send (spec §4.5).
   */
  if (commandAckOutcome(response) === 'unknown-outcome') {
    return { type: 'unacknowledged', status: response.status }
  }

  const data = decode(parsed)
  if (data === null) return { type: 'malformed' }
  return {
    type: 'created',
    data,
    replayed: readReplayed(response.headers),
    originalCorrelationId: readOriginalCorrelationId(response.headers),
  }
}

/**
 * A versioned update: `PUT`, success is EXACTLY 204, and the change is fenced on
 * the row version it was read at.
 *
 * WHICH VERSION. For a company-scoped Cari update it is the `CompanyPartyAccount`
 * LOGICAL version — `CounterpartyDetail.account.version`, not
 * `CounterpartyDetail.version`. The Party's own identity and version are a
 * different row and a different authorization story, and another company's account
 * is a third; fencing a company-scoped change on the Party version would make two
 * companies' editors collide over a row neither of them touched.
 *
 * It is a canonical positive Int64 DECIMAL STRING (1..9223372036854775807, no
 * leading zero, sign, fraction, exponent or whitespace) and never a SQL rowversion
 * byte array — an earlier description of it as a rowversion was wrong about what the
 * token is, though not about what this code does with it.
 *
 * Three differences from `postCommand`, all load-bearing:
 *
 *  1. THE FENCE is carried verbatim as the decimal string the accepted contract
 *     takes (`SetCompanyPartyAccountActiveState.expectedVersion`:
 *     `type: [integer, string]`, `pattern ^[1-9][0-9]*$`). Never parsed into a
 *     number — an int64 past 2^53 rounds, and a rounded token can pass a
 *     concurrency check it should have failed. A value that is not a lossless
 *     version is REFUSED here, before a request exists, because an unfenced
 *     versioned write would overwrite whatever the record has become since.
 *  2. SUCCESS HAS NO BODY. 204 means applied and nothing else, so the outcome
 *     carries `data: null` and no screen can paint the patch it sent as the
 *     record's new truth. The caller owes a fresh authorized read afterwards.
 *  3. A 2xx THAT IS NOT 204 is outside the contract and is reported as UNKNOWN —
 *     the update may have been applied — exactly as a non-201 2xx is for a create.
 *
 * The transport names no business field. `fields` is passed through as the caller
 * built it, so the accepted DTO is the form's responsibility and never guessed
 * here; only `expectedVersion` is owned by this function, and it overrides any
 * same-named key a caller might carry.
 */
export async function putVersionedCommand(
  path: string,
  context: CommandContext,
  expectedVersion: string,
  fields: Readonly<Record<string, unknown>>,
  signal?: AbortSignal,
): Promise<CommandOutcome<null>> {
  // Same write gate as every command: proven compatibility in THIS context first.
  if (!isCommandContractReady(context.contract)) return { type: 'contract-not-ready' }
  if (!isLosslessVersion(expectedVersion)) return { type: 'unfenced' }

  const headers: Record<string, string> = {
    ...devHeaders(),
    ...contractRequestHeader(),
    'Content-Type': 'application/json',
    'X-Company-Id': context.companyId,
    'Idempotency-Key': context.idempotencyKey,
  }

  let response: Response
  try {
    response = await fetch(`${API_BASE}${path}`, {
      method: 'PUT',
      headers,
      body: JSON.stringify({ ...fields, expectedVersion }),
      signal,
    })
  } catch {
    if (signal?.aborted) return { type: 'aborted' }
    if (!navigator.onLine) return { type: 'offline' }
    // No response for a WRITE is not "it did not happen". Same key on retry.
    return { type: 'network-error' }
  }

  if (response.status !== 204) {
    let parsed: unknown = null
    try {
      parsed = await response.json()
    } catch {
      parsed = null
    }
    return {
      type: 'http',
      status: response.status,
      code: problemCodeOf(parsed),
      body: parsed,
      replayed: readReplayed(response.headers),
    }
  }

  /**
   * A 204 the server did not acknowledge is NOT success, for the same reason a
   * 201 is not: without the acknowledgment we cannot tell which node answered
   * under which contract, and "applied" is the one reading that must not be
   * guessed. Unknown keeps the same key and forbids an automatic re-send.
   */
  if (commandAckOutcome(response) === 'unknown-outcome') {
    return { type: 'unacknowledged', status: response.status }
  }

  return {
    type: 'applied',
    data: null,
    replayed: readReplayed(response.headers),
    originalCorrelationId: readOriginalCorrelationId(response.headers),
  }
}

/**
 * `PUT /api/v1/counterparties/{partyId}/company-account/active-state` — the SELECTED
 * company account's reasoned deactivate / reactivate (`SetCounterpartyCompanyAccountActiveStateV1`).
 *
 * The body is exactly the contract's three properties: `isActive` (an explicit boolean —
 * `false` deactivates, `true` activates, never a string and never omitted), `reason`
 * (already canonical, see `buildAccountStatusPayload`) and the `expectedVersion` fence
 * `putVersionedCommand` owns — the ACCOUNT's version, carried as the decimal string the
 * schema's `[integer, string]` declaration accepts, the same way the contact-information
 * fence reaches the wire. No company in the body: `X-Company-Id` names the target
 * account, and a missing account is the server's 404, never a create.
 */
export function setCompanyAccountActiveState(
  context: CommandContext,
  partyId: string,
  expectedVersion: string,
  payload: AccountStatusPayload,
  signal?: AbortSignal,
): Promise<CommandOutcome<null>> {
  return putVersionedCommand(
    accountStatusPath(partyId),
    context,
    expectedVersion,
    { isActive: payload.isActive, reason: payload.reason },
    signal,
  )
}

/** `POST /api/v1/counterparties` — one Party + one account in the active company. */
export function createCounterparty(
  context: CommandContext,
  payload: CreateCounterpartyPayload,
  signal?: AbortSignal,
): Promise<CommandOutcome<CounterpartyMutationResult>> {
  // Built explicitly so the wire body is exactly the four contract fields, whatever
  // else a caller's object might carry.
  /**
   * `roles` is OMITTED when the user chose no classification.
   *
   * The contract makes absent and numeric 0 the same normalized payload (spec §3.1),
   * and absence is the more honest of the two: the user did not pick a
   * classification, and 0 reads like a value they selected. The two must stay
   * interchangeable for the SAME attempt — see `normalizedRolesForFingerprint`.
   */
  const body = {
    kind: payload.kind,
    legalName: payload.legalName,
    tradeName: payload.tradeName,
    ...(payload.roles === 0 ? {} : { roles: payload.roles }),
  }
  return postCommand(
    '/counterparties',
    context,
    body,
    (raw) => decodeCounterpartyMutation(raw, { activeCompanyId: context.companyId }),
    signal,
  )
}

/**
 * `POST /api/v1/counterparties/{partyId}/company-account` — adds the TARGET
 * company's account to an existing Party. The response is bound to `partyId`: a
 * 201 about any other Party is malformed, not success.
 */
export function linkCompanyAccount(
  context: CommandContext,
  partyId: string,
  payload: LinkCompanyAccountPayload,
  signal?: AbortSignal,
): Promise<CommandOutcome<CounterpartyMutationResult>> {
  // `{}` is the roleless link request the contract names (spec §3.1). An absent HTTP
  // body is NOT the same thing, and is never what this sends.
  const body = payload.roles === 0 ? {} : { roles: payload.roles }
  return postCommand(
    `/counterparties/${encodeURIComponent(partyId)}/company-account`,
    context,
    body,
    (raw) =>
      decodeCounterpartyMutation(raw, { activeCompanyId: context.companyId, expectedPartyId: partyId }),
    signal,
  )
}

/** Only `q`, `pageSize`, `cursor` exist on this endpoint (spec §6.3). */
export interface LinkCandidateQuery {
  readonly q?: string
  /** 1–100; the server default is 25 and is used when omitted. */
  readonly pageSize?: number
  readonly cursor?: string
}

export function buildLinkCandidateQuery(query: LinkCandidateQuery): string {
  const params = new URLSearchParams()
  if (query.q !== undefined && query.q.trim().length > 0) params.set('q', query.q.trim())
  if (query.pageSize !== undefined) params.set('pageSize', String(query.pageSize))
  if (query.cursor !== undefined) params.set('cursor', query.cursor)
  const serialized = params.toString()
  return serialized.length > 0 ? `?${serialized}` : ''
}

/**
 * `GET /api/v1/counterparties/company-account-link-candidates`, scoped to the
 * TARGET company. The server unions the actor's authorized source companies; the
 * browser never calls per company and never unions (§6.3).
 */
export async function fetchLinkCandidates(
  targetCompanyId: string,
  query: LinkCandidateQuery,
  signal?: AbortSignal,
  /**
   * The TARGET company's context. A candidate read is a full Cari GET, so it is also
   * the exchange that can open the write gate for the company the link will be
   * written into — which is exactly the company readiness must be proven in
   * (spec §4.2: "A şirketindeki teyit B'ye link yazmasını açmaz").
   */
  contractContext?: ContractContext | null,
): Promise<ViewOutcome<LinkCandidatePage>> {
  const headers: Record<string, string> = {
    ...devHeaders(),
    ...contractRequestHeader(),
    'X-Company-Id': targetCompanyId,
  }

  beginContractRead(contractContext ?? null)

  let response: Response
  try {
    response = await fetch(
      `${API_BASE}/counterparties/company-account-link-candidates${buildLinkCandidateQuery(query)}`,
      { headers, signal },
    )
  } catch {
    if (signal?.aborted) {
      recordContractReadFailure(contractContext ?? null, 'aborted')
      return { type: 'fence-stale' }
    }
    recordContractReadFailure(contractContext ?? null, 'unreachable')
    if (!navigator.onLine) return { type: 'offline' }
    return { type: 'network-error' }
  }

  let body: unknown = null
  try {
    body = await response.json()
  } catch {
    // Our own cancellation cut the body; see api.ts.
    if (signal?.aborted) {
      recordContractReadFailure(contractContext ?? null, 'aborted')
      return { type: 'fence-stale' }
    }
    body = null
  }

  const ack = contractAckOf(response)

  if (response.status !== 200) {
    recordContractRead(contractContext ?? null, {
      status: response.status,
      bodyValidated: false,
      ack,
    })
    return { type: 'http', status: response.status, body }
  }

  const page = decodeLinkCandidatePage(body, targetCompanyId)
  recordContractRead(contractContext ?? null, {
    status: response.status,
    bodyValidated: page !== null,
    ack,
  })
  if (page === null) return { type: 'malformed' }
  return { type: 'ok-single', data: page }
}

/**
 * How `roles` enters an attempt's payload fingerprint.
 *
 * Absent and numeric 0 are one payload on the wire, so they must be one payload to
 * the retry rule as well. If they were not, a user who left the picker untouched and
 * a user who explicitly cleared it would produce two different fingerprints for the
 * same command — and an explicit same-key retry would then look like a different
 * attempt and be refused (spec §4.5).
 */
export function normalizedRolesForFingerprint(roles: number): number {
  return roles === 0 ? 0 : roles
}
