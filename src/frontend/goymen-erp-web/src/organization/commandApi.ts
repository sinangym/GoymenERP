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
  /** Any other definite non-2xx the contract does not describe. Fails closed. */
  | { readonly kind: 'unexpected'; readonly status: number }

/** The outcome is UNKNOWN: the command may or may not have been applied. */
export type CommandUnknown =
  | { readonly kind: 'offline' }
  /** The fetch produced no response at all. */
  | { readonly kind: 'network-error' }
  /** 5xx — the server answered, but not about our command's result. */
  | { readonly kind: 'service-unavailable'; readonly status: number }
  /** Cancelled by us (unmount / context change). Nothing is known either. */
  | { readonly kind: 'aborted' }

export type CommandOutcome =
  | {
      readonly type: 'applied'
      /** True when the server replayed an earlier identical attempt. */
      readonly replayed: boolean
      readonly originalCorrelationId: string | null
    }
  | { readonly type: 'failed'; readonly failure: CommandFailure }
  | { readonly type: 'unknown'; readonly unknown: CommandUnknown }

function readReplayed(headers: Headers): boolean {
  return (headers.get('Idempotency-Replayed') ?? '').trim().toLowerCase() === 'true'
}

function readOriginalCorrelationId(headers: Headers): string | null {
  const value = headers.get('Idempotency-Original-Correlation-Id')
  return value !== null && value.trim().length > 0 ? value.trim() : null
}

function classifyFailure(status: number, body: unknown, replayed: boolean): CommandFailure {
  if (status === 400) return { kind: 'validation' }
  if (status === 401) return { kind: 'unauthenticated' }
  if (status === 403) return { kind: 'forbidden' }
  if (status === 404) return { kind: 'not-found' }
  if (status === 415) return { kind: 'unsupported-media' }
  if (status === 409) {
    const conflict = decodeVersionConflict(body)
    // The PRESENCE of the conflict object is the discriminator the backend named —
    // not the title text, which is localized and must never be parsed.
    return conflict === null
      ? { kind: 'idempotency-conflict' }
      : { kind: 'version-conflict', conflict, conflictIsCurrent: !replayed }
  }
  return { kind: 'unexpected', status }
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

  // Exact 204. A 200/202 for this command is not in the contract; treating one as
  // success would be asserting an effect the server never confirmed.
  if (response.status === 204) {
    return {
      type: 'applied',
      replayed: readReplayed(response.headers),
      originalCorrelationId: readOriginalCorrelationId(response.headers),
    }
  }

  if (response.status >= 500) {
    return {
      type: 'unknown',
      unknown: { kind: 'service-unavailable', status: response.status },
    }
  }

  let parsed: unknown = null
  try {
    parsed = await response.json()
  } catch {
    // A code-less or entirely empty problem body is expected here; the status still
    // classifies the answer.
    parsed = null
  }

  /**
   * Replay headers are read on FAILURES too, not only on 204. A replayed terminal
   * answer is still a replay, and for a 409 that changes what its numbers mean.
   */
  return {
    type: 'failed',
    failure: classifyFailure(response.status, parsed, readReplayed(response.headers)),
  }
}
