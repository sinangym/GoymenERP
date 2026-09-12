/**
 * The one Organization command: `PUT /api/v1/organization/locations/{id}/active-state`.
 *
 * Wire facts, all from the merged endpoint and the third Backend's source handover
 * (Issue #66 comment 5646385043):
 *
 *  - body is exactly `{isActive, expectedVersion, reason}`. `isActive` and
 *    `expectedVersion` are JSON-required; `expectedVersion > 0`; `reason` is 1..1000
 *    AFTER trim. Nothing else may appear — no company, no id, no predicted version;
 *  - headers: `X-Company-Id`, `Idempotency-Key` (one value, 1..128 after trim, no
 *    comma). The transport never mints or rotates the key — the caller owns it;
 *  - success is EXACTLY 204 with NO body. It returns no DTO and no new version, so a
 *    caller that wants the current state must re-READ it;
 *  - a replay is announced by `Idempotency-Replayed: true` +
 *    `Idempotency-Original-Correlation-Id`, and preserves the terminal 204/404/409;
 *  - 409 IS TWO DIFFERENT ANSWERS and the body is what separates them: a VERSION
 *    conflict carries `conflict:{expectedVersion,currentVersion}`, an IDEMPOTENCY
 *    conflict (same key, different payload/actor) carries no such object. They need
 *    different recoveries, so they are never collapsed into one state;
 *  - 400/404/409 bodies may be CODE-LESS `ErpCodeLessProblemDetailsContract`, and a
 *    malformed query/body 400 may have no body at all. Nothing here parses a
 *    localized `title`/`detail` to decide anything.
 */

import { decodeVersionConflict, versionToken, type VersionConflict } from './contract'
import { API_BASE, devHeaders } from './api'

export interface SetActiveStatePayload {
  readonly isActive: boolean
  /** The version READ from the server. Never a predicted one. */
  readonly expectedVersion: number
  /** Raw user text; trimmed here so the wire value is the canonical one. */
  readonly reason: string
}

export interface CommandContext {
  readonly companyId: string
  readonly locationId: string
  /** One key per submitted snapshot, supplied by the caller. */
  readonly idempotencyKey: string
}

/**
 * A DEFINITE non-success answer. Each of these means the command did not take effect
 * and the server has told us why in a way we are entitled to read (status, plus the
 * presence or absence of the conflict object).
 */
export type CommandFailure =
  /** 400 — domain validation or malformed body. May carry no body at all. */
  | { readonly kind: 'validation' }
  /** 401 — session gone. */
  | { readonly kind: 'unauthenticated' }
  /** 403 — not entitled to change this location's state. */
  | { readonly kind: 'forbidden' }
  /** 404 — no such location in this context. Non-disclosing. */
  | { readonly kind: 'not-found' }
  /**
   * 409 WITH the conflict object — someone else changed the row. Re-read and retry.
   *
   * `conflictIsCurrent` is the load-bearing part. The server stores a command's
   * terminal answer and REPLAYS it verbatim for a repeated key, so a replayed 409
   * carries the conflict snapshot as it was when the command first ran — which can be
   * several versions behind by the time it is replayed. Verified at
   * PR #241 head `76c1be3`, which asserts exactly that: after the record advanced
   * twice, the replayed 409 still reports the ORIGINAL `currentVersion`.
   *
   * So `currentVersion` may only be shown as "the record's current version" when this
   * answer is NOT a replay. Otherwise it is history, and only a fresh GET is entitled
   * to say what the version is now.
   */
  | {
      readonly kind: 'version-conflict'
      readonly conflict: VersionConflict
      readonly conflictIsCurrent: boolean
    }
  /**
   * 409 WITHOUT the conflict object — this key was already used for a DIFFERENT
   * payload or actor. Re-sending the same key cannot help and a new key would be a
   * second business command, so the screen must start a new, explicit attempt.
   */
  | { readonly kind: 'idempotency-conflict' }
  /** 415 — media type. A client bug, not a user-recoverable state. */
  | { readonly kind: 'unsupported-media' }

/** The outcome is UNKNOWN: the command may or may not have been applied. */
export type CommandUnknown =
  | { readonly kind: 'offline' }
  /** The fetch produced no response at all. */
  | { readonly kind: 'network-error' }
  /** 5xx — the server answered, but not about our command's result. */
  | { readonly kind: 'service-unavailable'; readonly status: number }
  /**
   * A 2xx that is NOT 204. The contract defines exactly one success code, so a 200 or
   * 202 tells us the request was handled by something whose answer we cannot read.
   * Reading it as success would assert an effect; reading it as refusal would assert
   * the opposite. Both are claims we are not entitled to make.
   */
  | { readonly kind: 'unexpected-success'; readonly status: number }
  /**
   * A response we cannot classify at all: a status the contract does not describe, or
   * a 409 whose body is missing/unparseable so the version-vs-idempotency
   * discriminator is unavailable. A response off the contract is NOT evidence that
   * the command did not apply, so it fails closed into UNKNOWN rather than into a
   * refusal that would tell the user "uygulanmadı".
   */
  | { readonly kind: 'undecodable'; readonly status: number }
  /** Cancelled by us (unmount / context change). Nothing is known either. */
  | { readonly kind: 'aborted' }

