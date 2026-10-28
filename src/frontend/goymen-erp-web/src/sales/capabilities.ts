/**
 * Sales screen mount ids and routes.
 *
 * STATUS: PUBLISHED. The backend shell provider (`SalesShellCapabilityProvider`) publishes
 * these ids and the shell mounts the three screens. They are split the way every other
 * lane splits them (a list grant does not imply a detail or a create grant). A capability
 * is not a permission: the API measures the grant afresh on every call, and an actor
 * without the capability gets the neutral not-found block.
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
  /** UI-60 S-07 Sevkiyat / İrsaliyeler — API-30 under `sales.drafts.read` (Sales shell contract 1.9). */
  dispatchesList: 'sales.dispatches.list',
  /** UI-61 S-08 Faturaya Hazır — API-31 under its own `sales.invoices.read` (Sales shell contract 1.9). */
  invoiceCandidatesList: 'sales.invoice-candidates.list',
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
  /** S-07: static, never a draft id. */
  dispatches: '/satis/sevkiyatlar',
  /** S-08: static, never a draft id. */
  invoiceCandidates: '/satis/faturaya-hazir',
} as const

export const salesDetailPath = (draftId: string) => `/satis/siparisler/${encodeURIComponent(draftId)}`

/**
 * UI-20 / S-01 "Satış Talebi / Teklif Havuzu": the ids the backend shell provider publishes (Sales shell contract 1.9), keyed
 * by the pool's own capability keys. Each screen is its own grant (a list grant does not imply a detail or a create grant);
 * the four non-routed controls share their detail's route. Listing an id grants nothing: the shell publishes it only to an
 * actor holding the names beside it, and the API re-measures every call.
 */
export const SALES_POOL_CAPABILITY_ID = {
  /** S-01 talep listesi — `sales.requests.read`. */
  requestsList: 'sales.requests.list',
  /** S-01 talep detayı — `sales.requests.read`. */
  requestsDetail: 'sales.requests.detail',
  /** S-01 yeni talep — `sales.requests.write`. */
  requestsCreate: 'sales.requests.create',
  /** Talep detayında onay / ret kontrolü — `sales.requests.read` + `sales.requests.approve` (SAT-D22). */
  requestsDecide: 'sales.requests.approve',
  /** S-01 teklif listesi — `sales.quotes.read`. */
  quotesList: 'sales.quotes.list',
  /** S-01 teklif detayı — `sales.quotes.read`. */
  quotesDetail: 'sales.quotes.detail',
  /** S-01 yeni teklif — `sales.quotes.write`. */
  quotesCreate: 'sales.quotes.create',
  /** Teklif detayında revizyon ve kayıp kontrolü — `sales.quotes.read` + `sales.quotes.write`. */
  quotesWrite: 'sales.quotes.write',
  /** Teklif detayında "Siparişe dönüştür" — `sales.quotes.read` + `sales.quotes.write` + `sales.drafts.create`. */
  quotesConvert: 'sales.quotes.convert',
  /**
   * Teklif detayında süresi geçmiş teklifin siparişe dönüştürülme onayı — `sales.quotes.read` +
   * `sales.quotes.expired-conversion.approve` (SAT-D24 / SAT-D44; bu izin hiçbir role verilmedi).
   */
  quotesApproveExpired: 'sales.quotes.expired-conversion.approve',
} as const

/** The S-01 pool routes the backend declares beside the ids above. Static `yeni` before each parameter route. */
export const SALES_POOL_ROUTE = {
  requests: '/satis/talepler',
  requestCreate: '/satis/talepler/yeni',
  requestDetail: '/satis/talepler/:requestId',
  quotes: '/satis/teklifler',
  quoteCreate: '/satis/teklifler/yeni',
  quoteDetail: '/satis/teklifler/:quoteId',
} as const
