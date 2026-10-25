/**
 * Sales screen mount ids and routes.
 *
 * STATUS: PROPOSED — NOT PUBLISHED. These ids are the frontend's mount keys for the
 * three screens, split the way every other lane splits them (a list grant must not
 * imply a detail or a create grant). They are NOT permissions and grant nothing:
 * #257 states that no Sales permission name exists yet and that absent mappings
 * default deny, and no backend shell provider publishes a `sales.*` capability. So
 * `CapabilityRoute` closes every Sales route onto the neutral not-found block in the
 * running app. That is the intended state until the central owner publishes the real
 * ids — at which point THIS file is renamed to match, and the mount handoff is sent.
 */

export const SALES_CAPABILITY = {
  /** S-02 sipariş taslakları listesi. */
  draftsList: 'sales.drafts.list',
  /** S-04 Sipariş 360 — draft detail. */
  draftsDetail: 'sales.drafts.detail',
  /** S-03 yeni satış siparişi taslağı. */
  draftsCreate: 'sales.drafts.create',
  /**
   * SALES-A1: the approve / reject CONTROL on the S-04 detail (a control of `draftsDetail`, not a
   * route: the shell publishes it only to an actor holding `sales.orders.approve` beside the read).
   * The submit button follows `draftsCreate`. The route of this control is the detail's own.
   */
  ordersApprove: 'sales.orders.approve',
} as const

/** Q9: non-routed control for recording customer acceptance of the current approved revision. */
export const SALES_CUSTOMER_ACCEPTANCE_RECORD_CAPABILITY = 'sales.orders.customer-acceptance.record' as const

/** K3: the commercial-revision CONTROL on S-04; separate from every routed Sales screen. */
export const SALES_PRICE_WRITE_CAPABILITY = 'sales.orders.price.write' as const
export const SALES_PRICE_FLOOR_EXCEPTION_APPROVE_CAPABILITY = 'sales.orders.price-floor-exception.approve' as const

/**
 * DEC-SAL-003: the planner's delivery-schedule update CONTROL on S-04 (dates only, no re-approval, audited). The shell
 * publishes it only to an actor holding `sales.orders.delivery-schedule.write` beside the read.
 */
export const SALES_DELIVERY_SCHEDULE_WRITE_CAPABILITY = 'sales.orders.delivery-schedule.write' as const

/**
 * DEC-SAL-005: the impact-preview + cancel CONTROL on S-04 for an approved order. The shell publishes it only to an actor
 * holding `sales.orders.cancel` beside the read; create, approve, price and schedule never confer it.
 */
export const SALES_CANCEL_CAPABILITY = 'sales.orders.cancel' as const

/**
 * Delegation No 30 / SAT-D12: the force-close impact preview + command CONTROL on S-04 for an approved order. The shell
 * publishes it only to an actor holding `sales.orders.force-close` beside the read; cancel never confers it.
 */
export const SALES_FORCE_CLOSE_CAPABILITY = 'sales.orders.force-close' as const

/**
 * DEC-SAL-002: the quantity-revision CONTROL on S-04 ("Revizyon oluştur"). The shell publishes it only to an actor holding
 * `sales.orders.quantity.write` beside the read; create, approve, price, schedule and cancel never confer it.
 */
export const SALES_QUANTITY_WRITE_CAPABILITY = 'sales.orders.quantity.write' as const

export type SalesCapabilityId =
  | (typeof SALES_CAPABILITY)[keyof typeof SALES_CAPABILITY]
  | typeof SALES_PRICE_WRITE_CAPABILITY
  | typeof SALES_PRICE_FLOOR_EXCEPTION_APPROVE_CAPABILITY
  | typeof SALES_DELIVERY_SCHEDULE_WRITE_CAPABILITY
  | typeof SALES_CANCEL_CAPABILITY
  | typeof SALES_FORCE_CLOSE_CAPABILITY
  | typeof SALES_QUANTITY_WRITE_CAPABILITY
  | typeof SALES_CUSTOMER_ACCEPTANCE_RECORD_CAPABILITY

export const SALES_ROUTE = {
  list: '/satis/siparisler',
  // Static before the parameter route: `yeni` must never be read as a draft id.
  create: '/satis/siparisler/yeni',
  detail: '/satis/siparisler/:draftId',
} as const

export const salesDetailPath = (draftId: string) => `/satis/siparisler/${encodeURIComponent(draftId)}`
