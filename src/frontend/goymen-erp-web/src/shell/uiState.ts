/**
 * Shared read-view UI states for the shell and every feature page.
 *
 * Design source: ERP-000K_SHELL_CONTRACT_v1.md §12 + Issue #129 mandatory states.
 * Package-independent; presentational components consume `RemoteViewState`, they
 * do not re-derive it.
 *
 * §12 invariants encoded here:
 *  - empty / forbidden / not-found / service-error are DISTINCT states, never the
 *    same text or box;
 *  - a fence-rejected (stale) response NEVER carries data — `stale-rejected` has
 *    no data field;
 *  - `forbidden` and `not-found` are non-disclosing (no foreign-record hints);
 *  - `unauthenticated` is the session/login path, not an error box.
 *
 * Not encoded here (caller concerns): retry must not issue a new write — an
 * unknown write outcome uses the ERP-000E key-based replay contract (see
 * ./shellState.ts §A.5), never a blind resend.
 */

export type RemoteViewState<T> =
  | { readonly kind: 'initial-loading' }
  /** No server-authorized company for this user — base shell only. */
  | { readonly kind: 'no-authorized-company' }
  /** Authorized, request succeeded, zero records exist. */
  | { readonly kind: 'empty-no-data' }
  /** Authorized, request succeeded, a filter/search excluded everything. */
  | { readonly kind: 'empty-filtered' }
  /** 401 — session expired / not authenticated. Login path, not an error box. */
  | { readonly kind: 'unauthenticated' }
  /** 403 — non-disclosing; does not confirm the record exists. */
  | { readonly kind: 'forbidden' }
  /** 404 — non-disclosing; identical shape whether or not a foreign record exists. */
  | { readonly kind: 'not-found' }
  /** 409 — optimistic concurrency / business conflict; owner API supplies the safe diff. */
  | { readonly kind: 'conflict'; readonly safeDiff?: unknown }
  /** No network. Retry must not resurrect stale context. */
  | { readonly kind: 'offline' }
  /**
   * The request never completed: no response arrived at all. The browser deliberately
   * hides WHY a fetch failed (CORS, DNS, refused connection, TLS all surface as the
   * same opaque error), so we cannot narrow it further — but we can say truthfully
   * that nothing came back, which is a different fact from a bad response body.
   */
  | { readonly kind: 'network-error' }
  /** 500 / 503 — service temporarily unavailable (incl. capability fail-closed 503). */
  | { readonly kind: 'service-unavailable' }
  /**
   * A response ARRIVED and its body did not match the contract. Fail closed.
   * Never used for a request that produced no response — see `network-error`.
   */
  | { readonly kind: 'malformed-response' }
  /** The fence rejected a late response from a superseded context. Carries NO data. */
  | { readonly kind: 'stale-rejected' }
  /** The only state that carries data. */
  | { readonly kind: 'ready'; readonly data: T }

/**
 * What a data attempt produced. The caller reduces its transport result to one of
 * these; `classifyOutcome` turns it into a `RemoteViewState`.
 */
export type ViewOutcome<T> =
  | { readonly type: 'loading' }
  | { readonly type: 'no-authorized-company' }
  | { readonly type: 'fence-stale' }
  | { readonly type: 'offline' }
  /** The fetch itself failed; no response was received. */
  | { readonly type: 'network-error' }
  /** `body` carries the ProblemDetails payload so callers can read its stable `code`. */
  | { readonly type: 'http'; readonly status: number; readonly body?: unknown }
  | { readonly type: 'malformed' }
  | { readonly type: 'ok'; readonly data: readonly T[]; readonly filterActive: boolean }
  | { readonly type: 'ok-single'; readonly data: T }

export function classifyOutcome<T>(outcome: ViewOutcome<T>): RemoteViewState<T | readonly T[]> {
  switch (outcome.type) {
    case 'loading':
      return { kind: 'initial-loading' }
    case 'no-authorized-company':
      return { kind: 'no-authorized-company' }
    case 'fence-stale':
      return { kind: 'stale-rejected' }
    case 'offline':
      return { kind: 'offline' }
    case 'network-error':
      return { kind: 'network-error' }
    case 'malformed':
      return { kind: 'malformed-response' }
    case 'http': {
      if (outcome.status === 401) return { kind: 'unauthenticated' }
      if (outcome.status === 403) return { kind: 'forbidden' }
      if (outcome.status === 404) return { kind: 'not-found' }
      if (outcome.status === 409) return { kind: 'conflict' }
      if (outcome.status >= 500) return { kind: 'service-unavailable' }
      // Any other non-2xx the shell did not expect fails closed rather than
      // rendering as generic empty.
      return { kind: 'malformed-response' }
    }
    case 'ok':
      if (outcome.data.length > 0) return { kind: 'ready', data: outcome.data }
      return outcome.filterActive ? { kind: 'empty-filtered' } : { kind: 'empty-no-data' }
    case 'ok-single':
      return { kind: 'ready', data: outcome.data }
    default:
      return { kind: 'malformed-response' }
  }
}

/** A stale-rejected or malformed state must never be treated as data. */
export function carriesData<T>(state: RemoteViewState<T>): state is { kind: 'ready'; data: T } {
  return state.kind === 'ready'
}

/** Feature mounts are gated on this — a non-ready shell state closes them. */
export function blocksFeatureMount<T>(state: RemoteViewState<T>): boolean {
  return state.kind !== 'ready'
}
