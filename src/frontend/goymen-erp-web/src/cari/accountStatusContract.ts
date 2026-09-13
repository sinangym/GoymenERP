/**
 * The selected company account's active-state command — the ONE place that names
 * the accepted DTO's properties and rules.
 *
 * Mirrors `SetCounterpartyCompanyAccountActiveStateV1` in
 * `contracts/openapi/goymen-erp-v1.json` at `main@fc14554d`:
 * `PUT /api/v1/counterparties/{partyId}/company-account/active-state`, body
 * `SetCompanyPartyAccountActiveState { isActive, expectedVersion, reason }`, success
 * EXACTLY 204 with no body. Business handed the slice over in
 * https://github.com/sinangym/GoymenERP/pull/232#issuecomment-5647422648 as the next
 * sub-item of §14.2 "Aktif/pasif yap"; nothing here adds a screen, a route, a field or
 * a permission.
 *
 * WHAT THE COMMAND IS NOT. It changes `account.isActive` of the SELECTED company's
 * account and nothing else — not the shared Party's own `isActive`, not another
 * company's account, not an address or a contact person. The card keeps those states
 * under separate labels for exactly this reason, and this file gives the command only
 * the fields the contract gives it.
 */

import { INT64_MAX_DECIMAL } from './wireGuards'

/**
 * The control capability the status action is offered under — PROPOSED, not a grant.
 *
 * The server publishes no status capability today: `CounterpartyShellCapabilityProvider`
 * declares list, create, link and detail, and Business is explicit that status
 * availability must NOT be derived from any of those. Until Backend publishes this id
 * in the provider, `hasCapability` answers false, the action is not rendered and no
 * PUT is ever issued — the correct state, not a gap. Reading a record, editing its
 * contact block and linking it are different authorities from changing its status
 * (`counterparty.accounts.status.update`, company-wide), and the id is named after
 * that permission the way the other Cari ids are named after theirs.
 *
 * A CONTROL, not a route: it gates a button inside C-03 and is read straight from the
 * server snapshot, so it does not enter `shell/capabilities.ts` — the same shape the
 * Organization lane's status control takes.
 */
export const CARI_CONTROL_CAPABILITY = {
  accountStatusUpdate: 'cari.accounts.status.update',
} as const

/** The three DTO properties, spelled once. */
export const ACCOUNT_STATUS_FIELD = {
  isActive: 'isActive',
  expectedVersion: 'expectedVersion',
  reason: 'reason',
} as const

/**
 * `reason`: trimmed, then 1..1000 characters (`x-erp-canonicalization: trim`). The
 * server measures `Reason.Trim().Length`, which is UTF-16 code units — the same thing
 * `String.prototype.length` counts, so the client and the server agree on a string with
 * surrogate pairs without either of them special-casing it.
 */
export const REASON_MIN_LENGTH = 1
export const REASON_MAX_LENGTH = 1000

/**
 * The version fence: the SELECTED ACCOUNT's logical version, never the Party's. The
 * card already holds it as a lossless decimal string (`CounterpartyDetail.account.version`);
 * the contract declares `expectedVersion` as `type: [integer, string]`, `format: int64`,
 * `pattern: ^[1-9][0-9]*$`, `minimum: 1`, and the DTO behind it is a positive Int64.
 *
 * It is carried to the wire as that decimal STRING, verbatim, by `putVersionedCommand` —
 * the schema's `string` branch, the same encoding the contact-information fence already
 * uses against the real server. The number is never parsed in the client: a value past
 * 2^53 rounds in an IEEE-754 double, and a rounded fence can pass a concurrency check it
 * should have failed. Whether the server binds the quoted form for this DTO is Backend's
 * to pin with evidence (its Minimal API JSON defaults read numbers from strings); the
 * client does not change the contract on a guess either way.
 */
export const ACCOUNT_VERSION_MAX = INT64_MAX_DECIMAL

export function accountStatusPath(partyId: string): string {
  return `/counterparties/${encodeURIComponent(partyId)}/company-account/active-state`
}

/** What the user is asking for. Derived from the state they were SHOWN, never guessed. */
export type AccountStatusIntent = 'deactivate' | 'activate'

export function intentFor(currentIsActive: boolean): AccountStatusIntent {
  return currentIsActive ? 'deactivate' : 'activate'
}

/** The explicit boolean the command requires — `false` deactivates, `true` activates. */
export function targetIsActive(intent: AccountStatusIntent): boolean {
  return intent === 'activate'
}

export type ReasonProblem = 'required' | 'too-long'

/** The wire form of the reason: the server trims, so the client sends what it will keep. */
export function canonicalReason(raw: string): string {
  return raw.trim()
}

export function reasonProblem(raw: string): ReasonProblem | null {
  const canonical = canonicalReason(raw)
  if (canonical.length < REASON_MIN_LENGTH) return 'required'
  if (canonical.length > REASON_MAX_LENGTH) return 'too-long'
  return null
}

/** The two business fields of the body. The fence is added by the transport. */
export interface AccountStatusPayload {
  readonly isActive: boolean
  readonly reason: string
}

/**
 * The body for one intent, or null when the reason cannot be sent. Built from the
 * canonical reason so that the same words typed with different surrounding whitespace
 * are ONE payload — which is what the server's idempotency fingerprint also sees
 * (`Reason.Trim()`), and what makes a same-key retry reproduce the request byte for byte.
 */
export function buildAccountStatusPayload(
  intent: AccountStatusIntent,
  rawReason: string,
): AccountStatusPayload | null {
  if (reasonProblem(rawReason) !== null) return null
  return { isActive: targetIsActive(intent), reason: canonicalReason(rawReason) }
}
