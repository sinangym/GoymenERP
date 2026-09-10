/**
 * ONE rule for how a Cari action is blocked.
 *
 * WHY THIS EXISTS AS CODE. The UX review names inconsistent action blocking twice:
 * §8.7/64 asks for a single rule, and §12/27 records three different behaviours on
 * three screens of one product as the negative evidence for why the rule has to be
 * written down before the third screen exists. The Cari surface is at two already —
 * an unauthorized detail link degrades to plain text, a declared-but-unbuilt route
 * renders a neutral not-found — and C-02/link will add more. A rule that lives only
 * in review comments is the situation §12/27 describes.
 *
 * THE RULE, and the one distinction it turns on: WHO is being told no.
 *
 * 1. NOT ENTITLED — the caller does not hold the capability or permission.
 *    Render NOTHING: no button, no disabled button, no explanation. A disabled
 *    control with the reason "you lack permission" confirms that the feature exists
 *    and that this record is a valid target for it, which is precisely the
 *    disclosure ERP-000K §10.2 forbids — a denied module and a nonexistent one must
 *    be indistinguishable. This is why `NotEntitled` carries no reason field: the
 *    type makes the leak unrepresentable rather than merely discouraged.
 *
 * 2. BLOCKED — the caller is entitled, but the record's own state says no right now
 *    (already linked, passive account, nothing selected, …).
 *    Render the control DISABLED with the reason visible. This is UX review §12/10:
 *    the source case is a customer-owned stock row whose reserve action is not
 *    hidden but made non-startable, so the operator learns the rule instead of
 *    hunting for a missing button. Nothing is disclosed, because the caller was
 *    already entitled to know the record's state.
 *
 * 3. ALLOWED — render it.
 *
 * The two are NOT interchangeable, and neither one is the "safe default" for the
 * other. Hiding a business-rule block teaches the operator that the screen is
 * broken; disabling an entitlement block leaks. Every Cari action states which one
 * it is, in these terms.
 *
 * SCOPE: the Cari surface. It is written here because this is where it is used and
 * where it can be tested; it is a candidate for the shell contract, which is a
 * different lane's file (ERP-000K_SHELL_CONTRACT).
 */

/** The caller is entitled and the record's state permits it. */
export interface ActionAllowed {
  readonly kind: 'allowed'
}

/**
 * The caller is not entitled. Deliberately EMPTY: there is no reason to render,
 * because rendering one is the disclosure.
 */
export interface ActionNotEntitled {
  readonly kind: 'not-entitled'
}

/** Entitled, but the record's own state blocks it. The reason is shown. */
export interface ActionBlocked {
  readonly kind: 'blocked'
  /** Turkish, user-facing, about the RECORD — never about the caller's permissions. */
  readonly reason: string
}

export type ActionGate = ActionAllowed | ActionNotEntitled | ActionBlocked

export const allowed: ActionAllowed = { kind: 'allowed' }
export const notEntitled: ActionNotEntitled = { kind: 'not-entitled' }

export function blocked(reason: string): ActionBlocked {
  return { kind: 'blocked', reason }
}

/**
 * Whether the control is rendered at all.
 *
 * `not-entitled` is the only gate that removes it. A blocked action stays on screen
 * so its reason can be read — that is the whole of §12/10.
 */
export function isRendered(gate: ActionGate): boolean {
  return gate.kind !== 'not-entitled'
}

/** Whether the rendered control is interactive. */
export function isEnabled(gate: ActionGate): boolean {
  return gate.kind === 'allowed'
}

/** The reason to show beside a rendered-but-disabled control, or null. */
export function reasonOf(gate: ActionGate): string | null {
  return gate.kind === 'blocked' ? gate.reason : null
}
