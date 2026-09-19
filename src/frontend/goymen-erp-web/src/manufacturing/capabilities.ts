/**
 * Capability ids of the Manufacturing order screen — the frontend's side of the
 * coordinator-approved MFG-UI card (D2), not a grant.
 *
 * The server's `ManufacturingShellCapabilityProvider` declares both ids STAGED
 * (`ImplementationAvailable: false`, card D1): until the browser suite accepts the
 * screen, the registry publishes neither, so `CapabilityRoute` closes the route onto
 * the neutral not-found block and `hasCapability` answers false. That is the intended
 * state, not a gap. The frontend never mints a capability to open its own screen, and
 * the server re-authorizes every request regardless (ERP-000K §10.2).
 *
 * WHY TWO IDS. They map onto two permissions that are enforced separately on the
 * server (`ManufacturingEndpoints`):
 *
 *   manufacturing.orders.detail <- manufacturing.orders.read
 *   manufacturing.orders.facts  <- manufacturing.orders.read + manufacturing.facts.record
 *
 * Reading an order and recording a fact on it are different authorities, so a reader
 * sees the balances and NO form (card D3: without the capability the control is not
 * drawn; the server enforces either way).
 *
 * This is a technical, functional sub-view (card D1). The canonical production screens
 * (PR-09/13/14/15) are `DECISION_ONLY_REFERENCE_MISSING`; nothing here is a locked
 * visual reference or the Master §7.7 realization modes.
 */

export const MANUFACTURING_CAPABILITY = {
  /** One production order: its roll balances. Route `/uretim/emir/:orderId`. */
  orderDetail: 'manufacturing.orders.detail',
  /** The fact forms INSIDE the order screen. Not a route — a control. */
  orderFacts: 'manufacturing.orders.facts',
} as const

export type ManufacturingCapabilityId =
  (typeof MANUFACTURING_CAPABILITY)[keyof typeof MANUFACTURING_CAPABILITY]

/** Route path per routed capability, as handed to the shell owner. */
export const MANUFACTURING_ROUTE: Record<string, string> = {
  [MANUFACTURING_CAPABILITY.orderDetail]: '/uretim/emir/:orderId',
}

/** Nav label. None: an order is opened by its id, never from the sidebar. */
export const MANUFACTURING_NAV_LABEL: Record<string, string> = {
  [MANUFACTURING_CAPABILITY.orderDetail]: '',
}

/** Concrete path to one order. */
export function orderDetailPath(orderId: string): string {
  return `/uretim/emir/${encodeURIComponent(orderId)}`
}