export type CommandOutcome =
  | {
      readonly type: 'applied'
      /** True when the server replayed an earlier identical attempt. */
      readonly replayed: boolean
      readonly originalCorrelationId: string | null
    }
  /**
   * A definite refusal. `replayed` is load-bearing: the server replays a stored
   * terminal answer for a repeated key, so a REPLAYED 404/409 is the original
   * command's own outcome and therefore resolves it. A non-replayed refusal on a
   * retry (a 403 at the auth gate, say) is about THAT request and says nothing about
   * whether the first one was recorded.
   */
  | { readonly type: 'failed'; readonly failure: CommandFailure; readonly replayed: boolean }
  | { readonly type: 'unknown'; readonly unknown: CommandUnknown }

function readReplayed(headers: Headers): boolean {
  return (headers.get('Idempotency-Replayed') ?? '').trim().toLowerCase() === 'true'
}

function readOriginalCorrelationId(headers: Headers): string | null {
  const value = headers.get('Idempotency-Original-Correlation-Id')
  return value !== null && value.trim().length > 0 ? value.trim() : null
}

function isObjectBody(body: unknown): boolean {
  return typeof body === 'object' && body !== null && !Array.isArray(body)
}

/**
 * Turns a non-2xx, non-5xx response into either a DEFINITE refusal or an explicitly
 * indeterminate one.
 *
 * The line is drawn at "does the contract describe this answer?". 400/401/403/404/415
 * are described and their status alone is the whole meaning. A 409 is described but is
 * TWO answers, told apart by the presence of the conflict object — so a 409 whose body
 * never arrived or would not parse leaves the discriminator unavailable, and guessing
 * "idempotency conflict" there would tell the user a specific thing we do not know.
 * Anything else is off-contract entirely.
 *
 * Both of those indeterminate cases return null and become UNKNOWN, because an answer
 * we cannot read is not evidence that the command did not apply.
 */
function classifyFailure(
  status: number,
  body: unknown,
  bodyParsed: boolean,
  replayed: boolean,
): CommandFailure | null {
  if (status === 400) return { kind: 'validation' }
  if (status === 401) return { kind: 'unauthenticated' }
  if (status === 403) return { kind: 'forbidden' }
  if (status === 404) return { kind: 'not-found' }
  if (status === 415) return { kind: 'unsupported-media' }
  if (status === 409) {
    const conflict = decodeVersionConflict(body)
    // The PRESENCE of the conflict object is the discriminator the backend named —
    // not the title text, which is localized and must never be parsed.
    if (conflict !== null) {
      return { kind: 'version-conflict', conflict, conflictIsCurrent: !replayed }
    }
    // A well-formed problem body with no conflict IS the idempotency answer. A body
    // that never parsed is not — it is a 409 we could not read.
    return bodyParsed && isObjectBody(body) ? { kind: 'idempotency-conflict' } : null
  }
  return null
}

/**
 * Sends the state change. Returns a definite outcome or an explicitly UNKNOWN one;
 * it never guesses, never retries on its own and never rotates the key.
 */
export async function setLocationActiveState(
  context: CommandContext,
  payload: SetActiveStatePayload,
  signal?: AbortSignal,
): Promise<CommandOutcome> {
  const body = {
    isActive: payload.isActive,
    // Decimal string: permitted by the request schema and immune to numeric
    // formatting surprises. See contract.ts `versionToken`.
    expectedVersion: versionToken(payload.expectedVersion),
    reason: payload.reason.trim(),
  }

  let response: Response
  try {
    response = await fetch(
      `${API_BASE}/organization/locations/${encodeURIComponent(context.locationId)}/active-state`,
      {
        method: 'PUT',
        headers: {
          ...devHeaders(),
          'Content-Type': 'application/json',
          'X-Company-Id': context.companyId,
          'Idempotency-Key': context.idempotencyKey,
        },
        body: JSON.stringify(body),
        signal,
      },
    )
  } catch {
    if (signal?.aborted) return { type: 'unknown', unknown: { kind: 'aborted' } }
    if (!navigator.onLine) return { type: 'unknown', unknown: { kind: 'offline' } }
    // For a WRITE, no response is NOT "it did not happen" — the request may have
    // reached the server. The caller keeps the same key.
    return { type: 'unknown', unknown: { kind: 'network-error' } }
  }

  const replayed = readReplayed(response.headers)

  // Exact 204. A 200/202 for this command is not in the contract; treating one as
  // success would be asserting an effect the server never confirmed — and treating it
  // as a refusal would assert the opposite. It is UNKNOWN (see CommandUnknown).
  if (response.status === 204) {
    return {
      type: 'applied',
      replayed,
      originalCorrelationId: readOriginalCorrelationId(response.headers),
    }
  }

  if (response.status >= 200 && response.status < 300) {
    return {
      type: 'unknown',
      unknown: { kind: 'unexpected-success', status: response.status },
    }
  }

  if (response.status >= 500) {
    return {
      type: 'unknown',
      unknown: { kind: 'service-unavailable', status: response.status },
    }
  }

  let parsed: unknown = null
  let bodyParsed = false
  try {
    parsed = await response.json()
    bodyParsed = true
  } catch {
    // A code-less or entirely empty problem body is expected on 400; for a 409 it
    // costs us the discriminator, which `classifyFailure` accounts for.
    parsed = null
  }

  const failure = classifyFailure(response.status, parsed, bodyParsed, replayed)
  if (failure === null) {
    return { type: 'unknown', unknown: { kind: 'undecodable', status: response.status } }
  }

  /**
   * Replay headers are read on FAILURES too, not only on 204. A replayed terminal
   * answer is the ORIGINAL command's own outcome, which is what lets the caller tell
   * "my command ended this way" from "this particular retry was turned away".
   */
  return { type: 'failed', failure, replayed }
}
